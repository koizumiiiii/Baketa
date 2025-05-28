using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Baketa.Infrastructure.Translation.Local.Onnx;

/// <summary>
/// OPUS-MT ONNX モデルを使用した翻訳エンジンの実装
/// </summary>
public class OpusMtOnnxEngine : OnnxTranslationEngine
{
    private readonly Baketa.Core.Translation.Models.IModelLoader _modelLoader;
    private readonly Baketa.Core.Translation.Models.ITokenizer _tokenizer;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="modelPath">ONNX モデルファイルのパス</param>
    /// <param name="tokenizerPath">SentencePiece トークナイザーモデルのパス</param>
    /// <param name="languagePair">言語ペア</param>
    /// <param name="options">翻訳オプション</param>
    /// <param name="loggerFactory">ロガーファクトリー</param>
    public OpusMtOnnxEngine(
        string modelPath,
        string tokenizerPath,
        LanguagePair languagePair,
        OnnxTranslationOptions options,
        ILoggerFactory loggerFactory)
        : base(
            modelPath,
            languagePair,
#pragma warning disable CA2000 // オブジェクトは基底クラスに渡され適切に管理される
            new OnnxModelLoader(loggerFactory.CreateLogger<OnnxModelLoader>()),
            CreateTokenizer(tokenizerPath, loggerFactory),
#pragma warning restore CA2000
            options,
            loggerFactory.CreateLogger<OpusMtOnnxEngine>())
    {
        Logger = loggerFactory.CreateLogger<OpusMtOnnxEngine>();
        _modelLoader = GetModelLoader();
        _tokenizer = GetTokenizer();
    }

    /// <inheritdoc/>
    public override string Name => "OPUS-MT ONNX Engine";

    /// <inheritdoc/>
    public override string Description => "Helsinki-NLP OPUS-MT モデルを使用したONNX翻訳エンジン";

    /// <summary>
    /// SentencePiece トークナイザーを作成
    /// </summary>
    /// <param name="tokenizerPath">トークナイザーモデルのパス</param>
    /// <param name="loggerFactory">ロガーファクトリー</param>
    /// <returns>トークナイザーインスタンス</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859:可能な場合は具象型を使用します", Justification = "将来的に異なる実装を返す可能性があるため、インターフェース型を使用")]
    private static Baketa.Core.Translation.Models.ITokenizer CreateTokenizer(string tokenizerPath, ILoggerFactory loggerFactory)
    {
#pragma warning disable CS0618 // 型またはメンバーが旧式式です
        var tokenizer = new TemporarySentencePieceTokenizer(
            tokenizerPath,
            "OPUS-MT SentencePiece",
            loggerFactory.CreateLogger<TemporarySentencePieceTokenizer>());
#pragma warning restore CS0618

        if (!tokenizer.Initialize())
        {
            throw new InvalidOperationException($"SentencePiece トークナイザーの初期化に失敗しました: {tokenizerPath}");
        }

        return tokenizer;
    }

    /// <inheritdoc/>
    protected override async Task<int[]> TranslateTokensAsync(int[] inputTokens, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputTokens, nameof(inputTokens));

        if (!_modelLoader.IsModelLoaded())
        {
            throw new InvalidOperationException("ONNX モデルがロードされていません");
        }

        try
        {
            // 入力テンソルの準備
            var inputTensor = await CreateInputTensorAsync(inputTokens, cancellationToken).ConfigureAwait(false);
            var inputs = new List<NamedOnnxValue> { inputTensor };

            IDisposableReadOnlyCollection<DisposableNamedOnnxValue>? outputs = null;
            try
            {
                // ONNX 推論の実行
                // OnnxModelLoaderのRunメソッドはIModelLoaderインターフェースに含まれていないため、
                // キャストしてアクセス
                if (_modelLoader is not OnnxModelLoader onnxLoader)
                {
                    throw new InvalidOperationException("ModelLoaderがOnnxModelLoaderではありません");
                }
                outputs = await Task.Run(() => onnxLoader.Run(inputs), cancellationToken).ConfigureAwait(false);

                // 出力テンソルから結果を抽出
                var outputTokens = await ExtractOutputTokensAsync(outputs, cancellationToken).ConfigureAwait(false);

                return outputTokens;
            }
            finally
            {
                outputs?.Dispose();
                // inputTensorはNamedOnnxValueなのでDispose不要
            }
        }
        catch (OperationCanceledException)
        {
            throw; // キャンセルは再スロー
        }
        catch (OnnxRuntimeException ex)
        {
            throw new InvalidOperationException($"ONNX 推論実行中にエラーが発生しました: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"翻訳処理中に予期しないエラーが発生しました: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 入力テンソルを作成
    /// </summary>
    /// <param name="inputTokens">入力トークン配列</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>入力テンソル</returns>
    private async Task<NamedOnnxValue> CreateInputTensorAsync(int[] inputTokens, CancellationToken cancellationToken)
    {
        await Task.Delay(1, cancellationToken).ConfigureAwait(false); // 非同期処理をシミュレート

        try
        {
            // OPUS-MT モデルの入力形式に合わせてテンソルを作成
            // 通常は [batch_size, sequence_length] の形状
            var batchSize = 1;
            var sequenceLength = inputTokens.Length;

            // 入力トークンを long 型の配列に変換（ONNX モデルの要求に応じて）
            var inputData = inputTokens.Select(token => (long)token).ToArray();

            // テンソルの作成
            var tensor = new DenseTensor<long>(inputData, [batchSize, sequenceLength]);

            return NamedOnnxValue.CreateFromTensor("input_ids", tensor);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"入力テンソルの作成中にエラーが発生しました: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 出力テンソルから結果トークンを抽出
    /// </summary>
    /// <param name="outputs">ONNX 推論の出力</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>出力トークン配列</returns>
    private async Task<int[]> ExtractOutputTokensAsync(
        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs, 
        CancellationToken cancellationToken)
    {
        await Task.Delay(1, cancellationToken).ConfigureAwait(false); // 非同期処理をシミュレート

        try
        {
            // 出力テンソルから結果を取得
            // OPUS-MT モデルの出力は通常 "output" または "last_hidden_state" という名前
            var outputTensor = outputs.FirstOrDefault(o => 
                o.Name == "output" || 
                o.Name == "last_hidden_state" || 
                o.Name == "logits");

            if (outputTensor?.Value is not Tensor<long> longTensor)
            {
                // float テンソルの場合は argmax で最大値のインデックスを取得
                if (outputTensor?.Value is Tensor<float> floatTensor)
                {
                    return ExtractTokensFromLogits(floatTensor);
                }

                throw new InvalidOperationException("適切な出力テンソルが見つかりません");
            }

            // long テンソルから直接トークンを抽出
            List<int> resultTokens = [];
            for (int i = 0; i < longTensor.Length; i++)
            {
                var token = (int)longTensor.GetValue(i);
                
                // 終了トークンの場合は処理を終了
                // SpecialTokensを取得するためにキャスト
#pragma warning disable CS0618 // 型またはメンバーが旧式式です
                if (_tokenizer is not TemporarySentencePieceTokenizer tempTokenizer)
                {
                    throw new InvalidOperationException("TokenizerがTemporarySentencePieceTokenizerではありません");
                }
                var specialTokens = tempTokenizer.GetSpecialTokens();
#pragma warning restore CS0618
                if (token == specialTokens.EndOfSentenceId)
                {
                    break;
                }

                // パディングトークンはスキップ
                if (token != specialTokens.PaddingId && token != specialTokens.UnknownId)
                {
                    resultTokens.Add(token);
                }
            }

#pragma warning disable IDE0305 // コレクションの初期化を簡素化 - 条件付きループのため適用不可
            return resultTokens.ToArray();
#pragma warning restore IDE0305
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"出力テンソルの処理中にエラーが発生しました: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// ロジットテンソルから最大確率のトークンを抽出
    /// </summary>
    /// <param name="logitsTensor">ロジットテンソル</param>
    /// <returns>抽出されたトークン配列</returns>
    private int[] ExtractTokensFromLogits(Tensor<float> logitsTensor)
    {
        try
        {
            var shape = logitsTensor.Dimensions.ToArray();
            if (shape.Length < 2)
            {
                throw new InvalidOperationException($"ロジットテンソルの次元が不正です: {shape.Length}");
            }

            var sequenceLength = shape[^2]; // 最後から2番目の次元（シーケンス長）
            var vocabSize = shape[^1];      // 最後の次元（語彙サイズ）

            List<int> resultTokens = [];
            // SpecialTokensを取得するためにキャスト
#pragma warning disable CS0618 // 型またはメンバーが旧式式です
            if (_tokenizer is not TemporarySentencePieceTokenizer tempTokenizer)
            {
                throw new InvalidOperationException("TokenizerがTemporarySentencePieceTokenizerではありません");
            }
            var specialTokens = tempTokenizer.GetSpecialTokens();
#pragma warning restore CS0618

            for (int seq = 0; seq < sequenceLength; seq++)
            {
                // 各位置での最大確率のトークンを見つける
                int maxTokenId = 0;
                float maxLogit = float.MinValue;

                for (int vocab = 0; vocab < vocabSize; vocab++)
                {
                    var logitValue = logitsTensor[0, seq, vocab]; // [batch, seq, vocab]
                    if (logitValue > maxLogit)
                    {
                        maxLogit = logitValue;
                        maxTokenId = vocab;
                    }
                }

                // 終了トークンの場合は処理を終了
                if (maxTokenId == specialTokens.EndOfSentenceId)
                {
                    break;
                }

                // パディングトークンや未知トークンはスキップ
                if (maxTokenId != specialTokens.PaddingId && maxTokenId != specialTokens.UnknownId)
                {
                    resultTokens.Add(maxTokenId);
                }
            }

#pragma warning disable IDE0305 // コレクションの初期化を簡素化 - 条件付きループのため適用不可
            return resultTokens.ToArray();
#pragma warning restore IDE0305
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"ロジット処理中にエラーが発生しました: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 前処理: 入力テキストの言語固有の調整
    /// </summary>
    /// <param name="text">入力テキスト</param>
    /// <returns>前処理されたテキスト</returns>
    protected string PreprocessText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        try
        {
            // OPUS-MT モデル固有の前処理
            // 言語ペアに応じた前処理を実装

            // 基本的な正規化
            var processed = text.Trim();

            // 言語固有の前処理
            if (ModelLanguagePair.SourceLanguage.Code.Equals("ja", StringComparison.OrdinalIgnoreCase))
            {
                // 日本語の前処理
                processed = NormalizeJapaneseText(processed);
            }
            else if (ModelLanguagePair.SourceLanguage.Code.Equals("en", StringComparison.OrdinalIgnoreCase))
            {
                // 英語の前処理
                processed = NormalizeEnglishText(processed);
            }

            return processed;
        }
#pragma warning disable CA1031 // 一般的な例外をキャッチしない
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "テキスト前処理中にエラーが発生しました。元のテキストを使用します");
            return text;
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// 日本語テキストの正規化
    /// </summary>
    /// <param name="text">日本語テキスト</param>
    /// <returns>正規化されたテキスト</returns>
    private string NormalizeJapaneseText(string text)
    {
        // 基本的な日本語正規化
        // 実際の実装では、より詳細な正規化が必要
        return text
            .Replace("　", " ", StringComparison.Ordinal)    // 全角スペースを半角に
            .Replace("。", ".", StringComparison.Ordinal)    // 全角句点を半角ピリオドに
            .Replace("、", ",", StringComparison.Ordinal);   // 全角読点を半角カンマに
    }

    /// <summary>
    /// 英語テキストの正規化
    /// </summary>
    /// <param name="text">英語テキスト</param>
    /// <returns>正規化されたテキスト</returns>
    private string NormalizeEnglishText(string text)
    {
        // 基本的な英語正規化
        return text
            .Replace("\u201C", "\"", StringComparison.Ordinal)   // 左ダブルクォートを標準に
            .Replace("\u201D", "\"", StringComparison.Ordinal)   // 右ダブルクォートを標準に
            .Replace("\u2018", "'", StringComparison.Ordinal)     // 左シングルクォートを標準に
            .Replace("\u2019", "'", StringComparison.Ordinal);    // 右シングルクォートを標準に
    }

    /// <summary>
    /// Logger プロパティ（protected アクセス用）
    /// </summary>
    protected ILogger<OpusMtOnnxEngine> Logger { get; }

    /// <inheritdoc/>
    protected override void DisposeManagedResources()
    {
        try
        {
            // IDisposableの場合のみ破棄
            if (_tokenizer is IDisposable disposableTokenizer)
            {
                disposableTokenizer.Dispose();
            }
            if (_modelLoader is IDisposable disposableLoader)
            {
                disposableLoader.Dispose();
            }
        }
        finally
        {
            base.DisposeManagedResources();
        }
    }

    /// <inheritdoc/>
    protected override async ValueTask DisposeAsyncCore()
    {
        try
        {
            // IDisposableの場合のみ破棄
            if (_tokenizer is IDisposable disposableTokenizer)
            {
                disposableTokenizer.Dispose();
            }
            if (_modelLoader is IDisposable disposableLoader)
            {
                disposableLoader.Dispose();
            }
        }
        finally
        {
            await base.DisposeAsyncCore().ConfigureAwait(false);
        }
    }
}
