using System.Drawing;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events.Capture;
using Baketa.Core.Events.EventTypes;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.EventHandlers.Capture;

/// <summary>
/// ROI画像キャプチャ完了イベントハンドラー
/// Phase 2.5実装 - 複数ROI画像の個別処理
/// </summary>
/// <remarks>
/// 各ROI画像に対して個別にCaptureCompletedEventを発行し、
/// 既存のOCR翻訳パイプラインを活用する。
/// 座標情報（AbsoluteRegion）を含めることで、正しいオーバーレイ表示を実現。
/// </remarks>
public class ROIImageCapturedEventHandler(
    IEventAggregator eventAggregator,
    ILogger<ROIImageCapturedEventHandler> logger) : IEventProcessor<ROIImageCapturedEvent>
{
    private readonly IEventAggregator _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
    private readonly ILogger<ROIImageCapturedEventHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public int Priority => 0; // 通常優先度

    public bool SynchronousExecution => false; // 非同期実行（複数ROIを並列処理）

    public async Task HandleAsync(ROIImageCapturedEvent eventData)
    {
        try
        {
            _logger.LogInformation("🎯 [MULTI_ROI] ROI画像処理開始: Index={ROIIndex}/{TotalROIs}, Region={AbsoluteRegion}",
                eventData.ROIIndex, eventData.TotalROIs, eventData.AbsoluteRegion);

            // 🎯 [PHASE2.5] CaptureCompletedEventを発行して既存パイプラインを活用
            // IsMultiROICapture = trueにより、AdaptiveCaptureServiceでの二重発行を防止
            var captureCompletedEvent = new CaptureCompletedEvent(
                capturedImage: eventData.Image,
                captureRegion: eventData.AbsoluteRegion,  // 絶対座標を渡す
                captureTime: TimeSpan.Zero)  // ROI処理時間は別途測定
            {
                ImageChangeSkipped = false,
                IsMultiROICapture = true,  // 🔥 重要: 複数ROI検出フラグ
                TotalROICount = eventData.TotalROIs,
                ROIIndex = eventData.ROIIndex
            };

            await _eventAggregator.PublishAsync(captureCompletedEvent).ConfigureAwait(false);

            _logger.LogDebug("✅ [MULTI_ROI] CaptureCompletedEvent発行完了: ROI {ROIIndex}/{TotalROIs}",
                eventData.ROIIndex, eventData.TotalROIs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [MULTI_ROI] ROI画像処理エラー: ROI {ROIIndex}/{TotalROIs}",
                eventData.ROIIndex, eventData.TotalROIs);
            // エラーが発生しても他のROI処理は継続
        }
    }
}
