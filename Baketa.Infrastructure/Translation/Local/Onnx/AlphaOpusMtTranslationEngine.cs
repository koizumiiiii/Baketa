using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;
using Baketa.Core.Translation.Abstractions;
using Baketa.Core.Performance;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece.Native;
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
    private readonly ITokenizer _sourceTokenizer;
    private readonly ITokenizer _targetTokenizer;
    private InferenceSession? _session;
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
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
    /// <param name="sourceTokenizerPath">ソース言語SentencePieceモデルのパス</param>
    /// <param name="languagePair">言語ペア</param>
    /// <param name="options">オプション</param>
    /// <param name="logger">ロガー</param>
    public AlphaOpusMtTranslationEngine(
        string modelPath,
        string sourceTokenizerPath,
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
        
        // SentencePieceトークナイザーを初期化（Native実装優先）
        var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder.AddConsole());
        
        // ソース用トークナイザー（日本語入力処理用）
        _sourceTokenizer = SentencePieceTokenizerFactory.Create(
            sourceTokenizerPath,
            "OPUS-MT Alpha Source Tokenizer",
            loggerFactory,
            useTemporary: false,
            useNative: true);
            
        // ターゲット用トークナイザー（英語出力処理用）
        // 複数のパターンを試行してtarget.spmファイルを見つける
        
        var sourceDir = Path.GetDirectoryName(sourceTokenizerPath) ?? "";
        var modelsBaseDir = Path.GetDirectoryName(sourceDir) ?? "";
        var officialHelsinkiDir = Path.Combine(modelsBaseDir, "Official_Helsinki");
        var officialTargetPath = Path.Combine(officialHelsinkiDir, "target.spm");
        
        string targetTokenizerPath;
        
        _logger.LogInformation("Target.spm 検索開始");
        _logger.LogInformation("ソースファイル: {SourcePath}", sourceTokenizerPath);
        _logger.LogInformation("公式Helsinkiディレクトリ: {OfficialDir}", officialHelsinkiDir);
        _logger.LogInformation("公式ターゲットファイル: {OfficialTargetPath}", officialTargetPath);
        
        if (File.Exists(officialTargetPath))
        {
            targetTokenizerPath = officialTargetPath;
            _logger.LogInformation("✅ 公式Helsinkiターゲットトークナイザーを使用: {TargetPath}", targetTokenizerPath);
        }
        else
        {
            _logger.LogWarning("❌ target.spmが見つかりません。ソーストークナイザーを代用します");
            _logger.LogWarning("検索したパス: {OfficialTargetPath}", officialTargetPath);
            targetTokenizerPath = sourceTokenizerPath; // フォールバック
        }
        
        _targetTokenizer = SentencePieceTokenizerFactory.Create(
            targetTokenizerPath,
            "OPUS-MT Alpha Target Tokenizer",
            loggerFactory,
            useTemporary: false,
            useNative: true);
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
            try
            {
                // ソーストークナイザーの初期化チェック
                var sourceInitialized = _sourceTokenizer switch
                {
                    OpusMtNativeTokenizer native => native.IsInitialized,
                    RealSentencePieceTokenizer real => real.IsInitialized,
                    _ => true
                };
                
                // ターゲットトークナイザーの初期化チェック
                var targetInitialized = _targetTokenizer switch
                {
                    OpusMtNativeTokenizer native => native.IsInitialized,
                    RealSentencePieceTokenizer real => real.IsInitialized,
                    _ => true
                };
                
                if (!sourceInitialized || !targetInitialized)
                {
                    _logger.LogError("SentencePieceトークナイザーが正しく初期化されていません (Source: {Source}, Target: {Target})", 
                        sourceInitialized, targetInitialized);
                    return Task.FromResult(false);
                }
            }
            catch { }

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

        using var translationMeasurement = new PerformanceMeasurement(
            MeasurementType.TranslationEngineExecution, 
            $"OPUS-MT翻訳処理 - テキスト:'{request.SourceText}' ({request.SourceText.Length}文字)")
            .WithAdditionalInfo($"Model:{System.IO.Path.GetFileName(ModelPath)}");

        try
        {
            // αテスト向けの簡易翻訳実装
            // 実際のOPUS-MTモデルが利用可能な場合は、本格的な推論を実行
            // 利用不可の場合は、テスト用の簡易翻訳を実行
            
            string translatedText;
            
            // ONNXファイルの存在を確認し、エラーがあれば例外を投げる
            if (!System.IO.File.Exists(ModelPath))
            {
                throw new FileNotFoundException($"ONNXモデルファイルが見つかりません: {ModelPath}");
            }
            
            // ONNX推論を強制実行（フォールバックなし）
            try
            {
                // テキストをトークン化（ソーストークナイザー使用）
                using var tokenizationMeasurement = new PerformanceMeasurement(
                    MeasurementType.SentencePieceTokenization, 
                    $"SentencePiece トークン化 - テキスト:'{request.SourceText}'");
                    
                var inputTokens = _sourceTokenizer.Tokenize(request.SourceText);
                var tokenizerResult = tokenizationMeasurement.Complete();
                
                _logger.LogDebug("入力テキスト '{SourceText}' をソーストークナイザーでトークン化: [{Tokens}] ({Duration}ms)", 
                    request.SourceText, string.Join(", ", inputTokens), tokenizerResult.Duration.TotalMilliseconds);
                
                // 長さ制限の適用
                if (inputTokens.Length > _options.MaxSequenceLength)
                {
                    var truncatedTokens = new int[_options.MaxSequenceLength];
                    Array.Copy(inputTokens, truncatedTokens, _options.MaxSequenceLength);
                    inputTokens = truncatedTokens;
                    _logger.LogDebug("トークン列を{MaxLength}に切り詰めました", _options.MaxSequenceLength);
                }

                // ONNX推論実行
                using var inferenceMeasurement = new PerformanceMeasurement(
                    MeasurementType.OnnxInference, 
                    $"ONNX推論実行 - トークン数:{inputTokens.Length}");
                    
                var outputTokens = await RunInferenceAsync(inputTokens, cancellationToken).ConfigureAwait(false);
                var inferenceResult = inferenceMeasurement.Complete();
                
                _logger.LogDebug("ONNX推論出力トークン: [{OutputTokens}] ({Duration}ms)", 
                    string.Join(", ", outputTokens), inferenceResult.Duration.TotalMilliseconds);

                // トークンをテキストにデコード（ターゲットトークナイザー使用）
                using var decodingMeasurement = new PerformanceMeasurement(
                    MeasurementType.SentencePieceTokenization, 
                    $"SentencePiece デコード - トークン数:{outputTokens.Length}");
                    
                translatedText = _targetTokenizer.Decode(outputTokens);
                var decodingResult = decodingMeasurement.Complete();
                
                _logger.LogDebug("出力トークン [{OutputTokens}] をターゲットトークナイザーでデコード: '{TranslatedText}' ({Duration}ms)", 
                    string.Join(", ", outputTokens), translatedText, decodingResult.Duration.TotalMilliseconds);
                
                var totalResult = translationMeasurement.Complete();
                _logger.LogInformation("ONNX推論による翻訳完了: '{SourceText}' -> '{TranslatedText}' (総時間:{Duration}ms)", 
                    request.SourceText, translatedText, totalResult.Duration.TotalMilliseconds);
            }
            catch (Exception inferenceEx)
            {
                _logger.LogError(inferenceEx, "ONNX推論に失敗しました: {ModelPath}", ModelPath);
                throw new InvalidOperationException($"ONNX推論エラー: {inferenceEx.Message}", inferenceEx);
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
        return _sourceTokenizer; // デフォルトではソーストークナイザーを返す
    }
    
    /// <summary>
    /// ターゲットトークナイザーを取得
    /// </summary>
    /// <returns>ターゲット言語用のトークナイザー</returns>
    public ITokenizer GetTargetTokenizer()
    {
        return _targetTokenizer;
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
    /// ONNX推論を実行（Greedy Search）
    /// </summary>
    private async Task<int[]> RunInferenceAsync(int[] inputTokens, CancellationToken cancellationToken = default)
    {
        if (_session == null)
        {
            throw new InvalidOperationException("セッションが初期化されていません");
        }
        
        // InferenceSessionへのアクセスをシリアライズして、スレッドセーフを保証
        await _sessionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RunInferenceInternalAsync(inputTokens, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    /// <summary>
    /// ONNX推論の内部実装（ロック内で実行）
    /// </summary>
    private Task<int[]> RunInferenceInternalAsync(int[] inputTokens, CancellationToken cancellationToken = default)
    {

        // Native Tokenizerの特殊トークンIDを取得（ソーストークナイザーから）
        var nativeSourceTokenizer = _sourceTokenizer as OpusMtNativeTokenizer;
        var bosTokenId = nativeSourceTokenizer?.GetSpecialTokenId("BOS") ?? 0L;
        var eosTokenId = nativeSourceTokenizer?.GetSpecialTokenId("EOS") ?? 0L; // Helsinki: BOS=EOS=0
        var unkTokenId = nativeSourceTokenizer?.GetSpecialTokenId("UNK") ?? 1L;
        var padTokenId = nativeSourceTokenizer?.GetSpecialTokenId("PAD") ?? 60715L; // Helsinki: PAD=60715
        
        // HelsinkiモデルのEOSが無効(-1)の場合はBOSと同じ値を使用
        if (eosTokenId < 0) eosTokenId = bosTokenId;

        // エンコーダー入力テンソルの作成
        var encoderInputTensor = new DenseTensor<long>(
            inputTokens.Select(t => (long)t).ToArray(),
            [1, inputTokens.Length]);

        // アテンションマスクの作成（全て1で有効なトークンを示す）
        var attentionMask = new long[inputTokens.Length];
        Array.Fill(attentionMask, 1L);
        var attentionMaskTensor = new DenseTensor<long>(
            attentionMask,
            [1, inputTokens.Length]);

        var decoderInputIds = new List<long> { bosTokenId };
        var outputTokens = new List<int>();
        
        const int maxLength = 100; // 最大生成長


        // Greedy Search ループ
        for (int step = 0; step < maxLength; step++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            // デコーダー入力テンソルの作成（現在の生成済みシーケンス）
            var decoderInputTensor = new DenseTensor<long>(
                decoderInputIds.ToArray(),
                [1, decoderInputIds.Count]);

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", encoderInputTensor),
                NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor),
                NamedOnnxValue.CreateFromTensor("decoder_input_ids", decoderInputTensor)
            };

            _logger.LogDebug("ステップ {Step}: デコーダー入力 [{DecoderInput}]", 
                step, string.Join(", ", decoderInputIds));

            // 推論実行
            using var results = _session.Run(inputs);
            
            // 出力の取得（logitsテンソル）
            var outputResult = results.FirstOrDefault(r => r.Name == "output") 
                ?? throw new InvalidOperationException("'output'という名前の推論結果が見つかりません");
            
            // logitsをfloat型として取得
            var logitsTensor = outputResult.AsTensor<float>() 
                ?? throw new InvalidOperationException("推論結果をFloat Tensorに変換できませんでした");

            // 最後のトークン位置のlogitsを取得
            var lastTokenLogits = new float[logitsTensor.Dimensions[2]]; // vocab_size
            for (int i = 0; i < lastTokenLogits.Length; i++)
            {
                lastTokenLogits[i] = logitsTensor[0, decoderInputIds.Count - 1, i];
            }

            // Greedy Search with Repetition Penalty: 最も確率の高いトークンを選択
            int nextTokenId = 0;
            float maxScore = float.MinValue;
            
            for (int i = 0; i < lastTokenLogits.Length; i++)
            {
                // 語彙範囲外のトークンをスキップ（ターゲット語彙サイズで判定）
                if (i >= _targetTokenizer.VocabularySize)
                    continue;
                
                // Helsinki OPUS-MT専用の特殊トークン処理
                bool shouldSkip = false;
                
                if (bosTokenId == eosTokenId && i == bosTokenId)
                {
                    // Helsinki OPUS-MT: BOS=EOS=0の場合
                    // 生成の最初の数ステップではBOS/EOSトークンを完全に除外
                    // これにより即座に終了することを防ぐ
                    if (step < 3) // 最初の3ステップは特殊トークンを除外
                    {
                        shouldSkip = true;
                    }
                    else
                    {
                        // 3ステップ目以降は終了判定として許可
                        shouldSkip = false;
                    }
                }
                else if (i == bosTokenId)
                {
                    // 通常のBOSトークンは生成対象から除外
                    shouldSkip = true;
                }
                else if (i == padTokenId)
                {
                    // PADトークンは常に除外
                    shouldSkip = true;
                }
                
                if (shouldSkip)
                    continue;
                
                // スコアを取得し、Repetition Penaltyを適用
                float score = lastTokenLogits[i];
                
                // Repetition Penalty: 既に生成されたトークンのスコアを減点
                if (_options.RepetitionPenalty > 1.0f && outputTokens.Contains(i))
                {
                    score /= _options.RepetitionPenalty;
                    _logger.LogDebug("繰り返しペナルティ適用: トークン{TokenId} スコア{OriginalScore:F3} -> {PenalizedScore:F3}",
                        i, lastTokenLogits[i], score);
                }
                    
                if (score > maxScore)
                {
                    maxScore = score;
                    nextTokenId = i;
                }
            }

            _logger.LogDebug("ステップ {Step}: 選択されたトークンID {TokenId} (スコア: {Score})", 
                step, nextTokenId, maxScore);

            // 語彙範囲外のトークンIDを検証・修正（ターゲット語彙サイズで判定）
            if (nextTokenId >= _targetTokenizer.VocabularySize || nextTokenId < 0)
            {
                _logger.LogWarning("語彙範囲外のトークン {TokenId} を UNK {UnkTokenId} に置換", 
                    nextTokenId, unkTokenId);
                nextTokenId = (int)unkTokenId; // UNKトークンに置換
            }

            // EOSトークンが生成されたら終了（Helsinki OPUS-MT対応）
            if (nextTokenId == eosTokenId && step >= 3) // 最初の3ステップはEOS判定をスキップ
            {
                _logger.LogDebug("EOS トークン {EosTokenId} が生成されました（ステップ {Step}）。生成を終了します。", eosTokenId, step);
                break;
            }

            // 生成されたトークンを追加
            decoderInputIds.Add(nextTokenId);
            outputTokens.Add(nextTokenId);
        }

        // 結果の検証とクリーニング（ターゲット語彙サイズで判定）
        var validTokens = outputTokens.Where(t => t >= 0 && t < _targetTokenizer.VocabularySize).ToArray();
        

        return Task.FromResult(validTokens);
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
                _ => text
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
                _ => text
            },
            
            _ => sourceText
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
            _sessionLock?.Dispose();
            _session?.Dispose();
            if (_sourceTokenizer is IDisposable disposableSourceTokenizer)
            {
                disposableSourceTokenizer.Dispose();
            }
            if (_targetTokenizer is IDisposable disposableTargetTokenizer)
            {
                disposableTargetTokenizer.Dispose();
            }
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

    /// <summary>
    /// 繰り返しペナルティ（1.0=無効、1.2推奨）
    /// 同じトークンの連続生成を抑制して翻訳品質を向上
    /// </summary>
    public float RepetitionPenalty { get; set; } = 1.2f;
}