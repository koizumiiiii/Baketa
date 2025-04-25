using Baketa.Core.Abstractions.Imaging;
using System;

namespace Baketa.Core.Events.EventTypes
{
    /// <summary>
    /// OCR進捗イベント
    /// </summary>
    public class OcrProgressEvent : EventBase
    {
        /// <summary>
        /// OCR処理元の画像
        /// </summary>
        public IImage SourceImage { get; }
        
        /// <summary>
        /// 進捗率 (0.0〜1.0)
        /// </summary>
        public float Progress { get; }
        
        /// <summary>
        /// 現在のステップ説明
        /// </summary>
        public string CurrentStep { get; }
        
        /// <summary>
        /// 経過時間
        /// </summary>
        public TimeSpan ElapsedTime { get; }
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="sourceImage">OCR処理元の画像</param>
        /// <param name="progress">進捗率</param>
        /// <param name="currentStep">現在のステップ説明</param>
        /// <param name="elapsedTime">経過時間</param>
        /// <exception cref="ArgumentNullException">sourceImageがnullの場合</exception>
        /// <exception cref="ArgumentOutOfRangeException">progressが0〜1の範囲外の場合</exception>
        public OcrProgressEvent(IImage sourceImage, float progress, string currentStep, TimeSpan elapsedTime)
        {
            if (progress < 0 || progress > 1)
                throw new ArgumentOutOfRangeException(nameof(progress), "進捗率は0.0〜1.0の範囲である必要があります");
                
            SourceImage = sourceImage ?? throw new ArgumentNullException(nameof(sourceImage));
            Progress = progress;
            CurrentStep = currentStep ?? string.Empty;
            ElapsedTime = elapsedTime;
        }
        
        /// <inheritdoc />
        public override string Name => "OcrProgress";
        
        /// <inheritdoc />
        public override string Category => "OCR";
    }
}