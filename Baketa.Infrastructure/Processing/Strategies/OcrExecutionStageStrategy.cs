using Baketa.Core.Abstractions.Processing;
using Baketa.Core.Models.Processing;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Baketa.Infrastructure.Processing.Strategies;

/// <summary>
/// OCR実行段階の処理戦略
/// 既存のOCR処理システムとの統合
/// </summary>
public class OcrExecutionStageStrategy : IProcessingStageStrategy
{
    private readonly ILogger<OcrExecutionStageStrategy> _logger;
    
    public ProcessingStageType StageType => ProcessingStageType.OcrExecution;
    public TimeSpan EstimatedProcessingTime => TimeSpan.FromMilliseconds(80);

    public OcrExecutionStageStrategy(ILogger<OcrExecutionStageStrategy> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ProcessingStageResult> ExecuteAsync(ProcessingContext context, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogDebug("OCR実行段階開始 - ContextId: {ContextId}", context.Input.ContextId);
            
            // TODO: 実際のOCRサービス統合
            // var ocrRequest = new OcrRequest
            // {
            //     Image = context.Input.CapturedImage,
            //     Region = context.Input.CaptureRegion,
            //     SourceWindowHandle = context.Input.SourceWindowHandle
            // };
            // var ocrResult = await _ocrService.ProcessImageAsync(ocrRequest, cancellationToken);
            
            // 現在はモックOCR処理（実装時に実際のOCRサービスに置き換え）
            await Task.Delay(80, cancellationToken).ConfigureAwait(false); // OCR処理時間をシミュレート
            
            var mockDetectedText = GenerateMockOcrText(context.Input.CaptureRegion);
            
            var result = new OcrExecutionResult
            {
                DetectedText = mockDetectedText,
                TextChunks = [], // TODO: 実際のTextChunkを設定
                ProcessingTime = stopwatch.Elapsed,
                Success = true
            };
            
            _logger.LogDebug("OCR実行段階完了 - テキスト長: {TextLength}, 処理時間: {ProcessingTime}ms",
                mockDetectedText.Length, stopwatch.Elapsed.TotalMilliseconds);
            
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

    /// <summary>
    /// モックOCRテキストを生成（実装時に削除）
    /// </summary>
    private static string GenerateMockOcrText(System.Drawing.Rectangle captureRegion)
    {
        var mockTexts = new[]
        {
            "Hello World",
            "Welcome to the game",
            "Press any key to continue",
            "Level 1 Complete",
            "Game Over",
            "New High Score",
            $"Region: {captureRegion.Width}x{captureRegion.Height}"
        };
        
        var random = new Random();
        return mockTexts[random.Next(mockTexts.Length)];
    }
}