using System;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece.Native;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Local.Onnx;

/// <summary>
/// SentencePieceトークナイザーのファクトリ
/// </summary>
public static class SentencePieceTokenizerFactory
{
    /// <summary>
    /// SentencePieceトークナイザーを作成（Native実装を優先）
    /// </summary>
    /// <param name="modelPath">モデルファイルパス</param>
    /// <param name="name">トークナイザー名</param>
    /// <param name="loggerFactory">ロガーファクトリー</param>
    /// <param name="useTemporary">暫定実装を強制的に使用するか</param>
    /// <param name="useNative">Native実装を優先使用するか</param>
    /// <returns>トークナイザーインスタンス</returns>
    public static ITokenizer Create(
        string modelPath,
        string name,
        ILoggerFactory loggerFactory,
        bool useTemporary = false,
        bool useNative = true)
    {
        if (useTemporary)
        {
#pragma warning disable CS0618 // 型またはメンバーが旧式式です
            var tempLogger = loggerFactory.CreateLogger<TemporarySentencePieceTokenizer>();
            return new TemporarySentencePieceTokenizer(modelPath, name, tempLogger);
#pragma warning restore CS0618
        }

        if (useNative)
        {
            try
            {
                // Native実装を優先使用
                var nativeTokenizer = CreateNativeAsync(modelPath, name, loggerFactory).GetAwaiter().GetResult();
                return nativeTokenizer;
            }
#pragma warning disable CA1031 // 一般的な例外をキャッチしない
            catch (Exception ex)
#pragma warning restore CA1031
            {
                var logger = loggerFactory.CreateLogger(typeof(SentencePieceTokenizerFactory));
                logger.LogWarning(ex, 
                    "Native SentencePieceトークナイザーの作成に失敗しました。RealSentencePieceTokenizerにフォールバックします: {ModelPath}",
                    modelPath);
            }
        }

        try
        {
            // 実際のSentencePieceトークナイザーを試す
            var realLogger = loggerFactory.CreateLogger<RealSentencePieceTokenizer>();
            return new RealSentencePieceTokenizer(modelPath, realLogger);
        }
#pragma warning disable CA1031 // 一般的な例外をキャッチしない
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // フォールバック: 暫定実装を使用
#pragma warning disable CS0618 // 型またはメンバーが旧式式です
            var logger = loggerFactory.CreateLogger<TemporarySentencePieceTokenizer>();
            logger.LogWarning(ex,
                "実際のSentencePieceトークナイザーの作成に失敗しました。暫定実装にフォールバックします: {ModelPath}",
                modelPath);
            
            return new TemporarySentencePieceTokenizer(modelPath, name, logger);
#pragma warning restore CS0618
        }
    }

    /// <summary>
    /// Native SentencePieceトークナイザーを非同期で作成
    /// </summary>
    /// <param name="modelPath">モデルファイルパス</param>
    /// <param name="name">トークナイザー名</param>
    /// <param name="loggerFactory">ロガーファクトリー</param>
    /// <returns>作成されたNativeトークナイザー</returns>
    private static async Task<OpusMtNativeTokenizer> CreateNativeAsync(
        string modelPath,
        string name,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger<OpusMtNativeTokenizer>();
        logger.LogInformation("Native SentencePieceトークナイザーの作成開始: {ModelPath}", modelPath);

        try
        {
            var tokenizer = await OpusMtNativeTokenizer.CreateAsync(modelPath).ConfigureAwait(false);
            logger.LogInformation("Native SentencePieceトークナイザーの作成完了: {Name}", name);
            return tokenizer;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Native SentencePieceトークナイザーの作成に失敗: {ModelPath}", modelPath);
            throw;
        }
    }
}
