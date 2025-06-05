using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Abstractions;
using Baketa.Core.Translation.Models;
using Baketa.Core.Translation.Configuration;
using Baketa.Core.Translation.Exceptions;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Local.Onnx.Chinese;

/// <summary>
/// 中国語翻訳専用エンジン
/// OPUS-MTプレフィックス指定による簡体字・繁体字制御機能を提供
/// </summary>
public class ChineseTranslationEngine : ITranslationEngine, IDisposable
{
    private readonly OpusMtOnnxEngine _baseEngine;
    private readonly ChineseLanguageProcessor _chineseProcessor;
    private readonly ILogger<ChineseTranslationEngine> _logger;
    private bool _disposed;

    /// <summary>
    /// 翻訳エンジンの名前
    /// </summary>
    public string Name => "Chinese Translation Engine";

    /// <summary>
    /// 翻訳エンジンの説明
    /// </summary>
    public string Description => "OPUS-MTモデルを使用した中国語翻訳エンジン（簡体字・繁体字対応）";

    /// <summary>
    /// ネットワーク接続が必要かどうか
    /// </summary>
    public bool RequiresNetwork => false;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="baseEngine">基本翻訳エンジン</param>
    /// <param name="chineseProcessor">中国語処理器</param>
    /// <param name="logger">ロガー</param>
    public ChineseTranslationEngine(
        OpusMtOnnxEngine baseEngine,
        ChineseLanguageProcessor chineseProcessor,
        ILogger<ChineseTranslationEngine> logger)
    {
        _baseEngine = baseEngine ?? throw new ArgumentNullException(nameof(baseEngine));
        _chineseProcessor = chineseProcessor ?? throw new ArgumentNullException(nameof(chineseProcessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 翻訳エンジンを初期化します
    /// </summary>
    /// <returns>初期化に成功した場合はtrue</returns>
    public async Task<bool> InitializeAsync()
    {
        try
        {
            _logger.LogInformation("中国語翻訳エンジンを初期化中...");
            
            // 基本エンジンの初期化
            var result = await _baseEngine.InitializeAsync().ConfigureAwait(false);
            
            if (result)
            {
                _logger.LogInformation("中国語翻訳エンジンの初期化が完了しました");
            }
            else
            {
                _logger.LogError("中国語翻訳エンジンの初期化に失敗しました");
            }
            
            return result;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "中国語翻訳エンジンの初期化中に無効な操作エラーが発生しました");
            return false;
        }
        catch (NotSupportedException ex)
        {
            _logger.LogError(ex, "中国語翻訳エンジンの初期化でサポートされていない操作が実行されました");
            return false;
        }
#pragma warning disable CA1031 // 初期化のエラーでは全ての例外をキャッチする必要がある
        catch (Exception ex)
        {
            _logger.LogError(ex, "中国語翻訳エンジンの初期化中に予期しないエラーが発生しました");
            return false;
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// 翻訳エンジンが準備完了しているか確認します
    /// </summary>
    /// <returns>準備完了している場合はtrue</returns>
    public async Task<bool> IsReadyAsync()
    {
        try
        {
            return await _baseEngine.IsReadyAsync().ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "中国語翻訳エンジンの準備状態確認中に無効な操作エラーが発生しました");
            return false;
        }
#pragma warning disable CA1031 // 準備状態確認では全ての例外をキャッチする必要がある
        catch (Exception ex)
        {
            _logger.LogError(ex, "中国語翻訳エンジンの準備状態確認中に予期しないエラーが発生しました");
            return false;
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// テキストを翻訳します
    /// </summary>
    /// <param name="request">翻訳リクエスト</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>翻訳レスポンス</returns>
    public async Task<TranslationResponse> TranslateAsync(
        TranslationRequest request, 
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(request.SourceText))
        {
            _logger.LogDebug("空のテキストが渡されました。そのまま返します。");
            return new TranslationResponse
            {
                RequestId = request.RequestId,
                SourceText = request.SourceText,
                TranslatedText = string.Empty,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                EngineName = Name,
                IsSuccess = true
            };
        }

        try
        {
            // 中国語処理の前処理
            var processedRequest = await PreprocessRequestAsync(request, cancellationToken).ConfigureAwait(false);

            _logger.LogDebug("中国語翻訳実行 - Source: {SourceLang}, Target: {TargetLang}", 
                processedRequest.SourceLanguage.Code, processedRequest.TargetLanguage.Code);

            // 基本エンジンで翻訳実行
            var response = await _baseEngine.TranslateAsync(processedRequest, cancellationToken).ConfigureAwait(false);

            // 翻訳結果の後処理
            var finalResponse = await PostprocessResponseAsync(response, request, cancellationToken).ConfigureAwait(false);

            _logger.LogDebug("中国語翻訳完了 - 結果長: {ResultLength}文字", 
                finalResponse.TranslatedText?.Length ?? 0);
            return finalResponse;
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "中国語翻訳パラメーターエラー - Source: {SourceLang}, Target: {TargetLang}", 
                request.SourceLanguage.Code, request.TargetLanguage.Code);
            
            return new TranslationResponse
            {
                RequestId = request.RequestId,
                SourceText = request.SourceText,
                TranslatedText = request.SourceText,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                EngineName = Name,
                IsSuccess = false,
                Error = new TranslationError
                {
                    ErrorCode = "INVALID_ARGUMENT",
                    Message = $"無効な引数: {ex.Message}",
                    ErrorType = TranslationErrorType.InvalidRequest,
                    Details = ex.ToString()
                }
            };
        }
        catch (TranslationException ex)
        {
            _logger.LogError(ex, "中国語翻訳エラー - Source: {SourceLang}, Target: {TargetLang}", 
                request.SourceLanguage.Code, request.TargetLanguage.Code);
            
            return new TranslationResponse
            {
                RequestId = request.RequestId,
                SourceText = request.SourceText,
                TranslatedText = request.SourceText,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                EngineName = Name,
                IsSuccess = false,
                Error = new TranslationError
                {
                    ErrorCode = "TRANSLATION_ERROR",
                    Message = $"翻訳エラー: {ex.Message}",
                    ErrorType = TranslationErrorType.ProcessingError,
                    Details = ex.ToString()
                }
            };
        }
#pragma warning disable CA1031 // 翻訳エラーは維続させるため、全ての例外をキャッチする必要がある
        catch (Exception ex)
        {
            _logger.LogError(ex, "中国語翻訳予期しないエラー - Source: {SourceLang}, Target: {TargetLang}", 
                request.SourceLanguage.Code, request.TargetLanguage.Code);
            
            return new TranslationResponse
            {
                RequestId = request.RequestId,
                SourceText = request.SourceText,
                TranslatedText = request.SourceText,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                EngineName = Name,
                IsSuccess = false,
                Error = new TranslationError
                {
                    ErrorCode = "CHINESE_TRANSLATION_FAILED",
                    Message = $"中国語翻訳に失敗しました: {ex.Message}",
                    ErrorType = TranslationErrorType.ProcessingError,
                    Details = ex.ToString()
                }
            };
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// 複数のテキストを一括翻訳します
    /// </summary>
    /// <param name="requests">翻訳リクエストのリスト</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>翻訳レスポンスのリスト</returns>
    public async Task<IReadOnlyList<TranslationResponse>> TranslateBatchAsync(
        IReadOnlyList<TranslationRequest> requests, 
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        
        ObjectDisposedException.ThrowIf(_disposed, this);

        var responses = new List<TranslationResponse>();

        foreach (var request in requests)
        {
            try
            {
                var response = await TranslateAsync(request, cancellationToken).ConfigureAwait(false);
                responses.Add(response);
            }
#pragma warning disable CA1031 // バッチ翻訳では全ての例外をキャッチして維続する必要がある
            catch (Exception ex)
            {
                _logger.LogError(ex, "バッチ翻訳中にエラーが発生しました - RequestId: {RequestId}", 
                    request.RequestId);
                
                responses.Add(new TranslationResponse
                {
                    RequestId = request.RequestId,
                    SourceText = request.SourceText,
                    TranslatedText = request.SourceText,
                    SourceLanguage = request.SourceLanguage,
                    TargetLanguage = request.TargetLanguage,
                    EngineName = Name,
                    IsSuccess = false,
                    Error = new TranslationError
                    {
                        ErrorCode = "BATCH_TRANSLATION_FAILED",
                        Message = $"バッチ翻訳に失敗しました: {ex.Message}",
                        ErrorType = TranslationErrorType.ProcessingError,
                        Details = ex.ToString()
                    }
                });
            }
#pragma warning restore CA1031
        }

        return responses.AsReadOnly();
    }

    /// <summary>
    /// サポートしている言語ペアを取得します
    /// </summary>
    /// <returns>サポートされている言語ペアのコレクション</returns>
    public async Task<IReadOnlyCollection<LanguagePair>> GetSupportedLanguagePairsAsync()
    {
        try
        {
            // 基本エンジンのサポート言語ペアを取得
            var basePairs = await _baseEngine.GetSupportedLanguagePairsAsync().ConfigureAwait(false);
            
            // 中国語関連のペアのみを抽出
            var chinesePairs = basePairs.Where(p => 
                IsChineseRelated(p.SourceLanguage.Code) || IsChineseRelated(p.TargetLanguage.Code))
                .ToList();

            return chinesePairs.AsReadOnly();
        }
#pragma warning disable CA1031 // サポート言語ペア取得では全ての例外をキャッチする必要がある
        catch (Exception ex)
        {
            _logger.LogError(ex, "サポート言語ペアの取得中に予期しないエラーが発生しました");
            return [];
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// 指定した言語ペアがサポートされているか確認します
    /// </summary>
    /// <param name="languagePair">言語ペア</param>
    /// <returns>サポートされている場合はtrue</returns>
    public async Task<bool> SupportsLanguagePairAsync(LanguagePair languagePair)
    {
        ArgumentNullException.ThrowIfNull(languagePair);

        try
        {
            // 中国語関連の言語ペアかチェック
            if (!IsChineseRelated(languagePair.SourceLanguage.Code) && 
                !IsChineseRelated(languagePair.TargetLanguage.Code))
            {
                return false;
            }

            return await _baseEngine.SupportsLanguagePairAsync(languagePair).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // 言語ペアサポート確認では全ての例外をキャッチする必要がある
        catch (Exception ex)
        {
            _logger.LogError(ex, "言語ペアサポート確認中に予期しないエラーが発生しました");
            return false;
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// テキストの言語を自動検出します
    /// </summary>
    /// <param name="text">検出対象テキスト</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>検出された言語と信頼度</returns>
    public async Task<LanguageDetectionResult> DetectLanguageAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        
        try
        {
            // 基本エンジンの言語検出を使用
            var result = await _baseEngine.DetectLanguageAsync(text, cancellationToken).ConfigureAwait(false);
            
            // 中国語の場合、より詳細な変種検出を行う
            if (IsChineseRelated(result.DetectedLanguage.Code))
            {
                var variant = DetectChineseVariant(text);
                var detectedLanguage = variant switch
                {
                    ChineseVariant.Simplified => Language.ChineseSimplified,
                    ChineseVariant.Traditional => Language.ChineseTraditional,
                    _ => result.DetectedLanguage
                };

                return new LanguageDetectionResult
                {
                    DetectedLanguage = detectedLanguage,
                    Confidence = result.Confidence,
                    IsReliable = result.IsReliable
                };
            }

            return result;
        }
#pragma warning disable CA1031 // 言語検出では全ての例外をキャッチする必要がある
        catch (Exception ex)
        {
            _logger.LogError(ex, "言語検出中に予期しないエラーが発生しました");
            return new LanguageDetectionResult
            {
                DetectedLanguage = Language.Auto,
                Confidence = 0.0,
                IsReliable = false
            };
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// 翻訳リクエストの前処理
    /// </summary>
    /// <param name="request">元のリクエスト</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>前処理されたリクエスト</returns>
    private async Task<TranslationRequest> PreprocessRequestAsync(
        TranslationRequest request,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false); // 非同期API用のダミー

        // 英語→中国語の場合のみプレフィックスを適用
        if (request.SourceLanguage.Code.Equals("en", StringComparison.OrdinalIgnoreCase) && 
            IsChineseRelated(request.TargetLanguage.Code))
        {
            var variant = LanguageConfiguration.GetChineseVariant(request.TargetLanguage.Code);
            var processedText = ApplyChineseVariantPrefix(request.SourceText, variant);

            return TranslationRequest.Create(processedText, request.SourceLanguage, request.TargetLanguage);
        }

        return request;
    }

    /// <summary>
    /// 翻訳レスポンスの後処理
    /// </summary>
    /// <param name="response">翻訳レスポンス</param>
    /// <param name="originalRequest">元のリクエスト</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>後処理されたレスポンス</returns>
    private async Task<TranslationResponse> PostprocessResponseAsync(
        TranslationResponse response,
        TranslationRequest originalRequest,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false); // 非同期API用のダミー

        // 必要に応じて追加の後処理を実装
        // 例：不適切な文字の除去、フォーマット調整など

        return response;
    }

    /// <summary>
    /// 中国語変種プレフィックスを適用
    /// </summary>
    /// <param name="text">元のテキスト</param>
    /// <param name="variant">中国語変種</param>
    /// <returns>プレフィックス付きテキスト</returns>
    private string ApplyChineseVariantPrefix(string text, ChineseVariant variant)
    {
        ArgumentNullException.ThrowIfNull(text);
        
        var prefix = variant.GetOpusPrefix();
        
        if (string.IsNullOrEmpty(prefix))
        {
            _logger.LogDebug("中国語変種 {Variant} にプレフィックスがありません", variant);
            return text;
        }

        // 既にプレフィックスが存在する場合は追加しない
        if (text.TrimStart().StartsWith(">>", StringComparison.Ordinal))
        {
            _logger.LogDebug("テキストには既にプレフィックスが存在します");
            return text;
        }

        var prefixedText = $"{prefix} {text}";
        _logger.LogDebug("中国語変種プレフィックス適用: {Prefix}", prefix);
        
        return prefixedText;
    }

    /// <summary>
    /// 中国語関連の言語コードかどうかを判定
    /// </summary>
    /// <param name="languageCode">言語コード</param>
    /// <returns>中国語関連の場合はtrue</returns>
    private static bool IsChineseRelated(string languageCode)
    {
        return LanguageConfiguration.IsChineseLanguageCode(languageCode);
    }

    /// <summary>
    /// 中国語変種の自動検出
    /// </summary>
    /// <param name="text">中国語テキスト</param>
    /// <returns>検出された中国語変種</returns>
    public ChineseVariant DetectChineseVariant(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        
        if (string.IsNullOrWhiteSpace(text))
        {
            return ChineseVariant.Auto;
        }

        var scriptType = _chineseProcessor.DetectScriptType(text);
        
        return scriptType switch
        {
            ChineseScriptType.Simplified => ChineseVariant.Simplified,
            ChineseScriptType.Traditional => ChineseVariant.Traditional,
            ChineseScriptType.Mixed => ChineseVariant.Auto, // 混在の場合は自動判定
            _ => ChineseVariant.Auto
        };
    }

    /// <summary>
    /// 中国語変種別の翻訳結果を取得
    /// </summary>
    /// <param name="text">翻訳するテキスト</param>
    /// <param name="sourceLang">ソース言語</param>
    /// <param name="targetLang">ターゲット言語</param>
    /// <returns>変種別翻訳結果</returns>
    public async Task<ChineseVariantTranslationResult> TranslateAllVariantsAsync(
        string text, 
        string sourceLang, 
        string targetLang)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(sourceLang);
        ArgumentNullException.ThrowIfNull(targetLang);
        
        if (!IsChineseRelated(targetLang))
        {
            throw new ArgumentException("ターゲット言語が中国語ではありません", nameof(targetLang));
        }

        _logger.LogDebug("中国語変種別翻訳開始 - テキスト: {Text}", 
            text.Length > 50 ? text[..50] + "..." : text);

        var result = new ChineseVariantTranslationResult
        {
            SourceText = text,
            SourceLanguage = sourceLang,
            TargetLanguage = targetLang
        };

        try
        {
            // 各変種での翻訳を並行実行
            var tasks = new Task<string>[]
            {
                TranslateVariantAsync(text, sourceLang, targetLang, ChineseVariant.Auto),
                TranslateVariantAsync(text, sourceLang, targetLang, ChineseVariant.Simplified),
                TranslateVariantAsync(text, sourceLang, targetLang, ChineseVariant.Traditional)
            };

            var results = await Task.WhenAll(tasks).ConfigureAwait(false);

            result.AutoResult = results[0];
            result.SimplifiedResult = results[1];
            result.TraditionalResult = results[2];
            result.IsSuccess = true;

            _logger.LogDebug("中国語変種別翻訳完了");
        }
#pragma warning disable CA1031 // 中国語変種別翻訳では全ての例外をキャッチする必要がある
        catch (Exception ex)
        {
            _logger.LogError(ex, "中国語変種別翻訳中に予期しないエラーが発生しました");
            result.IsSuccess = false;
            result.ErrorMessage = ex.Message;
        }
#pragma warning restore CA1031

        return result;
    }

    /// <summary>
    /// レガシーAPI: 翻訳を実行（標準インターフェース）
    /// </summary>
    /// <param name="text">翻訳するテキスト</param>
    /// <param name="sourceLang">ソース言語コード</param>
    /// <param name="targetLang">ターゲット言語コード</param>
    /// <returns>翻訳結果</returns>
    public async Task<string> TranslateAsync(string text, string sourceLang, string targetLang)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(sourceLang);
        ArgumentNullException.ThrowIfNull(targetLang);
        
        // 言語コードから中国語変種を自動判定
        var variant = LanguageConfiguration.GetChineseVariant(targetLang);
        return await TranslateAsync(text, sourceLang, targetLang, variant).ConfigureAwait(false);
    }

    /// <summary>
    /// 中国語変種を指定した翻訳を実行
    /// </summary>
    /// <param name="text">翻訳するテキスト</param>
    /// <param name="sourceLang">ソース言語コード</param>
    /// <param name="targetLang">ターゲット言語コード</param>
    /// <param name="variant">中国語変種</param>
    /// <returns>翻訳結果</returns>
    public async Task<string> TranslateAsync(
        string text, 
        string sourceLang, 
        string targetLang, 
        ChineseVariant variant = ChineseVariant.Auto)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(sourceLang);
        ArgumentNullException.ThrowIfNull(targetLang);
        
        var request = TranslationRequest.Create(text, Language.FromCode(sourceLang), Language.FromCode(targetLang));
        var response = await TranslateAsync(request).ConfigureAwait(false);
        return response.TranslatedText ?? string.Empty;
    }

    /// <summary>
    /// 指定された変種での翻訳を実行
    /// </summary>
    /// <param name="text">翻訳するテキスト</param>
    /// <param name="sourceLang">ソース言語</param>
    /// <param name="targetLang">ターゲット言語</param>
    /// <param name="variant">中国語変種</param>
    /// <returns>翻訳結果</returns>
    private async Task<string> TranslateVariantAsync(
        string text, 
        string sourceLang, 
        string targetLang, 
        ChineseVariant variant)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(sourceLang);
        ArgumentNullException.ThrowIfNull(targetLang);
        
        try
        {
            return await TranslateAsync(text, sourceLang, targetLang, variant).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // 変種別翻訳では全ての例外をキャッチして維続する必要がある
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "中国語変種 {Variant} での翻訳で予期しないエラーが発生しました", variant);
            return $"[翻訳エラー: {variant.GetDisplayName()}]";
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// リソースの解放
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// リソースの解放（仮想メソッド）
    /// </summary>
    /// <param name="disposing">マネージドリソースを解放するかどうか</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // マネージドリソースの解放
                // 基本エンジンと中国語処理器はDIコンテナによって管理されるため、解放しない
                _logger.LogDebug("ChineseTranslationEngine のマネージドリソースを解放しました");
            }

            // アンマネージドリソースの解放（該当なし）
            _disposed = true;
        }
    }
}

/// <summary>
/// 中国語変種別翻訳結果
/// </summary>
public class ChineseVariantTranslationResult
{
    /// <summary>
    /// 元のテキスト
    /// </summary>
    public string SourceText { get; set; } = string.Empty;

    /// <summary>
    /// ソース言語
    /// </summary>
    public string SourceLanguage { get; set; } = string.Empty;

    /// <summary>
    /// ターゲット言語
    /// </summary>
    public string TargetLanguage { get; set; } = string.Empty;

    /// <summary>
    /// 自動判定結果
    /// </summary>
    public string AutoResult { get; set; } = string.Empty;

    /// <summary>
    /// 簡体字結果
    /// </summary>
    public string SimplifiedResult { get; set; } = string.Empty;

    /// <summary>
    /// 繁体字結果
    /// </summary>
    public string TraditionalResult { get; set; } = string.Empty;

    /// <summary>
    /// 翻訳が成功したかどうか
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// エラーメッセージ（失敗した場合）
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 翻訳実行時刻
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}