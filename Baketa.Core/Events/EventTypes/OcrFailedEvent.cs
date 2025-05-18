using Baketa.Core.Abstractions.Imaging;
using System;

namespace Baketa.Core.Events.EventTypes;

    /// <summary>
    /// OCR失敗イベント
    /// </summary>
    public class OcrFailedEvent : EventBase
    {
        /// <summary>
        /// OCR処理元の画像
        /// </summary>
        public IImage? SourceImage { get; }
        
        /// <summary>
        /// 発生した例外
        /// </summary>
        public Exception? Exception { get; }
        
        /// <summary>
        /// エラーメッセージ
        /// </summary>
        public string ErrorMessage { get; }
        
        /// <summary>
        /// 経過時間
        /// </summary>
        public TimeSpan ElapsedTime { get; }
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="sourceImage">OCR処理元の画像</param>
        /// <param name="exception">発生した例外</param>
        /// <param name="errorMessage">エラーメッセージ</param>
        /// <param name="elapsedTime">経過時間</param>
        public OcrFailedEvent(IImage? sourceImage, Exception? exception, string? errorMessage = null, TimeSpan elapsedTime = default)
        {
            SourceImage = sourceImage;
            Exception = exception;
            ErrorMessage = errorMessage ?? exception?.Message ?? "不明なエラー";
            ElapsedTime = elapsedTime;
        }
        
        /// <inheritdoc />
        public override string Name => "OcrFailed";
        
        /// <inheritdoc />
        public override string Category => "OCR";
    }
