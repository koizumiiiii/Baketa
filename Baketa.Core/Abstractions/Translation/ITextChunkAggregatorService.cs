using Baketa.Core.Abstractions.Translation;

namespace Baketa.Core.Abstractions.Translation;

/// <summary>
/// テキストチャンクの時間軸集約を行うサービスのインターフェース
/// Phase 26-1: Clean Architecture修正 - Application層でのInfrastructure層抽象化
/// 翻訳品質向上のための文章ぶつ切り防止機能を提供します
/// </summary>
public interface ITextChunkAggregatorService
{
    /// <summary>
    /// 単一のテキストチャンクをTimedAggregatorに送信して時間軸集約を試行します
    /// </summary>
    /// <param name="chunk">送信するTextChunk</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>チャンク送信成功時: true, 集約無効またはエラー時: false</returns>
    Task<bool> TryAddTextChunkAsync(TextChunk chunk, CancellationToken cancellationToken = default);

    /// <summary>
    /// TimedAggregator機能が有効かどうかを示します
    /// </summary>
    bool IsFeatureEnabled { get; }

    /// <summary>
    /// 現在の集約待機チャンク数を取得します
    /// </summary>
    int PendingChunksCount { get; }
}