using System;
using System.Drawing;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Imaging;

namespace Baketa.Core.Events.EventTypes;

/// <summary>
/// OCR処理要求イベント
/// キャプチャ完了後にOCR処理開始を要求する
/// </summary>
/// <param name="capturedImage">キャプチャされた画像</param>
/// <param name="captureRegion">キャプチャ領域</param>
/// <param name="targetWindowHandle">対象ウィンドウハンドル（null可）</param>
/// <exception cref="ArgumentNullException">capturedImageがnullの場合</exception>
public class OcrRequestEvent(IImage capturedImage, Rectangle captureRegion, IntPtr? targetWindowHandle = null) : EventBase
{
    /// <summary>キャプチャされた画像</summary>
    public IImage CapturedImage { get; } = capturedImage ?? throw new ArgumentNullException(nameof(capturedImage));

    /// <summary>キャプチャ領域</summary>
    public Rectangle CaptureRegion { get; } = captureRegion;

    /// <summary>対象ウィンドウハンドル（null可）</summary>
    public IntPtr? TargetWindowHandle { get; } = targetWindowHandle;

    /// <inheritdoc />
    public override string Name => "OcrRequest";

    /// <inheritdoc />
    public override string Category => "OCR";
}
