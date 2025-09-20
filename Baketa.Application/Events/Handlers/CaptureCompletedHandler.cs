using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Memory;
using Baketa.Core.Abstractions.Processing;
using Baketa.Core.Events.EventTypes;
using Baketa.Core.Models.Processing;
using Baketa.Core.Settings;
using Baketa.Infrastructure.OCR.PaddleOCR.Diagnostics;
using Baketa.Core.Abstractions.Translation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using System;
using System.Threading.Tasks;

namespace Baketa.Application.Events.Handlers;

/// <summary>
/// キャプチャ完了イベントハンドラー
/// Phase 26-3: ITextChunkAggregatorService抽象化によるClean Architecture準拠
/// P1: 段階的フィルタリングシステム統合済み
/// </summary>
public class CaptureCompletedHandler : IEventProcessor<CaptureCompletedEvent>
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly ISmartProcessingPipelineService? _smartPipeline;
        private readonly ILogger<CaptureCompletedHandler>? _logger;
        private readonly IOptionsMonitor<ProcessingPipelineSettings>? _settings;
        private readonly ImageDiagnosticsSaver? _diagnosticsSaver;
        private readonly IOptionsMonitor<RoiDiagnosticsSettings>? _roiSettings;
        private readonly IImageToReferencedSafeImageConverter? _imageToReferencedConverter;
        private readonly ITextChunkAggregatorService _chunkAggregatorService;
        private readonly IConfiguration _configuration;

        public CaptureCompletedHandler(
            IEventAggregator eventAggregator,
            ITextChunkAggregatorService chunkAggregatorService,
            IConfiguration configuration,
            ISmartProcessingPipelineService? smartPipeline = null,
            ILogger<CaptureCompletedHandler>? logger = null,
            IOptionsMonitor<ProcessingPipelineSettings>? settings = null,
            ImageDiagnosticsSaver? diagnosticsSaver = null,
            IOptionsMonitor<RoiDiagnosticsSettings>? roiSettings = null,
            IImageToReferencedSafeImageConverter? imageToReferencedConverter = null)
        {
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _chunkAggregatorService = chunkAggregatorService ?? throw new ArgumentNullException(nameof(chunkAggregatorService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _smartPipeline = smartPipeline;
            _logger = logger;
            _settings = settings;
            _diagnosticsSaver = diagnosticsSaver;
            _roiSettings = roiSettings;
            _imageToReferencedConverter = imageToReferencedConverter;
        }
        
        /// <inheritdoc />
        public int Priority => 0;
        
        /// <inheritdoc />
        public bool SynchronousExecution => false;

    /// <inheritdoc />
    public async Task HandleAsync(CaptureCompletedEvent eventData)
        {
            // NULLチェック
            ArgumentNullException.ThrowIfNull(eventData);

            try
            {
                _logger?.LogDebug("キャプチャ完了イベント処理開始 - Image: {Width}x{Height}",
                    eventData.CapturedImage.Width, eventData.CapturedImage.Height);

                // 🎯 キャプチャ画像保存（設定が有効な場合）
                await SaveCaptureImagesIfEnabledAsync(eventData).ConfigureAwait(false);

                // 🔄 P1: 段階的フィルタリングシステム使用判定
                if (_smartPipeline != null)
                {
                    _logger?.LogDebug("段階的フィルタリングシステム使用開始");
                    await HandleWithStagedFilteringAsync(eventData).ConfigureAwait(false);
                }
                else
                {
                    _logger?.LogDebug("従来処理モード使用（段階的フィルタリング無効）");
                    await HandleLegacyModeAsync(eventData).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "CaptureCompletedHandler処理エラー: {ErrorType} - {ErrorMessage}", ex.GetType().Name, ex.Message);
                
                // エラー通知イベントを発行
                var errorNotificationEvent = new NotificationEvent(
                    $"キャプチャ後の処理でエラーが発生しました: {ex.Message}",
                    NotificationType.Error,
                    "処理エラー",
                    displayTime: 5000);
                    
                try
                {
                    await _eventAggregator.PublishAsync(errorNotificationEvent).ConfigureAwait(false);
                }
                catch
                {
                    // 通知イベント発行失敗は無視（ログ出力済み）
                }
                
                // 例外は再スローして上位で処理
                throw;
            }
        }

    /// <summary>
    /// P1: 段階的フィルタリングシステムを使用した処理
    /// </summary>
    private async Task HandleWithStagedFilteringAsync(CaptureCompletedEvent eventData)
    {
        ProcessingPipelineInput? input = null;
        ReferencedSafeImage? referencedSafeImage = null;
        
        try
        {
            // 🎯 Phase 3.15: IImageToReferencedSafeImageConverter を使用した統合変換
            _logger?.LogInformation("🎯 [PHASE3.15] CapturedImage型確認 - Type={ImageType}, Converter={ConverterAvailable}",
                eventData.CapturedImage?.GetType().Name ?? "null", _imageToReferencedConverter != null);

            if (_imageToReferencedConverter != null && eventData.CapturedImage != null)
            {
                try
                {
                    // Phase 3.15: 統合コンバーターで直接IImage→ReferencedSafeImage変換
                    _logger?.LogDebug("🎯 [PHASE3.15] IImage→ReferencedSafeImage変換開始");

                    referencedSafeImage = await _imageToReferencedConverter.ConvertAsync(
                        eventData.CapturedImage
                    ).ConfigureAwait(false);

                    _logger?.LogInformation("🎯 [PHASE3.15] ReferencedSafeImage作成完了 - 初期参照カウント: {RefCount}, サイズ: {Width}x{Height}",
                        referencedSafeImage.ReferenceCount, referencedSafeImage.Width, referencedSafeImage.Height);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "🎯 [PHASE3.15] ReferencedSafeImage作成失敗 - フォールバックして従来のIImage使用");
                    referencedSafeImage = null;
                }
            }

            if (referencedSafeImage == null)
            {
                _logger?.LogWarning("🎯 [PHASE3.15] ReferencedSafeImage作成不可 - 従来のIImage使用: Converter={ConverterAvailable}, ImageType={ImageType}",
                    _imageToReferencedConverter != null, eventData.CapturedImage?.GetType().Name ?? "null");
            }

            // 🚨 UltraThink Phase 59 緊急修正: using ブロック削除（非同期処理中の早期Dispose防止）
            input = new ProcessingPipelineInput
            {
                // 🎯 Phase 3.11: ReferencedSafeImage または従来のIImage を設定
                CapturedImage = referencedSafeImage ?? eventData.CapturedImage,
                CaptureRegion = eventData.CaptureRegion,
                SourceWindowHandle = IntPtr.Zero, // TODO: eventData から取得
                CaptureTimestamp = DateTime.UtcNow,
                // 🔧 [PHASE3.2_FIX] 画像所有権をfalseに変更（OCR処理完了まで画像を保持）
                OwnsImage = false,
                // TODO: 前回のハッシュやテキストを設定（キャッシュ機構が必要）
                Options = new ProcessingPipelineOptions
                {
                    // Gemini フィードバック反映: 設定から取得（ハードコーディング回避）
                    EnableStaging = _settings?.CurrentValue?.EnableStaging ?? true,
                    EnablePerformanceMetrics = _settings?.CurrentValue?.EnablePerformanceMetrics ?? true,
                    EnableEarlyTermination = _settings?.CurrentValue?.EnableEarlyTermination ?? true
                }
            };

            // 段階的処理パイプライン実行
            // 🔧 [PHASE3.2_FIX] 非同期処理完了まで画像を保持、完了後に手動でDispose
            var pipelineResult = await _smartPipeline!.ExecuteAsync(input).ConfigureAwait(false);
            
            _logger?.LogDebug("段階的処理完了 - 最終段階: {LastStage}, 総処理時間: {TotalTime}ms, 早期終了: {EarlyTerminated}",
                pipelineResult.LastCompletedStage, pipelineResult.TotalElapsedTime.TotalMilliseconds, pipelineResult.Metrics.EarlyTerminated);

            // 段階別結果に応じたイベント発行
            await PublishStageSpecificEventsAsync(pipelineResult, eventData).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "段階的フィルタリングシステム処理エラー");

            // フォールバック: 従来処理モード
            // 🎯 画像が破棄されていないか確認してからフォールバック処理を実行
            try
            {
                // 画像の状態を確認（Width/Height アクセスで破棄状態をチェック）
                if (eventData.CapturedImage != null)
                {
                    var _ = eventData.CapturedImage.Width; // 破棄されていればObjectDisposedException が発生
                    _logger?.LogWarning("段階的処理失敗 - 従来処理モードにフォールバック");
                    await HandleLegacyModeAsync(eventData).ConfigureAwait(false);
                }
                else
                {
                    _logger?.LogWarning("段階的処理失敗 - 画像が既にnull のためフォールバック不可");
                }
            }
            catch (ObjectDisposedException)
            {
                _logger?.LogWarning("段階的処理失敗 - 画像が既に破棄されているためフォールバック不可");
            }
        }
        finally
        {
            // 🔧 [PHASE3.2_FIX] ProcessingPipelineInput の手動Dispose
            // OwnsImage=false なので画像自体は破棄されず、ProcessingPipelineInput オブジェクトのみ破棄
            input?.Dispose();
            
            // 🔧 [PHASE3.2_FIX] ReferencedSafeImage の参照カウント管理を修正
            // OCR処理完了後のみ参照を解放（処理中の早期解放を防止）
            if (referencedSafeImage != null)
            {
                var finalRefCount = referencedSafeImage.ReferenceCount;
                _logger?.LogInformation("🔧 [PHASE3.2_FIX] CaptureCompletedHandler処理完了 - 参照解放前カウント: {RefCount}",
                    finalRefCount);
                
                // OCR処理が完全に終了してから参照を解放
                referencedSafeImage.ReleaseReference();
                
                _logger?.LogInformation("🔧 [PHASE3.2_FIX] CaptureCompletedHandler参照解放完了 - 最終カウント: {RefCount}",
                    referencedSafeImage.ReferenceCount);
            }
        }
    }

    /// <summary>
    /// 段階別結果に応じたイベント発行
    /// </summary>
    private async Task PublishStageSpecificEventsAsync(ProcessingPipelineResult result, CaptureCompletedEvent eventData)
    {
        try
        {
            // 🔍 [PHASE24] PublishStageSpecificEventsAsync条件分岐デバッグ開始
            _logger?.LogInformation("🔍 [PHASE24] PublishStageSpecificEventsAsync実行 - LastStage: {LastStage}, OcrResult: {OcrResult}, OcrSuccess: {OcrSuccess}",
                result.LastCompletedStage,
                result.OcrResult != null ? "NotNull" : "Null",
                result.OcrResult?.Success ?? false);

            _logger?.LogInformation("🔍 [PHASE26] TextChunkAggregatorService状態確認 - Service: {ServiceState}, IsEnabled: {IsEnabled}, TextChunks: {TextChunksCount}",
                _chunkAggregatorService != null ? "NotNull" : "Null",
                _chunkAggregatorService?.IsFeatureEnabled ?? false,
                result.OcrResult?.TextChunks?.Count ?? 0);

            // キャプチャ完了通知
            var captureNotification = new NotificationEvent(
                $"キャプチャ完了 - 処理時間: {result.TotalElapsedTime.TotalMilliseconds:F1}ms",
                NotificationType.Success,
                "段階的処理",
                displayTime: 2000);
            await _eventAggregator.PublishAsync(captureNotification).ConfigureAwait(false);

            // OCR完了時イベント - 🚀 Phase 22: TimedChunkAggregator統合処理
            _logger?.LogInformation("🔍 [PHASE24] OCR処理条件チェック - StageCheck: {StageCheck}, SuccessCheck: {SuccessCheck}",
                result.LastCompletedStage >= ProcessingStageType.OcrExecution,
                result.OcrResult?.Success == true);

            if (result.LastCompletedStage >= ProcessingStageType.OcrExecution && result.OcrResult?.Success == true)
            {
                _logger?.LogInformation("🚀 [PHASE22] OCR完了 - TimedChunkAggregator統合処理開始");

                // 🎯 Phase 22: EnhancedBatchOcrIntegrationServiceによるTimedChunkAggregator統合
                if (result.OcrResult.TextChunks?.Count > 0)
                {
                    _logger?.LogInformation("🎯 [PHASE22] TextChunks → TimedChunkAggregator送信開始 - チャンク数: {ChunkCount}",
                        result.OcrResult.TextChunks.Count);

                    // TextChunksをEnhancedBatchOcrIntegrationService経由でTimedChunkAggregatorに送信
                    int successfulChunks = 0;
                    foreach (var chunk in result.OcrResult.TextChunks)
                    {
                        try
                        {
                            // 🎯 Phase B緊急修正: OcrTextRegion → TextChunk変換アダプター
                            if (chunk is Baketa.Core.Abstractions.Translation.TextChunk textChunk)
                            {
                                // 🚀 Phase 26: 既存のTextChunk処理
                                _logger?.LogDebug("📥 [PHASE26] TextChunk送信 - ID: {ChunkId}, テキスト: '{Text}'",
                                    textChunk.ChunkId, textChunk.CombinedText);

                                var addedSuccessfully = await _chunkAggregatorService.TryAddTextChunkAsync(
                                    textChunk,
                                    CancellationToken.None
                                ).ConfigureAwait(false);

                                if (addedSuccessfully)
                                {
                                    successfulChunks++;
                                    _logger?.LogDebug("✅ [PHASE22] TextChunk送信成功 - ID: {ChunkId}", textChunk.ChunkId);
                                }
                                else
                                {
                                    _logger?.LogWarning("⚠️ [PHASE22] TextChunk送信失敗 - ID: {ChunkId}", textChunk.ChunkId);
                                }
                            }
                            else if (chunk is Baketa.Core.Abstractions.OCR.OcrTextRegion ocrRegion)
                            {
                                // 🚀 Phase B緊急修正: OcrTextRegion → TextChunk変換アダプター
                                _logger?.LogDebug("🔄 [PHASE_B_FIX] OcrTextRegion変換開始 - テキスト: '{Text}', 信頼度: {Confidence}",
                                    ocrRegion.Text, ocrRegion.Confidence);

                                // OcrTextRegion → PositionedTextResult変換
                                var positionedResult = new Baketa.Core.Abstractions.OCR.Results.PositionedTextResult
                                {
                                    Text = ocrRegion.Text,
                                    BoundingBox = ocrRegion.Bounds,
                                    Confidence = (float)ocrRegion.Confidence,
                                    ChunkId = Random.Shared.Next(1000000, 9999999),
                                    ProcessingTime = TimeSpan.Zero,
                                    DetectedLanguage = "jpn" // デフォルト言語
                                };

                                // PositionedTextResult → TextChunk変換
                                var convertedTextChunk = new Baketa.Core.Abstractions.Translation.TextChunk
                                {
                                    ChunkId = positionedResult.ChunkId,
                                    TextResults = [positionedResult],
                                    CombinedBounds = positionedResult.BoundingBox,
                                    CombinedText = positionedResult.Text,
                                    SourceWindowHandle = IntPtr.Zero, // TODO: eventData から取得（一時的にダミー値使用）
                                    DetectedLanguage = positionedResult.DetectedLanguage
                                };

                                _logger?.LogDebug("✅ [PHASE_B_FIX] OcrTextRegion変換完了 - ChunkId: {ChunkId}, テキスト: '{Text}'",
                                    convertedTextChunk.ChunkId, convertedTextChunk.CombinedText);

                                // 変換されたTextChunkをTimedChunkAggregatorに送信
                                var addedSuccessfully = await _chunkAggregatorService.TryAddTextChunkAsync(
                                    convertedTextChunk,
                                    CancellationToken.None
                                ).ConfigureAwait(false);

                                if (addedSuccessfully)
                                {
                                    successfulChunks++;
                                    _logger?.LogDebug("🎯 [PHASE_B_FIX] 変換TextChunk送信成功 - ID: {ChunkId}", convertedTextChunk.ChunkId);
                                }
                                else
                                {
                                    _logger?.LogWarning("⚠️ [PHASE_B_FIX] 変換TextChunk送信失敗 - ID: {ChunkId}", convertedTextChunk.ChunkId);
                                }
                            }
                            else
                            {
                                _logger?.LogWarning("⚠️ [PHASE22] 未対応のChunk型 - Type: {ChunkType}",
                                    chunk?.GetType().Name ?? "null");
                            }
                        }
                        catch (Exception chunkEx)
                        {
                            _logger?.LogError(chunkEx, "❌ [PHASE22] TextChunk送信エラー - ChunkType: {ChunkType}",
                                chunk?.GetType().Name ?? "null");
                        }
                    }

                    _logger?.LogInformation("📊 [PHASE22] TextChunk送信統計 - 成功: {Successful}/{Total}",
                        successfulChunks, result.OcrResult.TextChunks.Count);

                    _logger?.LogInformation("📤 [PHASE22] TextChunks送信完了 - TimedChunkAggregator集約待機中");
                    Console.WriteLine("📥 [PHASE22] TimedChunkAggregator統合フロー - 集約完了後に翻訳処理が実行されます");
                }
                else if (result.OcrResult.TextChunks?.Count > 0)
                {
                    // フォールバック: EnhancedBatchOcrIntegrationServiceが利用できない場合は従来のOCRCompletedEvent発行
                    _logger?.LogWarning("⚠️ [PHASE22] EnhancedBatchOcrIntegrationService利用不可 - 従来のOCRCompletedEvent発行にフォールバック");

                    var ocrResults = new List<Baketa.Core.Models.OCR.OcrResult>();
                    foreach (var chunk in result.OcrResult.TextChunks)
                    {
                        if (chunk is Baketa.Core.Abstractions.OCR.OcrTextRegion textRegion)
                        {
                            ocrResults.Add(Baketa.Core.Models.OCR.OcrResult.FromTextRegion(textRegion));
                        }
                        else if (!string.IsNullOrWhiteSpace(chunk?.ToString()))
                        {
                            ocrResults.Add(new Baketa.Core.Models.OCR.OcrResult(
                                text: chunk.ToString() ?? "",
                                bounds: System.Drawing.Rectangle.Empty,
                                confidence: 0.8f
                            ));
                        }
                    }

                    if (ocrResults.Count > 0)
                    {
                        var ocrCompletedEvent = new OcrCompletedEvent(
                            sourceImage: eventData.CapturedImage,
                            results: ocrResults.AsReadOnly(),
                            processingTime: result.OcrResult.ProcessingTime
                        );
                        await _eventAggregator.PublishAsync(ocrCompletedEvent).ConfigureAwait(false);
                        _logger?.LogInformation("🎯 [PHASE22] フォールバックOCRCompletedEvent発行完了");
                    }
                }
                else if (!string.IsNullOrWhiteSpace(result.OcrResult.DetectedText))
                {
                    // 最終フォールバック: DetectedTextのみ利用可能な場合
                    _logger?.LogInformation("🔄 [PHASE22] DetectedTextフォールバック処理");
                    var fallbackResult = new Baketa.Core.Models.OCR.OcrResult(
                        text: result.OcrResult.DetectedText,
                        bounds: System.Drawing.Rectangle.Empty,
                        confidence: 0.8f
                    );

                    var ocrCompletedEvent = new OcrCompletedEvent(
                        sourceImage: eventData.CapturedImage,
                        results: [fallbackResult],
                        processingTime: result.OcrResult.ProcessingTime
                    );
                    await _eventAggregator.PublishAsync(ocrCompletedEvent).ConfigureAwait(false);
                    _logger?.LogInformation("🎯 [PHASE22] DetectedTextフォールバック完了");
                }
                else
                {
                    _logger?.LogWarning("⚠️ [PHASE22] OCR結果が空 - 処理スキップ");
                }
            }

            // 翻訳完了時イベント - 🎯 [UltraThink修正] 翻訳実行段階を通過した場合にイベント発行
            _logger?.LogInformation("🎯 [UltraThink] 翻訳完了条件チェック - LastStage: {LastStage}, TranslationSuccess: {Success}",
                result.LastCompletedStage, result.TranslationResult?.Success ?? false);

            if (result.LastCompletedStage >= ProcessingStageType.TranslationExecution && result.TranslationResult?.Success == true)
            {
                // 設定から言語を動的取得
                var defaultSourceLanguage = _configuration.GetValue<string>("Translation:DefaultSourceLanguage", "en");
                var defaultTargetLanguage = _configuration.GetValue<string>("Translation:DefaultTargetLanguage", "ja");

                var translationEvent = new TranslationCompletedEvent(
                    sourceText: result.OcrResult?.DetectedText ?? "",
                    translatedText: result.TranslationResult.TranslatedText,
                    sourceLanguage: defaultSourceLanguage,
                    targetLanguage: defaultTargetLanguage,
                    processingTime: result.TranslationResult.ProcessingTime,
                    engineName: result.TranslationResult.EngineUsed);
                await _eventAggregator.PublishAsync(translationEvent).ConfigureAwait(false);
                
                _logger?.LogDebug("TranslationCompletedEvent発行 - 翻訳テキスト長: {TextLength}", result.TranslationResult.TranslatedText.Length);

                // 🎯 UltraThink修正: UI表示用のTranslationWithBoundsCompletedEventも発行
                var boundsEvent = new Baketa.Core.Events.EventTypes.TranslationWithBoundsCompletedEvent(
                    sourceText: result.OcrResult?.DetectedText ?? "",
                    translatedText: result.TranslationResult.TranslatedText,
                    sourceLanguage: defaultSourceLanguage,
                    targetLanguage: defaultTargetLanguage,
                    bounds: eventData.CaptureRegion, // キャプチャ領域を座標情報として使用
                    confidence: 0.95f, // デフォルト信頼度（実装時にOCR信頼度から設定）
                    engineName: result.TranslationResult.EngineUsed);

                await _eventAggregator.PublishAsync(boundsEvent).ConfigureAwait(false);
                
                _logger?.LogInformation("🎯 [UltraThink] TranslationWithBoundsCompletedEvent発行完了 - ID: {EventId}, Bounds: ({X},{Y},{Width},{Height})", 
                    boundsEvent.Id, eventData.CaptureRegion.X, eventData.CaptureRegion.Y, 
                    eventData.CaptureRegion.Width, eventData.CaptureRegion.Height);
                Console.WriteLine($"🎯 [UltraThink] TranslationWithBoundsCompletedEvent発行 - ID: {boundsEvent.Id}");
                Console.WriteLine($"🎯 [UltraThink] 座標情報: ({eventData.CaptureRegion.X},{eventData.CaptureRegion.Y}) サイズ: {eventData.CaptureRegion.Width}x{eventData.CaptureRegion.Height}");
            }

            // パフォーマンスメトリクス通知（デバッグ情報）
            if (result.Metrics.EarlyTerminated)
            {
                var performanceNotification = new NotificationEvent(
                    $"性能最適化: {result.Metrics.SkippedStages}段階スキップ、CPU削減: {result.Metrics.EstimatedCpuReduction:P0}",
                    NotificationType.Information,
                    "パフォーマンス",
                    displayTime: 3000);
                await _eventAggregator.PublishAsync(performanceNotification).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "段階別イベント発行エラー");
        }
    }

    /// <summary>
    /// 従来処理モード（段階的フィルタリング無効時）
    /// </summary>
    private async Task HandleLegacyModeAsync(CaptureCompletedEvent eventData)
    {
        // Phase 1: 画像変化検知によるOCR処理制御
        if (eventData.ImageChangeSkipped)
        {
            _logger?.LogDebug("画像変化なし - OCR処理スキップ");
            
            var skipNotification = new NotificationEvent(
                "画像変化なし - OCR処理をスキップしました",
                NotificationType.Information,
                "OCRスキップ",
                displayTime: 1000);
                
            await _eventAggregator.PublishAsync(skipNotification).ConfigureAwait(false);
            return; // OCRRequestEventを発行せずに終了
        }
        
        // キャプチャ完了通知
        var notificationEvent = new NotificationEvent(
            $"キャプチャが完了しました: {eventData.CapturedImage.Width}x{eventData.CapturedImage.Height}",
            NotificationType.Success,
            "キャプチャ完了",
            displayTime: 3000);
            
        await _eventAggregator.PublishAsync(notificationEvent).ConfigureAwait(false);
        
        // OCR処理要求イベント発行（従来方式）
        _logger?.LogDebug("OCR要求イベント発行 - Image: {Width}x{Height}", 
            eventData.CapturedImage.Width, eventData.CapturedImage.Height);
        
        var ocrRequestEvent = new OcrRequestEvent(
            eventData.CapturedImage,
            eventData.CaptureRegion,
            targetWindowHandle: null
        );
        
        await _eventAggregator.PublishAsync(ocrRequestEvent).ConfigureAwait(false);
        
        _logger?.LogDebug("OcrRequestEvent発行完了");
    }

    /// <summary>
    /// キャプチャ画像保存（設定が有効な場合）
    /// </summary>
    private async Task SaveCaptureImagesIfEnabledAsync(CaptureCompletedEvent eventData)
    {
        try
        {
            // 設定チェック
            var roiSettings = _roiSettings?.CurrentValue;
            if (roiSettings == null || !roiSettings.EnableCaptureImageSaving || _diagnosticsSaver == null)
            {
                _logger?.LogTrace("キャプチャ画像保存が無効またはサービスが利用不可");
                return;
            }

            // セッションID生成
            var sessionId = Guid.NewGuid().ToString("N")[..8];

            // 元画像のバイト配列取得
            var originalImageBytes = await eventData.CapturedImage.ToByteArrayAsync().ConfigureAwait(false);
            var originalWidth = eventData.CapturedImage.Width;
            var originalHeight = eventData.CapturedImage.Height;

            _logger?.LogDebug("キャプチャ画像保存開始 - セッションID: {SessionId}, サイズ: {Width}x{Height}, バイト数: {Bytes}",
                sessionId, originalWidth, originalHeight, originalImageBytes.Length);

            byte[]? scaledImageBytes = null;
            int? scaledWidth = null;
            int? scaledHeight = null;

            // 縮小画像保存が有効な場合の処理
            if (roiSettings.EnableScaledImageSaving)
            {
                // TODO: 縮小画像の取得方法を実装する必要がある
                // 現在はOCR処理時に縮小されるが、キャプチャ時点では元サイズのみ利用可能
                _logger?.LogTrace("縮小画像保存が有効ですが、キャプチャ時点では元画像のみ保存します");
            }

            // 画像保存実行
            await _diagnosticsSaver.SaveCaptureImagesAsync(
                originalImageBytes,
                scaledImageBytes,
                sessionId,
                originalWidth,
                originalHeight,
                scaledWidth,
                scaledHeight).ConfigureAwait(false);

            _logger?.LogInformation("キャプチャ画像保存完了 - セッションID: {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "キャプチャ画像保存中にエラーが発生しました: {ErrorType} - {ErrorMessage}",
                ex.GetType().Name, ex.Message);

            // 画像保存エラーはメインの処理を妨げない（ログ出力のみ）
        }
    }
}
