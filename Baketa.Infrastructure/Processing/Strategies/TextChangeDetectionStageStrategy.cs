using Baketa.Core.Abstractions.Processing;
using Baketa.Core.Models.Processing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Baketa.Infrastructure.Processing.Strategies;

/// <summary>
/// テキスト変化検知段階の処理戦略
/// OCR結果のテキストレベルでの変化を高速検出
/// </summary>
public class TextChangeDetectionStageStrategy : IProcessingStageStrategy
{
    private readonly ITextChangeDetectionService _textChangeService;
    private readonly IOptionsMonitor<ProcessingPipelineSettings> _settings;
    private readonly ILogger<TextChangeDetectionStageStrategy> _logger;
    
    public ProcessingStageType StageType => ProcessingStageType.TextChangeDetection;
    public TimeSpan EstimatedProcessingTime => TimeSpan.FromMilliseconds(1);

    public TextChangeDetectionStageStrategy(
        ITextChangeDetectionService textChangeService,
        IOptionsMonitor<ProcessingPipelineSettings> settings,
        ILogger<TextChangeDetectionStageStrategy> logger)
    {
        _textChangeService = textChangeService ?? throw new ArgumentNullException(nameof(textChangeService));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ProcessingStageResult> ExecuteAsync(ProcessingContext context, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var ocrResult = context.GetStageResult<OcrExecutionResult>(ProcessingStageType.OcrExecution);
            if (ocrResult?.DetectedText == null)
            {
                _logger.LogWarning("OCR結果が取得できません - テキスト変化検知をスキップ");
                return ProcessingStageResult.CreateError(StageType, "OCR結果が取得できません", stopwatch.Elapsed);
            }

            var currentText = ocrResult.DetectedText;
            var previousText = context.Input.PreviousOcrText;
            var contextId = context.Input.ContextId;

            _logger.LogDebug("テキスト変化検知開始 - ContextId: {ContextId}, CurrentLen: {CurrentLen}, PreviousLen: {PreviousLen}",
                contextId, currentText.Length, previousText?.Length ?? 0);

            TextChangeResult changeResult;
            
            if (string.IsNullOrEmpty(previousText))
            {
                // 初回実行時は変化ありとして処理継続
                _logger.LogDebug("初回テキスト検出 - 変化ありとして処理継続");
                changeResult = TextChangeResult.CreateFirstTime(currentText, stopwatch.Elapsed);
            }
            else
            {
                // テキスト変化検知実行
                changeResult = await _textChangeService.DetectTextChangeAsync(previousText, currentText, contextId).ConfigureAwait(false);
            }

            var threshold = _settings.CurrentValue.TextChangeThreshold;
            var hasSignificantChange = changeResult.HasChanged && changeResult.ChangePercentage >= threshold;

            _logger.LogDebug("テキスト変化検知完了 - 変化: {HasChanged}, 変化率: {ChangePercentage:F3}%, しきい値: {Threshold:F1}%",
                hasSignificantChange, changeResult.ChangePercentage * 100, threshold * 100);

            var result = new TextChangeDetectionResult
            {
                HasTextChanged = hasSignificantChange,
                ChangePercentage = changeResult.ChangePercentage,
                PreviousText = previousText,
                CurrentText = currentText,
                ProcessingTime = stopwatch.Elapsed,
                AlgorithmUsed = changeResult.AlgorithmUsed.ToString()
            };
            
            return ProcessingStageResult.CreateSuccess(StageType, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "テキスト変化検知段階でエラーが発生");
            return ProcessingStageResult.CreateError(StageType, ex.Message, stopwatch.Elapsed);
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    public bool ShouldExecute(ProcessingContext context)
    {
        // Stage 2でOCRが成功した場合のみ実行
        if (context.PreviousStageResult?.Success == true &&
            context.PreviousStageResult.Data is OcrExecutionResult ocrResult)
        {
            return ocrResult.Success && !string.IsNullOrEmpty(ocrResult.DetectedText);
        }
        
        // OCRステージの結果が存在する場合もチェック
        var existingOcrResult = context.GetStageResult<OcrExecutionResult>(ProcessingStageType.OcrExecution);
        return existingOcrResult?.Success == true && !string.IsNullOrEmpty(existingOcrResult.DetectedText);
    }
}

