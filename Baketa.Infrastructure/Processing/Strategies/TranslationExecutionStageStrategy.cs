using Baketa.Core.Abstractions.Processing;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Models.Processing;
using Baketa.Core.Models.Translation;
using Baketa.Core.Translation.Models;
using Baketa.Core.Events.EventTypes;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Baketa.Infrastructure.Processing.Strategies;

/// <summary>
/// 翻訳実行段階の処理戦略
/// 既存翻訳システムとの統合
/// </summary>
public class TranslationExecutionStageStrategy : IProcessingStageStrategy
{
    private readonly ILogger<TranslationExecutionStageStrategy> _logger;
    private readonly ITranslationEngine _translationEngine;
    private readonly IEventAggregator _eventAggregator;
    
    public ProcessingStageType StageType => ProcessingStageType.TranslationExecution;
    public TimeSpan EstimatedProcessingTime => TimeSpan.FromMilliseconds(200);

    public TranslationExecutionStageStrategy(
        ILogger<TranslationExecutionStageStrategy> logger,
        ITranslationEngine translationEngine,
        IEventAggregator eventAggregator)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _translationEngine = translationEngine ?? throw new ArgumentNullException(nameof(translationEngine));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
    }

    public async Task<ProcessingStageResult> ExecuteAsync(ProcessingContext context, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var ocrResult = context.GetStageResult<OcrExecutionResult>(ProcessingStageType.OcrExecution);
            if (ocrResult?.DetectedText == null)
            {
                _logger.LogWarning("翻訳対象テキストがありません");
                return ProcessingStageResult.CreateError(StageType, "翻訳対象テキストがありません", stopwatch.Elapsed);
            }

            _logger.LogDebug("翻訳実行段階開始 - ContextId: {ContextId}, テキスト長: {TextLength}",
                context.Input.ContextId, ocrResult.DetectedText.Length);

            // 実際の翻訳サービス統合
            var translationRequest = new TranslationRequest
            {
                SourceText = ocrResult.DetectedText,
                SourceLanguage = Language.Auto,
                TargetLanguage = Language.Japanese
            };
            
            var translationResult = await _translationEngine.TranslateAsync(translationRequest, cancellationToken).ConfigureAwait(false);
            
            var result = new TranslationExecutionResult
            {
                TranslatedText = translationResult?.TranslatedText ?? ocrResult.DetectedText,
                TranslatedChunks = [], // TODO: 実際のTranslatedChunkを設定
                ProcessingTime = stopwatch.Elapsed,
                Success = translationResult?.IsSuccess ?? false,
                EngineUsed = _translationEngine.GetType().Name
            };
            
            _logger.LogDebug("翻訳実行段階完了 - 翻訳テキスト長: {TranslatedLength}, 処理時間: {ProcessingTime}ms",
                result.TranslatedText.Length, stopwatch.Elapsed.TotalMilliseconds);
            
            // 🔄 [FIX] TranslationCompletedEvent発行 - 翻訳完了をUI表示へ通知
            if (result.Success)
            {
                try
                {
                    var translationCompletedEvent = new TranslationCompletedEvent(
                        sourceText: ocrResult.DetectedText,
                        translatedText: result.TranslatedText,
                        sourceLanguage: translationRequest.SourceLanguage.ToString().ToLowerInvariant(),
                        targetLanguage: translationRequest.TargetLanguage.ToString().ToLowerInvariant(),
                        processingTime: stopwatch.Elapsed,
                        engineName: result.EngineUsed
                    );
                    
                    await _eventAggregator.PublishAsync(translationCompletedEvent).ConfigureAwait(false);
                    
                    _logger.LogInformation("🔄 [FIX] TranslationCompletedEvent発行完了 - ID: {EventId}, テキスト: {SourceText} → {TranslatedText}",
                        translationCompletedEvent.Id, ocrResult.DetectedText, result.TranslatedText);
                    Console.WriteLine($"🔄 [FIX] TranslationCompletedEvent発行完了 - ID: {translationCompletedEvent.Id}");
                }
                catch (Exception eventEx)
                {
                    _logger.LogError(eventEx, "❌ TranslationCompletedEvent発行エラー");
                    Console.WriteLine($"❌ TranslationCompletedEvent発行エラー: {eventEx.Message}");
                }
            }
            
            return ProcessingStageResult.CreateSuccess(StageType, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "翻訳実行段階でエラーが発生");
            return ProcessingStageResult.CreateError(StageType, ex.Message, stopwatch.Elapsed);
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    public bool ShouldExecute(ProcessingContext context)
    {
        // Stage 3でテキスト変化が検知された場合のみ実行
        if (context.PreviousStageResult?.Success == true &&
            context.PreviousStageResult.Data is TextChangeDetectionResult textChange)
        {
            return textChange.HasTextChanged;
        }
        
        // テキスト変化検知ステージが実行されていない場合は実行する
        if (!context.HasStageResult(ProcessingStageType.TextChangeDetection))
        {
            // OCRが成功していれば実行
            var ocrResult = context.GetStageResult<OcrExecutionResult>(ProcessingStageType.OcrExecution);
            return ocrResult?.Success == true && !string.IsNullOrEmpty(ocrResult.DetectedText);
        }
        
        return false;
    }

}