using Baketa.Core.Abstractions.Imaging;
using System;
using System.Drawing;

namespace Baketa.Core.Events.EventTypes
{
    /// <summary>
    /// キャプチャ完了イベント
    /// </summary>
    public class CaptureCompletedEvent : EventBase
    {
        /// <summary>
        /// キャプチャされた画像
        /// </summary>
        public IImage CapturedImage { get; }
        
        /// <summary>
        /// キャプチャ領域
        /// </summary>
        public Rectangle CaptureRegion { get; }
        
        /// <summary>
        /// キャプチャ処理時間
        /// </summary>
        public TimeSpan CaptureTime { get; }
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="capturedImage">キャプチャされた画像</param>
        /// <param name="captureRegion">キャプチャ領域</param>
        /// <param name="captureTime">キャプチャ処理時間</param>
        /// <exception cref="ArgumentNullException">capturedImageがnullの場合</exception>
        public CaptureCompletedEvent(IImage capturedImage, Rectangle captureRegion, TimeSpan captureTime)
        {
            CapturedImage = capturedImage ?? throw new ArgumentNullException(nameof(capturedImage));
            CaptureRegion = captureRegion;
            CaptureTime = captureTime;
        }
        
        /// <inheritdoc />
        public override string Name => "CaptureCompleted";
        
        /// <inheritdoc />
        public override string Category => "Capture";
    }
}