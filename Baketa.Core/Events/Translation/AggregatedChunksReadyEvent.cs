using System;
using System.Collections.Generic;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Events;

namespace Baketa.Core.Events.Translation;

/// <summary>
/// チャンク集約完了イベント
/// TimedChunkAggregatorが時間軸集約を完了し、翻訳準備が整ったことを通知
/// Phase 12.2: 2重翻訳アーキテクチャ排除の一環として実装
/// </summary>
public sealed class AggregatedChunksReadyEvent : EventBase
{
    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="aggregatedChunks">集約されたテキストチャンクのリスト</param>
    /// <param name="sourceWindowHandle">ソースウィンドウハンドル</param>
    /// <exception cref="ArgumentNullException">aggregatedChunksがnullの場合</exception>
    public AggregatedChunksReadyEvent(
        IReadOnlyList<TextChunk> aggregatedChunks,
        IntPtr sourceWindowHandle)
    {
        ArgumentNullException.ThrowIfNull(aggregatedChunks, nameof(aggregatedChunks));

        AggregatedChunks = aggregatedChunks;
        SourceWindowHandle = sourceWindowHandle;
        AggregationCompletedAt = DateTime.UtcNow;
        SessionId = Guid.NewGuid().ToString("N")[..8];
    }

    /// <summary>
    /// 集約されたテキストチャンクのリスト
    /// </summary>
    public IReadOnlyList<TextChunk> AggregatedChunks { get; }

    /// <summary>
    /// ソースウィンドウハンドル
    /// </summary>
    public IntPtr SourceWindowHandle { get; }

    /// <summary>
    /// 集約完了タイムスタンプ
    /// </summary>
    public DateTime AggregationCompletedAt { get; }

    /// <summary>
    /// セッションID（トレーシング用）
    /// </summary>
    public string SessionId { get; }

    /// <inheritdoc />
    public override string Name => "AggregatedChunksReady";

    /// <inheritdoc />
    public override string Category => "Translation";
}
