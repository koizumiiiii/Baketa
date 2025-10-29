using Baketa.Core.Abstractions.Imaging;
using System;
using System.Drawing;

namespace Baketa.Core.Events.EventTypes;

/// <summary>
/// キャプチャ完了イベント
/// </summary>
/// <remarks>
/// コンストラクタ
/// </remarks>
/// <param name="capturedImage">キャプチャされた画像</param>
/// <param name="captureRegion">キャプチャ領域</param>
/// <param name="captureTime">キャプチャ処理時間</param>
/// <exception cref="ArgumentNullException">capturedImageがnullの場合</exception>
public class CaptureCompletedEvent(IImage capturedImage, Rectangle captureRegion, TimeSpan captureTime) : EventBase
    {
    /// <summary>
    /// キャプチャされた画像
    /// </summary>
    public IImage CapturedImage { get; } = capturedImage ?? throw new ArgumentNullException(nameof(capturedImage));

    /// <summary>
    /// キャプチャ領域
    /// </summary>
    public Rectangle CaptureRegion { get; } = captureRegion;

    /// <summary>
    /// キャプチャ処理時間
    /// </summary>
    public TimeSpan CaptureTime { get; } = captureTime;

    /// <summary>
    /// 画像変化検知によりOCR処理がスキップされたかどうか
    /// Phase 1: OCR処理最適化システム
    /// </summary>
    public bool ImageChangeSkipped { get; init; } = false;

    /// <summary>
    /// 複数ROI画像処理システムにより発行されたイベントかどうか
    /// Phase 2.5: 複数ROI画像処理システム
    /// </summary>
    /// <remarks>
    /// trueの場合、このイベントは複数ROI画像の1つを表し、
    /// AdaptiveCaptureServiceでの二重発行を防止するためのフラグとして使用される。
    /// </remarks>
    public bool IsMultiROICapture { get; init; } = false;

    /// <summary>
    /// 総ROI数（IsMultiROICapture = trueの場合のみ有効）
    /// </summary>
    public int TotalROICount { get; init; } = 0;

    /// <summary>
    /// ROIインデックス（IsMultiROICapture = trueの場合のみ有効、0始まり）
    /// </summary>
    public int ROIIndex { get; init; } = 0;

    /// <inheritdoc />
    public override string Name => "CaptureCompleted";

        /// <inheritdoc />
        public override string Category => "Capture";
    }
