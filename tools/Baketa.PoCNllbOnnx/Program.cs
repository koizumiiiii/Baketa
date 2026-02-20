// PoC: NLLB-200 ONNX 推論 (C# のみ)
// 目的: Python/gRPC なしで NLLB-200 翻訳モデルを C# から直接実行できることを検証
//
// 使用ライブラリ:
//   - Microsoft.ML.OnnxRuntime: ONNX モデル推論
//   - Microsoft.ML.Tokenizers: SentencePiece トークナイザー
//
// モデル: facebook/nllb-200-distilled-600M (Optimum で ONNX エクスポート済み)

using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Baketa.PoCNllbOnnx;

static class Program
{
    // NLLB-200 の特殊トークン
    const int PadTokenId = 1;
    const int EosTokenId = 2;  // </s>

    // モデルディレクトリ（Optimum エクスポート先）
    static readonly string ModelDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "models", "nllb-200-onnx");

    static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("=== NLLB-200 ONNX PoC (C# Direct Inference) ===\n");

        var modelDir = Path.GetFullPath(ModelDir);
        Console.WriteLine($"モデルディレクトリ: {modelDir}");

        // モデルファイルの存在確認
        var encoderPath = Path.Combine(modelDir, "encoder_model.onnx");
        var decoderPath = Path.Combine(modelDir, "decoder_model.onnx");
        var decoderWithPastPath = Path.Combine(modelDir, "decoder_with_past_model.onnx");

        if (!File.Exists(encoderPath))
        {
            Console.WriteLine($"[ERROR] encoder_model.onnx が見つかりません: {encoderPath}");
            Console.WriteLine("先に Optimum でモデルをエクスポートしてください:");
            Console.WriteLine("  py -3.10 -c \"from optimum.exporters.onnx import main_export; main_export('facebook/nllb-200-distilled-600M', 'models/nllb-200-onnx', task='text2text-generation-with-past')\"");
            return;
        }

        // C# トークナイザーのテスト
        Console.WriteLine("--- C# トークナイザー検証 ---");
        var nllbTokenizer = NllbTokenizer.FromDirectory(modelDir);
        nllbTokenizer.SourceLanguage = "eng_Latn";

        var testDataPath = Path.Combine(modelDir, "test_tokens.json");
        int[]? preTokenizedIds = null;
        int targetLangTokenId = 256079; // jpn_Jpan

        if (File.Exists(testDataPath))
        {
            var json = await File.ReadAllTextAsync(testDataPath);
            var testData = System.Text.Json.JsonSerializer.Deserialize<TestTokenData>(json);
            if (testData != null)
            {
                // C# トークナイザーでエンコード
                var csharpTokens = nllbTokenizer.Encode(testData.SourceText);
                preTokenizedIds = csharpTokens;
                targetLangTokenId = nllbTokenizer.GetLanguageTokenId(testData.TargetLang);

                Console.WriteLine($"  入力: \"{testData.SourceText}\"");
                Console.WriteLine($"  C# トークン: [{string.Join(", ", csharpTokens)}]");
                Console.WriteLine($"  Python トークン: [{string.Join(", ", testData.InputIds)}]");
                var tokensMatch = csharpTokens.SequenceEqual(testData.InputIds);
                Console.WriteLine($"  トークン一致: {tokensMatch}");

                if (!tokensMatch)
                {
                    Console.WriteLine("  [WARN] C# と Python のトークンが異なります。Python のトークンを使用します。");
                    preTokenizedIds = testData.InputIds;
                }
            }
        }

        // Step 1: ONNX セッション作成
        Console.WriteLine("\n--- Step 1: ONNX セッション作成 ---");
        var sw = Stopwatch.StartNew();

        var sessionOptions = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            InterOpNumThreads = Environment.ProcessorCount,
            IntraOpNumThreads = Environment.ProcessorCount,
        };

        using var encoderSession = new InferenceSession(encoderPath, sessionOptions);
        Console.WriteLine($"  エンコーダ読み込み: {sw.ElapsedMilliseconds}ms");
        PrintSessionInfo("Encoder", encoderSession);

        sw.Restart();
        // デコーダ（初回ステップ用 - KVキャッシュなし）
        using var decoderSession = File.Exists(decoderPath)
            ? new InferenceSession(decoderPath, sessionOptions)
            : null;
        if (decoderSession != null)
        {
            Console.WriteLine($"  デコーダ読み込み: {sw.ElapsedMilliseconds}ms");
            PrintSessionInfo("Decoder", decoderSession);
        }

        sw.Restart();
        // デコーダ（KVキャッシュ付き - 2ステップ目以降用）
        using var decoderWithPastSession = File.Exists(decoderWithPastPath)
            ? new InferenceSession(decoderWithPastPath, sessionOptions)
            : null;
        if (decoderWithPastSession != null)
        {
            Console.WriteLine($"  デコーダ(with past)読み込み: {sw.ElapsedMilliseconds}ms");
            PrintSessionInfo("Decoder(with past)", decoderWithPastSession);
        }

        // Step 2: テスト翻訳実行
        Console.WriteLine("\n--- Step 2: テスト翻訳 ---");

        if (preTokenizedIds == null)
        {
            Console.WriteLine("[WARN] test_tokens.json が見つかりません。");
            Console.WriteLine("Python で test_tokens.json を生成してください（generate_test_tokens.py）");
            return;
        }

        var effectiveDecoder = decoderSession ?? decoderWithPastSession
            ?? throw new InvalidOperationException("デコーダモデルが見つかりません。");

        // まず KVキャッシュなしモード（正確性検証用）
        Console.WriteLine("  [モード: KVキャッシュなし - 正確性検証]");
        sw.Restart();
        var outputIdsNoPast = RunGreedySearch(
            encoderSession,
            effectiveDecoder,
            null, // KVキャッシュなし
            preTokenizedIds,
            targetLangTokenId,
            maxLength: 128);
        var noPastTimeMs = sw.ElapsedMilliseconds;
        Console.WriteLine($"  KVなし出力: [{string.Join(", ", outputIdsNoPast)}] ({noPastTimeMs}ms)");

        // 次に KVキャッシュありモード（パフォーマンス検証用）
        Console.WriteLine("\n  [モード: KVキャッシュあり - パフォーマンス検証]");
        sw.Restart();
        var outputIds = RunGreedySearch(
            encoderSession,
            effectiveDecoder,
            decoderWithPastSession,
            preTokenizedIds,
            targetLangTokenId,
            maxLength: 128);
        var translationTimeMs = sw.ElapsedMilliseconds;
        Console.WriteLine($"  KVあり出力: [{string.Join(", ", outputIds)}] ({translationTimeMs}ms)");

        // 比較
        var match = outputIds.SequenceEqual(outputIdsNoPast);
        Console.WriteLine($"\n  KVキャッシュ有無の出力一致: {match}");
        if (!match)
        {
            Console.WriteLine("  [WARN] KVキャッシュモードで出力が異なります。KVなしの結果を採用。");
            outputIds = outputIdsNoPast;
            translationTimeMs = noPastTimeMs;
        }

        Console.WriteLine($"\n  出力トークンIDs: [{string.Join(", ", outputIds)}]");
        Console.WriteLine($"  出力トークン数: {outputIds.Length}");
        Console.WriteLine($"  翻訳時間: {translationTimeMs}ms");

        // C# デトークナイズ
        Console.WriteLine("\n--- C# デトークナイズ検証 ---");
        var translatedText = nllbTokenizer.Decode(outputIds);
        Console.WriteLine($"  C# デコード結果: {translatedText}");

        // トークンIDをファイルに保存
        var outputPath = Path.Combine(modelDir, "csharp_output_ids.json");
        var outputData = new { OutputIds = outputIds, TranslationTimeMs = translationTimeMs, TranslatedText = translatedText };
        await File.WriteAllTextAsync(outputPath,
            System.Text.Json.JsonSerializer.Serialize(outputData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"  出力保存先: {outputPath}");

        Console.WriteLine("\n=== PoC 完了: C# のみで翻訳パイプライン完結 ===");
    }

    /// <summary>
    /// テンソルをマネージドメモリへディープコピー
    /// OnnxRuntime の出力テンソルはネイティブメモリへのビューのため、
    /// Dispose 後に参照するとuse-after-freeになる
    /// </summary>
    static DenseTensor<float> CloneTensor(Tensor<float> source)
    {
        var dims = source.Dimensions.ToArray();
        var clone = new DenseTensor<float>(dims);
        if (source is DenseTensor<float> dense)
        {
            dense.Buffer.CopyTo(clone.Buffer);
        }
        else
        {
            throw new InvalidOperationException($"Expected DenseTensor, got {source.GetType().Name}");
        }
        return clone;
    }

    /// <summary>
    /// グリーディサーチによるseq2seq推論
    /// </summary>
    static int[] RunGreedySearch(
        InferenceSession encoder,
        InferenceSession decoder,
        InferenceSession? decoderWithPast,
        int[] inputIds,
        int targetLangTokenId,
        int maxLength = 128)
    {
        var batchSize = 1;
        var seqLen = inputIds.Length;

        // エンコーダ実行
        Console.WriteLine("  エンコーダ実行中...");
        var inputIdsTensor = new DenseTensor<long>(new[] { batchSize, seqLen });
        var attentionMaskTensor = new DenseTensor<long>(new[] { batchSize, seqLen });
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

        using var encoderResults = encoder.Run(encoderInputs);
        var encoderOutput = encoderResults.First().AsTensor<float>();
        Console.WriteLine($"  エンコーダ出力: shape=[{string.Join(", ", encoderOutput.Dimensions.ToArray())}]");

        // デコーダ自己回帰ループ（グリーディサーチ）
        Console.WriteLine("  デコーダ実行中 (グリーディサーチ)...");
        var generatedIds = new List<int> { EosTokenId, targetLangTokenId }; // </s> + target_lang

        // KVキャッシュ管理:
        //   encoderKvCache: 初回ステップで取得、全ステップで再利用（エンコーダ出力は不変）
        //     → マネージドメモリにクローンして保持（Dispose耐性）
        //   decoderKvCache: 毎ステップ更新（デコーダの自己注意は成長する）
        //     → Run完了後にDispose（Run中はネイティブメモリが有効）
        Dictionary<string, DenseTensor<float>>? encoderKvCache = null;
        DisposableResultCollection? previousResults = null;

        try
        {
            for (int step = 0; step < maxLength; step++)
            {
                IReadOnlyCollection<NamedOnnxValue> decoderOutputs;

                // encoder_attention_mask は全ステップ共通
                var decoderAttention = new DenseTensor<long>(new[] { batchSize, seqLen });
                for (int i = 0; i < seqLen; i++)
                    decoderAttention[0, i] = 1;

                if (step == 0 || decoderWithPast == null)
                {
                    // 初回ステップ: 全デコーダトークンを入力、KVキャッシュなし
                    var decoderInputIds = new DenseTensor<long>(new[] { batchSize, generatedIds.Count });
                    for (int i = 0; i < generatedIds.Count; i++)
                        decoderInputIds[0, i] = generatedIds[i];

                    var decoderInputs = new List<NamedOnnxValue>
                    {
                        NamedOnnxValue.CreateFromTensor("input_ids", decoderInputIds),
                        NamedOnnxValue.CreateFromTensor("encoder_attention_mask", decoderAttention),
                    };

                    AddEncoderHiddenStates(decoderInputs, encoderOutput, decoder);

                    previousResults?.Dispose();
                    var result = decoder.Run(decoderInputs);
                    previousResults = new DisposableResultCollection(result);
                    decoderOutputs = result;

                    // エンコーダKVキャッシュをクローンして保存（初回のみ、以後再利用）
                    // クローンが必要: previousResults.Dispose() でネイティブメモリが解放されるため、
                    // マネージドメモリにコピーしてuse-after-freeを回避
                    if (decoderWithPast != null)
                    {
                        encoderKvCache = new Dictionary<string, DenseTensor<float>>();
                        foreach (var r in previousResults.Results)
                        {
                            if (r.Name.Contains(".encoder."))
                            {
                                encoderKvCache[r.Name] = CloneTensor(r.AsTensor<float>());
                            }
                        }
                        Console.WriteLine($"  エンコーダKVキャッシュ保存: {encoderKvCache.Count} テンソル (クローン済み)");
                    }
                }
                else
                {
                    // 2ステップ目以降: 最後のトークンのみ + KVキャッシュ
                    var lastTokenTensor = new DenseTensor<long>(new[] { batchSize, 1 });
                    lastTokenTensor[0, 0] = generatedIds[^1];

                    var decoderInputs = new List<NamedOnnxValue>
                    {
                        NamedOnnxValue.CreateFromTensor("input_ids", lastTokenTensor),
                        NamedOnnxValue.CreateFromTensor("encoder_attention_mask", decoderAttention),
                    };

                    AddEncoderHiddenStates(decoderInputs, encoderOutput, decoderWithPast);

                    // KVキャッシュの受け渡し
                    var pastInputNames = decoderWithPast.InputMetadata.Keys
                        .Where(n => n.StartsWith("past_key_values", StringComparison.Ordinal))
                        .ToList();

                    foreach (var inputName in pastInputNames)
                    {
                        var presentName = inputName.Replace("past_key_values", "present");

                        if (inputName.Contains(".encoder."))
                        {
                            // エンコーダKVキャッシュ: マネージドメモリにクローン済み → Dispose の影響なし
                            if (encoderKvCache!.TryGetValue(presentName, out var cachedTensor))
                            {
                                decoderInputs.Add(NamedOnnxValue.CreateFromTensor(inputName, cachedTensor));
                            }
                        }
                        else
                        {
                            // デコーダKVキャッシュ: 前ステップの出力を使用
                            var value = previousResults!.Results.FirstOrDefault(r => r.Name == presentName);
                            if (value != null)
                            {
                                decoderInputs.Add(NamedOnnxValue.CreateFromTensor(inputName, value.AsTensor<float>()));
                            }
                        }
                    }

                    // 重要: Run() の後に Dispose する
                    // Run() 中は decoderInputs のテンソル（previousResults のネイティブメモリ参照）が有効である必要がある
                    var result = decoderWithPast.Run(decoderInputs);
                    previousResults?.Dispose(); // 旧結果を解放（Run 完了後なので安全）
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

                if (bestId == EosTokenId)
                {
                    Console.WriteLine($"  EOS検出 (step {step})");
                    break;
                }

                generatedIds.Add(bestId);

                if (step % 10 == 0)
                    Console.Write(".");
            }
        }
        finally
        {
            previousResults?.Dispose();
        }

        Console.WriteLine();

        // 先頭の </s> + target_lang を除いたトークンIDを返す
        return generatedIds.Skip(2).ToArray();
    }

    static void AddEncoderHiddenStates(
        List<NamedOnnxValue> inputs,
        Tensor<float> encoderOutput,
        InferenceSession session)
    {
        // セッションの入力名に合わせてエンコーダ出力を追加
        var inputNames = session.InputMetadata.Keys.ToList();
        var encoderHiddenName = inputNames.FirstOrDefault(n =>
            n.Contains("encoder_hidden") || n.Contains("last_hidden") || n == "encoder_outputs");

        if (encoderHiddenName != null)
        {
            inputs.Add(NamedOnnxValue.CreateFromTensor(encoderHiddenName, encoderOutput));
        }
    }

    static void AddPastKeyValues(
        List<NamedOnnxValue> inputs,
        DisposableResultCollection previousResults,
        InferenceSession session)
    {
        // KVキャッシュの受け渡し:
        //   decoder出力: present.0.decoder.key → decoder_with_past入力: past_key_values.0.decoder.key
        var inputNames = session.InputMetadata.Keys
            .Where(n => n.StartsWith("past_key_values", StringComparison.Ordinal))
            .ToList();

        foreach (var inputName in inputNames)
        {
            // past_key_values.0.decoder.key → present.0.decoder.key
            var presentName = inputName.Replace("past_key_values", "present");
            var value = previousResults.Results.FirstOrDefault(r => r.Name == presentName);
            if (value != null)
            {
                inputs.Add(NamedOnnxValue.CreateFromTensor(inputName, value.AsTensor<float>()));
            }
            else
            {
                Console.WriteLine($"  [WARN] KVキャッシュ未発見: {presentName}");
            }
        }
    }

    static void PrintSessionInfo(string name, InferenceSession session)
    {
        Console.WriteLine($"  [{name}] 入力: {string.Join(", ", session.InputMetadata.Keys)}");
        Console.WriteLine($"  [{name}] 出力: {string.Join(", ", session.OutputMetadata.Keys)}");
    }
}

/// <summary>
/// Python で生成したテストトークンデータ
/// </summary>
record TestTokenData
{
    public string SourceText { get; init; } = "";
    public string TargetLang { get; init; } = "";
    public int[] InputIds { get; init; } = [];
    public int TargetLangTokenId { get; init; }
    public string ExpectedTranslation { get; init; } = "";
}

/// <summary>
/// IDisposableResult のコレクション管理
/// </summary>
sealed class DisposableResultCollection : IDisposable
{
    public IReadOnlyList<DisposableNamedOnnxValue> Results { get; }
    private bool _disposed;

    public DisposableResultCollection(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results)
    {
        Results = results.ToList();
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
