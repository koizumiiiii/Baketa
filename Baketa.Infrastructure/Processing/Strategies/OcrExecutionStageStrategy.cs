using Baketa.Core.Abstractions.Processing;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Models.Processing;
using Baketa.Core.Models.OCR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Linq;

namespace Baketa.Infrastructure.Processing.Strategies;

/// <summary>
/// OCR実行段階の処理戦略
/// 既存のOCR処理システムとの統合
/// </summary>
public class OcrExecutionStageStrategy : IProcessingStageStrategy
{
    private readonly ILogger<OcrExecutionStageStrategy> _logger;
    private readonly IOcrEngine _ocrEngine;
    
    public ProcessingStageType StageType => ProcessingStageType.OcrExecution;
    public TimeSpan EstimatedProcessingTime => TimeSpan.FromMilliseconds(80);

    public OcrExecutionStageStrategy(
        ILogger<OcrExecutionStageStrategy> logger,
        IOcrEngine ocrEngine)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _ocrEngine = ocrEngine ?? throw new ArgumentNullException(nameof(ocrEngine));
    }

    public async Task<ProcessingStageResult> ExecuteAsync(ProcessingContext context, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogDebug("OCR実行段階開始 - ContextId: {ContextId}", context.Input.ContextId);
            
            // 実際のOCRサービス統合
            string detectedText;
            List<object> textChunks = [];
            
            OcrResults ocrResults;
            if (context.Input.CaptureRegion != Rectangle.Empty)
            {
                // 特定領域でのOCR処理
                ocrResults = await _ocrEngine.RecognizeAsync(context.Input.CapturedImage, context.Input.CaptureRegion, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // 全体画像でのOCR処理
                ocrResults = await _ocrEngine.RecognizeAsync(context.Input.CapturedImage, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            
            // OCR結果から文字列とチャンクを取得
            detectedText = string.Join(" ", ocrResults.TextRegions.Select(r => r.Text));
            textChunks = ocrResults.TextRegions.Cast<object>().ToList();
            
            var result = new OcrExecutionResult
            {
                DetectedText = detectedText ?? "",
                TextChunks = textChunks,
                ProcessingTime = stopwatch.Elapsed,
                Success = !string.IsNullOrEmpty(detectedText),
                ErrorMessage = string.IsNullOrEmpty(detectedText) ? "OCRでテキストが検出されませんでした" : null
            };
            
            _logger.LogDebug("OCR実行段階完了 - テキスト長: {TextLength}, 処理時間: {ProcessingTime}ms",
                result.DetectedText.Length, stopwatch.Elapsed.TotalMilliseconds);
            
            return ProcessingStageResult.CreateSuccess(StageType, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OCR実行段階でエラーが発生");
            return ProcessingStageResult.CreateError(StageType, ex.Message, stopwatch.Elapsed);
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    public bool ShouldExecute(ProcessingContext context)
    {
        // Stage 1で画像変化が検知された場合のみ実行
        if (context.PreviousStageResult?.Success == true &&
            context.PreviousStageResult.Data is ImageChangeDetectionResult imageChange)
        {
            return imageChange.HasChanged;
        }
        
        // Stage 1が実行されていない場合は実行する
        return !context.HasStageResult(ProcessingStageType.ImageChangeDetection);
    }

}