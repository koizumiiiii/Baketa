using Baketa.Core.Abstractions.Processing;
using Baketa.Core.Models.Processing;
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
    
    public ProcessingStageType StageType => ProcessingStageType.TranslationExecution;
    public TimeSpan EstimatedProcessingTime => TimeSpan.FromMilliseconds(200);

    public TranslationExecutionStageStrategy(ILogger<TranslationExecutionStageStrategy> logger)
    {
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
                _logger.LogWarning("翻訳対象テキストがありません");
                return ProcessingStageResult.CreateError(StageType, "翻訳対象テキストがありません", stopwatch.Elapsed);
            }

            _logger.LogDebug("翻訳実行段階開始 - ContextId: {ContextId}, テキスト長: {TextLength}",
                context.Input.ContextId, ocrResult.DetectedText.Length);

            // TODO: 実際の翻訳サービス統合
            // var translationRequest = new TranslationRequest
            // {
            //     Text = ocrResult.DetectedText,
            //     SourceLanguage = "auto",
            //     TargetLanguage = "ja",
            //     TextChunks = ocrResult.TextChunks,
            //     SourceWindowHandle = context.Input.SourceWindowHandle
            // };
            // var translationResult = await _translationService.TranslateAsync(translationRequest, cancellationToken);
            
            // 現在はモック翻訳処理（実装時に実際の翻訳サービスに置き換え）
            await Task.Delay(200, cancellationToken).ConfigureAwait(false); // 翻訳処理時間をシミュレート
            
            var mockTranslatedText = GenerateMockTranslation(ocrResult.DetectedText);
            
            var result = new TranslationExecutionResult
            {
                TranslatedText = mockTranslatedText,
                TranslatedChunks = [], // TODO: 実際のTranslatedChunkを設定
                ProcessingTime = stopwatch.Elapsed,
                Success = true,
                EngineUsed = "MockTranslationEngine"
            };
            
            _logger.LogDebug("翻訳実行段階完了 - 翻訳テキスト長: {TranslatedLength}, 処理時間: {ProcessingTime}ms",
                mockTranslatedText.Length, stopwatch.Elapsed.TotalMilliseconds);
            
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

    /// <summary>
    /// モック翻訳テキストを生成（実装時に削除）
    /// </summary>
    private static string GenerateMockTranslation(string originalText)
    {
        // 簡単な翻訳シミュレーション
        var translations = new Dictionary<string, string>
        {
            { "Hello World", "こんにちは世界" },
            { "Welcome to the game", "ゲームへようこそ" },
            { "Press any key to continue", "何かキーを押して続行" },
            { "Level 1 Complete", "レベル1完了" },
            { "Game Over", "ゲームオーバー" },
            { "New High Score", "新記録達成" }
        };
        
        if (translations.TryGetValue(originalText, out var translation))
        {
            return translation;
        }
        
        // デフォルト翻訳
        return $"[翻訳] {originalText}";
    }
}