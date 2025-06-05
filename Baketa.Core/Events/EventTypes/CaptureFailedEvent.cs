using System;
using System.Drawing;

namespace Baketa.Core.Events.EventTypes;

    /// <summary>
    /// キャプチャ失敗イベント
    /// </summary>
    public class CaptureFailedEvent(Rectangle captureRegion, Exception? exception, string? errorMessage = null) : EventBase
    {
        /// <summary>
        /// キャプチャ領域
        /// </summary>
        public Rectangle CaptureRegion { get; } = captureRegion;
        
        /// <summary>
        /// 発生した例外
        /// </summary>
        public Exception? Exception { get; } = exception ?? new InvalidOperationException("不明なエラー");
        
        /// <summary>
        /// エラーメッセージ
        /// </summary>
        public string ErrorMessage { get; } = errorMessage ?? exception?.Message ?? "不明なエラー";
        

        
        /// <inheritdoc />
        public override string Name => "CaptureFailed";
        
        /// <inheritdoc />
        public override string Category => "Capture";
    }
