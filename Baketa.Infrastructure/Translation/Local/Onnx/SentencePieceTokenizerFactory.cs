using System;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Local.Onnx;

/// <summary>
/// SentencePieceトークナイザーのファクトリ
/// </summary>
public static class SentencePieceTokenizerFactory
{
    /// <summary>
    /// SentencePieceトークナイザーを作成（実装版を優先）
    /// </summary>
    /// <param name="modelPath">モデルファイルパス</param>
    /// <param name="name">トークナイザー名</param>
    /// <param name="loggerFactory">ロガーファクトリー</param>
    /// <param name="useTemporary">暫定実装を強制的に使用するか</param>
    /// <returns>トークナイザーインスタンス</returns>
    public static ITokenizer Create(
        string modelPath,
        string name,
        ILoggerFactory loggerFactory,
        bool useTemporary = false)
    {
        if (useTemporary)
        {
#pragma warning disable CS0618 // 型またはメンバーが旧式式です
            var tempLogger = loggerFactory.CreateLogger<TemporarySentencePieceTokenizer>();
            return new TemporarySentencePieceTokenizer(modelPath, name, tempLogger);
#pragma warning restore CS0618
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
}
