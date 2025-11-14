using System;
using System.Collections.Generic;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Models.OCR;

namespace Baketa.Core.Events.EventTypes;

/// <summary>
/// OCR完了イベント
/// </summary>
/// <remarks>
/// コンストラクタ
/// </remarks>
/// <param name="sourceImage">OCR処理元の画像</param>
/// <param name="results">OCR結果リスト</param>
/// <param name="processingTime">OCR処理時間</param>
/// <exception cref="ArgumentNullException">sourceImageまたはresultsがnullの場合</exception>
public class OcrCompletedEvent(IImage sourceImage, IReadOnlyList<OcrResult> results, TimeSpan processingTime) : EventBase
{
    /// <summary>
    /// OCR処理元の画像
    /// </summary>
    public IImage SourceImage { get; } = sourceImage ?? throw new ArgumentNullException(nameof(sourceImage));

    /// <summary>
    /// OCR結果リスト
    /// </summary>
    public IReadOnlyList<OcrResult> Results { get; } = results ?? throw new ArgumentNullException(nameof(results));

    /// <summary>
    /// OCR処理時間
    /// </summary>
    public TimeSpan ProcessingTime { get; } = processingTime;

    /// <inheritdoc />
    public override string Name => "OcrCompleted";

    /// <inheritdoc />
    public override string Category => "OCR";
}
