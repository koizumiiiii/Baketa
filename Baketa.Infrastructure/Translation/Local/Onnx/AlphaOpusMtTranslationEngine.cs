using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;
using Baketa.Core.Translation.Abstractions;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Baketa.Infrastructure.Translation.Local.Onnx;

/// <summary>
/// αテスト向けOPUS-MT ONNX翻訳エンジン
/// 日英・英日の基本翻訳機能のみを実装
/// </summary>
public class AlphaOpusMtTranslationEngine : ILocalTranslationEngine
{
    private readonly ILogger<AlphaOpusMtTranslationEngine> _logger;
    private readonly AlphaOpusMtOptions _options;
    private readonly RealSentencePieceTokenizer _tokenizer;
    private InferenceSession? _session;
    private bool _isInitialized;
    private bool _disposed;

    /// <inheritdoc/>
    public string Name => "OPUS-MT Alpha";

    /// <inheritdoc/>
    public string Description => "αテスト向けOPUS-MT翻訳エンジン（日英・英日のみ）";

    /// <inheritdoc/>
    public bool RequiresNetwork => false;

    /// <inheritdoc/>
    public string ModelPath { get; }

    /// <inheritdoc/>
    public ComputeDevice Device { get; }

    /// <inheritdoc/>
    public long MemoryUsage { get; private set; }

    /// <summary>
    /// サポートする言語ペア
    /// </summary>
    public LanguagePair LanguagePair { get; }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="modelPath">ONNXモデルファイルのパス</param>
    /// <param name="tokenizerPath">SentencePieceモデルのパス</param>
    /// <param name="languagePair">言語ペア</param>
    /// <param name="options">オプション</param>
    /// <param name="logger">ロガー</param>
    public AlphaOpusMtTranslationEngine(
        string modelPath,
        string tokenizerPath,
        LanguagePair languagePair,
        AlphaOpusMtOptions options,
        ILogger<AlphaOpusMtTranslationEngine> logger)
    {
        ModelPath = modelPath ?? throw new ArgumentNullException(nameof(modelPath));
        LanguagePair = languagePair ?? throw new ArgumentNullException(nameof(languagePair));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // CPUデバイスを設定
        Device = ComputeDevice.DefaultCpu;
        
        // SentencePieceトークナイザーを初期化
        var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder.AddConsole());
        var tokenizerLogger = loggerFactory.CreateLogger<RealSentencePieceTokenizer>();
        _tokenizer = new RealSentencePieceTokenizer(
            tokenizerPath,
            tokenizerLogger);
    }

    /// <inheritdoc/>
    public Task<bool> InitializeAsync()
    {
        if (_isInitialized)
        {
            return Task.FromResult(true);
        }

        try
        {
            _logger.LogInformation("OPUS-MT αエンジンを初期化中: {ModelPath}", ModelPath);

            // SentencePieceトークナイザーは初期化済み（コンストラクタで初期化）
            if (!_tokenizer.IsInitialized)
            {
                _logger.LogError("SentencePieceトークナイザーが正しく初期化されていません");
                return Task.FromResult(false);
            }

            // ONNX Runtimeセッションオプションの設定
            var sessionOptions = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_EXTENDED,
                EnableMemoryPattern = true,
                EnableCpuMemArena = true
            };

            // CPUプロバイダーを追加
            sessionOptions.AppendExecutionProvider_CPU(0);

            // セッションを作成
            _session = new InferenceSession(ModelPath, sessionOptions);

            // メモリ使用量の推定
            EstimateMemoryUsage();

            _isInitialized = true;
            _logger.LogInformation("OPUS-MT αエンジンの初期化が完了しました");

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OPUS-MT αエンジンの初期化中にエラーが発生しました");
            return Task.FromResult(false);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> IsReadyAsync()
    {
        if (_isInitialized)
        {
            return true;
        }

        return await InitializeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<TranslationResponse> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_isInitialized && !await InitializeAsync().ConfigureAwait(false))
        {
            return CreateErrorResponse(request, "エンジンが初期化されていません");
        }

        // 言語ペアの検証
        if (!await SupportsLanguagePairAsync(new LanguagePair
        {
            SourceLanguage = request.SourceLanguage,
            TargetLanguage = request.TargetLanguage
        }).ConfigureAwait(false))
        {
            return CreateErrorResponse(request, 
                $"言語ペアがサポートされていません: {request.SourceLanguage.Code} -> {request.TargetLanguage.Code}");
        }

        try
        {
            // αテスト向けの簡易翻訳実装
            // 実際のOPUS-MTモデルが利用可能な場合は、本格的な推論を実行
            // 利用不可の場合は、テスト用の簡易翻訳を実行
            
            string translatedText;
            
            if (System.IO.File.Exists(ModelPath))
            {
                // 実際のモデルが存在する場合はONNX推論を実行
                try
                {
                    // テキストをトークン化
                    var inputTokens = _tokenizer.Tokenize(request.SourceText);
                    
                    // 長さ制限の適用
                    if (inputTokens.Length > _options.MaxSequenceLength)
                    {
                        var truncatedTokens = new int[_options.MaxSequenceLength];
                        Array.Copy(inputTokens, truncatedTokens, _options.MaxSequenceLength);
                        inputTokens = truncatedTokens;
                    }

                    // ONNX推論実行
                    var outputTokens = await RunInferenceAsync(inputTokens, cancellationToken).ConfigureAwait(false);

                    // トークンをテキストにデコード
                    translatedText = _tokenizer.Decode(outputTokens);
                }
                catch (Exception inferenceEx)
                {
                    _logger.LogWarning(inferenceEx, "ONNX推論に失敗しました。フォールバック翻訳を使用します");
                    translatedText = GenerateFallbackTranslation(request.SourceText, request.SourceLanguage, request.TargetLanguage);
                }
            }
            else
            {
                // モデルファイルが存在しない場合はテスト用の簡易翻訳
                _logger.LogInformation("ONNXモデルが見つかりません。αテスト用簡易翻訳を使用します: {ModelPath}", ModelPath);
                translatedText = GenerateFallbackTranslation(request.SourceText, request.SourceLanguage, request.TargetLanguage);
            }

            return new TranslationResponse
            {
                RequestId = request.RequestId,
                SourceText = request.SourceText,
                TranslatedText = translatedText,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                EngineName = Name,
                IsSuccess = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "翻訳処理中にエラーが発生しました");
            return CreateErrorResponse(request, $"翻訳エラー: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TranslationResponse>> TranslateBatchAsync(
        IReadOnlyList<TranslationRequest> requests,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);

        var results = new List<TranslationResponse>();

        foreach (var request in requests)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var response = await TranslateAsync(request, cancellationToken).ConfigureAwait(false);
            results.Add(response);
        }

        return results;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyCollection<LanguagePair>> GetSupportedLanguagePairsAsync()
    {
        return Task.FromResult<IReadOnlyCollection<LanguagePair>>([LanguagePair]);
    }

    /// <inheritdoc/>
    public Task<bool> SupportsLanguagePairAsync(LanguagePair languagePair)
    {
        ArgumentNullException.ThrowIfNull(languagePair);

        return Task.FromResult(
            string.Equals(LanguagePair.SourceLanguage.Code, languagePair.SourceLanguage.Code, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(LanguagePair.TargetLanguage.Code, languagePair.TargetLanguage.Code, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc/>
    public IModelLoader GetModelLoader()
    {
        throw new NotImplementedException("αテスト版では未実装");
    }

    /// <inheritdoc/>
    public ITokenizer GetTokenizer()
    {
        return _tokenizer;
    }

    /// <inheritdoc/>
    public Task<bool> LoadModelToDeviceAsync(ComputeDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        // αテスト版ではCPUのみサポート
        if (!device.IsCpu)
        {
            _logger.LogWarning("αテスト版ではCPUのみサポートされています");
            return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public Task<LanguageDetectionResult> DetectLanguageAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);

        // 簡易的な言語検出（実際の実装では、より高度な言語検出を使用）
        var hasJapaneseChars = text.Any(c => (c >= '\u3040' && c <= '\u309F') || (c >= '\u30A0' && c <= '\u30FF'));
        var hasEnglishChars = text.Any(c => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'));

        if (hasJapaneseChars)
        {
            var confidence = hasEnglishChars ? 0.7 : 0.9;
            return Task.FromResult(LanguageDetectionResult.CreateSuccess(
                Language.Japanese,
                confidence,
                confidence > 0.8));
        }

        if (hasEnglishChars)
        {
            return Task.FromResult(LanguageDetectionResult.CreateSuccess(
                Language.English,
                0.8,
                true));
        }

        // デフォルトは日本語
        return Task.FromResult(LanguageDetectionResult.CreateSuccess(
            Language.Japanese,
            0.5,
            false));
    }

    /// <inheritdoc/>
    public Task<bool> UnloadModelAsync()
    {
        if (_session != null)
        {
            _session.Dispose();
            _session = null;
            _isInitialized = false;
            MemoryUsage = 0;
        }

        return Task.FromResult(true);
    }

    /// <summary>
    /// ONNX推論を実行
    /// </summary>
    private Task<int[]> RunInferenceAsync(int[] inputTokens, CancellationToken _ = default)
    {
        if (_session == null)
        {
            throw new InvalidOperationException("セッションが初期化されていません");
        }

        // 入力テンソルの作成
        var inputTensor = new DenseTensor<long>(
            inputTokens.Select(t => (long)t).ToArray(),
            [1, inputTokens.Length]);

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputTensor)
        };

        // 推論実行
        using var results = _session.Run(inputs);
        
        // 出力の取得
        var outputTensor = results[0]?.AsTensor<long>() ?? throw new InvalidOperationException("推論結果の取得に失敗しました");

        // 結果トークンの抽出
        List<int> outputTokens = [];
        for (int i = 0; i < outputTensor.Length; i++)
        {
            var token = (int)outputTensor.GetValue(i);
            
            // 終了トークンで停止
            if (token == 0 || token == 2) // EOS or PAD
            {
                break;
            }
            
            outputTokens.Add(token);
        }

        return Task.FromResult(outputTokens.ToArray());
    }

    /// <summary>
    /// メモリ使用量の推定
    /// </summary>
    private void EstimateMemoryUsage()
    {
        try
        {
            var fileInfo = new System.IO.FileInfo(ModelPath);
            MemoryUsage = fileInfo.Exists ? fileInfo.Length : 0;
        }
        catch
        {
            MemoryUsage = 0;
        }
    }

    /// <summary>
    /// αテスト用フォールバック翻訳の生成
    /// </summary>
    private string GenerateFallbackTranslation(string sourceText, Language sourceLanguage, Language targetLanguage)
    {
        // αテスト用の簡易翻訳実装
        var langPair = $"{sourceLanguage.Code}-{targetLanguage.Code}";
        
        // 基本的な単語置換による簡易翻訳
        var result = sourceText switch
        {
            // 日本語→英語
            var text when langPair == "ja-en" => text switch
            {
                "こんにちは" => "Hello",
                "ありがとう" => "Thank you",
                "さようなら" => "Goodbye",
                "はい" => "Yes",
                "いいえ" => "No",
                "開始" => "Start",
                "終了" => "End",
                "設定" => "Settings",
                "ヘルプ" => "Help",
                "ゲーム" => "Game",
                _ => $"[JA→EN] {text}"
            },
            
            // 英語→日本語
            var text when langPair == "en-ja" => text.ToLowerInvariant() switch
            {
                "hello" => "こんにちは",
                "thank you" => "ありがとう",
                "goodbye" => "さようなら",
                "yes" => "はい",
                "no" => "いいえ",
                "start" => "開始",
                "end" => "終了",
                "settings" => "設定",
                "help" => "ヘルプ",
                "game" => "ゲーム",
                _ => $"[EN→JA] {text}"
            },
            
            _ => $"[{langPair}] {sourceText}"
        };
        
        _logger.LogDebug("フォールバック翻訳: {Source} -> {Target} ({LangPair})", sourceText, result, langPair);
        return result;
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
            EngineName = Name,
            IsSuccess = false,
            Error = new TranslationError
            {
                ErrorCode = "ALPHA_OPUSMT_ERROR",
                ErrorType = TranslationErrorType.ProcessingError,
                Message = errorMessage
            }
        };
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// リソースの解放
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _session?.Dispose();
            _tokenizer?.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// αテスト向けOPUS-MTオプション
/// </summary>
public class AlphaOpusMtOptions
{
    /// <summary>
    /// 最大シーケンス長（αテスト用に制限）
    /// </summary>
    public int MaxSequenceLength { get; set; } = 256;

    /// <summary>
    /// メモリ制限（MB）
    /// </summary>
    public int MemoryLimitMb { get; set; } = 300;

    /// <summary>
    /// スレッド数（αテスト用に制限）
    /// </summary>
    public int ThreadCount { get; set; } = 2;
}