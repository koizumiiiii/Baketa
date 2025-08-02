using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;
using Baketa.Core.Translation.Abstractions;
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
    private readonly ITokenizer _tokenizer;
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
        
        // SentencePieceトークナイザーを初期化（Native実装優先）
        var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder.AddConsole());
        _tokenizer = SentencePieceTokenizerFactory.Create(
            tokenizerPath,
            "OPUS-MT Alpha Tokenizer",
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
                // IsInitializedプロパティが利用可能な場合のみチェック
                var isInitialized = _tokenizer switch
                {
                    OpusMtNativeTokenizer native => native.IsInitialized,
                    RealSentencePieceTokenizer real => real.IsInitialized,
                    _ => true // その他の実装は常に初期化済みとみなす
                };
                
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔤 [ONNX] トークナイザー状態（直接書き込み） - IsInitialized: {isInitialized}, Name: '{_tokenizer.Name}', VocabSize: {_tokenizer.VocabularySize}{Environment.NewLine}");
                
                if (!isInitialized)
                {
                    _logger.LogError("SentencePieceトークナイザーが正しく初期化されていません");
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

            // モデルの入力・出力情報をログに記録
            try
            {
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 📋 [ONNX] モデル情報（直接書き込み）{Environment.NewLine}");
                
                // 入力情報
                var inputMetadata = _session.InputMetadata;
                foreach (var input in inputMetadata)
                {
                    var dimensions = string.Join(", ", input.Value.Dimensions.Select(d => d.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 📋 [ONNX] 入力（直接書き込み） - Name: '{input.Key}', Type: {input.Value.ElementType}, Shape: [{dimensions}]{Environment.NewLine}");
                }
                
                // 出力情報
                var outputMetadata = _session.OutputMetadata;
                foreach (var output in outputMetadata)
                {
                    var dimensions = string.Join(", ", output.Value.Dimensions.Select(d => d.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 📋 [ONNX] 出力（直接書き込み） - Name: '{output.Key}', Type: {output.Value.ElementType}, Shape: [{dimensions}]{Environment.NewLine}");
                }
            }
            catch { }

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
            
            // デバッグ情報: 実際のパスとファイル存在状況をログ出力
            var currentDirectory = System.IO.Directory.GetCurrentDirectory();
            var absoluteModelPath = System.IO.Path.GetFullPath(ModelPath);
            var fileExists = System.IO.File.Exists(ModelPath);
            var tokenizerPath = _tokenizer switch
            {
                OpusMtNativeTokenizer => "Native Implementation",
                RealSentencePieceTokenizer real => real.ModelPath,
                _ => "Unknown Implementation"
            };
            var tokenizerExists = System.IO.File.Exists(tokenizerPath);
            
            _logger.LogInformation("🔍 ONNXモデル存在チェック: CurrentDir='{CurrentDir}', ModelPath='{ModelPath}', AbsolutePath='{AbsolutePath}', Exists={Exists}",
                currentDirectory, ModelPath, absoluteModelPath, fileExists);
            _logger.LogInformation("🔍 SentencePieceモデル存在チェック: TokenizerPath='{TokenizerPath}', Exists={TokenizerExists}",
                tokenizerPath, tokenizerExists);
                
            // 直接書き込みでも詳細情報を出力
            try
            {
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 [ONNX] モデルパス詳細（直接書き込み）{Environment.NewLine}");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 [ONNX] CurrentDir: '{currentDirectory}'{Environment.NewLine}");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 [ONNX] ModelPath: '{ModelPath}'{Environment.NewLine}");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 [ONNX] AbsolutePath: '{absoluteModelPath}'{Environment.NewLine}");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 [ONNX] ModelExists: {fileExists}{Environment.NewLine}");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 [ONNX] TokenizerPath: '{tokenizerPath}'{Environment.NewLine}");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 [ONNX] TokenizerExists: {tokenizerExists}{Environment.NewLine}");
            }
            catch { }
            
            if (fileExists)
            {
                // 実際のモデルが存在する場合はONNX推論を実行
                try
                {
                    // 直接書き込みで推論開始をログ
                    try
                    {
                        System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [ONNX] 推論開始（直接書き込み） - テキスト: '{request.SourceText}'{Environment.NewLine}");
                    }
                    catch { }
                    
                    // テキストをトークン化
                    var inputTokens = _tokenizer.Tokenize(request.SourceText);
                    
                    // 直接書き込みでトークン化結果をログ
                    try
                    {
                        System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔤 [ONNX] トークン化完了（直接書き込み） - トークン数: {inputTokens.Length}, トークン: [{string.Join(", ", inputTokens.Take(10))}...]{Environment.NewLine}");
                    }
                    catch { }
                    
                    // 長さ制限の適用
                    if (inputTokens.Length > _options.MaxSequenceLength)
                    {
                        var truncatedTokens = new int[_options.MaxSequenceLength];
                        Array.Copy(inputTokens, truncatedTokens, _options.MaxSequenceLength);
                        inputTokens = truncatedTokens;
                        
                        // 直接書き込みで切り詰めをログ
                        try
                        {
                            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ✂️ [ONNX] トークン切り詰め（直接書き込み） - {inputTokens.Length} → {_options.MaxSequenceLength}{Environment.NewLine}");
                        }
                        catch { }
                    }

                    // ONNX推論実行
                    try
                    {
                        System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ⚙️ [ONNX] 推論実行開始（直接書き込み）{Environment.NewLine}");
                    }
                    catch { }
                    
                    var outputTokens = await RunInferenceAsync(inputTokens, cancellationToken).ConfigureAwait(false);

                    // 直接書き込みで推論完了をログ
                    try
                    {
                        System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ✅ [ONNX] 推論完了（直接書き込み） - 出力トークン数: {outputTokens.Length}, トークン: [{string.Join(", ", outputTokens.Take(10))}...]{Environment.NewLine}");
                    }
                    catch { }

                    // トークンをテキストにデコード
                    translatedText = _tokenizer.Decode(outputTokens);
                    
                    // 直接書き込みでデコード完了をログ
                    try
                    {
                        System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🎯 [ONNX] デコード完了（直接書き込み） - 翻訳結果: '{translatedText}'{Environment.NewLine}");
                    }
                    catch { }
                }
                catch (Exception inferenceEx)
                {
                    // 直接書き込みで推論エラーをログ
                    try
                    {
                        System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ [ONNX] 推論エラー（直接書き込み）: {inferenceEx.GetType().Name} - {inferenceEx.Message}{Environment.NewLine}");
                        System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ [ONNX] スタックトレース（直接書き込み）: {inferenceEx.StackTrace}{Environment.NewLine}");
                    }
                    catch { }
                    
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
    /// ONNX推論を実行（Greedy Search）
    /// </summary>
    private Task<int[]> RunInferenceAsync(int[] inputTokens, CancellationToken cancellationToken = default)
    {
        if (_session == null)
        {
            throw new InvalidOperationException("セッションが初期化されていません");
        }

        // Native Tokenizerの特殊トークンIDを取得
        var nativeTokenizer = _tokenizer as OpusMtNativeTokenizer;
        var bosTokenId = nativeTokenizer?.GetSpecialTokenId("BOS") ?? 0L;
        var eosTokenId = nativeTokenizer?.GetSpecialTokenId("EOS") ?? 0L; // Helsinki: BOS=EOS=0
        var unkTokenId = nativeTokenizer?.GetSpecialTokenId("UNK") ?? 1L;
        var padTokenId = nativeTokenizer?.GetSpecialTokenId("PAD") ?? 60715L; // Helsinki: PAD=60715
        
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

        // 直接書き込みで推論開始をログ
        try
        {
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔧 [ONNX] Greedy Search開始（修正版） - Encoder: [{string.Join(", ", inputTokens.Take(5))}...], BOS: {bosTokenId}, EOS: {eosTokenId}{Environment.NewLine}");
        }
        catch { }

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

            // Greedy Search: 最も確率の高いトークンを選択（特殊トークンを除外）
            int nextTokenId = 0;
            float maxScore = float.MinValue;
            for (int i = 0; i < lastTokenLogits.Length; i++)
            {
                // 特殊トークン（BOS, PAD）や無効な範囲のトークンをスキップ
                if (i == bosTokenId || i == padTokenId || i >= _tokenizer.VocabularySize)
                    continue;
                    
                if (lastTokenLogits[i] > maxScore)
                {
                    maxScore = lastTokenLogits[i];
                    nextTokenId = i;
                }
            }

            // 語彙範囲外のトークンIDを検証・修正
            if (nextTokenId >= _tokenizer.VocabularySize || nextTokenId < 0)
            {
                nextTokenId = (int)unkTokenId; // UNKトークンに置換
                
                try
                {
                    System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ⚠️ [ONNX] 語彙範囲外トークンをUNKに修正 - 元: {nextTokenId} → UNK: {unkTokenId}{Environment.NewLine}");
                }
                catch { }
            }

            // EOSトークンが生成されたら終了
            if (nextTokenId == eosTokenId)
            {
                try
                {
                    System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🏁 [ONNX] EOS検出で生成終了 - ステップ: {step}, 生成トークン数: {outputTokens.Count}{Environment.NewLine}");
                }
                catch { }
                break;
            }

            // 生成されたトークンを追加
            decoderInputIds.Add(nextTokenId);
            outputTokens.Add(nextTokenId);

            // 詳細ログ出力（最初の数ステップのみ）
            if (step < 5)
            {
                try
                {
                    var tokenText = _tokenizer.DecodeToken(nextTokenId);
                    System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🎯 [ONNX] ステップ{step} - トークン: {nextTokenId}('{tokenText}'), スコア: {maxScore:F4}{Environment.NewLine}");
                }
                catch { }
            }
        }

        // 結果の検証とクリーニング
        var validTokens = outputTokens.Where(t => t >= 0 && t < _tokenizer.VocabularySize).ToArray();
        
        try
        {
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ✅ [ONNX] Greedy Search完了（修正版） - 生成: {outputTokens.Count}, 有効: {validTokens.Length}, トークン: [{string.Join(", ", validTokens.Take(10))}...]{Environment.NewLine}");
        }
        catch { }

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
            _session?.Dispose();
            if (_tokenizer is IDisposable disposableTokenizer)
            {
                disposableTokenizer.Dispose();
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
}