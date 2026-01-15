using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.License; // [Issue #78 Phase 4] ILicenseManager用
using Baketa.Core.Abstractions.Services; // 🔥 [COORDINATE_FIX] ICoordinateTransformationService用
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.UI;
using Baketa.Core.Abstractions.UI.Overlays; // 🔧 [OVERLAY_UNIFICATION] IOverlayManager統一インターフェース用
using Baketa.Core.Events.Translation;
using Baketa.Core.License.Models; // [Issue #78 Phase 4] FeatureType用
using Baketa.Core.Models.Translation;
using Baketa.Core.Models.Validation; // [Issue #78 Phase 4] ValidatedTextChunk用
using Baketa.Core.Translation.Abstractions; // [Issue #78 Phase 4] IParallelTranslationOrchestrator用
using Baketa.Core.Translation.Models;
using Baketa.Application.Services.Translation; // [Issue #291] ITranslationControlService用
using Microsoft.Extensions.Logging;
using Language = Baketa.Core.Translation.Models.Language;

namespace Baketa.Application.EventHandlers.Translation;

/// <summary>
/// 集約済みチャンクに対してバッチ翻訳を実行するイベントハンドラ
/// Phase 12.2: 2重翻訳アーキテクチャ排除の中核実装
///
/// TimedChunkAggregatorから発行されるAggregatedChunksReadyEventを受信し、
/// CoordinateBasedTranslationService.ProcessBatchTranslationAsync()相当の処理を実行
/// </summary>
public sealed class AggregatedChunksReadyEventHandler : IEventProcessor<AggregatedChunksReadyEvent>
{
    // 🔥 [PHASE1_SEMAPHORE] 翻訳実行制御用セマフォ（1並列のみ許可）
    // Gemini推奨の多層防御アーキテクチャ - 第2層: 物理的排他制御
    private static readonly SemaphoreSlim _translationExecutionSemaphore = new(1, 1);

    private readonly Baketa.Core.Abstractions.Translation.ITranslationService _translationService;
    private readonly IStreamingTranslationService? _streamingTranslationService;
    // 🔧 [OVERLAY_UNIFICATION] IInPlaceTranslationOverlayManager → IOverlayManager に統一
    private readonly IOverlayManager _overlayManager;
    private readonly ILanguageConfigurationService _languageConfig;
    private readonly IEventAggregator _eventAggregator;
    private readonly ILogger<AggregatedChunksReadyEventHandler> _logger;
    private readonly ICoordinateTransformationService _coordinateTransformationService; // 🔥 [COORDINATE_FIX]
    private readonly Core.Abstractions.Settings.IUnifiedSettingsService _unifiedSettingsService;
    // [Issue #78 Phase 4] 並列翻訳オーケストレーター
    private readonly IParallelTranslationOrchestrator? _parallelTranslationOrchestrator;
    private readonly ILicenseManager? _licenseManager;
    // [Issue #273] Cloud翻訳可用性統合サービス
    private readonly Core.Abstractions.Translation.ICloudTranslationAvailabilityService? _cloudTranslationAvailabilityService;
    // [Issue #291] 翻訳状態確認用サービス（キャンセル状態チェック）
    // NOTE: CancellationToken伝播により不要になったが、将来の拡張用に保持
    private readonly ITranslationControlService? _translationControlService;

    public AggregatedChunksReadyEventHandler(
        Baketa.Core.Abstractions.Translation.ITranslationService translationService,
        // 🔧 [OVERLAY_UNIFICATION] IInPlaceTranslationOverlayManager → IOverlayManager に統一
        IOverlayManager overlayManager,
        ILanguageConfigurationService languageConfig,
        IEventAggregator eventAggregator,
        ILogger<AggregatedChunksReadyEventHandler> logger,
        ICoordinateTransformationService coordinateTransformationService, // 🔥 [COORDINATE_FIX]
        Core.Abstractions.Settings.IUnifiedSettingsService unifiedSettingsService,
        IStreamingTranslationService? streamingTranslationService = null,
        // [Issue #78 Phase 4] 並列翻訳オーケストレーター（オプショナル）
        IParallelTranslationOrchestrator? parallelTranslationOrchestrator = null,
        ILicenseManager? licenseManager = null,
        // [Issue #273] Cloud翻訳可用性統合サービス（オプショナル）
        Core.Abstractions.Translation.ICloudTranslationAvailabilityService? cloudTranslationAvailabilityService = null,
        // [Issue #291] 翻訳状態確認用サービス（オプショナル）
        ITranslationControlService? translationControlService = null)
    {
        _translationService = translationService ?? throw new ArgumentNullException(nameof(translationService));
        _overlayManager = overlayManager ?? throw new ArgumentNullException(nameof(overlayManager));
        _languageConfig = languageConfig ?? throw new ArgumentNullException(nameof(languageConfig));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _coordinateTransformationService = coordinateTransformationService ?? throw new ArgumentNullException(nameof(coordinateTransformationService)); // 🔥 [COORDINATE_FIX]
        _unifiedSettingsService = unifiedSettingsService ?? throw new ArgumentNullException(nameof(unifiedSettingsService));
        _streamingTranslationService = streamingTranslationService;
        // [Issue #78 Phase 4] 並列翻訳オーケストレーター
        _parallelTranslationOrchestrator = parallelTranslationOrchestrator;
        _licenseManager = licenseManager;
        // [Issue #273] Cloud翻訳可用性統合サービス
        _cloudTranslationAvailabilityService = cloudTranslationAvailabilityService;
        // [Issue #291] 翻訳状態確認用サービス
        _translationControlService = translationControlService;
    }

    /// <inheritdoc />
    public int Priority => 0;

    /// <inheritdoc />
    public bool SynchronousExecution => false; // 🔧 [FIX] 並列処理を許可して120秒ブロック時のデッドロック回避

    /// <summary>
    /// 🔥 [STOP_CLEANUP] Stop時のセマフォ強制リセット
    /// 問題: タイムアウト中（0-10秒）にStopしても、セマフォが保持されたまま残る
    /// 解決策: Stop時にセマフォの状態を強制的にリセットし、次のStartで即座に翻訳可能にする
    /// </summary>
    public static void ResetSemaphoreForStop()
    {
        try
        {
            Console.WriteLine($"🔍 [STOP_CLEANUP_DEBUG] メソッド開始 - CurrentCount: {_translationExecutionSemaphore.CurrentCount}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"💥 [STOP_CLEANUP_DEBUG] Console.WriteLine失敗: {ex.GetType().Name} - {ex.Message}");
        }

        // セマフォが既に取得されている場合（CurrentCount == 0）のみリセット
        if (_translationExecutionSemaphore.CurrentCount == 0)
        {
            try
            {
                _translationExecutionSemaphore.Release();
                Console.WriteLine("🔓 [STOP_CLEANUP] セマフォ強制解放完了 - Stop時クリーンアップ");
            }
            catch (SemaphoreFullException)
            {
                // 既に解放済み（CurrentCount == 1）の場合は無視
                Console.WriteLine("ℹ️ [STOP_CLEANUP] セマフォは既に解放済み");
            }
        }
        else
        {
            Console.WriteLine($"ℹ️ [STOP_CLEANUP] セマフォは既に利用可能 - CurrentCount: {_translationExecutionSemaphore.CurrentCount}");
        }
    }

    /// <inheritdoc />
    /// <summary>
    /// [Issue #291] CancellationToken対応のイベント処理
    /// </summary>
    public async Task HandleAsync(AggregatedChunksReadyEvent eventData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        // [Issue #291] キャンセルチェック（早期リターン）
        if (cancellationToken.IsCancellationRequested)
        {
            _logger?.LogInformation("🛑 [Issue #291] 翻訳が停止されたため、イベント処理をスキップします (SessionId: {SessionId})", eventData.SessionId);
            return;
        }

        // 🔥 [PHASE1_SEMAPHORE] セマフォ取得（並行実行防止）
        // WaitAsync(0) = 即座に判定、ブロッキングなし
        if (!await _translationExecutionSemaphore.WaitAsync(0).ConfigureAwait(false))
        {
            // 既に翻訳実行中の場合はスキップ
            _logger.LogWarning("⚠️ [PHASE1] 翻訳実行中のため、SessionId: {SessionId} をスキップ（並行実行防止）",
                eventData.SessionId);

            // 🔥 [GEMINI_FEEDBACK] UI/UXフィードバック強化
            _logger?.LogDebug($"⏳ [PHASE1] 翻訳スキップ - 別の翻訳実行中（SessionId: {eventData.SessionId}）");
            Console.WriteLine($"⏳ [PHASE1] 翻訳スキップ - 別の翻訳実行中（SessionId: {eventData.SessionId}）");

            return; // 早期リターン - イベント破棄
        }

        // 🔥 [PHASE12.2_NEW_ARCH] Gemini推奨の見える化ログ
        Console.WriteLine($"✅✅✅ [PHASE12.2_NEW_ARCH] AggregatedChunksReadyEventHandler開始. SessionId: {eventData.SessionId}, ChunkCount: {eventData.AggregatedChunks.Count}");
        _logger?.LogDebug($"✅✅✅ [PHASE12.2_NEW_ARCH] AggregatedChunksReadyEventHandler開始. SessionId: {eventData.SessionId}, ChunkCount: {eventData.AggregatedChunks.Count}");

        try
        {
            // 🔥 確実なログ出力（ファイル直接書き込み）
            _logger?.LogDebug($"🔥🔥🔥 [PHASE12.2_HANDLER] HandleAsync tryブロック開始 - SessionId: {eventData.SessionId}, ChunkCount: {eventData.AggregatedChunks.Count}");
            Console.WriteLine($"🔥🔥🔥 [PHASE12.2_HANDLER] HandleAsync tryブロック開始 - SessionId: {eventData.SessionId}, ChunkCount: {eventData.AggregatedChunks.Count}");

            _logger?.LogInformation("🔥 [PHASE12.2] 集約チャンク受信 - {Count}個, SessionId: {SessionId}",
                eventData.AggregatedChunks.Count, eventData.SessionId);
            // [Code Review] LogCritical → LogDebug に変更（通常処理の開始ログにCriticalは不適切）
            _logger?.LogDebug("✅✅✅ [PHASE12.2_NEW_ARCH] AggregatedChunksReadyEventHandler開始. SessionId: {SessionId}", eventData.SessionId);

            // 集約されたチャンクをリストに変換
            var aggregatedChunks = eventData.AggregatedChunks.ToList();

            // 🔥 [CONFIDENCE_FILTER] 信頼度フィルタリング - 低信頼度結果を翻訳から除外
            var ocrSettings = _unifiedSettingsService.GetOcrSettings();
            var confidenceThreshold = ocrSettings?.ConfidenceThreshold ?? 0.70;

            // [Issue #229] ボーダーライン緩和設定の取得
            var enableBorderlineRelaxation = ocrSettings?.EnableBorderlineConfidenceRelaxation ?? true;
            var borderlineMinConfidence = ocrSettings?.BorderlineMinConfidence ?? 0.60;
            var borderlineRelaxedThreshold = ocrSettings?.BorderlineRelaxedThreshold ?? 0.65;
            var borderlineMinTextLength = ocrSettings?.BorderlineMinTextLength ?? 5;
            var borderlineMinBoundsHeight = ocrSettings?.BorderlineMinBoundsHeight ?? 25;
            var borderlineMinAspectRatio = ocrSettings?.BorderlineMinAspectRatio ?? 2.0;

            // 🔍 [DIAGNOSTIC] 各チャンクの信頼度をログ出力
            var passedChunks = new List<TextChunk>();
            var borderlineAcceptedCount = 0;

            foreach (var chunk in aggregatedChunks)
            {
                var confidence = chunk.AverageConfidence;
                var textLength = chunk.CombinedText?.Length ?? 0;
                var boundsHeight = chunk.CombinedBounds.Height;
                var boundsWidth = chunk.CombinedBounds.Width;
                var aspectRatio = boundsHeight > 0 ? (double)boundsWidth / boundsHeight : 0;

                // ケース1: 通常閾値を超える → 通過
                if (confidence >= confidenceThreshold)
                {
                    passedChunks.Add(chunk);
                    _logger.LogInformation("🔍 [OCR_CHUNK] ✅PASS Conf={Confidence:F3} Threshold={Threshold:F2} Text='{Text}'",
                        confidence, confidenceThreshold,
                        chunk.CombinedText?.Length > 50 ? chunk.CombinedText[..50] + "..." : chunk.CombinedText);
                    continue;
                }

                // ケース2: ボーダーライン緩和を試行
                if (enableBorderlineRelaxation &&
                    confidence >= borderlineMinConfidence &&
                    confidence < confidenceThreshold &&
                    confidence >= borderlineRelaxedThreshold &&
                    textLength >= borderlineMinTextLength &&
                    boundsHeight >= borderlineMinBoundsHeight &&
                    aspectRatio >= borderlineMinAspectRatio &&
                    !IsNoisePattern(chunk.CombinedText))
                {
                    // ボーダーライン条件を満たす → 緩和閾値で採用
                    passedChunks.Add(chunk);
                    borderlineAcceptedCount++;
                    // IsNoisePattern が false を返した時点で chunk.CombinedText は null でないことが保証される
                    _logger.LogInformation(
                        "🔍 [OCR_CHUNK] ✅BORDERLINE Conf={Confidence:F3} RelaxedThreshold={RelaxedThreshold:F2} " +
                        "TextLen={TextLen} Height={Height} AspectRatio={AspectRatio:F1} Text='{Text}'",
                        confidence, borderlineRelaxedThreshold, textLength, boundsHeight, aspectRatio,
                        chunk.CombinedText.Length > 50 ? chunk.CombinedText[..50] + "..." : chunk.CombinedText);
                    Console.WriteLine($"🎯 [BORDERLINE_ACCEPTED] Conf={confidence:F3} Text='{chunk.CombinedText}'");
                    continue;
                }

                // ケース3: 閾値未満 → 却下
                _logger.LogInformation("🔍 [OCR_CHUNK] ❌FAIL Conf={Confidence:F3} Threshold={Threshold:F2} Text='{Text}'",
                    confidence, confidenceThreshold,
                    chunk.CombinedText?.Length > 50 ? chunk.CombinedText[..50] + "..." : chunk.CombinedText);
            }

            var highConfidenceChunks = passedChunks;
            var filteredByConfidenceCount = aggregatedChunks.Count - highConfidenceChunks.Count;

            if (filteredByConfidenceCount > 0 || borderlineAcceptedCount > 0)
            {
                Console.WriteLine($"🔍 [CONFIDENCE_FILTER] 信頼度フィルタリング: {filteredByConfidenceCount}件除外, {borderlineAcceptedCount}件ボーダーライン採用（閾値={confidenceThreshold:F2}）");
                _logger.LogInformation(
                    "🔍 [CONFIDENCE_FILTER] 信頼度{Threshold:F2}未満の{FilteredCount}件をフィルタリング, {BorderlineCount}件ボーダーライン採用（残り{RemainingCount}件）",
                    confidenceThreshold, filteredByConfidenceCount, borderlineAcceptedCount, highConfidenceChunks.Count);
            }

            // 🔥 [HALLUCINATION_FILTER] 繰り返しフレーズ検出 - OCRハルシネーション除外
            var validChunks = highConfidenceChunks
                .Where(chunk => !IsRepetitiveHallucination(chunk.CombinedText))
                .ToList();

            var filteredByHallucinationCount = highConfidenceChunks.Count - validChunks.Count;
            if (filteredByHallucinationCount > 0)
            {
                Console.WriteLine($"🚫 [HALLUCINATION_FILTER] 繰り返しフレーズ検出: {filteredByHallucinationCount}件除外（OCRハルシネーション）");
                _logger.LogWarning(
                    "🚫 [HALLUCINATION_FILTER] 繰り返しフレーズ{FilteredCount}件をフィルタリング（残り{RemainingCount}件）",
                    filteredByHallucinationCount, validChunks.Count);
            }

            // 空でないチャンクのみフィルタリング（ハルシネーションフィルタリング後）
            var nonEmptyChunks = validChunks
                .Where(chunk => !string.IsNullOrWhiteSpace(chunk.CombinedText))
                .ToList();

            // 空のチャンクに空文字列を設定
            foreach (var emptyChunk in aggregatedChunks.Where(c => string.IsNullOrWhiteSpace(c.CombinedText)))
            {
                emptyChunk.TranslatedText = "";
            }

            if (nonEmptyChunks.Count == 0)
            {
                _logger.LogWarning("⚠️ [PHASE12.2] 翻訳可能なチャンクが0個 - 処理スキップ");
                return;
            }

            // ============================================================
            // [Issue #290] Fork-Join: 事前計算されたCloud AI翻訳結果を優先使用
            // ============================================================
            List<string> translationResults;

            if (eventData.HasPreComputedCloudResult)
            {
                // 事前計算されたCloud AI翻訳結果が利用可能
                _logger?.LogInformation(
                    "🚀 [Issue #290] Fork-Join: 事前計算されたCloud AI翻訳結果を使用 (Engine={Engine})",
                    eventData.PreComputedCloudResult!.UsedEngine);
#if DEBUG
                Console.WriteLine($"🚀 [Issue #290] Fork-Join: 事前計算Cloud AI結果を使用 - Engine: {eventData.PreComputedCloudResult!.UsedEngine}");
#endif

                var cloudResponse = eventData.PreComputedCloudResult!.Response;

                // Cloud AI翻訳結果からテキストを抽出
                if (cloudResponse?.Texts is { Count: > 0 } cloudTexts)
                {
                    // [Issue #296] Originalテキストでマッチング
                    // Cloud AI（Gemini）は画像から再OCRするため、順序がローカルOCRと異なる場合がある
                    translationResults = MatchCloudTranslationsToChunks(nonEmptyChunks, cloudTexts);

                    _logger?.LogDebug(
                        "✅ [Issue #296] Fork-Join Cloud AI翻訳結果: {CloudCount}個 → {MatchedCount}個マッチ",
                        cloudTexts.Count, translationResults.Count(r => !string.IsNullOrEmpty(r)));
                }
                else if (!string.IsNullOrEmpty(cloudResponse?.TranslatedText))
                {
                    // 単一テキスト結果
                    translationResults = [cloudResponse.TranslatedText];
                    _logger?.LogDebug("✅ [Issue #290] Fork-Join Cloud AI翻訳結果: 単一テキスト取得");
                }
                else
                {
                    // Cloud AI結果が空 → ローカル翻訳にフォールバック
                    _logger?.LogWarning("⚠️ [Issue #290] Fork-Join Cloud AI翻訳結果が空 - ローカル翻訳にフォールバック");
                    // [Issue #291] CancellationTokenを伝播
                    translationResults = await ExecuteBatchTranslationAsync(
                        nonEmptyChunks,
                        cancellationToken).ConfigureAwait(false);
                }
            }
            else if (ShouldUseParallelTranslation(eventData))
            {
                // [Issue #78 Phase 4] 並列翻訳（ローカル + Cloud AI）を実行
                _logger?.LogDebug("🌐 [Phase4] 並列翻訳モード開始 - ChunkCount: {Count}", nonEmptyChunks.Count);
#if DEBUG
                Console.WriteLine($"🌐 [Phase4] 並列翻訳モード開始 - ChunkCount: {nonEmptyChunks.Count}");
#endif

                // [Issue #291] CreateLinkedTokenSourceで外部キャンセルとタイムアウトを連携
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                var parallelResult = await ExecuteParallelTranslationAsync(
                    nonEmptyChunks,
                    eventData,
                    linkedCts.Token).ConfigureAwait(false);

                if (parallelResult.IsSuccess && parallelResult.ValidatedChunks.Count > 0)
                {
                    // ValidatedChunksから翻訳結果を取得
                    translationResults = parallelResult.ValidatedChunks
                        .Select(v => v.TranslatedText)
                        .ToList();

                    // [Code Review] 相互検証でチャンク数が変化した場合は警告
                    var originalChunkCount = nonEmptyChunks.Count;

                    // nonEmptyChunksをValidatedChunksのOriginalChunkで更新（座標情報保持）
                    nonEmptyChunks = parallelResult.ValidatedChunks
                        .Select(v => v.OriginalChunk)
                        .ToList();

                    if (originalChunkCount != nonEmptyChunks.Count)
                    {
                        _logger?.LogWarning(
                            "⚠️ [Phase4] 相互検証でチャンク数が変化: {Original} → {Validated}（フィルタリングまたは統合/分割）",
                            originalChunkCount, nonEmptyChunks.Count);
                    }

                    _logger?.LogDebug("✅ [Phase4] 並列翻訳完了 - Engine: {Engine}, 結果数: {Count}",
                        parallelResult.EngineUsed, translationResults.Count);
#if DEBUG
                    Console.WriteLine($"✅ [Phase4] 並列翻訳完了 - Engine: {parallelResult.EngineUsed}, 結果数: {translationResults.Count}");
#endif

                    // 統計ログ
                    if (parallelResult.ValidationStatistics != null)
                    {
                        _logger?.LogInformation(
                            "📊 [Phase4] 相互検証統計: AcceptanceRate={Rate:P1}, CrossValidated={CrossValidated}, LocalOnly={LocalOnly}, Rescued={Rescued}",
                            parallelResult.ValidationStatistics.AcceptanceRate,
                            parallelResult.ValidationStatistics.CrossValidatedCount,
                            parallelResult.ValidationStatistics.LocalOnlyCount,
                            parallelResult.ValidationStatistics.RescuedCount);
                    }
                }
                else
                {
                    // 並列翻訳失敗 → ローカル翻訳にフォールバック
                    _logger?.LogWarning("⚠️ [Phase4] 並列翻訳失敗 - ローカル翻訳にフォールバック: {Error}",
                        parallelResult.Error?.Message ?? "不明");
#if DEBUG
                    Console.WriteLine($"⚠️ [Phase4] 並列翻訳失敗 - ローカル翻訳にフォールバック");
#endif

                    // [Issue #291] CancellationTokenを伝播
                    translationResults = await ExecuteBatchTranslationAsync(
                        nonEmptyChunks,
                        cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                // 従来のローカル翻訳のみ
                _logger?.LogDebug($"🚀🚀🚀 [PHASE12.2_HANDLER] ExecuteBatchTranslationAsync呼び出し直前 - ChunkCount: {nonEmptyChunks.Count}");
                Console.WriteLine($"🚀🚀🚀 [PHASE12.2_HANDLER] ExecuteBatchTranslationAsync呼び出し直前 - ChunkCount: {nonEmptyChunks.Count}");

                // [Issue #291] CancellationTokenを伝播
                translationResults = await ExecuteBatchTranslationAsync(
                    nonEmptyChunks,
                    cancellationToken).ConfigureAwait(false);
            }

            _logger?.LogDebug($"✅✅✅ [PHASE12.2_HANDLER] 翻訳完了 - 結果数: {translationResults.Count}");
            Console.WriteLine($"✅✅✅ [PHASE12.2_HANDLER] 翻訳完了 - 結果数: {translationResults.Count}");

#if DEBUG
            // 🚨 [ULTRATHINK_TRACE1] 翻訳完了直後トレースログ
            var timestamp1 = DateTime.Now.ToString("HH:mm:ss.fff");
            var threadId1 = Environment.CurrentManagedThreadId;
            System.IO.File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "baketa_debug.log"),
                $"[{timestamp1}][T{threadId1:D2}] 🚨 [ULTRATHINK_TRACE1] 翻訳完了直後 - 結果数: {translationResults.Count}\r\n");
#endif

            // 翻訳結果を各チャンクに設定
            for (int i = 0; i < Math.Min(nonEmptyChunks.Count, translationResults.Count); i++)
            {
                nonEmptyChunks[i].TranslatedText = translationResults[i];
                _logger.LogInformation("🔧 [TRANSLATION_RESULT] チャンク{Index}: '{Original}' → '{Translated}'",
                    i, nonEmptyChunks[i].CombinedText, translationResults[i]);
            }

#if DEBUG
            // 🚨 [ULTRATHINK_TRACE2] 翻訳結果設定完了トレースログ
            var timestamp2 = DateTime.Now.ToString("HH:mm:ss.fff");
            var threadId2 = Environment.CurrentManagedThreadId;
            System.IO.File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "baketa_debug.log"),
                $"[{timestamp2}][T{threadId2:D2}] 🚨 [ULTRATHINK_TRACE2] 翻訳結果設定完了 - チャンク数: {nonEmptyChunks.Count}\r\n");
#endif

            // 🛑 [Issue #291] オーバーレイ表示前にCancellationTokenをチェック
            // Gemini推奨: CancellationTokenを使用した堅牢なキャンセル検知
            if (cancellationToken.IsCancellationRequested)
            {
                _logger?.LogInformation("🛑 [Issue #291] 翻訳が停止されたため、オーバーレイ表示をスキップします (SessionId: {SessionId})", eventData.SessionId);
                return;
            }

            // 🧹 [OVERLAY_CLEANUP] 新しいオーバーレイ表示前に古いオーバーレイをクリア
            try
            {
                await _overlayManager.HideAllAsync().ConfigureAwait(false);
                _logger?.LogDebug("🧹 [OVERLAY_CLEANUP] 古いオーバーレイをクリアしました");
                Console.WriteLine("🧹 [OVERLAY_CLEANUP] 古いオーバーレイをクリアしました");
            }
            catch (Exception cleanupEx)
            {
                _logger?.LogWarning(cleanupEx, "⚠️ [OVERLAY_CLEANUP] オーバーレイクリーンアップ中にエラー - 処理継続");
                Console.WriteLine($"⚠️ [OVERLAY_CLEANUP] クリーンアップエラー: {cleanupEx.Message}");
            }

            // 🔧 [OVERLAY_UNIFICATION] 統一IOverlayManager.ShowAsync()で直接オーバーレイ表示
            // Gemini推奨: TranslationWithBoundsCompletedEventを経由せず、直接オーバーレイ表示
            // 理由: イベントハンドラー未実装により表示されない問題を解決
            // アーキテクチャ: Application層 → Core層(IOverlayManager)への依存は正しい（DIP準拠）
            _logger?.LogDebug($"🔥 [OVERLAY_FIX] 直接オーバーレイ表示開始 - チャンク数: {nonEmptyChunks.Count}");
            Console.WriteLine($"🔥 [OVERLAY_FIX] 直接オーバーレイ表示開始 - チャンク数: {nonEmptyChunks.Count}");

#if DEBUG
            // 🚨 [ULTRATHINK_TRACE3] オーバーレイ表示ループ開始直前トレースログ
            var timestamp3 = DateTime.Now.ToString("HH:mm:ss.fff");
            var threadId3 = Environment.CurrentManagedThreadId;
            System.IO.File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "baketa_debug.log"),
                $"[{timestamp3}][T{threadId3:D2}] 🚨 [ULTRATHINK_TRACE3] オーバーレイ表示ループ開始直前 - ループ回数: {Math.Min(nonEmptyChunks.Count, translationResults.Count)}\r\n");
#endif

            for (int i = 0; i < Math.Min(nonEmptyChunks.Count, translationResults.Count); i++)
            {
                // [Issue #291] ループ内でもキャンセルチェック（早期終了）
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger?.LogInformation("🛑 [Issue #291] 翻訳が停止されたため、残りのオーバーレイ表示をスキップします ({Completed}/{Total})", i, nonEmptyChunks.Count);
                    break;
                }

                var chunk = nonEmptyChunks[i];
                // chunk.TranslatedTextは既にLine 176で設定済み

                // 🔥 [FIX6_NORMALIZE] ROI相対座標 → 画像絶対座標の正規化
                // Gemini推奨: キャッシュ保存前（オーバーレイ表示前）に座標を正規化
                // CaptureRegion == null: フルスクリーンキャプチャ → 変換不要
                // CaptureRegion != null: ROIキャプチャ → CombinedBoundsにOffsetを加算
                chunk = NormalizeChunkCoordinates(chunk);

                _logger.LogInformation("🔥 [FIX6_NORMALIZE] 座標正規化完了 - ChunkId: {ChunkId}, CaptureRegion: {CaptureRegion}, Bounds: ({X},{Y},{W}x{H})",
                    chunk.ChunkId,
                    chunk.CaptureRegion.HasValue ? $"({chunk.CaptureRegion.Value.X},{chunk.CaptureRegion.Value.Y})" : "null",
                    chunk.CombinedBounds.X, chunk.CombinedBounds.Y,
                    chunk.CombinedBounds.Width, chunk.CombinedBounds.Height);

                // 🔥🔥🔥 [FIX4_FULLSCREEN_COORD] フルスクリーンキャプチャ座標変換修正
                // 問題: ROIキャプチャ(CaptureRegion != null) → ROI_COORD_FIX実行 → 画像絶対座標
                //       フルスクリーンキャプチャ(CaptureRegion == null) → ROI_COORD_FIX未実行 → 画像相対座標
                // 解決: 全てのチャンクに対してConvertRoiToScreenCoordinates実行
                //       ROI_COORD_FIX実行済み: 画像絶対座標 → スクリーン絶対座標変換
                //       ROI_COORD_FIX未実行: 画像相対座標 → スクリーン絶対座標変換
                var isBorderlessOrFullscreen = _coordinateTransformationService.DetectBorderlessOrFullscreen(chunk.SourceWindowHandle);

                // 🚀 [Issue #193] GPUリサイズ後の座標は既にFullScreenOcrCaptureStrategyで
                // 元ウィンドウサイズにスケーリング済みのため、DPI補正をスキップする
                Console.WriteLine($"🚀🚀🚀 [Issue #193 DEBUG] ConvertRoiToScreenCoordinates呼び出し前 - Bounds: ({chunk.CombinedBounds.X},{chunk.CombinedBounds.Y},{chunk.CombinedBounds.Width}x{chunk.CombinedBounds.Height}), alreadyScaledToOriginalSize=true");
                var screenBounds = _coordinateTransformationService.ConvertRoiToScreenCoordinates(
                    chunk.CombinedBounds,  // 画像絶対座標またはROI相対座標
                    chunk.SourceWindowHandle,
                    roiScaleFactor: 1.0f,
                    isBorderlessOrFullscreen: isBorderlessOrFullscreen,
                    alreadyScaledToOriginalSize: true);  // 🚀 [Issue #193] 座標は既にスケーリング済み
                Console.WriteLine($"🚀🚀🚀 [Issue #193 DEBUG] ConvertRoiToScreenCoordinates呼び出し後 - Result: ({screenBounds.X},{screenBounds.Y},{screenBounds.Width}x{screenBounds.Height})");

                _logger?.LogDebug("🔥 [FIX4_FULLSCREEN_COORD] 座標変換実行 - 画像座標:({X},{Y}) → スクリーン座標:({SX},{SY})",
                    chunk.CombinedBounds.X, chunk.CombinedBounds.Y, screenBounds.X, screenBounds.Y);

                // 座標変換不要 - chunk.CombinedBoundsをそのまま使用して新しいチャンクインスタンスを作成
                // AverageConfidenceは計算プロパティのため、TextResultsから自動計算される
                var chunkWithScreenCoords = new TextChunk
                {
                    ChunkId = chunk.ChunkId,
                    TextResults = chunk.TextResults,
                    CombinedBounds = screenBounds, // 画像絶対座標（CoordinateBasedTranslationServiceで変換済み）
                    CombinedText = chunk.CombinedText,
                    TranslatedText = chunk.TranslatedText,
                    SourceWindowHandle = chunk.SourceWindowHandle,
                    DetectedLanguage = chunk.DetectedLanguage
                };

                // 🔧 [OVERLAY_UNIFICATION] 統一IOverlayManager.ShowAsync()で直接オーバーレイ表示（スクリーン絶対座標使用）
                var translationSettings = _unifiedSettingsService.GetTranslationSettings();
                var content = new OverlayContent
                {
                    Text = chunkWithScreenCoords.TranslatedText,
                    OriginalText = chunkWithScreenCoords.CombinedText,
                    FontSize = translationSettings.OverlayFontSize
                };

                var position = new OverlayPosition
                {
                    X = chunkWithScreenCoords.CombinedBounds.X,
                    Y = chunkWithScreenCoords.CombinedBounds.Y,
                    Width = chunkWithScreenCoords.CombinedBounds.Width,
                    Height = chunkWithScreenCoords.CombinedBounds.Height
                };

#if DEBUG
                // 🚨 [ULTRATHINK_TRACE4] ShowAsync呼び出し直前トレースログ
                var timestamp4 = DateTime.Now.ToString("HH:mm:ss.fff");
                var threadId4 = Environment.CurrentManagedThreadId;
                var overlayManagerType = _overlayManager?.GetType().FullName ?? "NULL";
                System.IO.File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "baketa_debug.log"),
                    $"[{timestamp4}][T{threadId4:D2}] 🚨 [ULTRATHINK_TRACE4] ShowAsync呼び出し直前 - チャンク{i}, Text: '{content.Text}', Position: ({position.X},{position.Y},{position.Width}x{position.Height}), OverlayManagerType: {overlayManagerType}\r\n");
#endif

                try
                {
                    await _overlayManager.ShowAsync(content, position).ConfigureAwait(false);

#if DEBUG
                    // 🚨 [ULTRATHINK_TRACE5] ShowAsync呼び出し完了トレースログ
                    var timestamp5 = DateTime.Now.ToString("HH:mm:ss.fff");
                    var threadId5 = Environment.CurrentManagedThreadId;
                    System.IO.File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "baketa_debug.log"),
                        $"[{timestamp5}][T{threadId5:D2}] 🚨 [ULTRATHINK_TRACE5] ShowAsync呼び出し完了 - チャンク{i}\r\n");
#endif
                }
                catch (Exception showAsyncEx)
                {
#if DEBUG
                    // 🚨 [ULTRATHINK_TRACE5_ERROR] ShowAsync例外トレースログ
                    var timestampErr = DateTime.Now.ToString("HH:mm:ss.fff");
                    var threadIdErr = Environment.CurrentManagedThreadId;
                    System.IO.File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "baketa_debug.log"),
                        $"[{timestampErr}][T{threadIdErr:D2}] 💥 [ULTRATHINK_TRACE5_ERROR] ShowAsync例外 - チャンク{i}, Exception: {showAsyncEx.GetType().Name}, Message: {showAsyncEx.Message}\r\n");
#endif
                    throw;
                }

                _logger?.LogDebug($"✅ [OVERLAY_FIX] チャンク{i}オーバーレイ表示完了 - Text: '{chunk.TranslatedText}', Bounds: ({chunk.CombinedBounds.X},{chunk.CombinedBounds.Y},{chunk.CombinedBounds.Width}x{chunk.CombinedBounds.Height})");
                Console.WriteLine($"✅ [OVERLAY_FIX] チャンク{i}オーバーレイ表示完了 - Text: '{chunk.TranslatedText}'");
            }

#if DEBUG
            // 🚨 [ULTRATHINK_TRACE6] オーバーレイ表示ループ完了トレースログ
            var timestamp6 = DateTime.Now.ToString("HH:mm:ss.fff");
            var threadId6 = Environment.CurrentManagedThreadId;
            System.IO.File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "baketa_debug.log"),
                $"[{timestamp6}][T{threadId6:D2}] 🚨 [ULTRATHINK_TRACE6] オーバーレイ表示ループ完了 - 表示数: {nonEmptyChunks.Count}\r\n");
#endif

            Console.WriteLine($"✅✅✅ [OVERLAY_FIX] オーバーレイ表示完了 - {nonEmptyChunks.Count}個表示");

            _logger.LogInformation("✅ [PHASE12.2] バッチ翻訳・個別イベント発行完了 - SessionId: {SessionId}, 翻訳数: {Count}",
                eventData.SessionId, translationResults.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE12.2] 集約チャンクイベント処理エラー - フォールバックイベント発行 - SessionId: {SessionId}",
                eventData.SessionId);

            // 🔥 [FALLBACK] 個別翻訳失敗時にフォールバックイベントを発行
            // AggregatedChunksFailedEventを発行し、CoordinateBasedTranslationServiceが全画面一括翻訳を実行
            try
            {
                var sourceLanguage = _languageConfig.GetSourceLanguageCode();
                var targetLanguage = _languageConfig.GetTargetLanguageCode();

                var failedEvent = new AggregatedChunksFailedEvent
                {
                    SessionId = eventData.SessionId,
                    FailedChunks = [.. eventData.AggregatedChunks],
                    SourceLanguage = sourceLanguage,
                    TargetLanguage = targetLanguage,
                    ErrorMessage = ex.Message,
                    ErrorException = ex
                };

                await _eventAggregator.PublishAsync(failedEvent).ConfigureAwait(false);
                _logger.LogInformation("✅ [FALLBACK] AggregatedChunksFailedEvent発行完了 - SessionId: {SessionId}",
                    eventData.SessionId);
            }
            catch (Exception publishEx)
            {
                _logger.LogError(publishEx, "❌ [FALLBACK] AggregatedChunksFailedEvent発行失敗 - SessionId: {SessionId}",
                    eventData.SessionId);
            }

            // 例外を再スローせず正常終了（フォールバック処理に委ねる）
        }
        finally
        {
            // 🔥 [PHASE1_SEMAPHORE] セマフォ解放（必ず実行）
            _translationExecutionSemaphore.Release();
            _logger?.LogDebug($"🔓 [PHASE1] セマフォ解放完了 - SessionId: {eventData.SessionId}");
        }
    }

    /// <summary>
    /// 🔥 [FIX6_NORMALIZE] TextChunk座標正規化メソッド
    /// ROI相対座標 → 画像絶対座標の変換を実行
    ///
    /// Gemini推奨アプローチ (Option B):
    /// - キャッシュ保存前に座標を正規化し、再利用時に変換不要にする
    /// - CombinedBounds: ROI相対座標 → 画像絶対座標に変換
    /// - CaptureRegion: コンテキスト情報として保持（座標検証・デバッグ用）
    /// </summary>
    /// <param name="chunk">正規化対象のTextChunk（ROI相対座標）</param>
    /// <returns>正規化後のTextChunk（画像絶対座標）</returns>
    private TextChunk NormalizeChunkCoordinates(TextChunk chunk)
    {
        // 座標は前段のPaddleOcrResultConverterで既に絶対座標に変換済みのため、ここでは何もしない。
        // [Code Review] no-opメソッドのためLogDebugに変更（本番ログを汚染しない）
        _logger.LogDebug("ℹ️ [COORD_FIX] 座標正規化は不要です。座標は既に絶対値のはずです: ({X},{Y})",
            chunk.CombinedBounds.X, chunk.CombinedBounds.Y);
        return chunk;
    }

    /// <summary>
    /// バッチ翻訳実行
    /// CoordinateBasedTranslationService.ProcessBatchTranslationAsync()のLine 363-450相当の処理
    /// </summary>
    private async Task<List<string>> ExecuteBatchTranslationAsync(
        List<TextChunk> chunks,
        CancellationToken cancellationToken)
    {
        // 🔥 メソッド開始を確実に記録
        _logger?.LogDebug($"🎯🎯🎯 [PHASE12.2_BATCH] ExecuteBatchTranslationAsync メソッド開始 - ChunkCount: {chunks.Count}");
        Console.WriteLine($"🎯🎯🎯 [PHASE12.2_BATCH] ExecuteBatchTranslationAsync メソッド開始 - ChunkCount: {chunks.Count}");

        var batchTexts = chunks.Select(c => c.CombinedText).ToList();

        _logger?.LogDebug($"🎯 [PHASE12.2_BATCH] バッチテキスト作成完了 - テキスト数: {batchTexts.Count}");

        try
        {
            _logger?.LogDebug($"🚀 [PHASE12.2_BATCH] バッチ翻訳試行開始 - テキスト数: {batchTexts.Count}");
            _logger.LogInformation("🚀 [PHASE12.2] バッチ翻訳試行開始 - テキスト数: {Count}", batchTexts.Count);

            // ストリーミング翻訳サービスが利用可能な場合はそれを使用
            if (_streamingTranslationService != null)
            {
                _logger?.LogDebug($"🔥 [PHASE12.2_BATCH] ストリーミング翻訳サービス使用");
                _logger.LogDebug("🔥 [PHASE12.2] ストリーミング翻訳サービス使用");

                // CoordinateBasedTranslationServiceと同じシグネチャ
                _logger?.LogDebug($"📞 [PHASE12.2_BATCH] TranslateBatchWithStreamingAsync呼び出し直前");

                // 🔥 [PHASE3.1_FIX] 設定から言語ペア取得（ハードコード削除）
                var languagePair = _languageConfig.GetCurrentLanguagePair();
                var sourceLanguage = Language.FromCode(languagePair.SourceCode);
                var targetLanguage = Language.FromCode(languagePair.TargetCode);

                _logger?.LogDebug($"🌍 [PHASE3.1_FIX] 言語ペア取得完了 - {languagePair.SourceCode} → {languagePair.TargetCode}");
                Console.WriteLine($"🌍 [PHASE3.1_FIX] 言語ペア取得完了 - {languagePair.SourceCode} → {languagePair.TargetCode}");

                // 🔥🔥🔥 [CALL_DEBUG] 呼び出し直前の詳細デバッグ
                Console.WriteLine($"🔥🔥🔥 [CALL_DEBUG] _streamingTranslationService型: {_streamingTranslationService?.GetType().FullName ?? "null"}");
                Console.WriteLine($"🔥🔥🔥 [CALL_DEBUG] batchTexts数: {batchTexts?.Count ?? 0}");
                Console.WriteLine($"🔥🔥🔥 [CALL_DEBUG] sourceLanguage: {sourceLanguage?.Code}, targetLanguage: {targetLanguage?.Code}");
                Console.WriteLine($"🔥🔥🔥 [CALL_DEBUG] TranslateBatchWithStreamingAsync await 開始...");

                List<string> results;
                try
                {
                    results = await _streamingTranslationService.TranslateBatchWithStreamingAsync(
                        batchTexts,
                        sourceLanguage,
                        targetLanguage,
                        null!, // OnChunkCompletedコールバックは不要（バッチ完了後にオーバーレイ表示）
                        cancellationToken).ConfigureAwait(false);

                    Console.WriteLine($"🔥🔥🔥 [CALL_DEBUG] TranslateBatchWithStreamingAsync await 完了 - 結果数: {results?.Count ?? 0}");
                }
                catch (Exception callEx)
                {
                    Console.WriteLine($"💥💥💥 [CALL_ERROR] TranslateBatchWithStreamingAsync例外: {callEx.GetType().Name}");
                    Console.WriteLine($"💥💥💥 [CALL_ERROR] Message: {callEx.Message}");
                    Console.WriteLine($"💥💥💥 [CALL_ERROR] StackTrace: {callEx.StackTrace}");
                    throw;
                }

                _logger?.LogDebug($"✅ [PHASE12.2_BATCH] TranslateBatchWithStreamingAsync完了 - 結果数: {results?.Count ?? 0}");
                return results ?? [];
            }
            else
            {
                // 通常の翻訳サービスを使用
                _logger?.LogDebug($"🔥🔥🔥 [PHASE12.2_BATCH] DefaultTranslationService使用（_streamingTranslationService is null）");
                Console.WriteLine($"🔥🔥🔥 [PHASE12.2_BATCH] DefaultTranslationService使用（_streamingTranslationService is null）");
                _logger.LogDebug("🔥 [PHASE12.2] DefaultTranslationService使用");

                // 🔥 [PHASE3.1_FIX] 設定から言語ペア取得（ハードコード削除）
                var languagePair = _languageConfig.GetCurrentLanguagePair();
                var sourceLanguage = Language.FromCode(languagePair.SourceCode);
                var targetLanguage = Language.FromCode(languagePair.TargetCode);

                _logger?.LogDebug($"🌍 [PHASE3.1_FIX] 言語ペア取得完了 - {languagePair.SourceCode} → {languagePair.TargetCode}");
                Console.WriteLine($"🌍 [PHASE3.1_FIX] 言語ペア取得完了 - {languagePair.SourceCode} → {languagePair.TargetCode}");

                var results = new List<string>();
                for (int i = 0; i < batchTexts.Count; i++)
                {
                    var text = batchTexts[i];
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _logger?.LogDebug($"⚠️ [PHASE12.2_BATCH] キャンセル要求検出 - Index: {i}");
                        break;
                    }

                    _logger?.LogDebug($"📞📞📞 [PHASE12.2_BATCH] TranslateAsync呼び出し直前 - Index: {i}, Text: '{text}'");
                    Console.WriteLine($"📞📞📞 [PHASE12.2_BATCH] TranslateAsync呼び出し直前 - Index: {i}, Text: '{text}'");

                    var response = await _translationService.TranslateAsync(
                        text,
                        sourceLanguage,
                        targetLanguage,
                        null,
                        cancellationToken).ConfigureAwait(false);

                    _logger?.LogDebug($"✅✅✅ [PHASE12.2_BATCH] TranslateAsync完了 - Index: {i}, TranslatedText: '{response.TranslatedText}'");
                    Console.WriteLine($"✅✅✅ [PHASE12.2_BATCH] TranslateAsync完了 - Index: {i}, TranslatedText: '{response.TranslatedText}'");

                    results.Add(response.TranslatedText);
                }

                _logger?.LogDebug($"✅ [PHASE12.2_BATCH] DefaultTranslationService完了 - 結果数: {results.Count}");
                return results;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE12.2] バッチ翻訳処理エラー");
            throw;
        }
    }

    // [Code Review] 未使用メソッド DisplayTranslationOverlayAsync を削除
    // HandleAsync 内で直接オーバーレイ表示ロジックを実装済みのため不要

    /// <summary>
    /// [Issue #229] ノイズパターンを検出（ボーダーライン緩和の除外条件）
    /// </summary>
    /// <remarks>
    /// Geminiフィードバック反映:
    /// - 同じ文字の繰り返し（例: "111111", "●●●"）
    /// - 記号のみのテキスト
    /// - その他のUIノイズパターン
    /// </remarks>
    private static bool IsNoisePattern(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;

        // 同じ文字の繰り返し（5回以上）を検出
        // 例: "111111", "●●●●●", "........."
        if (text.Length >= 5)
        {
            var firstChar = text[0];
            var allSame = true;
            for (int i = 1; i < text.Length; i++)
            {
                if (text[i] != firstChar)
                {
                    allSame = false;
                    break;
                }
            }
            if (allSame)
                return true;
        }

        // 文字・数字が全く含まれない（記号のみ）
        var alphaNumCount = 0;
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
                alphaNumCount++;
        }
        if (alphaNumCount == 0)
            return true;

        // 括弧に囲まれた数字のみ（例: "(111111111)"）
        if (text.StartsWith('(') && text.EndsWith(')'))
        {
            var inner = text[1..^1];
            if (inner.All(c => char.IsDigit(c)))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 🔥 [HALLUCINATION_FILTER] 繰り返しフレーズ検出
    /// OCRエンジンがループに陥り、同じフレーズを繰り返すハルシネーションを検出
    /// 例: "THE STATE OF THE STATE OF THE STATE OF..."
    /// </summary>
    /// <param name="text">検査対象テキスト</param>
    /// <returns>繰り返しハルシネーションの場合true</returns>
    /// <remarks>
    /// Geminiレビュー反映:
    /// - 短いテキスト（20文字未満）はスキップ（ゲームUIの正当な繰り返し許容）
    /// - 空白区切り単語の繰り返しは正当性が高いためスキップ
    /// </remarks>
    private static bool IsRepetitiveHallucination(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // 短いテキストはスキップ（日本語の場合、20文字未満は正当な繰り返しの可能性）
        // 例: "クリア クリア クリア", "はい はい はい"
        const int minTextLength = 20;
        if (text.Length < minTextLength)
            return false;

        // 空白区切りの「同一単語」繰り返しのみ許容（ゲームUI等）
        // 例: "クリア クリア クリア" → 許容（1種類の単語）
        // 例: "THE PARTY OF THE PARTY OF" → ハルシネーション（複数種類の単語でフレーズ繰り返し）
        var words = text.Split([' ', '　'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 3 && words.Distinct().Count() == 1)
        {
            // 同一単語の繰り返しは正当なUIテキスト
            return false;
        }

        // 最小繰り返し検出長（これより短いフレーズは無視）
        const int minPhraseLength = 4;
        // 最小繰り返し回数（この回数以上繰り返されたらハルシネーション）
        const int minRepetitionCount = 3;

        // 様々なフレーズ長で繰り返しをチェック
        for (int phraseLen = minPhraseLength; phraseLen <= text.Length / minRepetitionCount; phraseLen++)
        {
            var phrase = text[..phraseLen];

            // 空白のみのフレーズは無視
            if (string.IsNullOrWhiteSpace(phrase))
                continue;

            // このフレーズが何回繰り返されているかカウント
            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(phrase, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                count++;
                index += phrase.Length;
            }

            // 繰り返し回数が閾値以上、かつテキストの大部分を占める場合
            if (count >= minRepetitionCount)
            {
                // テキストの50%以上が同じフレーズの繰り返しで構成されている
                var repetitionRatio = (double)phrase.Length * count / text.Length;
                if (repetitionRatio >= 0.5)
                {
                    // Geminiレビュー反映: Console.WriteLineは開発時の確認用として残す
                    // 本番ではこのログはフィルタリングログで代替される
                    Console.WriteLine($"🚫 [HALLUCINATION_DETECT] 繰り返し検出: '{phrase}' が {count}回繰り返し（占有率: {repetitionRatio:P0}）");
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// [Issue #78 Phase 4/5] 並列翻訳を使用すべきかを判定
    /// </summary>
    /// <param name="eventData">集約チャンクイベントデータ</param>
    /// <returns>並列翻訳を使用すべき場合true</returns>
    private bool ShouldUseParallelTranslation(AggregatedChunksReadyEvent eventData)
    {
        // [Issue #280+#281] 診断ログ: 各条件をInfo レベルで出力
        _logger?.LogInformation(
            "🔍 [Phase4診断] 並列翻訳判定開始 - Orchestrator={Orchestrator}, LicenseManager={LicenseManager}, CloudAvailability={CloudAvailability}, HasImageData={HasImageData}",
            _parallelTranslationOrchestrator != null,
            _licenseManager != null,
            _cloudTranslationAvailabilityService?.IsEffectivelyEnabled,
            eventData.HasImageData);

        // 並列翻訳オーケストレーターが利用可能か
        if (_parallelTranslationOrchestrator == null)
        {
            _logger?.LogInformation("🔍 [Phase4] 並列翻訳スキップ: オーケストレーター未登録");
            return false;
        }

        // ライセンスマネージャーが利用可能か
        if (_licenseManager == null)
        {
            _logger?.LogInformation("🔍 [Phase4] 並列翻訳スキップ: ライセンスマネージャー未登録");
            return false;
        }

        // [Issue #273] Cloud翻訳可用性統合サービスで判定
        // ライセンス状態とユーザー設定の両方を統合チェック
        if (_cloudTranslationAvailabilityService != null)
        {
            if (!_cloudTranslationAvailabilityService.IsEffectivelyEnabled)
            {
                _logger?.LogInformation(
                    "🔍 [Issue #273] 並列翻訳スキップ: Cloud翻訳無効 (Entitled={Entitled}, Preferred={Preferred})",
                    _cloudTranslationAvailabilityService.IsEntitled,
                    _cloudTranslationAvailabilityService.IsPreferred);
                return false;
            }
        }
        else
        {
            // フォールバック: 旧ロジック（ICloudTranslationAvailabilityService未登録時）
            // 注: このフォールバックは段階的移行のため意図的に残しています。
            // ICloudTranslationAvailabilityServiceがDIコンテナに登録されるまでの互換性を保つため。
            // 将来的にすべての環境で新サービスが利用可能になれば削除可能です。
            // Cloud AI翻訳機能が利用可能か（Pro/Premiaプラン）
            if (!_licenseManager.IsFeatureAvailable(FeatureType.CloudAiTranslation))
            {
                _logger?.LogInformation("🔍 [Phase4] 並列翻訳スキップ: Cloud AI翻訳機能が無効（Free/Standardプラン）");
                return false;
            }

            // [Issue #280+#281] ユーザー設定でCloud AI翻訳が有効か（UseLocalEngineで判定）
            var translationSettings = _unifiedSettingsService.GetTranslationSettings();
            if (translationSettings.UseLocalEngine)
            {
                _logger?.LogInformation("🔍 [Issue #280] 並列翻訳スキップ: ローカル翻訳が選択されている");
                return false;
            }
        }

        // 画像データが利用可能か
        if (!eventData.HasImageData)
        {
            _logger?.LogInformation("🔍 [Phase4] 並列翻訳スキップ: 画像データなし");
            return false;
        }

        // セッショントークンが利用可能か
        var sessionId = _licenseManager.CurrentState.SessionId;
        if (string.IsNullOrEmpty(sessionId))
        {
            _logger?.LogInformation("🔍 [Phase4] 並列翻訳スキップ: セッショントークンなし");
            return false;
        }

        _logger?.LogInformation("✅ [Issue #273] 並列翻訳使用: 全条件クリア");
        return true;
    }

    /// <summary>
    /// [Issue #78 Phase 4] 並列翻訳を実行
    /// </summary>
    /// <param name="chunks">翻訳対象のテキストチャンク</param>
    /// <param name="eventData">集約チャンクイベントデータ</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>並列翻訳結果</returns>
    private async Task<ParallelTranslationResult> ExecuteParallelTranslationAsync(
        List<TextChunk> chunks,
        AggregatedChunksReadyEvent eventData,
        CancellationToken cancellationToken)
    {
        // 前提条件: このメソッドは ShouldUseParallelTranslation() が true を返した場合のみ呼び出される
        // したがって、以下の null-forgiving operator (!) は安全:
        // - _licenseManager: ShouldUseParallelTranslation() で null チェック済み
        // - _parallelTranslationOrchestrator: ShouldUseParallelTranslation() で null チェック済み
        // - eventData.ImageBase64: ShouldUseParallelTranslation() で HasImageData チェック済み

        try
        {
            // 言語ペアを取得
            var languagePair = _languageConfig.GetCurrentLanguagePair();

            // セッショントークンを取得（ShouldUseParallelTranslation で存在確認済み）
            var sessionToken = _licenseManager!.CurrentState.SessionId;

            // 並列翻訳リクエストを作成
            var request = new ParallelTranslationRequest
            {
                OcrChunks = chunks,
                ImageBase64 = eventData.ImageBase64!, // HasImageData で null でないことが保証済み
                ImageWidth = eventData.ImageWidth,
                ImageHeight = eventData.ImageHeight,
                SourceLanguage = languagePair.SourceCode,
                TargetLanguage = languagePair.TargetCode,
                SessionToken = sessionToken,
                UseCloudTranslation = true,
                EnableCrossValidation = true
            };

            _logger?.LogDebug(
                "🌐 [Phase4] ParallelTranslationRequest作成: Chunks={Chunks}, ImageSize={Width}x{Height}, Lang={Source}→{Target}",
                chunks.Count, eventData.ImageWidth, eventData.ImageHeight,
                languagePair.SourceCode, languagePair.TargetCode);

            // 並列翻訳を実行（ShouldUseParallelTranslation で null でないことが保証済み）
            var result = await _parallelTranslationOrchestrator!.TranslateAsync(request, cancellationToken)
                .ConfigureAwait(false);

            _logger?.LogInformation(
                "🌐 [Phase4] 並列翻訳完了: Success={Success}, Engine={Engine}, TotalTime={TotalTime}ms",
                result.IsSuccess, result.EngineUsed, result.Timing.TotalDuration.TotalMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ [Phase4] 並列翻訳エラー");

            return ParallelTranslationResult.Failure(
                new TranslationErrorDetail
                {
                    Code = TranslationErrorDetail.Codes.InternalError,
                    Message = ex.Message,
                    IsRetryable = true
                });
        }
    }

    /// <summary>
    /// [Issue #296] Cloud AI翻訳結果をOCRチャンクにマッチング
    /// </summary>
    /// <remarks>
    /// Cloud AI（Gemini）は画像から独自にOCRを実行するため、
    /// ローカルOCR（Surya）とは検出順序が異なる場合がある。
    /// Originalテキストを使用してマッチングし、正しい翻訳を対応付ける。
    ///
    /// マッチング戦略:
    /// 1. 完全一致: chunk.CombinedText == cloudText.Original
    /// 2. 正規化一致: 空白・改行を除去して比較
    /// 3. 部分一致: cloudText.Originalがchunk.CombinedTextを含む（または逆）
    /// 4. フォールバック: インデックスベースマッピング
    /// </remarks>
    private List<string> MatchCloudTranslationsToChunks(
        List<TextChunk> chunks,
        IReadOnlyList<TranslatedTextItem> cloudTexts)
    {
        var results = new List<string>(chunks.Count);

        // Cloud AI結果をOriginalテキストでルックアップ可能にする
        var exactMatchMap = cloudTexts
            .Where(t => !string.IsNullOrEmpty(t.Original))
            .GroupBy(t => t.Original)
            .ToDictionary(
                g => g.Key,
                g => g.First().Translation ?? string.Empty,
                StringComparer.Ordinal);

        // 正規化マップ（空白・改行除去）
        var normalizedMap = cloudTexts
            .Where(t => !string.IsNullOrEmpty(t.Original))
            .GroupBy(t => NormalizeText(t.Original))
            .ToDictionary(
                g => g.Key,
                g => g.First().Translation ?? string.Empty,
                StringComparer.Ordinal);

        var matchedCount = 0;
        var normalizedMatchCount = 0;
        var partialMatchCount = 0;
        var fallbackCount = 0;

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunkText = chunks[i].CombinedText ?? string.Empty;
            string translation;

            // 1. 完全一致
            if (exactMatchMap.TryGetValue(chunkText, out translation!))
            {
                results.Add(translation);
                matchedCount++;
                continue;
            }

            // 2. 正規化一致
            var normalizedChunkText = NormalizeText(chunkText);
            if (!string.IsNullOrEmpty(normalizedChunkText) &&
                normalizedMap.TryGetValue(normalizedChunkText, out translation!))
            {
                results.Add(translation);
                normalizedMatchCount++;
                _logger?.LogDebug(
                    "🔍 [Issue #296] 正規化マッチ: Chunk[{Index}] '{ChunkText}' → '{Translation}'",
                    i, chunkText.Length > 30 ? chunkText[..30] + "..." : chunkText,
                    translation.Length > 30 ? translation[..30] + "..." : translation);
                continue;
            }

            // 3. 部分一致（CloudのOriginalがChunkを含む、または逆）
            var partialMatch = cloudTexts.FirstOrDefault(t =>
                !string.IsNullOrEmpty(t.Original) &&
                (t.Original.Contains(chunkText, StringComparison.OrdinalIgnoreCase) ||
                 chunkText.Contains(t.Original, StringComparison.OrdinalIgnoreCase)));

            if (partialMatch != null)
            {
                results.Add(partialMatch.Translation ?? string.Empty);
                partialMatchCount++;
                _logger?.LogDebug(
                    "🔍 [Issue #296] 部分マッチ: Chunk[{Index}] '{ChunkText}' ⊂⊃ '{CloudOriginal}' → '{Translation}'",
                    i,
                    chunkText.Length > 20 ? chunkText[..20] + "..." : chunkText,
                    partialMatch.Original?.Length > 20 ? partialMatch.Original[..20] + "..." : partialMatch.Original,
                    partialMatch.Translation?.Length > 20 ? partialMatch.Translation[..20] + "..." : partialMatch.Translation);
                continue;
            }

            // 4. フォールバック: インデックスベース（最終手段）
            if (i < cloudTexts.Count)
            {
                results.Add(cloudTexts[i].Translation ?? string.Empty);
                fallbackCount++;
                _logger?.LogWarning(
                    "⚠️ [Issue #296] フォールバック（インデックス）: Chunk[{Index}] '{ChunkText}' → CloudTexts[{Index}]",
                    i, chunkText.Length > 30 ? chunkText[..30] + "..." : chunkText, i);
            }
            else
            {
                results.Add(string.Empty);
                _logger?.LogWarning(
                    "⚠️ [Issue #296] マッチなし: Chunk[{Index}] '{ChunkText}' - Cloud AI結果に対応なし",
                    i, chunkText.Length > 50 ? chunkText[..50] + "..." : chunkText);
            }
        }

        _logger?.LogInformation(
            "📊 [Issue #296] マッチング統計: 完全一致={Exact}, 正規化={Normalized}, 部分={Partial}, フォールバック={Fallback}, 合計={Total}",
            matchedCount, normalizedMatchCount, partialMatchCount, fallbackCount, chunks.Count);

#if DEBUG
        Console.WriteLine($"📊 [Issue #296] マッチング統計: 完全={matchedCount}, 正規化={normalizedMatchCount}, 部分={partialMatchCount}, FB={fallbackCount}");
#endif

        return results;
    }

    /// <summary>
    /// [Issue #296] テキスト正規化（マッチング用）
    /// </summary>
    private static string NormalizeText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // 空白・改行・制御文字を除去
        return new string(text
            .Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c))
            .ToArray());
    }
}
