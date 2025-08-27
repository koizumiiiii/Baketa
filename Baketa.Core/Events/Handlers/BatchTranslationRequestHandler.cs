using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events.EventTypes;

namespace Baketa.Core.Events.Handlers;

/// <summary>
/// バッチ翻訳要求ハンドラー - 複数の翻訳要求を個別のTranslationRequestEventに分解処理
/// 
/// TPL Dataflowで制御されたバッチを受信し、個別の翻訳要求として再配信することで
/// 既存のTranslationRequestHandlerの豊富な処理ロジックを活用
/// </summary>
public class BatchTranslationRequestHandler(
    IEventAggregator eventAggregator, 
    ILogger<BatchTranslationRequestHandler> logger) 
    : IEventProcessor<BatchTranslationRequestEvent>
{
    private readonly IEventAggregator _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
    private readonly ILogger<BatchTranslationRequestHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public int Priority => 100; // TranslationRequestHandlerより高い優先度で処理

    /// <inheritdoc />
    public bool SynchronousExecution => false;

    /// <inheritdoc />
    public async Task HandleAsync(BatchTranslationRequestEvent eventData)
    {
        _logger.LogDebug("BatchTranslationRequestHandler.HandleAsync開始: BatchSize={BatchSize}, Summary={Summary}", 
            eventData.BatchSize, eventData.BatchSummary);

        ArgumentNullException.ThrowIfNull(eventData);

        if (eventData.OcrResults == null || !eventData.OcrResults.Any())
        {
            _logger.LogWarning("BatchTranslationRequestEvent内にOCR結果が存在しません");
            return;
        }

        _logger.LogInformation("バッチ翻訳処理開始: {BatchSize}個の翻訳要求を個別処理に分解", 
            eventData.BatchSize);

        // バッチ内の各OCR結果を個別のTranslationRequestEventとして発行
        var individualTasks = eventData.OcrResults.Select(async (ocrResult, index) =>
        {
            try
            {
                _logger.LogTrace("個別翻訳要求作成中 [{Index}/{Total}]: '{Text}'", 
                    index + 1, eventData.BatchSize, ocrResult.Text[..Math.Min(20, ocrResult.Text.Length)]);

                // 個別のTranslationRequestEventを作成
                var individualEvent = new TranslationRequestEvent(
                    ocrResult: ocrResult,
                    sourceLanguage: eventData.SourceLanguage,
                    targetLanguage: eventData.TargetLanguage);

                // 既存のTranslationRequestHandlerに処理を委譲
                await _eventAggregator.PublishAsync(individualEvent).ConfigureAwait(false);

                _logger.LogTrace("個別翻訳要求発行完了 [{Index}/{Total}]: '{Text}'", 
                    index + 1, eventData.BatchSize, ocrResult.Text[..Math.Min(20, ocrResult.Text.Length)]);

                return true; // 成功
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "個別翻訳要求処理中にエラー [{Index}/{Total}]: '{Text}' - {ErrorType}: {ErrorMessage}", 
                    index + 1, eventData.BatchSize, 
                    ocrResult.Text[..Math.Min(20, ocrResult.Text.Length)], 
                    ex.GetType().Name, ex.Message);

                return false; // 失敗
            }
        });

        // すべての個別翻訳要求の処理を待機
        var results = await Task.WhenAll(individualTasks).ConfigureAwait(false);

        // 処理結果の統計
        var successCount = results.Count(r => r);
        var failureCount = results.Length - successCount;

        if (failureCount > 0)
        {
            _logger.LogWarning("バッチ翻訳処理完了 - 成功: {Success}, 失敗: {Failed}/{Total}", 
                successCount, failureCount, eventData.BatchSize);
        }
        else
        {
            _logger.LogInformation("バッチ翻訳処理完了 - 全{Total}件の翻訳要求を正常発行", 
                eventData.BatchSize);
        }

        // 処理完了メトリクス出力（デバッグ用）
        _logger.LogDebug("BatchTranslationRequestHandler処理統計: " +
            "BatchSize={BatchSize}, SuccessRate={SuccessRate:P1}, " +
            "Languages={SourceLang}->{TargetLang}, Summary={Summary}",
            eventData.BatchSize,
            (double)successCount / eventData.BatchSize,
            eventData.SourceLanguage,
            eventData.TargetLanguage,
            eventData.BatchSummary);
    }
}