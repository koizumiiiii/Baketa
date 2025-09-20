using Baketa.Core.Abstractions.Processing;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Models.Processing;
using Baketa.Core.Translation.Models;
using Baketa.Core.Events.EventTypes;
using CoreTranslationRequest = Baketa.Core.Translation.Models.TranslationRequest;
using Baketa.Core.Utilities; // 🎯 [TRANSLATION_DEBUG_LOG] DebugLogUtility用
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
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
    private readonly IConfiguration _configuration;

    public ProcessingStageType StageType => ProcessingStageType.TranslationExecution;
    public TimeSpan EstimatedProcessingTime => TimeSpan.FromMilliseconds(200);

    public TranslationExecutionStageStrategy(
        ILogger<TranslationExecutionStageStrategy> logger,
        ITranslationEngine translationEngine,
        IEventAggregator eventAggregator,
        IConfiguration configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _translationEngine = translationEngine ?? throw new ArgumentNullException(nameof(translationEngine));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
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

            // 🎯 [TRANSLATION_DEBUG_LOG] 翻訳処理開始をデバッグログに出力
            DebugLogUtility.WriteLog($"🌐 [TRANSLATION_START] 翻訳処理開始 - 元テキスト: '{ocrResult.DetectedText}'");
            DebugLogUtility.WriteLog($"🌐 [TRANSLATION_START] テキスト長: {ocrResult.DetectedText.Length}文字, エンジン: {_translationEngine.GetType().Name}");

            // 設定から言語を動的取得
            var defaultSourceLanguage = _configuration.GetValue<string>("Translation:DefaultSourceLanguage", "en");
            var defaultTargetLanguage = _configuration.GetValue<string>("Translation:DefaultTargetLanguage", "ja");

            // 実際の翻訳サービス統合
            var translationRequest = new CoreTranslationRequest
            {
                SourceText = ocrResult.DetectedText,
                SourceLanguage = Language.FromCode(defaultSourceLanguage),
                TargetLanguage = Language.FromCode(defaultTargetLanguage)
            };
            
            var translationResult = await _translationEngine.TranslateAsync(translationRequest, cancellationToken).ConfigureAwait(false);

            // 🎯 [TRANSLATION_DEBUG_LOG] 翻訳エンジン結果をデバッグログに出力
            DebugLogUtility.WriteLog($"🌐 [TRANSLATION_RESULT] 翻訳エンジン応答 - IsSuccess: {translationResult?.IsSuccess ?? false}");
            DebugLogUtility.WriteLog($"🌐 [TRANSLATION_RESULT] 翻訳結果: '{translationResult?.TranslatedText ?? "(null)"}'");
            DebugLogUtility.WriteLog($"🌐 [TRANSLATION_RESULT] 翻訳テキスト長: {translationResult?.TranslatedText?.Length ?? 0}文字");

            // 🎯 [PHASE3.3_DEBUG] 翻訳エンジン結果の詳細ログ（UltraThink調査）
            _logger.LogInformation("🔍 [PHASE3.3_DEBUG] 翻訳エンジン結果詳細 - IsSuccess: {IsSuccess}, TranslatedText長: {TextLength}, TranslatedText: '{TranslatedText}'",
                translationResult?.IsSuccess ?? false, translationResult?.TranslatedText?.Length ?? 0, translationResult?.TranslatedText ?? "(null)");

            // 🎯 [PHASE3.3] 翻訳成功判定ロジック修正（UltraThink実用的解決策）
            // 翻訳エンジンのIsSuccessフラグに関係なく、翻訳テキストが存在すれば成功とみなす
            var isTranslationSuccessful = !string.IsNullOrWhiteSpace(translationResult?.TranslatedText) &&
                                        translationResult?.TranslatedText != ocrResult.DetectedText; // 元テキストと異なる場合のみ

            // 🎯 [TRANSLATION_DEBUG_LOG] 翻訳成功判定結果をデバッグログに出力
            DebugLogUtility.WriteLog($"🌐 [TRANSLATION_JUDGMENT] 翻訳成功判定: {isTranslationSuccessful}");
            DebugLogUtility.WriteLog($"🌐 [TRANSLATION_JUDGMENT] 判定理由: テキスト存在={!string.IsNullOrWhiteSpace(translationResult?.TranslatedText)}, 元テキストと異なる={translationResult?.TranslatedText != ocrResult.DetectedText}");

            var result = new TranslationExecutionResult
            {
                TranslatedText = translationResult?.TranslatedText ?? ocrResult.DetectedText,
                TranslatedChunks = [], // TODO: 実際のTranslatedChunkを設定
                ProcessingTime = stopwatch.Elapsed,
                Success = isTranslationSuccessful,
                EngineUsed = _translationEngine.GetType().Name
            };

            _logger.LogInformation("🎯 [PHASE3.3] 翻訳実行段階完了 - 成功: {Success}, 翻訳テキスト長: {TranslatedLength}, 処理時間: {ProcessingTime}ms",
                isTranslationSuccessful, result.TranslatedText?.Length ?? 0, stopwatch.Elapsed.TotalMilliseconds);
            Console.WriteLine($"🎯 [PHASE3.3] 翻訳段階完了 - Success: {isTranslationSuccessful}, TranslatedText: '{result.TranslatedText}'");

            // 🎯 [TRANSLATION_DEBUG_LOG] 翻訳処理完了をデバッグログに出力
            DebugLogUtility.WriteLog($"🌐 [TRANSLATION_COMPLETE] 翻訳処理完了 - 成功: {isTranslationSuccessful}");
            DebugLogUtility.WriteLog($"🌐 [TRANSLATION_COMPLETE] 最終結果: '{result.TranslatedText}'");
            DebugLogUtility.WriteLog($"🌐 [TRANSLATION_COMPLETE] 処理時間: {stopwatch.Elapsed.TotalMilliseconds:F1}ms, エンジン: {result.EngineUsed}");

            // 🔄 [FIX] TranslationCompletedEvent発行 - 翻訳完了をUI表示へ通知（成功判定修正）
            if (isTranslationSuccessful)
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

                    _logger.LogInformation("🎯 [PHASE3.3] TranslationCompletedEvent発行完了 - ID: {EventId}, テキスト: {SourceText} → {TranslatedText}",
                        translationCompletedEvent.Id, ocrResult.DetectedText, result.TranslatedText);
                    Console.WriteLine($"🎯 [PHASE3.3] TranslationCompletedEvent発行完了 - ID: {translationCompletedEvent.Id}");

                    // 🎯 [TRANSLATION_DEBUG_LOG] TranslationCompletedEvent発行をデバッグログに出力
                    DebugLogUtility.WriteLog($"🌐 [TRANSLATION_EVENT] TranslationCompletedEvent発行 - ID: {translationCompletedEvent.Id}");
                    DebugLogUtility.WriteLog($"🌐 [TRANSLATION_EVENT] 翻訳ペア: '{ocrResult.DetectedText}' → '{result.TranslatedText}'");
                }
                catch (Exception eventEx)
                {
                    _logger.LogError(eventEx, "❌ TranslationCompletedEvent発行エラー");
                    Console.WriteLine($"❌ TranslationCompletedEvent発行エラー: {eventEx.Message}");
                }
            }
            else
            {
                _logger.LogWarning("🎯 [PHASE3.3] 翻訳失敗によりTranslationCompletedEvent発行スキップ - IsSuccess: {IsSuccess}, TranslatedText: '{TranslatedText}'",
                    translationResult?.IsSuccess, translationResult?.TranslatedText);
                Console.WriteLine($"🎯 [PHASE3.3] 翻訳失敗 - IsSuccess: {translationResult?.IsSuccess}, TranslatedText: '{translationResult?.TranslatedText}'");

                // 🎯 [TRANSLATION_DEBUG_LOG] 翻訳失敗をデバッグログに出力
                DebugLogUtility.WriteLog($"🌐 [TRANSLATION_FAILED] 翻訳失敗によりイベント発行スキップ");
                DebugLogUtility.WriteLog($"🌐 [TRANSLATION_FAILED] エンジンIsSuccess: {translationResult?.IsSuccess}, TranslatedText: '{translationResult?.TranslatedText}'");
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