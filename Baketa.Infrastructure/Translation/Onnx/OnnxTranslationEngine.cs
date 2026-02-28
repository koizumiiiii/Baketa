using System.Diagnostics;
using System.IO;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Translation;
using Baketa.Core.Translation.Models;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Baketa.Infrastructure.Translation.Onnx;

/// <summary>
/// NLLB-200 ONNX モデルを使用したローカル翻訳エンジン
/// Python/gRPC サーバー不要で C# のみで推論を実行
/// </summary>
public sealed class OnnxTranslationEngine : TranslationEngineBase
{
    private readonly string _modelDirectory;
    private readonly bool _useKvCache;

    private NllbTokenizer? _tokenizer;
    private InferenceSession? _encoderSession;
    private InferenceSession? _decoderSession;
    private InferenceSession? _decoderWithPastSession;

    private readonly SemaphoreSlim _inferenceLock = new(1, 1);

    /// <summary>
    /// Baketa言語コード → NLLB-200言語コード マッピング
    /// </summary>
    private static readonly Dictionary<string, string> BaketaToNllb = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "eng_Latn",
        ["ja"] = "jpn_Jpan",
        ["zh-CN"] = "zho_Hans",
        ["zh-TW"] = "zho_Hant",
        ["ko"] = "kor_Hang",
        ["fr"] = "fra_Latn",
        ["de"] = "deu_Latn",
        ["es"] = "spa_Latn",
        ["ru"] = "rus_Cyrl",
        ["ar"] = "arb_Arab",
        ["pt"] = "por_Latn",
        ["it"] = "ita_Latn",
        ["nl"] = "nld_Latn",
        ["pl"] = "pol_Latn",
        ["tr"] = "tur_Latn",
        ["vi"] = "vie_Latn",
        ["th"] = "tha_Thai",
        ["id"] = "ind_Latn",
        ["hi"] = "hin_Deva",
    };

    private static readonly IReadOnlyList<LanguagePair> SupportedPairs = BuildSupportedPairs();

    public override string Name => "NLLB-200 ONNX";

    public IReadOnlyList<string> Aliases { get; } = ["NLLB200", "NLLB200-ONNX", "onnx-nllb", "nllb-onnx"];

    public override string Description => "Local NLLB-200 translation via ONNX Runtime (no Python required)";

    public override bool RequiresNetwork => false;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="modelDirectory">ONNX モデルディレクトリパス</param>
    /// <param name="logger">ロガー</param>
    /// <param name="useKvCache">KVキャッシュを使用する（高速化）</param>
    public OnnxTranslationEngine(
        string modelDirectory,
        ILogger<OnnxTranslationEngine> logger,
        bool useKvCache = true)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(modelDirectory);
        _modelDirectory = modelDirectory;
        _useKvCache = useKvCache;
    }

    protected override Task<bool> InitializeInternalAsync()
    {
        try
        {
            // int8量子化モデル（*_quantized.onnx）があればそちらを優先
            var encoderPath = ResolveModelPath("encoder_model");
            var decoderPath = ResolveModelPath("decoder_model");
            var decoderWithPastPath = ResolveModelPath("decoder_with_past_model");

            if (encoderPath == null)
            {
                Logger.LogError("ONNX encoder model not found in: {Dir}", _modelDirectory);
                return Task.FromResult(false);
            }
            if (decoderPath == null)
            {
                Logger.LogError("ONNX decoder model not found in: {Dir}", _modelDirectory);
                return Task.FromResult(false);
            }

            var sessionOptions = CreateSessionOptions();

            var sw = Stopwatch.StartNew();

            _tokenizer = NllbTokenizer.FromDirectory(_modelDirectory);
            Logger.LogInformation("NllbTokenizer loaded: {Elapsed}ms", sw.ElapsedMilliseconds);

            sw.Restart();
            _encoderSession = new InferenceSession(encoderPath, sessionOptions);
            Logger.LogInformation("ONNX encoder session loaded: {Elapsed}ms, model={Model}",
                sw.ElapsedMilliseconds, Path.GetFileName(encoderPath));

            sw.Restart();
            _decoderSession = new InferenceSession(decoderPath, sessionOptions);
            Logger.LogInformation("ONNX decoder session loaded: {Elapsed}ms, model={Model}",
                sw.ElapsedMilliseconds, Path.GetFileName(decoderPath));

            if (_useKvCache && decoderWithPastPath != null)
            {
                sw.Restart();
                _decoderWithPastSession = new InferenceSession(decoderWithPastPath, sessionOptions);
                Logger.LogInformation("ONNX decoder_with_past session loaded: {Elapsed}ms, model={Model}",
                    sw.ElapsedMilliseconds, Path.GetFileName(decoderWithPastPath));
            }

            Logger.LogInformation("OnnxTranslationEngine initialized: model={ModelDir}, kvCache={UseKvCache}",
                _modelDirectory, _useKvCache && _decoderWithPastSession != null);

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to initialize OnnxTranslationEngine");
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// モデルファイルパスを解決（量子化版を優先）
    /// </summary>
    private string? ResolveModelPath(string baseName)
    {
        // 量子化モデルを優先
        var quantizedPath = Path.Combine(_modelDirectory, $"{baseName}_quantized.onnx");
        if (File.Exists(quantizedPath))
        {
            Logger.LogDebug("Using quantized model: {Path}", Path.GetFileName(quantizedPath));
            return quantizedPath;
        }

        var standardPath = Path.Combine(_modelDirectory, $"{baseName}.onnx");
        return File.Exists(standardPath) ? standardPath : null;
    }

    /// <summary>
    /// SessionOptions を作成（CPU実行、INT8量子化モデルに最適化）
    /// </summary>
    private SessionOptions CreateSessionOptions()
    {
        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            InterOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2),
            IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2),
        };

        Logger.LogInformation("[Issue #445] CPU execution provider: InterOp={InterOp}, IntraOp={IntraOp} threads",
            options.InterOpNumThreads, options.IntraOpNumThreads);

        return options;
    }

    public override Task<IReadOnlyCollection<LanguagePair>> GetSupportedLanguagePairsAsync()
    {
        IReadOnlyCollection<LanguagePair> result = SupportedPairs;
        return Task.FromResult(result);
    }

    protected override async Task<TranslationResponse> TranslateInternalAsync(
        TranslationRequest request,
        CancellationToken cancellationToken)
    {
        if (_tokenizer == null || _encoderSession == null || _decoderSession == null)
        {
            return new TranslationResponse
            {
                RequestId = request.RequestId,
                SourceText = request.SourceText,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                EngineName = Name,
                IsSuccess = false,
                Error = new TranslationError
                {
                    ErrorCode = TranslationError.ServiceUnavailable,
                    Message = "OnnxTranslationEngine is not initialized"
                }
            };
        }

        var srcNllb = ToNllbCode(request.SourceLanguage.Code);
        var tgtNllb = ToNllbCode(request.TargetLanguage.Code);

        if (srcNllb == null || tgtNllb == null)
        {
            return new TranslationResponse
            {
                RequestId = request.RequestId,
                SourceText = request.SourceText,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                EngineName = Name,
                IsSuccess = false,
                Error = new TranslationError
                {
                    ErrorCode = TranslationError.UnsupportedLanguagePair,
                    Message = $"Unsupported language: src={request.SourceLanguage.Code}, tgt={request.TargetLanguage.Code}"
                }
            };
        }

        // OnnxRuntime はスレッドセーフではないためロック
        await _inferenceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            _tokenizer.SourceLanguage = srcNllb;
            var inputIds = _tokenizer.Encode(request.SourceText);
            var targetLangTokenId = _tokenizer.GetLanguageTokenId(tgtNllb);

            var outputIds = RunGreedySearch(
                inputIds,
                targetLangTokenId,
                maxLength: 128);

            var translatedText = _tokenizer.Decode(outputIds);

            return new TranslationResponse
            {
                RequestId = request.RequestId,
                SourceText = request.SourceText,
                TranslatedText = translatedText,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                EngineName = Name,
                IsSuccess = true,
                ConfidenceScore = 1.0f,
            };
        }
        finally
        {
            _inferenceLock.Release();
        }
    }

    /// <summary>
    /// グリーディサーチによる seq2seq 推論
    /// </summary>
    private int[] RunGreedySearch(int[] inputIds, int targetLangTokenId, int maxLength = 128)
    {
        var batchSize = 1;
        var seqLen = inputIds.Length;

        // エンコーダ実行
        var inputIdsTensor = new DenseTensor<long>([batchSize, seqLen]);
        var attentionMaskTensor = new DenseTensor<long>([batchSize, seqLen]);
        for (int i = 0; i < seqLen; i++)
        {
            inputIdsTensor[0, i] = inputIds[i];
            attentionMaskTensor[0, i] = 1;
        }

        var encoderInputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIdsTensor),
            NamedOnnxValue.CreateFromTensor("attention_mask", attentionMaskTensor),
        };

        using var encoderResults = _encoderSession!.Run(encoderInputs);
        var encoderOutput = encoderResults.First().AsTensor<float>();

        // デコーダ自己回帰ループ
        var generatedIds = new List<int> { NllbTokenizer.EosTokenId, targetLangTokenId };

        Dictionary<string, DenseTensor<float>>? encoderKvCache = null;
        DisposableResultCollection? previousResults = null;

        try
        {
            for (int step = 0; step < maxLength; step++)
            {
                IReadOnlyCollection<NamedOnnxValue> decoderOutputs;

                var decoderAttention = new DenseTensor<long>([batchSize, seqLen]);
                for (int i = 0; i < seqLen; i++)
                    decoderAttention[0, i] = 1;

                if (step == 0 || _decoderWithPastSession == null)
                {
                    // 初回ステップ: 全デコーダトークンを入力
                    var decoderInputIds = new DenseTensor<long>([batchSize, generatedIds.Count]);
                    for (int i = 0; i < generatedIds.Count; i++)
                        decoderInputIds[0, i] = generatedIds[i];

                    var decoderInputs = new List<NamedOnnxValue>
                    {
                        NamedOnnxValue.CreateFromTensor("input_ids", decoderInputIds),
                        NamedOnnxValue.CreateFromTensor("encoder_attention_mask", decoderAttention),
                    };

                    AddEncoderHiddenStates(decoderInputs, encoderOutput, _decoderSession!);

                    previousResults?.Dispose();
                    var result = _decoderSession!.Run(decoderInputs);
                    previousResults = new DisposableResultCollection(result);
                    decoderOutputs = result;

                    // エンコーダKVキャッシュをクローンして保存
                    if (_decoderWithPastSession != null)
                    {
                        encoderKvCache = new Dictionary<string, DenseTensor<float>>();
                        foreach (var r in previousResults.Results)
                        {
                            if (r.Name.Contains(".encoder.", StringComparison.Ordinal))
                            {
                                encoderKvCache[r.Name] = CloneTensor(r.AsTensor<float>());
                            }
                        }
                    }
                }
                else
                {
                    // 2ステップ目以降: 最後のトークンのみ + KVキャッシュ
                    var lastTokenTensor = new DenseTensor<long>([batchSize, 1]);
                    lastTokenTensor[0, 0] = generatedIds[^1];

                    var decoderInputs = new List<NamedOnnxValue>
                    {
                        NamedOnnxValue.CreateFromTensor("input_ids", lastTokenTensor),
                        NamedOnnxValue.CreateFromTensor("encoder_attention_mask", decoderAttention),
                    };

                    AddEncoderHiddenStates(decoderInputs, encoderOutput, _decoderWithPastSession);

                    // KVキャッシュの受け渡し
                    foreach (var inputName in _decoderWithPastSession.InputMetadata.Keys
                        .Where(n => n.StartsWith("past_key_values", StringComparison.Ordinal)))
                    {
                        var presentName = inputName.Replace("past_key_values", "present", StringComparison.Ordinal);

                        if (inputName.Contains(".encoder.", StringComparison.Ordinal))
                        {
                            if (encoderKvCache!.TryGetValue(presentName, out var cachedTensor))
                                decoderInputs.Add(NamedOnnxValue.CreateFromTensor(inputName, cachedTensor));
                        }
                        else
                        {
                            var value = previousResults!.Results.FirstOrDefault(r => r.Name == presentName);
                            if (value != null)
                                decoderInputs.Add(NamedOnnxValue.CreateFromTensor(inputName, value.AsTensor<float>()));
                        }
                    }

                    // Run 後に Dispose（use-after-free 防止）
                    var result = _decoderWithPastSession.Run(decoderInputs);
                    previousResults?.Dispose();
                    previousResults = new DisposableResultCollection(result);
                    decoderOutputs = result;
                }

                // logits からグリーディ選択
                var logits = decoderOutputs.First(x => x.Name == "logits").AsTensor<float>();
                var vocabSize = logits.Dimensions[^1];
                var lastPos = logits.Dimensions[1] - 1;

                int bestId = 0;
                float bestScore = float.MinValue;
                for (int v = 0; v < vocabSize; v++)
                {
                    var score = logits[0, lastPos, v];
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestId = v;
                    }
                }

                if (bestId == NllbTokenizer.EosTokenId)
                    break;

                generatedIds.Add(bestId);
            }
        }
        finally
        {
            previousResults?.Dispose();
        }

        // 先頭の </s> + target_lang を除いたトークンIDを返す
        return generatedIds.Skip(2).ToArray();
    }

    private static DenseTensor<float> CloneTensor(Tensor<float> source)
    {
        var dims = source.Dimensions.ToArray();
        var clone = new DenseTensor<float>(dims);
        if (source is DenseTensor<float> dense)
            dense.Buffer.CopyTo(clone.Buffer);
        else
            throw new InvalidOperationException($"Expected DenseTensor, got {source.GetType().Name}");
        return clone;
    }

    private static void AddEncoderHiddenStates(
        List<NamedOnnxValue> inputs,
        Tensor<float> encoderOutput,
        InferenceSession session)
    {
        var encoderHiddenName = session.InputMetadata.Keys.FirstOrDefault(n =>
            n.Contains("encoder_hidden", StringComparison.Ordinal)
            || n.Contains("last_hidden", StringComparison.Ordinal)
            || n == "encoder_outputs");

        if (encoderHiddenName != null)
            inputs.Add(NamedOnnxValue.CreateFromTensor(encoderHiddenName, encoderOutput));
    }

    /// <summary>
    /// Baketa言語コード → NLLB言語コード変換
    /// </summary>
    internal static string? ToNllbCode(string baketaCode)
    {
        if (BaketaToNllb.TryGetValue(baketaCode, out var nllb))
            return nllb;

        // NLLB形式がそのまま渡された場合（例: "eng_Latn"）
        if (baketaCode.Contains('_'))
            return baketaCode;

        return null;
    }

    private static IReadOnlyList<LanguagePair> BuildSupportedPairs()
    {
        var languages = new[]
        {
            Language.English,
            Language.Japanese,
            Language.ChineseSimplified,
            Language.ChineseTraditional,
            Language.Korean,
            Language.French,
            Language.German,
            Language.Spanish,
            Language.Russian,
            Language.Arabic,
        };

        var pairs = new List<LanguagePair>();
        foreach (var src in languages)
        {
            foreach (var tgt in languages)
            {
                if (src.Code != tgt.Code)
                    pairs.Add(new LanguagePair { SourceLanguage = src, TargetLanguage = tgt });
            }
        }
        return pairs.AsReadOnly();
    }

    protected override void DisposeManagedResources()
    {
        _encoderSession?.Dispose();
        _decoderSession?.Dispose();
        _decoderWithPastSession?.Dispose();
        _inferenceLock.Dispose();
        base.DisposeManagedResources();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        _encoderSession?.Dispose();
        _decoderSession?.Dispose();
        _decoderWithPastSession?.Dispose();
        _inferenceLock.Dispose();
        await base.DisposeAsyncCore().ConfigureAwait(false);
    }

    /// <summary>
    /// IDisposableResult のコレクション管理
    /// </summary>
    private sealed class DisposableResultCollection : IDisposable
    {
        public IReadOnlyList<DisposableNamedOnnxValue> Results { get; }
        private bool _disposed;

        public DisposableResultCollection(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results)
        {
            Results = [.. results];
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                foreach (var r in Results)
                    r.Dispose();
                _disposed = true;
            }
        }
    }
}
