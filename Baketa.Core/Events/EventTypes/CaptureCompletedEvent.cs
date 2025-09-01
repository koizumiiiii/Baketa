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

    /// <inheritdoc />
    public override string Name => "CaptureCompleted";
        
        /// <inheritdoc />
        public override string Category => "Capture";
    }
