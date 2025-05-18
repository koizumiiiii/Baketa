using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Models.OCR;
using System;
using System.Collections.Generic;

namespace Baketa.Core.Events.EventTypes;

    /// <summary>
    /// OCR完了イベント
    /// </summary>
    public class OcrCompletedEvent : EventBase
    {
        /// <summary>
        /// OCR処理元の画像
        /// </summary>
        public IImage SourceImage { get; }
        
        /// <summary>
        /// OCR結果リスト
        /// </summary>
        public IReadOnlyList<OcrResult> Results { get; }
        
        /// <summary>
        /// OCR処理時間
        /// </summary>
        public TimeSpan ProcessingTime { get; }
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="sourceImage">OCR処理元の画像</param>
        /// <param name="results">OCR結果リスト</param>
        /// <param name="processingTime">OCR処理時間</param>
        /// <exception cref="ArgumentNullException">sourceImageまたはresultsがnullの場合</exception>
        public OcrCompletedEvent(IImage sourceImage, IReadOnlyList<OcrResult> results, TimeSpan processingTime)
        {
            SourceImage = sourceImage ?? throw new ArgumentNullException(nameof(sourceImage));
            Results = results ?? throw new ArgumentNullException(nameof(results));
            ProcessingTime = processingTime;
        }
        
        /// <inheritdoc />
        public override string Name => "OcrCompleted";
        
        /// <inheritdoc />
        public override string Category => "OCR";
    }
