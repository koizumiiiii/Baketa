using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.OCR.Results;
using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.UI;
using Baketa.Core.Events.EventTypes;
using Baketa.Core.Models.OCR;
using Baketa.Core.Translation.Models;
using Baketa.Core.Translation.Pipeline;
using Language = Baketa.Core.Models.Translation.Language;
using Microsoft.Extensions.Logging;
using ITranslationServiceCore = Baketa.Core.Abstractions.Translation.ITranslationService;
using PipelineTranslationResult = Baketa.Core.Translation.Pipeline.TranslationResult;

namespace Baketa.Application.Services.Translation;

/// <summary>
/// 統一翻訳パイプラインサービス
/// CoordinateBasedTranslationServiceとOcrCompletedHandler_ImprovedのTPL Dataflow機能を統合
/// 
/// 5段階パイプライン:
/// 1. Entry Block (BufferBlock) - OCR結果受付
/// 2. Pre-processing Block (TransformBlock) - ROI処理・重複チェック
/// 3. Batching Block (BatchBlock) - 効率化バッチ処理
/// 4. Parallel Translation Block (TransformBlock) - 並列翻訳実行  
/// 5. UI Update Block (ActionBlock) - 統一表示制御
/// </summary>
public sealed class TranslationPipelineService : IEventProcessor<OcrCompletedEvent>, IDisposable
{
    private readonly IEventAggregator _eventAggregator;
    private readonly IUnifiedSettingsService _settingsService;
    private readonly ITranslationServiceCore _translationService;
    // 🔧 [OVERLAY_UNIFICATION] IInPlaceTranslationOverlayManager → IOverlayManager に統一
    // 現在は未使用だが、将来の使用を想定してIOverlayManagerに変更
    private readonly Baketa.Core.Abstractions.UI.Overlays.IOverlayManager _overlayManager;
    private readonly ILogger<TranslationPipelineService> _logger;
    private readonly ILanguageConfigurationService _languageConfig;
    private readonly CancellationTokenSource _cancellationTokenSource;

    // TPL Dataflow Pipeline Components
    private readonly BufferBlock<OcrResult> _entryBlock;
    private readonly TransformBlock<OcrResult, TranslationJob> _preprocessingBlock;
    private readonly BatchBlock<TranslationJob> _batchingBlock;
    private readonly TransformBlock<TranslationJob[], PipelineTranslationResult[]> _translationBlock;
    private readonly ActionBlock<PipelineTranslationResult[]> _uiUpdateBlock;
    private readonly System.Threading.Timer _batchTimer;

    // Pipeline Configuration (from design document)
    private const int BatchSize = 3;
    private const int BatchTimeoutMs = 100;
    private const int MaxDegreeOfParallelism = 2; // 並列度復元: Phase 1.5リファクタリングにより元の設定に復元
    private const int BufferBlockCapacity = 100;
    private const int BatchBlockCapacity = 100;
    private const int TranslationBlockCapacity = 10;
    private const int UIUpdateBlockCapacity = 10;

    /// <inheritdoc />
    public int Priority => 0;

    /// <inheritdoc />
    public bool SynchronousExecution => false;

    public TranslationPipelineService(
        IEventAggregator eventAggregator,
        IUnifiedSettingsService settingsService,
        ITranslationServiceCore translationService,
        Baketa.Core.Abstractions.UI.Overlays.IOverlayManager overlayManager,
        ILogger<TranslationPipelineService> logger,
        ILanguageConfigurationService languageConfig)
    {
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _translationService = translationService ?? throw new ArgumentNullException(nameof(translationService));
        _overlayManager = overlayManager ?? throw new ArgumentNullException(nameof(overlayManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _languageConfig = languageConfig ?? throw new ArgumentNullException(nameof(languageConfig));
        _cancellationTokenSource = new CancellationTokenSource();

        // Stage 1: Entry Block - OCR結果受付
        _entryBlock = new BufferBlock<OcrResult>(new DataflowBlockOptions
        {
            BoundedCapacity = BufferBlockCapacity,
            CancellationToken = _cancellationTokenSource.Token
        });

        // Stage 2: Pre-processing Block - ROI処理・重複チェック
        _preprocessingBlock = new TransformBlock<OcrResult, TranslationJob>(
            ProcessOcrResultAsync,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = BufferBlockCapacity,
                CancellationToken = _cancellationTokenSource.Token
            });

        // Stage 3: Batching Block - 効率化バッチ処理
        _batchingBlock = new BatchBlock<TranslationJob>(
            batchSize: BatchSize,
            new GroupingDataflowBlockOptions
            {
                BoundedCapacity = BatchBlockCapacity,
                CancellationToken = _cancellationTokenSource.Token
            });

        // Stage 4: Parallel Translation Block - 並列翻訳実行
        _translationBlock = new TransformBlock<TranslationJob[], PipelineTranslationResult[]>(
            ProcessTranslationBatchAsync,
            new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = MaxDegreeOfParallelism,
                BoundedCapacity = TranslationBlockCapacity,
                CancellationToken = _cancellationTokenSource.Token
            });

        // Stage 5: UI Update Block - 統一表示制御
        _uiUpdateBlock = new ActionBlock<PipelineTranslationResult[]>(
            ProcessUIUpdateAsync,
            new ExecutionDataflowBlockOptions
            {
                BoundedCapacity = UIUpdateBlockCapacity,
                CancellationToken = _cancellationTokenSource.Token
            });

        // Pipeline Linking
        LinkPipelineBlocks();

        // Batch Timeout Timer - 散発的要求対応（100ms間隔）
        _batchTimer = new System.Threading.Timer(FlushBatchAsync, null, BatchTimeoutMs, BatchTimeoutMs);

        _logger.LogInformation(
            "TranslationPipelineService初期化完了 - BatchSize={BatchSize}, Timeout={TimeoutMs}ms, Parallelism={Parallelism}",
            BatchSize, BatchTimeoutMs, MaxDegreeOfParallelism);
    }

    /// <inheritdoc />
    public async Task HandleAsync(OcrCompletedEvent eventData, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("TranslationPipelineService.HandleAsync開始: Results={ResultCount}",
            eventData?.Results?.Count ?? 0);

        ArgumentNullException.ThrowIfNull(eventData);

        // OCR結果が存在しない場合は通知を抑制（無音処理）
        if (eventData.Results == null || !eventData.Results.Any())
        {
            // テキスト未検出時は通知せずに静かに終了
            _logger.LogDebug("OCR処理完了: テキスト未検出のため処理をスキップ");
            return;
        }

        // OCR成功通知
        var successNotificationEvent = new NotificationEvent(
            $"OCR処理が完了しました: {eventData.Results.Count}個のテキスト領域を検出",
            NotificationType.Success,
            "OCR完了",
            displayTime: 3000);

        await _eventAggregator.PublishAsync(successNotificationEvent).ConfigureAwait(false);

        _logger.LogInformation("統一翻訳パイプライン処理開始: {ResultCount}個のOCR結果をパイプラインに投入",
            eventData.Results.Count);

        // OCR結果をパイプラインに投入
        var enqueued = 0;
        var failed = 0;

        foreach (var result in eventData.Results)
        {
            try
            {
                // バックプレッシャー対応: SendAsyncで待機可能な投入
                var success = await _entryBlock.SendAsync(result, _cancellationTokenSource.Token).ConfigureAwait(false);

                if (success)
                {
                    enqueued++;
                    _logger.LogTrace("OCR結果をパイプラインに投入成功: '{Text}'",
                        result.Text[..Math.Min(20, result.Text.Length)]);
                }
                else
                {
                    failed++;
                    _logger.LogWarning("OCR結果のパイプライン投入失敗（容量制限）: '{Text}'",
                        result.Text[..Math.Min(20, result.Text.Length)]);
                }
            }
            catch (InvalidOperationException ex)
            {
                failed++;
                _logger.LogError(ex, "OCR結果のパイプライン投入で例外: '{Text}'",
                    result.Text[..Math.Min(20, result.Text.Length)]);
            }
        }

        _logger.LogInformation("パイプラインへの投入完了 - 成功: {Success}, 失敗: {Failed}", enqueued, failed);
    }

    /// <summary>
    /// パイプラインブロックをリンク
    /// </summary>
    private void LinkPipelineBlocks()
    {
        // Stage 1 → Stage 2
        _entryBlock.LinkTo(_preprocessingBlock, new DataflowLinkOptions { PropagateCompletion = true });

        // Stage 2 → Stage 3: フィルタリング（有効なジョブのみ）
        _preprocessingBlock.LinkTo(_batchingBlock, new DataflowLinkOptions { PropagateCompletion = true },
            job => job.IsValid);

        // Stage 2 → NullTarget: 無効なジョブは破棄
        _preprocessingBlock.LinkTo(DataflowBlock.NullTarget<TranslationJob>());

        // Stage 3 → Stage 4
        _batchingBlock.LinkTo(_translationBlock, new DataflowLinkOptions { PropagateCompletion = true });

        // Stage 4 → Stage 5
        _translationBlock.LinkTo(_uiUpdateBlock, new DataflowLinkOptions { PropagateCompletion = true });
    }

    /// <summary>
    /// Stage 2: OCR結果の前処理（ROI処理・重複チェック統合）
    /// CoordinateBasedTranslationServiceのロジックを移植
    /// </summary>
    /// <param name="ocrResult">OCR結果</param>
    /// <returns>翻訳ジョブ</returns>
    private async Task<TranslationJob> ProcessOcrResultAsync(OcrResult ocrResult)
    {
        try
        {
            // 翻訳設定取得（設定ベース言語使用）
            var languagePair = _languageConfig.GetCurrentLanguagePair();
            var sourceLanguageCode = languagePair.SourceCode;
            var targetLanguageCode = languagePair.TargetCode;

            // ROI座標情報の判定（CoordinateBasedTranslationServiceロジック移植）
            var hasCoordinateInfo = HasValidCoordinateInfo(ocrResult);
            var displayMode = hasCoordinateInfo
                ? TranslationDisplayMode.InPlace
                : TranslationDisplayMode.Default;

            CoordinateInfo? coordinateInfo = null;
            if (hasCoordinateInfo)
            {
                var bounds = ocrResult.Bounds;
                coordinateInfo = new CoordinateInfo(bounds, IntPtr.Zero); // WindowHandle は後で設定
            }

            // TranslationJob作成
            var job = TranslationJob.FromSingleResult(
                ocrResult,
                sourceLanguageCode,
                targetLanguageCode,
                displayMode,
                coordinateInfo);

            _logger.LogTrace("前処理完了: Job={JobId}, Mode={Mode}, Valid={Valid}",
                job.JobId, displayMode, job.IsValid);

            return job;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OCR結果前処理でエラー: '{Text}'",
                ocrResult.Text[..Math.Min(20, ocrResult.Text.Length)]);

            // エラー時は空のジョブを返す（フィルタリングで除外される）
            return TranslationJob.Empty;
        }

        // 非同期処理完了（コンパイラ警告対策）
    }

    /// <summary>
    /// Stage 4: バッチ翻訳処理（並列実行）
    /// OcrCompletedHandler_Improvedのロジックを統合
    /// </summary>
    /// <param name="jobBatch">翻訳ジョブのバッチ</param>
    /// <returns>翻訳結果の配列</returns>
    private async Task<PipelineTranslationResult[]> ProcessTranslationBatchAsync(TranslationJob[] jobBatch)
    {
        if (jobBatch == null || jobBatch.Length == 0)
        {
            return Array.Empty<PipelineTranslationResult>();
        }

        _logger.LogDebug("バッチ翻訳処理開始: {BatchSize}個のジョブを処理", jobBatch.Length);

        // Phase 1.5: 固定クールダウン削除 - appsettings.jsonのMaxConnections制御で十分

        try
        {
            // 🔧 CRITICAL FIX: Direct ITranslationService call instead of Fire-and-Forget
            // Extract texts for batch translation
            var textsToTranslate = jobBatch.SelectMany(job => job.OcrResults.Select(ocr => ocr.Text)).ToList();
            var sourceLanguageCode = jobBatch[0].SourceLanguage;
            var targetLanguageCode = jobBatch[0].TargetLanguage;

            _logger.LogDebug("Direct batch translation call: {TextCount} texts, {SourceLang} → {TargetLang}",
                textsToTranslate.Count, sourceLanguageCode, targetLanguageCode);

            // Convert language codes to Language objects
            var sourceLanguage = Language.FromCode(sourceLanguageCode);
            var targetLanguage = Language.FromCode(targetLanguageCode);

            // Direct translation service call to get actual results
            var translationResponses = await _translationService.TranslateBatchAsync(
                textsToTranslate.AsReadOnly(),
                sourceLanguage,
                targetLanguage).ConfigureAwait(false);

            // Convert translation results to pipeline format
            var results = new List<PipelineTranslationResult>();
            int translationIndex = 0;

            foreach (var job in jobBatch)
            {
                foreach (var ocrResult in job.OcrResults)
                {
                    if (translationIndex < translationResponses.Count)
                    {
                        var response = translationResponses[translationIndex];
                        if (response.IsSuccess && !string.IsNullOrEmpty(response.TranslatedText))
                        {
                            results.Add(PipelineTranslationResult.FromJob(
                                job,
                                response.TranslatedText,
                                TimeSpan.FromMilliseconds(response.ProcessingTimeMs),
                                response.ConfidenceScore));
                        }
                        else
                        {
                            results.Add(PipelineTranslationResult.CreateError(
                                ocrResult.Text,
                                response.Error?.Message ?? "Translation failed",
                                job.JobId,
                                job.DisplayMode,
                                job.CoordinateInfo));
                        }
                    }
                    else
                    {
                        // Fallback for mismatched counts
                        results.Add(PipelineTranslationResult.CreateError(
                            ocrResult.Text,
                            "Translation count mismatch",
                            job.JobId,
                            job.DisplayMode,
                            job.CoordinateInfo));
                    }
                    translationIndex++;
                }
            }

            _logger.LogDebug("Direct translation completed: {ResultCount} results generated",
                results.Count);

            return [.. results];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "バッチ翻訳処理でエラー発生: BatchSize={BatchSize}", jobBatch.Length);

            // エラー時は個別フォールバック処理
            return await FallbackToIndividualTranslationAsync(jobBatch).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Stage 5: UI更新処理（統一表示制御）
    /// InPlace/Default表示の分岐処理
    /// </summary>
    /// <param name="resultsBatch">翻訳結果のバッチ</param>
    private async Task ProcessUIUpdateAsync(PipelineTranslationResult[] resultsBatch)
    {
        if (resultsBatch == null || resultsBatch.Length == 0)
        {
            return;
        }

        _logger.LogDebug("UI更新処理開始: {ResultCount}個の結果を処理", resultsBatch.Length);

        foreach (var result in resultsBatch)
        {
            try
            {
                switch (result.DisplayMode)
                {
                    case TranslationDisplayMode.InPlace:
                        await ProcessInPlaceDisplayAsync(result).ConfigureAwait(false);
                        break;

                    case TranslationDisplayMode.Default:
                        await ProcessDefaultDisplayAsync(result).ConfigureAwait(false);
                        break;

                    default:
                        _logger.LogWarning("未知の表示モード: {DisplayMode}", result.DisplayMode);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UI更新処理でエラー: '{Text}'",
                    result.OriginalText[..Math.Min(20, result.OriginalText.Length)]);
            }
        }
    }

    /// <summary>
    /// タイムアウト時のバッチフラッシュ（散発的要求対応）
    /// </summary>
    /// <param name="state">タイマー状態（未使用）</param>
    private void FlushBatchAsync(object? state)
    {
        try
        {
            _batchingBlock.TriggerBatch();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "バッチフラッシュでエラー");
        }
    }

    /// <summary>
    /// バッチ翻訳失敗時の個別フォールバック処理
    /// 🔧 GEMINI CRITICAL FIX: Direct ITranslationService calls instead of Fire-and-Forget
    /// </summary>
    /// <param name="jobBatch">失敗したジョブバッチ</param>
    /// <returns>翻訳結果の配列</returns>
    private async Task<PipelineTranslationResult[]> FallbackToIndividualTranslationAsync(TranslationJob[] jobBatch)
    {
        _logger.LogWarning("バッチ翻訳失敗。個別フォールバック処理開始: {BatchSize}個のジョブを直接処理", jobBatch.Length);

        var results = new List<PipelineTranslationResult>();
        var sourceLanguage = Language.FromCode(jobBatch[0].SourceLanguage);
        var targetLanguage = Language.FromCode(jobBatch[0].TargetLanguage);

        foreach (var job in jobBatch)
        {
            // 1ジョブに複数テキストが含まれる可能性を考慮
            foreach (var ocrResult in job.OcrResults)
            {
                try
                {
                    _logger.LogDebug("個別翻訳実行: '{Text}'",
                        ocrResult.Text[..Math.Min(20, ocrResult.Text.Length)]);

                    // 🔧 DIRECT CALL: ITranslationServiceの単一翻訳メソッドを直接呼び出し
                    var response = await _translationService.TranslateAsync(
                        ocrResult.Text,
                        sourceLanguage,
                        targetLanguage,
                        context: null,
                        cancellationToken: CancellationToken.None).ConfigureAwait(false);

                    if (response.IsSuccess && !string.IsNullOrEmpty(response.TranslatedText))
                    {
                        results.Add(PipelineTranslationResult.FromJob(
                            job,
                            response.TranslatedText,
                            TimeSpan.FromMilliseconds(response.ProcessingTimeMs),
                            response.ConfidenceScore));

                        _logger.LogDebug("個別翻訳成功: '{Original}' → '{Translated}'",
                            ocrResult.Text[..Math.Min(15, ocrResult.Text.Length)],
                            response.TranslatedText[..Math.Min(15, response.TranslatedText.Length)]);
                    }
                    else
                    {
                        results.Add(PipelineTranslationResult.CreateError(
                            ocrResult.Text,
                            response.Error?.Message ?? "Individual translation failed",
                            job.JobId,
                            job.DisplayMode,
                            job.CoordinateInfo));

                        _logger.LogWarning("個別翻訳失敗: '{Text}' - {Error}",
                            ocrResult.Text[..Math.Min(20, ocrResult.Text.Length)],
                            response.Error?.Message ?? "Unknown error");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "個別フォールバック翻訳でエラー: '{Text}'", ocrResult.Text);
                    results.Add(PipelineTranslationResult.CreateError(
                        ocrResult.Text,
                        ex.Message,
                        job.JobId,
                        job.DisplayMode,
                        job.CoordinateInfo));
                }
            }
        }

        _logger.LogInformation("個別フォールバック処理完了: {ResultCount}個の結果を生成", results.Count);
        return [.. results];
    }

    /// <summary>
    /// InPlace表示処理
    /// CoordinateBasedTranslationServiceの座標ベース表示ロジックを統合実装
    /// </summary>
    /// <param name="result">翻訳結果</param>
    private async Task ProcessInPlaceDisplayAsync(PipelineTranslationResult result)
    {
        try
        {
            // 🚫 [DUPLICATE_DISPLAY_FIX] 重複表示問題修正
            // TranslationWithBoundsCompletedHandler → OverlayUpdateEvent 経由で表示されるため、
            // こちらでの直接表示は無効化
            _logger.LogDebug("InPlace表示はOverlayUpdateEvent経由で実行されます - 直接表示をスキップ: '{Text}'",
                result.OriginalText[..Math.Min(20, result.OriginalText.Length)]);

            // CoordinateInfo が存在しない場合の警告ログは残す
            if (result.CoordinateInfo == null)
            {
                _logger.LogDebug("CoordinateInfo不在のためInPlace表示を完全にスキップ: '{Text}'",
                    result.OriginalText[..Math.Min(20, result.OriginalText.Length)]);
                return;
            }

            // 🔄 [DISPLAY_FLOW] 表示フローの詳細をログ出力
            _logger.LogDebug("InPlace表示フロー: TranslationPipelineService → (スキップ) → TranslationWithBoundsCompletedHandler → OverlayUpdateEvent → InPlaceTranslationOverlayManager");

            // ✅ 重複表示回避のため、この処理は何もせず正常終了
            await Task.CompletedTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "InPlace表示スキップ処理でエラー: '{Text}'",
                result.OriginalText[..Math.Min(30, result.OriginalText.Length)]);

            // エラー時もフォールバックは行わない（OverlayUpdateEvent経由で表示される）
        }
    }

    /// <summary>
    /// PipelineTranslationResultからTextChunkを作成
    /// CoordinateInfoの情報を使用してTextChunkの必須プロパティを設定
    /// </summary>
    /// <param name="result">パイプライン翻訳結果</param>
    /// <param name="defaultSourceLanguage">デフォルトソース言語</param>
    /// <returns>作成されたTextChunk</returns>
    private static TextChunk CreateTextChunkFromResult(PipelineTranslationResult result, string defaultSourceLanguage)
    {
        if (result.CoordinateInfo == null)
        {
            throw new ArgumentException("CoordinateInfo is required for TextChunk creation", nameof(result));
        }

        var coord = result.CoordinateInfo;

        // PositionedTextResultを作成
        var positionedTextResult = new PositionedTextResult
        {
            Text = result.OriginalText,
            BoundingBox = new System.Drawing.Rectangle(coord.X, coord.Y, coord.Width, coord.Height),
            Confidence = result.Confidence, // 翻訳結果の信頼度
            ChunkId = result.JobId.GetHashCode(), // JobIdからChunkIdを生成
            ProcessingTime = result.ProcessingTime,
            DetectedLanguage = defaultSourceLanguage, // 設定ベース言語
            Orientation = TextOrientation.Horizontal
        };

        // TextChunkを作成
        return new TextChunk
        {
            ChunkId = result.JobId.GetHashCode(), // JobIdからChunkIdを生成
            TextResults = new List<PositionedTextResult> { positionedTextResult }.AsReadOnly(),
            CombinedBounds = new System.Drawing.Rectangle(coord.X, coord.Y, coord.Width, coord.Height),
            CombinedText = result.OriginalText,
            TranslatedText = result.TranslatedText,
            SourceWindowHandle = coord.WindowHandle,
            DetectedLanguage = defaultSourceLanguage
        };
    }

    /// <summary>
    /// Default表示処理
    /// </summary>
    /// <param name="result">翻訳結果</param>
    private async Task ProcessDefaultDisplayAsync(PipelineTranslationResult result)
    {
        // 言語設定を取得
        var languagePair = _languageConfig.GetCurrentLanguagePair();

        // 通常のTranslationCompletedEventを発行
        var completedEvent = new TranslationCompletedEvent(
            sourceText: result.OriginalText,
            translatedText: result.TranslatedText,
            sourceLanguage: languagePair.SourceCode, // 設定ベース言語
            targetLanguage: languagePair.TargetCode, // 設定ベース言語
            processingTime: result.ProcessingTime,
            engineName: "Pipeline");

        await _eventAggregator.PublishAsync(completedEvent).ConfigureAwait(false);

        _logger.LogTrace("Default表示イベント発行完了: '{Text}'",
            result.OriginalText[..Math.Min(20, result.OriginalText.Length)]);
    }

    /// <summary>
    /// OCR結果が有効な座標情報を持っているかチェック
    /// CoordinateBasedTranslationServiceのロジックを移植
    /// </summary>
    /// <param name="ocrResult">OCR結果</param>
    /// <returns>有効な座標情報を持つ場合true</returns>
    private static bool HasValidCoordinateInfo(OcrResult ocrResult)
    {
        return ocrResult.Bounds.Width > 0 &&
               ocrResult.Bounds.Height > 0 &&
               ocrResult.Bounds.X >= 0 &&
               ocrResult.Bounds.Y >= 0;
    }


    /// <summary>
    /// リソースの安全な解放
    /// </summary>
    public void Dispose()
    {
        _logger.LogInformation("TranslationPipelineService終了処理開始");

        try
        {
            // バッチタイマー停止
            _batchTimer?.Dispose();

            // キャンセレーション実行
            _cancellationTokenSource.Cancel();

            // パイプライン完了と待機
            _entryBlock.Complete();
            var completionTasks = new[]
            {
                _preprocessingBlock.Completion,
                _batchingBlock.Completion,
                _translationBlock.Completion,
                _uiUpdateBlock.Completion
            };

            Task.WaitAll(completionTasks, TimeSpan.FromSeconds(5));

            _cancellationTokenSource.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TranslationPipelineService終了処理でエラー");
        }
        finally
        {
            _logger.LogInformation("TranslationPipelineService終了処理完了");
        }

        GC.SuppressFinalize(this);
    }
}
