using System;
using System.Collections.Generic;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Events;
using Baketa.Core.Translation.Abstractions; // [Issue #290] FallbackTranslationResult用

namespace Baketa.Core.Events.Translation;

/// <summary>
/// チャンク集約完了イベント
/// TimedChunkAggregatorが時間軸集約を完了し、翻訳準備が整ったことを通知
/// Phase 12.2: 2重翻訳アーキテクチャ排除の一環として実装
/// </summary>
/// <remarks>
/// Issue #78 Phase 4: Cloud AI翻訳統合
/// - ImageBase64, ImageWidth, ImageHeight を追加（並列翻訳用）
/// - 既存の引数2つのコンストラクタは後方互換性のため維持
///
/// Issue #290: Fork-Join並列実行
/// - PreComputedCloudResult を追加（OCRと並列実行されたCloud AI翻訳結果）
/// </remarks>
public sealed class AggregatedChunksReadyEvent : EventBase
{
    /// <summary>
    /// コンストラクタ（後方互換性のため維持）
    /// </summary>
    /// <param name="aggregatedChunks">集約されたテキストチャンクのリスト</param>
    /// <param name="sourceWindowHandle">ソースウィンドウハンドル</param>
    /// <exception cref="ArgumentNullException">aggregatedChunksがnullの場合</exception>
    public AggregatedChunksReadyEvent(
        IReadOnlyList<TextChunk> aggregatedChunks,
        IntPtr sourceWindowHandle)
        : this(aggregatedChunks, sourceWindowHandle, null, 0, 0)
    {
    }

    /// <summary>
    /// コンストラクタ（Cloud AI翻訳用画像データ付き）
    /// </summary>
    /// <param name="aggregatedChunks">集約されたテキストチャンクのリスト</param>
    /// <param name="sourceWindowHandle">ソースウィンドウハンドル</param>
    /// <param name="imageBase64">画像データ（Base64エンコード、Cloud AI翻訳用）</param>
    /// <param name="imageWidth">画像幅</param>
    /// <param name="imageHeight">画像高さ</param>
    /// <exception cref="ArgumentNullException">aggregatedChunksがnullの場合</exception>
    public AggregatedChunksReadyEvent(
        IReadOnlyList<TextChunk> aggregatedChunks,
        IntPtr sourceWindowHandle,
        string? imageBase64,
        int imageWidth,
        int imageHeight)
        : this(aggregatedChunks, sourceWindowHandle, imageBase64, imageWidth, imageHeight, null)
    {
    }

    /// <summary>
    /// コンストラクタ（Fork-Join結果付き）
    /// </summary>
    /// <param name="aggregatedChunks">集約されたテキストチャンクのリスト</param>
    /// <param name="sourceWindowHandle">ソースウィンドウハンドル</param>
    /// <param name="imageBase64">画像データ（Base64エンコード、Cloud AI翻訳用）</param>
    /// <param name="imageWidth">画像幅</param>
    /// <param name="imageHeight">画像高さ</param>
    /// <param name="preComputedCloudResult">事前計算されたCloud AI翻訳結果（OCRと並列実行、Issue #290）</param>
    /// <exception cref="ArgumentNullException">aggregatedChunksがnullの場合</exception>
    public AggregatedChunksReadyEvent(
        IReadOnlyList<TextChunk> aggregatedChunks,
        IntPtr sourceWindowHandle,
        string? imageBase64,
        int imageWidth,
        int imageHeight,
        FallbackTranslationResult? preComputedCloudResult)
    {
        ArgumentNullException.ThrowIfNull(aggregatedChunks, nameof(aggregatedChunks));

        AggregatedChunks = aggregatedChunks;
        SourceWindowHandle = sourceWindowHandle;
        ImageBase64 = imageBase64;
        ImageWidth = imageWidth;
        ImageHeight = imageHeight;
        PreComputedCloudResult = preComputedCloudResult;
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
    /// 画像データ（Base64エンコード、Cloud AI翻訳用）
    /// </summary>
    /// <remarks>
    /// Issue #78 Phase 4: 並列翻訳オーケストレーター統合
    /// Pro/Premiaプランでのみ使用
    /// </remarks>
    public string? ImageBase64 { get; }

    /// <summary>
    /// 画像幅（Cloud AI翻訳用）
    /// </summary>
    public int ImageWidth { get; }

    /// <summary>
    /// 画像高さ（Cloud AI翻訳用）
    /// </summary>
    public int ImageHeight { get; }

    /// <summary>
    /// Cloud AI翻訳用の画像データが利用可能かどうか
    /// </summary>
    public bool HasImageData => !string.IsNullOrEmpty(ImageBase64) && ImageWidth > 0 && ImageHeight > 0;

    /// <summary>
    /// 事前計算されたCloud AI翻訳結果（OCRと並列実行済み、Issue #290）
    /// </summary>
    /// <remarks>
    /// この結果が設定されている場合、AggregatedChunksReadyEventHandlerは
    /// Cloud AI翻訳を再実行する必要がない
    /// </remarks>
    public FallbackTranslationResult? PreComputedCloudResult { get; }

    /// <summary>
    /// 事前計算されたCloud AI翻訳結果が利用可能かどうか
    /// </summary>
    public bool HasPreComputedCloudResult => PreComputedCloudResult?.IsSuccess == true;

    /// <summary>
    /// [Issue #381] 実際に送信するCloud画像幅（ログ・トークン推定用）
    /// 0の場合はImageWidth（元サイズ）がフォールバックとして使用される
    /// </summary>
    public int CloudImageWidth { get; init; }

    /// <summary>
    /// [Issue #381] 実際に送信するCloud画像高さ（ログ・トークン推定用）
    /// 0の場合はImageHeight（元サイズ）がフォールバックとして使用される
    /// </summary>
    public int CloudImageHeight { get; init; }

    /// <summary>
    /// [Issue #379] 翻訳モード（Singleshotモード時にGateフィルタリングをバイパス）
    /// </summary>
    public TranslationMode TranslationMode { get; init; } = TranslationMode.Live;

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
