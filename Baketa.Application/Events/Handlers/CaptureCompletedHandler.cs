using System;
using System.IO;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Memory;
using Baketa.Core.Abstractions.Processing;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Events.EventTypes;
using Baketa.Core.Models.Processing;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    private readonly IOptionsMonitor<RoiDiagnosticsSettings>? _roiSettings;
    private readonly IImageToReferencedSafeImageConverter? _imageToReferencedConverter;
    private readonly ITextChunkAggregatorService _chunkAggregatorService;
    private readonly ILanguageConfigurationService _languageConfig;
    private readonly ITranslationModeService? _translationModeService;

    public CaptureCompletedHandler(
        IEventAggregator eventAggregator,
        ITextChunkAggregatorService chunkAggregatorService,
        ILanguageConfigurationService languageConfig,
        ISmartProcessingPipelineService? smartPipeline = null,
        ILogger<CaptureCompletedHandler>? logger = null,
        IOptionsMonitor<ProcessingPipelineSettings>? settings = null,
        IOptionsMonitor<RoiDiagnosticsSettings>? roiSettings = null,
        IImageToReferencedSafeImageConverter? imageToReferencedConverter = null,
        ITranslationModeService? translationModeService = null)
    {
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _chunkAggregatorService = chunkAggregatorService ?? throw new ArgumentNullException(nameof(chunkAggregatorService));
        _languageConfig = languageConfig ?? throw new ArgumentNullException(nameof(languageConfig));
        _smartPipeline = smartPipeline;
        _logger = logger;
        _settings = settings;
        _roiSettings = roiSettings;
        _imageToReferencedConverter = imageToReferencedConverter;
        _translationModeService = translationModeService;
    }

    /// <inheritdoc />
    public int Priority => 0;

    /// <inheritdoc />
    public bool SynchronousExecution => false;

    /// <inheritdoc />
    public async Task HandleAsync(CaptureCompletedEvent eventData, CancellationToken cancellationToken = default)
    {
        // NULLチェック
        ArgumentNullException.ThrowIfNull(eventData);

        // 🔥 [PHASE5] ROI関連チェック削除 - ROI廃止により不要

        try
        {
            _logger?.LogDebug("キャプチャ完了イベント処理開始 - Image: {Width}x{Height}",
                eventData.CapturedImage.Width, eventData.CapturedImage.Height);

            // 🎯 キャプチャ画像保存（設定が有効な場合）
            await SaveCaptureImagesIfEnabledAsync(eventData).ConfigureAwait(false);

            // 🔥 [PERFORMANCE_FIX] OCR重複実行削除 - TranslationOrchestrationServiceが独自にOCR実行
            //
            // **削除理由:**
            // 旧アーキテクチャ（CaptureCompletedEvent → OCR → 翻訳）の名残として残っていたが、
            // 現在のアーキテクチャではTranslationOrchestrationService → CoordinateBasedTranslationService →
            // SmartProcessingPipelineServiceが正規ルートとしてOCR実行するため、この処理は完全に冗長。
            //
            // **影響範囲:**
            // - Singleshotモード: TranslationOrchestrationServiceが直接OCR実行（影響なし）
            // - Liveモード: 同様にTranslationOrchestrationServiceが実行（影響なし）
            // - 画面変化検知: 同様にTranslationOrchestrationServiceが実行（影響なし）
            //
            // **期待効果:**
            // - OCR処理時間50%削減: 4.0秒 → 2.0秒
            // - ROI画像保存50%削減: 2.4秒 → 1.2秒（開発ビルド）
            // - 合計削減: 3.2秒削減（40%改善）
            //
            // **削除されたコード:**
            // if (_smartPipeline != null)
            // {
            //     _logger?.LogDebug("段階的フィルタリングシステム使用開始");
            //     await HandleWithStagedFilteringAsync(eventData).ConfigureAwait(false);
            // }
            // else
            // {
            //     _logger?.LogDebug("従来処理モード使用（段階的フィルタリング無効）");
            //     await HandleLegacyModeAsync(eventData).ConfigureAwait(false);
            // }

            _logger?.LogInformation("🔥 [PERFORMANCE_FIX] CaptureCompletedHandlerのOCR重複実行を削除 - TranslationOrchestrationServiceが正規ルートでOCR実行");
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

                    // 🔧 [SINGLESHOT_FIX] 個別翻訳モード時は早期終了を無効化（Singleshotで毎回OCR実行を保証）
                    EnableEarlyTermination = ShouldSkipIntegratedTranslation() ? false : (_settings?.CurrentValue?.EnableEarlyTermination ?? true),

                    // 🔧 [SINGLESHOT_FIX] Singleshotモード時は強制完全実行を有効化（画面変化に関係なくOCRを実行）
                    ForceCompleteExecution = ShouldSkipIntegratedTranslation(),

                    // UltraThink Phase 3: 個別翻訳実行時の統合翻訳スキップ制御
                    // グルーピング後のOCR結果が複数存在する場合は個別翻訳を実行するため統合翻訳をスキップ
                    SkipIntegratedTranslation = ShouldSkipIntegratedTranslation()

                    // 🔥 [PHASE5] ROI関連設定削除 - ROI廃止により不要
                }
            };

            // 🐛 [DEBUG] パイプラインオプション確認
            _logger?.LogInformation("🔍 [PIPELINE_OPTIONS] ForceCompleteExecution={ForceCompleteExecution}, EnableEarlyTermination={EnableEarlyTermination}, SkipIntegratedTranslation={SkipIntegratedTranslation}",
                input.Options.ForceCompleteExecution, input.Options.EnableEarlyTermination, input.Options.SkipIntegratedTranslation);

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
                    _logger?.LogInformation("🎯 [PHASE1_FIX] 統合OcrResult生成処理開始 - チャンク数: {ChunkCount}",
                        result.OcrResult.TextChunks.Count);

                    // 🔥 Phase A+実装: 距離ベースグルーピングによる適切な統合
                    var ocrResults = CreateOptimizedOcrResults(result.OcrResult.TextChunks);
                    if (ocrResults.Count > 0)
                    {
                        _logger?.LogInformation("🎯 [PHASE_A+] 距離ベースグルーピング完了 - グループ数: {GroupCount}",
                            ocrResults.Count);

                        foreach (var ocrResult in ocrResults)
                        {
                            _logger?.LogInformation("🎯 [PHASE_A+] グループ - テキスト: '{Text}', Bounds: ({X},{Y},{W},{H})",
                                ocrResult.Text.Length > 50 ? ocrResult.Text[..50] + "..." : ocrResult.Text,
                                ocrResult.Bounds.X, ocrResult.Bounds.Y,
                                ocrResult.Bounds.Width, ocrResult.Bounds.Height);
                        }

                        var ocrCompletedEvent = new OcrCompletedEvent(
                            sourceImage: eventData.CapturedImage,
                            results: ocrResults.AsReadOnly(),
                            processingTime: result.OcrResult.ProcessingTime
                        );
                        await _eventAggregator.PublishAsync(ocrCompletedEvent).ConfigureAwait(false);
                        _logger?.LogInformation("🎯 [PHASE_A+] OcrCompletedEvent発行完了 - 近接チャンクのみ統合");
                    }

                    /* 🔥 個別チャンク送信を無効化（分離表示の原因）
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
                    */
                }
#pragma warning disable CS0162 // 到達不可能コード: 意図的に無効化されたフォールバックロジック
                else if (false) // 🔥 到達不可能コード無効化（Line 267と同じ条件のため絶対到達しない）
                {
                    // フォールバック: EnhancedBatchOcrIntegrationServiceが利用できない場合は従来のOCRCompletedEvent発行
                    _logger?.LogWarning("⚠️ [PHASE22] EnhancedBatchOcrIntegrationService利用不可 - 従来のOCRCompletedEvent発行にフォールバック");

                    // 🎯 Phase 1実装: 統合OcrResult生成（分離表示問題解決）
                    var ocrResults = new List<Baketa.Core.Models.OCR.OcrResult>();
                    var unifiedOcrResult = CreateUnifiedOcrResult(result.OcrResult.TextChunks);
                    if (unifiedOcrResult != null)
                    {
                        ocrResults.Add(unifiedOcrResult);
                        _logger?.LogInformation("🎯 [PHASE1] 統合OcrResult生成完了 - テキスト: '{Text}', Bounds: ({X},{Y},{W},{H})",
                            unifiedOcrResult.Text, unifiedOcrResult.Bounds.X, unifiedOcrResult.Bounds.Y,
                            unifiedOcrResult.Bounds.Width, unifiedOcrResult.Bounds.Height);
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
#pragma warning restore CS0162
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

            // 翻訳完了時イベント - 🎯 [DUPLICATE_TRANSLATION_FIX]
            // 新アーキテクチャ(AggregatedChunksReadyEventHandler)が直接オーバーレイ表示を行うため、
            // この古いイベント発行パスは二重翻訳の原因となるため無効化する。
            // if (result.LastCompletedStage >= ProcessingStageType.TranslationExecution && result.TranslationResult?.Success == true)
            // {
            //     // 統一言語設定サービスから言語ペア取得
            //     var languagePair = _languageConfig.GetCurrentLanguagePair();
            //
            //     var translationEvent = new TranslationCompletedEvent(
            //         sourceText: result.OcrResult?.DetectedText ?? "",
            //         translatedText: result.TranslationResult.TranslatedText,
            //         sourceLanguage: languagePair.Source.DisplayName,
            //         targetLanguage: languagePair.Target.DisplayName,
            //         processingTime: result.TranslationResult.ProcessingTime,
            //         engineName: result.TranslationResult.EngineUsed);
            //     await _eventAggregator.PublishAsync(translationEvent).ConfigureAwait(false);
            //     
            //     _logger?.LogDebug("TranslationCompletedEvent発行 - 翻訳テキスト長: {TextLength}", result.TranslationResult.TranslatedText.Length);
            //
            //     // 🎯 UltraThink修正: UI表示用のTranslationWithBoundsCompletedEventも発行
            //     var boundsEvent = new Baketa.Core.Events.EventTypes.TranslationWithBoundsCompletedEvent(
            //         sourceText: result.OcrResult?.DetectedText ?? "",
            //         translatedText: result.TranslationResult.TranslatedText,
            //         sourceLanguage: languagePair.Source.DisplayName,
            //         targetLanguage: languagePair.Target.DisplayName,
            //         bounds: eventData.CaptureRegion, // キャプチャ領域を座標情報として使用
            //         confidence: 0.95f, // デフォルト信頼度（実装時にOCR信頼度から設定）
            //         engineName: result.TranslationResult.EngineUsed);
            //
            //     await _eventAggregator.PublishAsync(boundsEvent).ConfigureAwait(false);
            //     
            //     _logger?.LogInformation("🎯 [UltraThink] TranslationWithBoundsCompletedEvent発行完了 - ID: {EventId}, Bounds: ({X},{Y},{Width},{Height})", 
            //         boundsEvent.Id, eventData.CaptureRegion.X, eventData.CaptureRegion.Y, 
            //         eventData.CaptureRegion.Width, eventData.CaptureRegion.Height);
            //     Console.WriteLine($"🎯 [UltraThink] TranslationWithBoundsCompletedEvent発行 - ID: {boundsEvent.Id}");
            //     Console.WriteLine($"🎯 [UltraThink] 座標情報: ({eventData.CaptureRegion.X},{eventData.CaptureRegion.Y}) サイズ: {eventData.CaptureRegion.Width}x{eventData.CaptureRegion.Height}");
            // }

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
    /// NOTE: [PP-OCRv5削除] ImageDiagnosticsSaver削除に伴い無効化
    /// </summary>
    private Task SaveCaptureImagesIfEnabledAsync(CaptureCompletedEvent eventData)
    {
        // NOTE: [PP-OCRv5削除] ImageDiagnosticsSaver削除に伴い画像保存機能は無効化
        _logger?.LogTrace("キャプチャ画像保存は無効化されています（PP-OCRv5削除）");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Phase 2: OCRチャンクを統合した単一OcrResultを生成
    /// 分離表示問題解決のためのヘルパーメソッド
    /// </summary>
    private static Baketa.Core.Models.OCR.OcrResult? CreateUnifiedOcrResult(IEnumerable<object>? textChunks)
    {
        if (textChunks == null)
            return null;

        var validChunks = new List<(string text, System.Drawing.Rectangle bounds, float confidence)>();

        foreach (var chunk in textChunks)
        {
            if (chunk is Baketa.Core.Abstractions.OCR.OcrTextRegion textRegion)
            {
                if (!string.IsNullOrWhiteSpace(textRegion.Text))
                {
                    validChunks.Add((textRegion.Text, textRegion.Bounds, (float)textRegion.Confidence));
                }
            }
            else if (!string.IsNullOrWhiteSpace(chunk?.ToString()))
            {
                validChunks.Add((chunk.ToString() ?? "", System.Drawing.Rectangle.Empty, 0.8f));
            }
        }

        if (validChunks.Count == 0)
            return null;

        // Phase 2: テキスト統合 (Y座標→X座標順ソート + スペース結合)
        var sortedChunks = validChunks
            .OrderBy(c => c.bounds.Y)
            .ThenBy(c => c.bounds.X)
            .ToList();

        var combinedText = string.Join(" ", sortedChunks.Select(c => c.text));
        var combinedBounds = CalculateCombinedBounds(sortedChunks.Select(c => c.bounds));
        var averageConfidence = CalculateWeightedConfidence(sortedChunks.Select(c => c.confidence));

        return new Baketa.Core.Models.OCR.OcrResult(
            text: combinedText,
            bounds: combinedBounds,
            confidence: averageConfidence);
    }

    /// <summary>
    /// Phase 2: 複数のバウンディングボックスを統合
    /// </summary>
    private static System.Drawing.Rectangle CalculateCombinedBounds(IEnumerable<System.Drawing.Rectangle> bounds)
    {
        var validBounds = bounds.Where(b => !b.IsEmpty).ToList();
        if (validBounds.Count == 0)
            return System.Drawing.Rectangle.Empty;

        var firstBound = validBounds[0];
        var minX = firstBound.X;
        var minY = firstBound.Y;
        var maxX = firstBound.X + firstBound.Width;
        var maxY = firstBound.Y + firstBound.Height;

        for (int i = 1; i < validBounds.Count; i++)
        {
            var bound = validBounds[i];
            minX = Math.Min(minX, bound.X);
            minY = Math.Min(minY, bound.Y);
            maxX = Math.Max(maxX, bound.X + bound.Width);
            maxY = Math.Max(maxY, bound.Y + bound.Height);
        }

        return new System.Drawing.Rectangle(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>
    /// Phase 2: 加重平均信頼度計算
    /// </summary>
    private static float CalculateWeightedConfidence(IEnumerable<float> confidences)
    {
        var confidenceList = confidences.ToList();
        if (confidenceList.Count == 0)
            return 0.8f; // デフォルト値

        return confidenceList.Average();
    }

    /// <summary>
    /// Phase A+: 距離ベースグルーピングによる最適化されたOcrResult生成
    /// 近接チャンクのみ統合し、離れたチャンクは個別に保持
    /// </summary>
    private static List<Baketa.Core.Models.OCR.OcrResult> CreateOptimizedOcrResults(IEnumerable<object>? textChunks)
    {
        var results = new List<Baketa.Core.Models.OCR.OcrResult>();
        if (textChunks == null)
            return results;

        // チャンクを準備
        var validChunks = new List<(string text, System.Drawing.Rectangle bounds, float confidence)>();
        foreach (var chunk in textChunks)
        {
            if (chunk is Baketa.Core.Abstractions.OCR.OcrTextRegion textRegion)
            {
                if (!string.IsNullOrWhiteSpace(textRegion.Text))
                {
                    validChunks.Add((textRegion.Text, textRegion.Bounds, (float)textRegion.Confidence));
                }
            }
            else if (!string.IsNullOrWhiteSpace(chunk?.ToString()))
            {
                validChunks.Add((chunk.ToString() ?? "", System.Drawing.Rectangle.Empty, 0.8f));
            }
        }

        if (validChunks.Count == 0)
            return results;

        // 距離ベースグルーピング（UltraThink修正: 文章統合に最適化）
        var groups = GroupChunksByProximity(validChunks, threshold: 10.0f); // 10ピクセル以内を近接とみなす（文章内の単語のみ統合）

#if DEBUG
        // 🔍 [DEBUG] グルーピング結果デバッグ（ファイル出力）
        var debugLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "grouping_debug.txt");
        var debugText = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 [GROUPING_DEBUG] グルーピング結果: {validChunks.Count}個のチャンク → {groups.Count}個のグループ{Environment.NewLine}";

        for (int i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            debugText += $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 [GROUPING_DEBUG] グループ{i + 1}: {group.Count}個のチャンク{Environment.NewLine}";
            foreach (var chunk in group)
            {
                debugText += $"   - '{chunk.text}' at ({chunk.bounds.X},{chunk.bounds.Y}){Environment.NewLine}";
            }
        }

        try
        {
            System.IO.File.AppendAllText(debugLogPath, debugText);
        }
        catch (Exception ex)
        {
            // ログ出力エラーは無視
        }
#endif

        // 各グループを処理
        foreach (var group in groups)
        {
            if (group.Count == 1)
            {
                // 単独チャンクはそのまま
                var chunk = group[0];
                results.Add(new Baketa.Core.Models.OCR.OcrResult(
                    text: chunk.text,
                    bounds: chunk.bounds,
                    confidence: chunk.confidence));
            }
            else
            {
                // 近接チャンクは統合
                var sortedGroup = group
                    .OrderBy(c => c.bounds.Y)
                    .ThenBy(c => c.bounds.X)
                    .ToList();

                var combinedText = string.Join(" ", sortedGroup.Select(c => c.text));
                var combinedBounds = CalculateCombinedBounds(sortedGroup.Select(c => c.bounds));
                var averageConfidence = CalculateWeightedConfidence(sortedGroup.Select(c => c.confidence));

                results.Add(new Baketa.Core.Models.OCR.OcrResult(
                    text: combinedText,
                    bounds: combinedBounds,
                    confidence: averageConfidence));
            }
        }

        return results;
    }

    /// <summary>
    /// チャンクを近接度でグルーピング（改善版：過剰な連鎖的グルーピングを防止）
    /// </summary>
    private static List<List<(string text, System.Drawing.Rectangle bounds, float confidence)>>
        GroupChunksByProximity(List<(string text, System.Drawing.Rectangle bounds, float confidence)> chunks, float threshold)
    {
        var groups = new List<List<(string text, System.Drawing.Rectangle bounds, float confidence)>>();
        var visited = new bool[chunks.Count];

        for (int i = 0; i < chunks.Count; i++)
        {
            if (visited[i])
                continue;

            var group = new List<(string text, System.Drawing.Rectangle bounds, float confidence)>();
            group.Add(chunks[i]);
            visited[i] = true;

            // 現在のチャンクに直接近接するチャンクのみを追加（BFSではなく直接比較）
            for (int j = i + 1; j < chunks.Count; j++)
            {
                if (visited[j])
                    continue;

                // グループ内の全メンバーとの距離を確認（全てが近接している場合のみ追加）
                bool allProximate = true;
                foreach (var member in group)
                {
                    if (!IsProximate(member.bounds, chunks[j].bounds, threshold))
                    {
                        allProximate = false;
                        break;
                    }
                }

                if (allProximate)
                {
                    group.Add(chunks[j]);
                    visited[j] = true;
                }
            }

            groups.Add(group);
        }

        return groups;
    }

    /// <summary>
    /// 2つのバウンディングボックスが近接しているか判定
    /// </summary>
    private static bool IsProximate(System.Drawing.Rectangle rect1, System.Drawing.Rectangle rect2, float threshold)
    {
        // 矩形の中心間距離を計算
        var centerX1 = rect1.X + rect1.Width / 2.0f;
        var centerY1 = rect1.Y + rect1.Height / 2.0f;
        var centerX2 = rect2.X + rect2.Width / 2.0f;
        var centerY2 = rect2.Y + rect2.Height / 2.0f;

        var distance = Math.Sqrt(Math.Pow(centerX2 - centerX1, 2) + Math.Pow(centerY2 - centerY1, 2));

        // エッジ間の最短距離も考慮（より精密な判定）
        var horizontalGap = Math.Max(0, Math.Max(rect1.Left - rect2.Right, rect2.Left - rect1.Right));
        var verticalGap = Math.Max(0, Math.Max(rect1.Top - rect2.Bottom, rect2.Top - rect1.Bottom));
        var edgeDistance = Math.Sqrt(horizontalGap * horizontalGap + verticalGap * verticalGap);

        var isProximate = edgeDistance <= threshold;

#if DEBUG
        // 🔍 [DEBUG] 近接判定の詳細（ファイル出力）
        if (edgeDistance <= threshold + 5) // 閾値付近をログ出力
        {
            var debugLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "grouping_debug.txt");
            var debugText = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 [PROXIMITY_DEBUG] 近接判定: エッジ距離={edgeDistance:F1}px, 閾値={threshold}px → {(isProximate ? "統合" : "分離")}{Environment.NewLine}";
            debugText += $"   Rect1: ({rect1.X},{rect1.Y},{rect1.Width}x{rect1.Height}){Environment.NewLine}";
            debugText += $"   Rect2: ({rect2.X},{rect2.Y},{rect2.Width}x{rect2.Height}){Environment.NewLine}";

            try
            {
                System.IO.File.AppendAllText(debugLogPath, debugText);
            }
            catch (Exception ex)
            {
                // ログ出力エラーは無視
            }
        }
#endif

        return isProximate;
    }

    /// <summary>
    /// 統合翻訳をスキップすべきかどうかを判断
    /// UltraThink Phase 3: 個別翻訳実行時の重複防止制御
    /// 🔧 [SINGLESHOT_FIX] Singleshotモードの判定を追加（早期終了を無効化）
    /// </summary>
    /// <returns>統合翻訳をスキップする場合はtrue</returns>
    private bool ShouldSkipIntegratedTranslation()
    {
        // 現在の実装では、PriorityAwareOcrCompletedHandlerが有効な場合は常に個別翻訳を優先
        // 将来的には、より詳細な条件判断（チャンク数、グルーピング設定等）を追加可能

        try
        {
            // 🔧 [SINGLESHOT_FIX] Singleshotモード時は常に個別翻訳を優先（早期終了を無効化）
            _logger?.LogDebug("🔍 [SINGLESHOT_DEBUG] _translationModeService is null: {IsNull}", _translationModeService == null);

            if (_translationModeService != null)
            {
                var currentMode = _translationModeService.CurrentMode;
                _logger?.LogDebug("🔍 [SINGLESHOT_DEBUG] CurrentMode: {CurrentMode}", currentMode);

                if (currentMode == Core.Abstractions.Services.TranslationMode.Singleshot)
                {
                    _logger?.LogDebug("🎯 [SINGLESHOT_FIX] Singleshotモード検出 - 統合翻訳をスキップ、早期終了を無効化");
                    return true;
                }
            }

            // PriorityAwareOcrCompletedHandlerの存在確認
            // EventAggregatorに登録されているハンドラーをチェックすることは困難なため、
            // 設定ベースでの判断を実装

            // 段階的処理が有効で、グルーピング機能を使用する場合は個別翻訳を実行
            var enableStaging = _settings?.CurrentValue?.EnableStaging ?? true;

            if (enableStaging)
            {
                _logger?.LogDebug("🎯 [TRANSLATION_CONTROL] 個別翻訳優先モード - 統合翻訳をスキップ");
                return true;
            }

            _logger?.LogDebug("🎯 [TRANSLATION_CONTROL] 統合翻訳モード - 統合翻訳を実行");
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "🎯 [TRANSLATION_CONTROL] 翻訳制御判定でエラー - 統合翻訳を実行");
            return false; // エラー時は安全側（統合翻訳実行）
        }
    }
}
