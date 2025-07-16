using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;
using Baketa.Core.Translation.Abstractions;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Local.Onnx;

/// <summary>
/// αテスト向けOPUS-MT翻訳サービス
/// 複数の言語ペアを管理し、翻訳リクエストを適切なエンジンに転送
/// </summary>
public class AlphaOpusMtTranslationService : IDisposable
{
    private readonly AlphaOpusMtEngineFactory _engineFactory;
    private readonly ILogger<AlphaOpusMtTranslationService> _logger;
    private readonly Dictionary<string, AlphaOpusMtTranslationEngine> _engines = [];
    private readonly SemaphoreSlim _engineLock = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="engineFactory">エンジンファクトリー</param>
    /// <param name="logger">ロガー</param>
    public AlphaOpusMtTranslationService(
        AlphaOpusMtEngineFactory engineFactory,
        ILogger<AlphaOpusMtTranslationService> logger)
    {
        _engineFactory = engineFactory ?? throw new ArgumentNullException(nameof(engineFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// サポートされている言語ペアを取得
    /// </summary>
    /// <returns>サポートされている言語ペアのリスト</returns>
    public async Task<IReadOnlyList<LanguagePair>> GetSupportedLanguagePairsAsync()
    {
        try
        {
            var availabilityInfo = await _engineFactory.CheckAvailabilityAsync().ConfigureAwait(false);
            return availabilityInfo.AvailableLanguagePairs;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "サポートされている言語ペアの取得中にエラーが発生しました");
            return [];
        }
    }

    /// <summary>
    /// 指定された言語ペアがサポートされているかチェック
    /// </summary>
    /// <param name="languagePair">言語ペア</param>
    /// <returns>サポートされている場合はtrue</returns>
    public async Task<bool> SupportsLanguagePairAsync(LanguagePair languagePair)
    {
        ArgumentNullException.ThrowIfNull(languagePair);

        try
        {
            var supportedPairs = await GetSupportedLanguagePairsAsync().ConfigureAwait(false);
            return supportedPairs.Any(pair =>
                string.Equals(pair.SourceLanguage.Code, languagePair.SourceLanguage.Code, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(pair.TargetLanguage.Code, languagePair.TargetLanguage.Code, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "言語ペアサポートチェック中にエラーが発生しました");
            return false;
        }
    }

    /// <summary>
    /// 翻訳を実行
    /// </summary>
    /// <param name="request">翻訳リクエスト</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>翻訳レスポンス</returns>
    public async Task<TranslationResponse> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            var engine = await GetOrCreateEngineAsync(request.SourceLanguage, request.TargetLanguage, cancellationToken).ConfigureAwait(false);
            if (engine == null)
            {
                return CreateErrorResponse(request, "翻訳エンジンの取得に失敗しました");
            }

            return await engine.TranslateAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "翻訳処理中にエラーが発生しました");
            return CreateErrorResponse(request, $"翻訳エラー: {ex.Message}");
        }
    }

    /// <summary>
    /// バッチ翻訳を実行
    /// </summary>
    /// <param name="requests">翻訳リクエストのリスト</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>翻訳レスポンスのリスト</returns>
    public async Task<IReadOnlyList<TranslationResponse>> TranslateBatchAsync(
        IReadOnlyList<TranslationRequest> requests,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);

        var results = new List<TranslationResponse>();

        // 言語ペアごとにグループ化
        var groupedRequests = requests.GroupBy(r => new { SourceLangCode = r.SourceLanguage.Code, TargetLangCode = r.TargetLanguage.Code });

        foreach (var group in groupedRequests)
        {
            try
            {
                var sourceLanguage = group.First().SourceLanguage;
                var targetLanguage = group.First().TargetLanguage;

                var engine = await GetOrCreateEngineAsync(sourceLanguage, targetLanguage, cancellationToken).ConfigureAwait(false);
                if (engine == null)
                {
                    // エンジンが取得できない場合、エラーレスポンスを作成
                    foreach (var request in group)
                    {
                        results.Add(CreateErrorResponse(request, "翻訳エンジンの取得に失敗しました"));
                    }
                    continue;
                }

                // 同じ言語ペアのリクエストをバッチ処理
                var batchResults = await engine.TranslateBatchAsync([.. group], cancellationToken).ConfigureAwait(false);
                results.AddRange(batchResults);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "バッチ翻訳処理中にエラーが発生しました");
                
                // エラーが発生した場合、該当グループのすべてのリクエストにエラーレスポンスを作成
                foreach (var request in group)
                {
                    results.Add(CreateErrorResponse(request, $"バッチ翻訳エラー: {ex.Message}"));
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        return results;
    }

    /// <summary>
    /// 翻訳サービスの初期化
    /// </summary>
    /// <returns>初期化が成功した場合はtrue</returns>
    public async Task<bool> InitializeAsync()
    {
        try
        {
            _logger.LogInformation("OPUS-MT α翻訳サービスを初期化中...");

            var availabilityInfo = await _engineFactory.CheckAvailabilityAsync().ConfigureAwait(false);
            
            if (!availabilityInfo.IsPartiallyAvailable)
            {
                _logger.LogWarning("利用可能なOPUS-MTモデルが見つかりませんでした。不足ファイル: {MissingFiles}",
                    string.Join(", ", availabilityInfo.MissingFiles));
                return false;
            }

            _logger.LogInformation("OPUS-MT α翻訳サービスの初期化が完了しました。利用可能言語ペア: {Count}",
                availabilityInfo.AvailableLanguagePairs.Count);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OPUS-MT α翻訳サービスの初期化中にエラーが発生しました");
            return false;
        }
    }

    /// <summary>
    /// 翻訳サービスの準備状態を確認
    /// </summary>
    /// <returns>準備ができている場合はtrue</returns>
    public async Task<bool> IsReadyAsync()
    {
        try
        {
            var availabilityInfo = await _engineFactory.CheckAvailabilityAsync().ConfigureAwait(false);
            return availabilityInfo.IsPartiallyAvailable;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "翻訳サービスの準備状態確認中にエラーが発生しました");
            return false;
        }
    }

    /// <summary>
    /// 指定された言語ペアのエンジンを取得または作成
    /// </summary>
    private async Task<AlphaOpusMtTranslationEngine?> GetOrCreateEngineAsync(
        Language sourceLanguage,
        Language targetLanguage,
        CancellationToken cancellationToken)
    {
        var key = $"{sourceLanguage.Code}-{targetLanguage.Code}";

        await _engineLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_engines.TryGetValue(key, out var existingEngine))
            {
                return existingEngine;
            }

            var languagePair = new LanguagePair
            {
                SourceLanguage = sourceLanguage,
                TargetLanguage = targetLanguage
            };

            var engine = await _engineFactory.CreateEngineAsync(languagePair).ConfigureAwait(false);
            if (engine == null)
            {
                return null;
            }

            // エンジンの初期化
            if (!await engine.InitializeAsync().ConfigureAwait(false))
            {
                engine.Dispose();
                return null;
            }

            _engines[key] = engine;
            return engine;
        }
        finally
        {
            _engineLock.Release();
        }
    }

    /// <summary>
    /// エラーレスポンスの作成
    /// </summary>
    private TranslationResponse CreateErrorResponse(TranslationRequest request, string errorMessage)
    {
        return new TranslationResponse
        {
            RequestId = request.RequestId,
            SourceText = request.SourceText,
            TranslatedText = string.Empty,
            SourceLanguage = request.SourceLanguage,
            TargetLanguage = request.TargetLanguage,
            EngineName = "OPUS-MT Alpha Service",
            IsSuccess = false,
            Error = new TranslationError
            {
                ErrorCode = "ALPHA_SERVICE_ERROR",
                ErrorType = TranslationErrorType.ProcessingError,
                Message = errorMessage
            }
        };
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
    /// ファイナライザー
    /// </summary>
    ~AlphaOpusMtTranslationService()
    {
        Dispose(false);
    }

    /// <summary>
    /// リソースの解放
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            foreach (var engine in _engines.Values)
            {
                engine.Dispose();
            }
            _engines.Clear();
            _engineLock.Dispose();
            _disposed = true;
        }
    }
}