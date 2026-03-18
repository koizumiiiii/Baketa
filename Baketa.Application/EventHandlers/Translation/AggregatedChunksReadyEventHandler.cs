using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.License; // [Issue #78 Phase 4] ILicenseManager用
using Baketa.Core.Abstractions.Processing; // [Issue #293] ITextChangeDetectionService用
using Baketa.Core.Abstractions.Roi; // [Issue #293] IRoiManager用
using Baketa.Core.Models.Roi; // [Issue #354] NormalizedRect用
using Baketa.Core.Abstractions.Services; // 🔥 [COORDINATE_FIX] ICoordinateTransformationService用
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.UI;
using Baketa.Core.Abstractions.Validation; // [Issue #414] IFuzzyTextMatcher用
using Baketa.Core.Abstractions.UI.Overlays; // 🔧 [OVERLAY_UNIFICATION] IOverlayManager統一インターフェース用
using Baketa.Core.Events.EventTypes;
using Baketa.Core.Events.Translation;
using Baketa.Core.License.Models; // [Issue #78 Phase 4] FeatureType用
using Baketa.Core.Models.Text; // [Issue #293] GateRegionInfo用
using Baketa.Core.Models.Translation;
using Baketa.Core.Translation.Abstractions; // TranslatedTextItem用
using Baketa.Core.Translation.Models;
using Baketa.Application.Services.Translation; // [Issue #291] ITranslationControlService用
using Baketa.Core.Settings; // [Issue #379] RoiManagerSettings用
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options; // [Issue #379] IOptions用
using Language = Baketa.Core.Models.Translation.Language;

namespace Baketa.Application.EventHandlers.Translation;

/// <summary>
/// 集約済みチャンクに対してバッチ翻訳を実行するイベントハンドラ
/// Phase 12.2: 2重翻訳アーキテクチャ排除の中核実装
///
/// TimedChunkAggregatorから発行されるAggregatedChunksReadyEventを受信し、
/// CoordinateBasedTranslationService.ProcessBatchTranslationAsync()相当の処理を実行
/// </summary>
public sealed partial class AggregatedChunksReadyEventHandler : IEventProcessor<AggregatedChunksReadyEvent>
{
    // [Issue #380] 座標ベースフォールバックマッチングのIoU閾値
    // Cloud AI BoundingBoxとOCRチャンクCombinedBoundsの重なり判定に使用
    private const float CoordinateMatchIoUThreshold = 0.3f;

    // [Issue #387] Cloud結果主導チャンクのChunkId開始オフセット
    // Surya由来のChunkIdと区別するため
    private const int CloudDrivenChunkIdOffset = 10000;

    // 🔥 [PHASE1_SEMAPHORE] 翻訳実行制御用セマフォ（1並列のみ許可）
    // Gemini推奨の多層防御アーキテクチャ - 第2層: 物理的排他制御
    private static readonly SemaphoreSlim _translationExecutionSemaphore = new(1, 1);

    // [Issue #392] ResetSemaphoreForStopとfinallyブロックの二重解放を防止するフラグ
    // ResetSemaphoreForStopがセマフォを解放した場合、finallyブロックでの解放をスキップ
    private static volatile bool _semaphoreReleasedByStop;

    // [Issue #415] Cloud翻訳キャッシュ（Fork-Join段階で画像ハッシュベースの抑制に移行）
    private readonly ICloudTranslationCache? _cloudTranslationCache;

    private readonly Baketa.Core.Abstractions.Translation.ITranslationService _translationService;
    private readonly IStreamingTranslationService? _streamingTranslationService;
    // 🔧 [OVERLAY_UNIFICATION] IInPlaceTranslationOverlayManager → IOverlayManager に統一
    private readonly IOverlayManager _overlayManager;
    private readonly ILanguageConfigurationService _languageConfig;
    private readonly IEventAggregator _eventAggregator;
    private readonly ILogger<AggregatedChunksReadyEventHandler> _logger;
    private readonly ICoordinateTransformationService _coordinateTransformationService; // 🔥 [COORDINATE_FIX]
    private readonly Core.Abstractions.Settings.IUnifiedSettingsService _unifiedSettingsService;
    private readonly ILicenseManager? _licenseManager;
    // [Issue #273] Cloud翻訳可用性統合サービス
    private readonly Core.Abstractions.Translation.ICloudTranslationAvailabilityService? _cloudTranslationAvailabilityService;
    // [Issue #291] 翻訳状態確認用サービス（キャンセル状態チェック）
    // NOTE: CancellationToken伝播により不要になったが、将来の拡張用に保持
    private readonly ITranslationControlService? _translationControlService;
    // [Issue #293] テキスト変化検知サービス（Gate判定用）
    private readonly ITextChangeDetectionService? _textChangeDetectionService;
    // [Issue #293] ROI管理サービス（ヒートマップ値取得用）
    private readonly IRoiManager? _roiManager;
    // [Issue #379] ROI管理設定（OCR信頼度閾値等）
    private readonly RoiManagerSettings _roiSettings;
    // [Issue #414] ファジーテキストマッチング（Cloud結果のあいまい一致検証用）
    private readonly IFuzzyTextMatcher? _fuzzyTextMatcher;

    // [Issue #491] OCR不検出ベースのオーバーレイ自動クリア
    // 前サイクルでOCRがテキストを検出したゾーンのセット
    private HashSet<string> _previousCycleZones = [];
    // ゾーンごとの連続不検出カウンター
    private readonly ConcurrentDictionary<string, int> _zoneAbsenceCounter = new();
    // 不検出と判定する連続サイクル数
    private const int OcrAbsenceThreshold = 3;

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
        ILicenseManager? licenseManager = null,
        // [Issue #273] Cloud翻訳可用性統合サービス（オプショナル）
        Core.Abstractions.Translation.ICloudTranslationAvailabilityService? cloudTranslationAvailabilityService = null,
        // [Issue #291] 翻訳状態確認用サービス（オプショナル）
        ITranslationControlService? translationControlService = null,
        // [Issue #293] テキスト変化検知サービス（オプショナル）
        ITextChangeDetectionService? textChangeDetectionService = null,
        // [Issue #293] ROI管理サービス（オプショナル）
        IRoiManager? roiManager = null,
        // [Issue #379] ROI管理設定（オプショナル）
        IOptions<RoiManagerSettings>? roiSettings = null,
        // [Issue #414] ファジーテキストマッチング（オプショナル）
        IFuzzyTextMatcher? fuzzyTextMatcher = null,
        // [Issue #415] Cloud翻訳キャッシュ（オプショナル）
        ICloudTranslationCache? cloudTranslationCache = null)
    {
        _translationService = translationService ?? throw new ArgumentNullException(nameof(translationService));
        _overlayManager = overlayManager ?? throw new ArgumentNullException(nameof(overlayManager));
        _languageConfig = languageConfig ?? throw new ArgumentNullException(nameof(languageConfig));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _coordinateTransformationService = coordinateTransformationService ?? throw new ArgumentNullException(nameof(coordinateTransformationService)); // 🔥 [COORDINATE_FIX]
        _unifiedSettingsService = unifiedSettingsService ?? throw new ArgumentNullException(nameof(unifiedSettingsService));
        _streamingTranslationService = streamingTranslationService;
        _licenseManager = licenseManager;
        // [Issue #273] Cloud翻訳可用性統合サービス
        _cloudTranslationAvailabilityService = cloudTranslationAvailabilityService;
        // [Issue #291] 翻訳状態確認用サービス
        _translationControlService = translationControlService;
        // [Issue #293] テキスト変化検知サービス
        _textChangeDetectionService = textChangeDetectionService;
        // [Issue #293] ROI管理サービス
        _roiManager = roiManager;
        // [Issue #379] ROI管理設定
        _roiSettings = roiSettings?.Value ?? RoiManagerSettings.CreateDefault();
        // [Issue #414] ファジーテキストマッチング
        _fuzzyTextMatcher = fuzzyTextMatcher;
        // [Issue #415] Cloud翻訳キャッシュ
        _cloudTranslationCache = cloudTranslationCache;
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
                // [Issue #392] finallyブロックでの二重解放を防止するフラグを先に設定
                _semaphoreReleasedByStop = true;
                _translationExecutionSemaphore.Release();
                Console.WriteLine("🔓 [STOP_CLEANUP] セマフォ強制解放完了 - Stop時クリーンアップ");
            }
            catch (SemaphoreFullException)
            {
                // 既に解放済み（CurrentCount == 1）の場合は無視
                _semaphoreReleasedByStop = false; // フラグをリセット
                Console.WriteLine("ℹ️ [STOP_CLEANUP] セマフォは既に解放済み");
            }
        }
        else
        {
            Console.WriteLine($"ℹ️ [STOP_CLEANUP] セマフォは既に利用可能 - CurrentCount: {_translationExecutionSemaphore.CurrentCount}");
        }

        // [Issue #415] Cloud翻訳キャッシュのクリアはFork-Join側（CoordinateBasedTranslationService.ResetTranslationState）で実施
    }

    /// <inheritdoc />
    /// <summary>
    /// [Issue #291] CancellationToken対応のイベント処理
    /// </summary>
    public async Task HandleAsync(AggregatedChunksReadyEvent eventData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        // [Issue #391] 前回サイクルのフラグ残留を防止（新しいHandleAsync呼び出しごとにリセット）
        _semaphoreReleasedByStop = false;

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

            // [Issue #399] チャンク密度ガード: 小さなROIから大量テキスト検出 = ノイズ
            {
                var chunksByBounds = aggregatedChunks
                    .GroupBy(c => (c.CombinedBounds.X / 200, c.CombinedBounds.Y / 200))
                    .Where(g => g.Count() > 5)
                    .ToList();

                foreach (var denseGroup in chunksByBounds)
                {
                    var groupList = denseGroup.ToList();
                    var groupBounds = groupList.Select(c => c.CombinedBounds).ToList();
                    var minX = groupBounds.Min(b => b.X);
                    var minY = groupBounds.Min(b => b.Y);
                    var maxX = groupBounds.Max(b => b.X + b.Width);
                    var maxY = groupBounds.Max(b => b.Y + b.Height);
                    var area = (maxX - minX) * (maxY - minY);
                    var groupCount = groupList.Count;
                    var areaPerChunk = area / groupCount;

                    if (areaPerChunk < 3000) // 1チャンクあたり3000px²未満 = 密集しすぎ
                    {
                        _logger.LogWarning(
                            "[Issue #399] ノイズ密度検出: {Count}チャンクが{Area}px²に密集（{PerChunk}px²/chunk）- 除外",
                            groupCount, area, areaPerChunk);
                        var removeSet = new HashSet<TextChunk>(groupList);
                        aggregatedChunks.RemoveAll(c => removeSet.Contains(c));
                    }
                }
            }

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

            // [Issue #293] ROI信頼度緩和設定の取得
            var enableRoiRelaxation = ocrSettings?.EnableRoiConfidenceRelaxation ?? true;
            var roiConfidenceThreshold = ocrSettings?.RoiConfidenceThreshold ?? 0.40;
            var roiMinTextLength = ocrSettings?.RoiMinTextLength ?? 3;

            // 🔍 [DIAGNOSTIC] 各チャンクの信頼度をログ出力
            var passedChunks = new List<TextChunk>();
            var borderlineAcceptedCount = 0;
            var roiRelaxedAcceptedCount = 0;
            var cloudBypassCount = 0;

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

                // ケース3: [Issue #293] ROI信頼度緩和を試行
                // ROI学習済み領域で検出されたテキストには低い閾値を適用
                // 条件: ROI緩和有効 + 信頼度がROI閾値以上 + ノイズパターンでない + 最小テキスト長を満たす
                if (enableRoiRelaxation &&
                    confidence >= roiConfidenceThreshold &&
                    confidence < confidenceThreshold &&
                    textLength >= roiMinTextLength &&
                    !IsNoisePattern(chunk.CombinedText))
                {
                    // ROI緩和条件を満たす → 採用
                    passedChunks.Add(chunk);
                    roiRelaxedAcceptedCount++;
                    _logger.LogInformation(
                        "🔍 [OCR_CHUNK] ✅ROI_RELAXED Conf={Confidence:F3} RoiThreshold={RoiThreshold:F2} " +
                        "TextLen={TextLen} Text='{Text}'",
                        confidence, roiConfidenceThreshold, textLength,
                        chunk.CombinedText!.Length > 50 ? chunk.CombinedText[..50] + "..." : chunk.CombinedText);
                    Console.WriteLine($"🎯 [ROI_RELAXED_ACCEPTED] Conf={confidence:F3} Text='{chunk.CombinedText}'");
                    continue;
                }

                // ケース4: [Issue #478] Cloud翻訳結果 + Surya検出一致によるバイパス
                // Cloud結果が存在し、このSuryaチャンクとBBoxが一致する場合は信頼度に関わらず採用
                if (eventData.HasPreComputedCloudResult &&
                    !IsNoisePattern(chunk.CombinedText))
                {
                    var cloudTexts = eventData.PreComputedCloudResult!.Response?.Texts;
                    if (cloudTexts is { Count: > 0 })
                    {
                        var matchingCloud = FindMatchingCloudResult(
                            chunk.CombinedBounds, cloudTexts,
                            eventData.ImageWidth, eventData.ImageHeight);

                        if (matchingCloud != null)
                        {
                            passedChunks.Add(chunk);
                            cloudBypassCount++;
                            _logger.LogInformation(
                                "[Issue #478] [OCR_CHUNK] CLOUD_BYPASS Conf={Confidence:F3} CloudText='{CloudText}' SuryaText='{SuryaText}'",
                                confidence,
                                matchingCloud.Original?.Length > 50 ? matchingCloud.Original[..50] + "..." : matchingCloud.Original,
                                chunk.CombinedText!.Length > 50 ? chunk.CombinedText[..50] + "..." : chunk.CombinedText);
                            continue;
                        }
                    }
                }

                // ケース5: 閾値未満 → 却下
                _logger.LogInformation("🔍 [OCR_CHUNK] ❌FAIL Conf={Confidence:F3} Threshold={Threshold:F2} Text='{Text}'",
                    confidence, confidenceThreshold,
                    chunk.CombinedText?.Length > 50 ? chunk.CombinedText[..50] + "..." : chunk.CombinedText);
            }

            var highConfidenceChunks = passedChunks;
            var filteredByConfidenceCount = aggregatedChunks.Count - highConfidenceChunks.Count;

            if (filteredByConfidenceCount > 0 || borderlineAcceptedCount > 0 || roiRelaxedAcceptedCount > 0 || cloudBypassCount > 0)
            {
                Console.WriteLine($"🔍 [CONFIDENCE_FILTER] 信頼度フィルタリング: {filteredByConfidenceCount}件除外, {borderlineAcceptedCount}件ボーダーライン採用, {roiRelaxedAcceptedCount}件ROI緩和採用, {cloudBypassCount}件Cloudバイパス採用（閾値={confidenceThreshold:F2}）");
                _logger.LogInformation(
                    "🔍 [CONFIDENCE_FILTER] 信頼度{Threshold:F2}未満の{FilteredCount}件をフィルタリング, {BorderlineCount}件ボーダーライン採用, {RoiRelaxedCount}件ROI緩和採用, {CloudBypassCount}件Cloudバイパス採用（残り{RemainingCount}件）",
                    confidenceThreshold, filteredByConfidenceCount, borderlineAcceptedCount, roiRelaxedAcceptedCount, cloudBypassCount, highConfidenceChunks.Count);
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

            // [Issue #397][Issue #466] ゴミテキストフィルタ（アスペクト比 + 反復パターン + 数字記号のみ + スクリプト混入）
            var preGarbageCount = nonEmptyChunks.Count;
#if DEBUG
            var garbageTexts = nonEmptyChunks
                .Where(chunk => IsGarbageText(chunk))
                .Select(chunk => chunk.CombinedText?.Trim() ?? "")
                .ToList();
            if (garbageTexts.Count > 0)
            {
                _logger.LogDebug(
                    "[Issue #466] フィルタ対象ゴミテキスト: [{GarbageTexts}]",
                    string.Join(", ", garbageTexts.Select(t => $"\"{t}\"")));
            }
#endif
            nonEmptyChunks = nonEmptyChunks
                .Where(chunk => !IsGarbageText(chunk))
                .ToList();
            var garbageFilteredCount = preGarbageCount - nonEmptyChunks.Count;
            if (garbageFilteredCount > 0)
            {
                _logger.LogWarning(
                    "[Issue #397] ゴミテキスト{Count}件をフィルタリング（残り{Remaining}件）",
                    garbageFilteredCount, nonEmptyChunks.Count);
            }

            // 空のチャンクに空文字列を設定
            foreach (var emptyChunk in aggregatedChunks.Where(c => string.IsNullOrWhiteSpace(c.CombinedText)))
            {
                emptyChunk.TranslatedText = "";
            }

            if (nonEmptyChunks.Count == 0)
            {
                _logger.LogWarning("⚠️ [PHASE12.2] 翻訳可能なチャンクが0個 - 処理スキップ");

                // [Issue #557] ローディング終了イベントを発火して「準備中」表示を解除
                try
                {
                    await _eventAggregator.PublishAsync(
                        new Baketa.Core.Events.EventTypes.FirstTranslationResultReceivedEvent(),
                        cancellationToken).ConfigureAwait(false);
                    _logger.LogDebug("[Issue #557] チャンク0個のためローディング終了イベント発火");
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "[Issue #557] ローディング終了イベント発火失敗（継続）");
                }

                return;
            }

            // ============================================================
            // [Issue #293] Gate判定: テキスト変化検知によるフィルタリング
            // [Issue #379] Singleshotモード時はGateフィルタリングをバイパス
            // ============================================================
            if (eventData.TranslationMode == Baketa.Core.Abstractions.Services.TranslationMode.Singleshot)
            {
                _logger?.LogInformation("🚪 [Issue #379] Gate判定スキップ: Singleshotモード（強制翻訳）, ChunkCount={Count}", nonEmptyChunks.Count);
            }
            else
            {
                nonEmptyChunks = await ApplyGateFilteringAsync(
                    nonEmptyChunks,
                    eventData.ImageWidth,
                    eventData.ImageHeight,
                    cancellationToken).ConfigureAwait(false);

                if (nonEmptyChunks.Count == 0)
                {
                    _logger.LogInformation("🚪 [Issue #293] Gate判定: 全チャンクが変化なしと判定されスキップ");

                    // [Issue #557] ローディング終了イベントを発火
                    try
                    {
                        await _eventAggregator.PublishAsync(
                            new Baketa.Core.Events.EventTypes.FirstTranslationResultReceivedEvent(),
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug(ex, "[Issue #557] ローディング終了イベント発火失敗（継続）");
                    }

                    return;
                }
            }


            // ============================================================
            // [Issue #290] Fork-Join: 事前計算されたCloud AI翻訳結果を優先使用
            // ============================================================
            List<string> translationResults;

            // [Issue #307] 翻訳処理時間計測とエンジン追跡
            var translationStopwatch = Stopwatch.StartNew();
            var engineUsed = "Default";

            if (eventData.HasPreComputedCloudResult)
            {
                // 事前計算されたCloud AI翻訳結果が利用可能
                // [Issue #307] エンジン名を記録（FallbackLevel enumを文字列に変換）
                engineUsed = eventData.PreComputedCloudResult!.UsedEngine.ToString();
                _logger?.LogInformation(
                    "🚀 [Issue #290] Fork-Join: 事前計算されたCloud AI翻訳結果を使用 (Engine={Engine})",
                    engineUsed);
#if DEBUG
                Console.WriteLine($"🚀 [Issue #290] Fork-Join: 事前計算Cloud AI結果を使用 - Engine: {engineUsed}");
#endif

                var cloudResponse = eventData.PreComputedCloudResult!.Response;

                // Cloud AI翻訳結果からテキストを抽出
                if (cloudResponse?.Texts is { Count: > 0 } cloudTexts)
                {
                    // [Issue #387] Cloud結果にBoundingBoxがある場合はCloud結果主導アプローチ
                    // Cloud AI（Gemini）の意味的テキスト分離を活かし、個別BoundingBoxで正確なオーバーレイ表示
                    var hasCloudBoundingBoxes = cloudTexts.Any(t => t.HasBoundingBox);

                    // [Issue #387] 診断ログ: Cloud結果の詳細を出力
                    for (int ci = 0; ci < cloudTexts.Count; ci++)
                    {
                        var ct = cloudTexts[ci];
                        _logger?.LogInformation(
                            "[Issue #387] Cloud結果[{Index}]: Original='{Original}' Translation='{Translation}' HasBBox={HasBBox} BBox={BBox}",
                            ci,
                            ct.Original?.Length > 50 ? ct.Original[..50] + "..." : ct.Original,
                            ct.Translation?.Length > 50 ? ct.Translation[..50] + "..." : ct.Translation,
                            ct.HasBoundingBox,
                            ct.HasBoundingBox ? $"({ct.BoundingBox!.Value.X},{ct.BoundingBox!.Value.Y},{ct.BoundingBox!.Value.Width}x{ct.BoundingBox!.Value.Height})" : "N/A");
                    }
                    _logger?.LogInformation(
                        "[Issue #387] hasCloudBoundingBoxes={HasBBox}, ImageSize={W}x{H}, SuryaChunks={Count}",
                        hasCloudBoundingBoxes, eventData.ImageWidth, eventData.ImageHeight, nonEmptyChunks.Count);
                    for (int si = 0; si < nonEmptyChunks.Count; si++)
                    {
                        var sc = nonEmptyChunks[si];
                        _logger?.LogInformation(
                            "[Issue #387] SuryaChunk[{Index}]: ChunkId={ChunkId} Bounds=({X},{Y},{W}x{H}) Text='{Text}'",
                            si, sc.ChunkId,
                            sc.CombinedBounds.X, sc.CombinedBounds.Y, sc.CombinedBounds.Width, sc.CombinedBounds.Height,
                            sc.CombinedText?.Length > 50 ? sc.CombinedText[..50] + "..." : sc.CombinedText);
                    }

                    // [Issue #398] ハルシネーションガード: 結果件数が異常に多い場合はCloud結果を破棄
                    const int MaxReasonableCloudResults = 20;
                    if (cloudTexts.Count > MaxReasonableCloudResults)
                    {
                        _logger?.LogWarning(
                            "[Issue #398] ハルシネーション検出: Cloud結果{Count}件が閾値{Max}件を超過 - 全Cloud結果を破棄",
                            cloudTexts.Count, MaxReasonableCloudResults);

                        // Cloud結果を使わず、ローカル翻訳にフォールバック
                        translationResults = await ExecuteBatchTranslationAsync(
                            nonEmptyChunks,
                            cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                    // Cloud結果の重複排除（Gemini APIが同一テキストを二重出力する場合の防御）
                    // Original + BoundingBox座標が一致するアイテムを重複とみなす
                    var dedupedCloudTexts = cloudTexts
                        .GroupBy(t => (t.Original, t.BoundingBox?.X, t.BoundingBox?.Y, t.BoundingBox?.Width, t.BoundingBox?.Height))
                        .Select(g => g.First())
                        .ToList();

                    if (dedupedCloudTexts.Count < cloudTexts.Count)
                    {
                        _logger?.LogWarning(
                            "Cloud結果重複排除: {OriginalCount}件 → {DedupedCount}件（{RemovedCount}件の重複を除去）",
                            cloudTexts.Count, dedupedCloudTexts.Count, cloudTexts.Count - dedupedCloudTexts.Count);
                    }

                    // [Issue #414] サイクル間重複検出（ログ記録 + キャッシュ更新のみ）
                    // NOTE: Cloud APIコールは既に完了済みのため、ここでの結果フィルタリングは行わない。
                    // 結果を除外するとオーバーレイが消失する（毎サイクル再作成のため）。
                    // 将来的にはFork-Join段階でAPIコール自体を抑制する設計に移行予定。
                    UpdateCloudResultCache(dedupedCloudTexts);

                    if (hasCloudBoundingBoxes && eventData.ImageWidth > 0 && eventData.ImageHeight > 0)
                    {
                        _logger?.LogInformation(
                            "[Issue #387] Cloud結果主導アプローチ: BoundingBox付きCloud結果を起点に処理");

                        var (cloudOverlayChunks, cloudTranslations) = CreateCloudDrivenOverlayItems(
                            nonEmptyChunks,
                            dedupedCloudTexts,
                            eventData.ImageWidth,
                            eventData.ImageHeight);

                        if (cloudOverlayChunks.Count > 0)
                        {
                            // Cloud主導の結果でnonEmptyChunksとtranslationResultsを置換
                            nonEmptyChunks = cloudOverlayChunks;
                            translationResults = cloudTranslations;
                            _logger?.LogInformation(
                                "[Issue #387] Cloud結果主導: {Count}個のオーバーレイアイテム作成",
                                cloudOverlayChunks.Count);
                        }
                        else
                        {
                            // Cloud主導で0件 → 従来のSurya主導マッチングにフォールバック
                            _logger?.LogWarning(
                                "[Issue #387] Cloud結果主導で有効アイテム0件 → Surya主導マッチングにフォールバック");
                            translationResults = MatchCloudTranslationsToChunks(
                                nonEmptyChunks,
                                dedupedCloudTexts,
                                eventData.ImageWidth,
                                eventData.ImageHeight);
                        }
                    }
                    else
                    {
                        // BoundingBoxなし → 従来のテキストマッチング
                        // [Issue #296] Originalテキストでマッチング
                        // [Issue #380] 座標フォールバックマッチングのため画像サイズも渡す
                        translationResults = MatchCloudTranslationsToChunks(
                            nonEmptyChunks,
                            dedupedCloudTexts,
                            eventData.ImageWidth,
                            eventData.ImageHeight);
                    }

                    _logger?.LogDebug(
                        "✅ [Issue #387] Fork-Join Cloud AI翻訳結果: {CloudCount}個（重複排除後） → {MatchedCount}個マッチ",
                        dedupedCloudTexts.Count, translationResults.Count(r => !string.IsNullOrEmpty(r)));
                    } // end of else (件数ガード通過)
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
            else
            {
                // 従来のローカル翻訳のみ
                // [Issue #307] エンジン名を記録
                engineUsed = "Local";
                _logger?.LogDebug($"🚀🚀🚀 [PHASE12.2_HANDLER] ExecuteBatchTranslationAsync呼び出し直前 - ChunkCount: {nonEmptyChunks.Count}");
                Console.WriteLine($"🚀🚀🚀 [PHASE12.2_HANDLER] ExecuteBatchTranslationAsync呼び出し直前 - ChunkCount: {nonEmptyChunks.Count}");

                // [Issue #291] CancellationTokenを伝播
                translationResults = await ExecuteBatchTranslationAsync(
                    nonEmptyChunks,
                    cancellationToken).ConfigureAwait(false);
            }

            // [Issue #307] 翻訳処理時間を記録
            translationStopwatch.Stop();
            var processingTime = translationStopwatch.Elapsed;

            _logger?.LogDebug($"✅✅✅ [PHASE12.2_HANDLER] 翻訳完了 - 結果数: {translationResults.Count}");
            Console.WriteLine($"✅✅✅ [PHASE12.2_HANDLER] 翻訳完了 - 結果数: {translationResults.Count}");

            // [Issue #307] TranslationCompletedEventを発行（Analytics用）
            // AnalyticsEventProcessorがこのイベントを購読して使用統計を記録
            try
            {
                var languagePair = _languageConfig.GetCurrentLanguagePair();
                var translationCompletedEvent = new TranslationCompletedEvent(
                    sourceText: "[batch]",  // プライバシー考慮: 実際のテキストは送信しない
                    translatedText: "[batch]",
                    sourceLanguage: languagePair.SourceCode,
                    targetLanguage: languagePair.TargetCode,
                    processingTime: processingTime,
                    engineName: engineUsed,
                    isBatchAnalytics: true);

                await _eventAggregator.PublishAsync(translationCompletedEvent, cancellationToken).ConfigureAwait(false);
                _logger?.LogInformation(
                    "[Issue #307] TranslationCompletedEvent発行: Engine={Engine}, ProcessingTime={Time}ms, Lang={Source}→{Target}",
                    engineUsed, (long)processingTime.TotalMilliseconds, languagePair.SourceCode, languagePair.TargetCode);
            }
            catch (Exception eventEx)
            {
                // イベント発行失敗はアプリ動作に影響しない
                _logger?.LogWarning(eventEx, "[Issue #307] TranslationCompletedEvent発行失敗（継続）");
            }

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
                var translated = translationResults[i];

                // [Issue #483] 翻訳結果の品質検証 - ゴミ出力をフィルタリング
                if (IsGarbageTranslation(translated, nonEmptyChunks[i].CombinedText))
                {
                    _logger.LogWarning(
                        "[Issue #483] ゴミ翻訳検出 チャンク{Index}: '{Original}' → '{Translated}' - 表示をスキップ",
                        i, nonEmptyChunks[i].CombinedText, translated);
                    nonEmptyChunks[i].TranslatedText = ""; // 空文字設定でオーバーレイ表示をスキップ
                    continue;
                }

                nonEmptyChunks[i].TranslatedText = translated;
                _logger.LogInformation("🔧 [TRANSLATION_RESULT] チャンク{Index}: '{Original}' → '{Translated}'",
                    i, nonEmptyChunks[i].CombinedText, translated);
            }

            // ============================================================
            // [Issue #354] Phase 2/3: ROI学習の重み付けと負の強化
            // ============================================================
            if (_roiManager?.IsEnabled == true && eventData.ImageWidth > 0 && eventData.ImageHeight > 0)
            {
                try
                {
                    // Cloud AI翻訳が使用されたかどうかを判定（weight=2を適用）
                    var isCloudTranslation = engineUsed.Contains("Gemini", StringComparison.OrdinalIgnoreCase) ||
                                             engineUsed.Contains("Cloud", StringComparison.OrdinalIgnoreCase) ||
                                             engineUsed.Contains("OpenAI", StringComparison.OrdinalIgnoreCase);
                    var learningWeight = isCloudTranslation ? 2 : 1;

                    _logger?.LogInformation(
                        "[Issue #354] ROI学習: Engine={Engine}, Weight={Weight}, ChunkCount={Count}",
                        engineUsed, learningWeight, nonEmptyChunks.Count);

                    // 翻訳成功したチャンクの重み付き学習
                    var successfulDetections = new List<(NormalizedRect bounds, float confidence, int weight)>();
                    var missRegions = new List<NormalizedRect>();

                    for (int i = 0; i < Math.Min(nonEmptyChunks.Count, translationResults.Count); i++)
                    {
                        var chunk = nonEmptyChunks[i];
                        var translatedText = translationResults[i];

                        // TODO: [Issue #483] ゴミ翻訳（IsGarbageTranslation=true）も現在は
                        //       「翻訳成功」として学習される。chunk.TranslatedTextで判定すべきか要検討。

                        // 正規化座標を計算
                        var normalizedBounds = new NormalizedRect
                        {
                            X = (float)chunk.CombinedBounds.X / eventData.ImageWidth,
                            Y = (float)chunk.CombinedBounds.Y / eventData.ImageHeight,
                            Width = (float)chunk.CombinedBounds.Width / eventData.ImageWidth,
                            Height = (float)chunk.CombinedBounds.Height / eventData.ImageHeight
                        };

                        // Phase 2: 翻訳成功した領域を重み付き学習
                        if (!string.IsNullOrEmpty(translatedText))
                        {
                            var confidence = chunk.AverageConfidence;
                            successfulDetections.Add((normalizedBounds, confidence, learningWeight));

                            // [Issue #379] P3-1: 低信頼度OCR結果は翻訳成功でもMissとして記録
                            if (confidence < _roiSettings.LowConfidenceMissRecordingThreshold)
                            {
                                missRegions.Add(normalizedBounds);
                                _logger?.LogDebug(
                                    "[Issue #379] 低信頼度OCR Miss記録（翻訳成功だが信頼度低）: Chunk={Index}, Confidence={Confidence:F2}",
                                    i, confidence);
                            }
                        }
                        // Phase 3: 翻訳結果が空の場合はmissとして報告
                        // [Issue #379] P1-1: OCR信頼度が高い場合は翻訳失敗であり、OCR missではない
                        else
                        {
                            var confidence = chunk.AverageConfidence;
                            if (confidence < _roiSettings.OcrConfidenceThresholdForMissSkip)
                            {
                                missRegions.Add(normalizedBounds);
                                _logger?.LogDebug(
                                    "[Issue #354] Miss記録: Chunk={Index}, Bounds=({X:F3},{Y:F3}), Confidence={Confidence:F2}",
                                    i, normalizedBounds.X, normalizedBounds.Y, confidence);
                            }
                            else
                            {
                                _logger?.LogDebug(
                                    "[Issue #379] Miss記録スキップ（翻訳失敗, OCR信頼度高）: Chunk={Index}, Confidence={Confidence:F2}",
                                    i, confidence);
                            }
                        }
                    }

                    // Phase 2: 重み付き学習を実行
                    if (successfulDetections.Count > 0)
                    {
                        _roiManager.ReportTextDetectionsWithWeight(successfulDetections, changedRegions: null);
                        _logger?.LogInformation(
                            "[Issue #354] ROI学習完了: SuccessCount={Success}, Weight={Weight}",
                            successfulDetections.Count, learningWeight);
#if DEBUG
                        Console.WriteLine($"📚 [Issue #354] ROI学習: {successfulDetections.Count}件成功, weight={learningWeight}");
#endif

                        // [Issue #379] A案: 翻訳成功した領域と重なる除外ゾーンを自動解除
                        var totalRemoved = 0;
                        foreach (var (bounds, _, _) in successfulDetections)
                        {
                            totalRemoved += _roiManager.RemoveOverlappingExclusionZones(bounds);
                        }
                        if (totalRemoved > 0)
                        {
                            _logger?.LogInformation(
                                "[Issue #379] 翻訳成功による除外ゾーン自動解除: RemovedCount={Count}",
                                totalRemoved);
                        }
                    }

                    // Phase 3: Missを報告
                    foreach (var missRegion in missRegions)
                    {
                        _roiManager.ReportMiss(missRegion);
                    }

                    if (missRegions.Count > 0)
                    {
                        _logger?.LogInformation(
                            "[Issue #354] Miss報告完了: MissCount={Miss}",
                            missRegions.Count);
#if DEBUG
                        Console.WriteLine($"⚠️ [Issue #354] Miss報告: {missRegions.Count}件");
#endif
                    }

                    // [Issue #354] ROI学習結果をプロファイルに保存
                    if (successfulDetections.Count > 0 || missRegions.Count > 0)
                    {
                        await _roiManager.SaveCurrentProfileAsync(cancellationToken).ConfigureAwait(false);
                        _logger?.LogDebug("[Issue #354] ROIプロファイル保存完了");
                    }
                }
                catch (Exception roiEx)
                {
                    _logger?.LogWarning(roiEx, "[Issue #354] ROI学習中にエラー（処理は継続）");
                }
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

            // 🧹 [OVERLAY_CLEANUP] Gate PASSしたチャンク領域のオーバーレイのみ削除
            // HideAllAsync()は使用しない — BLOCKされたチャンクのオーバーレイを巻き添えで消さないため
            try
            {
                foreach (var chunk in nonEmptyChunks)
                {
                    var bounds = chunk.CombinedBounds;
                    if (bounds.Width > 0 && bounds.Height > 0)
                    {
                        var area = new System.Drawing.Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height);
                        await _overlayManager.HideOverlaysInAreaAsync(area, excludeChunkId: -1, cancellationToken).ConfigureAwait(false);
                    }
                }
                _logger?.LogDebug("🧹 [OVERLAY_CLEANUP] Gate PASSチャンク領域のオーバーレイをクリア: {Count}領域", nonEmptyChunks.Count);
            }
            catch (Exception cleanupEx)
            {
                _logger?.LogWarning(cleanupEx, "⚠️ [OVERLAY_CLEANUP] オーバーレイクリーンアップ中にエラー - 処理継続");
            }

            // 🔧 [OVERLAY_UNIFICATION] 統一IOverlayManager.ShowAsync()で直接オーバーレイ表示
            // Gemini推奨: TranslationWithBoundsCompletedEventを経由せず、直接オーバーレイ表示
            // 理由: イベントハンドラー未実装により表示されない問題を解決
            // アーキテクチャ: Application層 → Core層(IOverlayManager)への依存は正しい（DIP準拠）
            _logger?.LogDebug($"🔥 [OVERLAY_FIX] 直接オーバーレイ表示開始 - チャンク数: {nonEmptyChunks.Count}");
            Console.WriteLine($"🔥 [OVERLAY_FIX] 直接オーバーレイ表示開始 - チャンク数: {nonEmptyChunks.Count}");

            // [Issue #557] オーバーレイ表示直前にローディング終了イベントを発火
            // 翻訳結果がユーザーに見える直前まで「準備中」を表示し続ける
            try
            {
                await _eventAggregator.PublishAsync(new Baketa.Core.Events.EventTypes.FirstTranslationResultReceivedEvent(), cancellationToken).ConfigureAwait(false);
                _logger?.LogDebug("[Issue #557] オーバーレイ表示直前にローディング終了イベント発火");
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "[Issue #557] ローディング終了イベント発火失敗（継続）");
            }

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

                // [Issue #483] ゴミ翻訳でTranslatedTextが空に設定されたチャンクをスキップ
                if (string.IsNullOrEmpty(chunk.TranslatedText))
                    continue;

                // [FIX6_NORMALIZE] ROI相対座標 → 画像絶対座標の正規化
                // Gemini推奨: キャッシュ保存前（オーバーレイ表示前）に座標を正規化
                // CaptureRegion == null: フルスクリーンキャプチャ → 変換不要
                // CaptureRegion != null: ROIキャプチャ → CombinedBoundsにOffsetを加算
                chunk = NormalizeChunkCoordinates(chunk);

                // [Issue #370] ログバッチ化: 詳細ログはDebugレベルに変更
                _logger.LogDebug("Coordinate normalized - ChunkId: {ChunkId}, CaptureRegion: {CaptureRegion}, Bounds: ({X},{Y},{W}x{H})",
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
                    FontSize = translationSettings.OverlayFontSize,
                    FontFamily = GetOverlayFontFamily(translationSettings.DefaultTargetLanguage)
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

                // [Race condition fix] ShowAsync直前のキャンセルチェック
                // Stop後にin-flightの翻訳処理がオーバーレイを作成してしまう問題を防止
                // L841のループ先頭チェックからここまで~85行のコード実行中にStopされる可能性がある
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger?.LogInformation("🛑 ShowAsync直前にキャンセル検出 - オーバーレイ表示をスキップ ({Completed}/{Total})",
                        i, nonEmptyChunks.Count);
                    break;
                }

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

            // [Issue #370] ログバッチ化: 座標正規化の要約ログを1行で出力
            var processedCount = Math.Min(nonEmptyChunks.Count, translationResults.Count);
            if (processedCount > 0)
            {
                _logger.LogInformation("Coordinate normalization complete: {Count} chunks processed", processedCount);
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
            // [Issue #392] ResetSemaphoreForStopが既に解放済みの場合はスキップ
            if (_semaphoreReleasedByStop)
            {
                _semaphoreReleasedByStop = false; // フラグをリセット
                _logger?.LogDebug("🔓 [PHASE1] セマフォはResetSemaphoreForStopで解放済み - スキップ (SessionId: {SessionId})", eventData.SessionId);
            }
            else
            {
                try
                {
                    _translationExecutionSemaphore.Release();
                    _logger?.LogDebug("🔓 [PHASE1] セマフォ解放完了 - SessionId: {SessionId}", eventData.SessionId);
                }
                catch (SemaphoreFullException)
                {
                    // ResetSemaphoreForStopとの競合によるレースコンディション対策
                    _logger?.LogWarning("⚠️ [PHASE1] セマフォ二重解放検出 - 無視 (SessionId: {SessionId})", eventData.SessionId);
                }
            }
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

        // [Issue #555] 近接グループ化で結合されたテキスト内の改行を除去
        // OCRが複数行にわたるテキストを検出した場合、改行が残るがこれは
        // 表示上の折り返しであり文の区切りではないため、翻訳API（DeepL/Google）が
        // 別々の文として翻訳してしまう問題を防止する
        var batchTexts = chunks.Select(c => c.CombinedText?.Replace("\n", "") ?? "").ToList();

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

        // [Issue #399] キリル文字検出（ゲームテキストに出現しない文字体系）
        if (text.Any(c => c is (>= '\u0400' and <= '\u04FF')))
            return true;

        // [Issue #399] 純粋な数値テキスト（符号・小数点含む）: -4864, 40.00, 70
        var stripped = text.Trim();
        if (stripped.Length > 0 && stripped.All(c => char.IsDigit(c) || c is '-' or '+' or '.'))
            return true;

        // [Issue #399] 極短テキスト（1-2文字）で英数字のみ（CJK以外）: e, 70, п
        if (stripped.Length <= 2 && !stripped.Any(c => c is (>= '\u4E00' and <= '\u9FFF')
            or (>= '\u3040' and <= '\u309F') or (>= '\u30A0' and <= '\u30FF')))
            return true;

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
    /// <summary>
    /// [Issue #397] P1-4: ゴミテキスト判定
    /// アスペクト比・反復パターンにより、翻訳不要なノイズテキストを除去
    /// </summary>
    internal static bool IsGarbageText(TextChunk chunk)
    {
        // 1. アスペクト比フィルタ: H/W > 3.0（極端に縦長な矩形 = 装飾/ゴミ）
        if (chunk.CombinedBounds.Width > 0 && chunk.CombinedBounds.Height > 0)
        {
            var hwRatio = (float)chunk.CombinedBounds.Height / chunk.CombinedBounds.Width;
            if (hwRatio > 3.0f)
                return true;
        }

        var text = chunk.CombinedText?.Trim();
        if (string.IsNullOrEmpty(text)) return false;

        // 2. 空白除去後の反復単一文字（例: "！！！", "！ ！ ！"）
        var stripped = text.Replace(" ", "").Replace("\u3000", "");
        if (stripped.Length >= 2 && stripped.Distinct().Count() == 1 && !char.IsLetterOrDigit(stripped[0]))
            return true;

        // 3. 単一の非英数字文字（例: "！", "？", "・"）
        if (stripped.Length == 1 && !char.IsLetterOrDigit(stripped[0]))
            return true;

        // 4. [Issue #397] Gate C: 短い非CJKテキスト + 非英数字文字（OCRノイズ）
        //    例: "N (A) Ä" → stripped "N(A)Ä" → 非英数字 '(' ')' を含む → garbage
        if (stripped.Length >= 2 && stripped.Length <= 5
            && !HasCjkCharacter(stripped)
            && stripped.Any(c => !char.IsLetterOrDigit(c)))
            return true;

        // 5. [Issue #466] 数字・記号のみ（例: "710-40", "1", "12:34"）
        if (IsDigitOrPunctuationOnly(stripped))
            return true;

        // 6. [Issue #466] キリル文字等の予期しないスクリプト混入（例: "Q.Load ш Q.Save"）
        if (ContainsUnexpectedScript(stripped))
            return true;

        // 7. [Issue #466] CJK文字を含まない3文字未満のテキスト（例: "Ch", "A"）
        //    ただしCJK文字を含む場合は保護（日本語2文字名「桃子」等）
        if (stripped.Length < 3 && !HasCjkCharacter(stripped))
            return true;

        // 8. [Issue #482] HTML/LaTeXマークアップ検出
        //    OCRがソースコードやUI要素を誤検出した場合のフィルタ
        //    例: "<math>\sim</math>", "\frac \alpha \beta"
        //    NOTE: 空白を含むtextに対して実行（strippedだと単語境界が消失し偽マッチが発生）
        var markupLength = 0;
        foreach (Match m in HtmlTagRegex().Matches(text))
            markupLength += m.Length;
        foreach (Match m in LatexCommandRegex().Matches(text))
            markupLength += m.Length;
        if (text.Length > 0 && (double)markupLength / text.Length >= 0.5)
            return true;

        return false;
    }

    /// <summary>
    /// [Issue #483] 翻訳結果の品質検証
    /// NLLB等のSeq2Seqモデルが生成するゴミ出力パターンを検出
    /// </summary>
    internal static bool IsGarbageTranslation(string? translatedText, string? sourceText)
    {
        if (string.IsNullOrWhiteSpace(translatedText))
            return false; // null/空はゴミではなく「翻訳なし」として別途処理

        var trimmed = translatedText.Trim();

        // 1. 文字・数字が全く含まれない（記号・括弧のみ）
        //    例: " ()  ()  ()  () ", "■ ■ ■", "---...---"
        if (!trimmed.Any(c => char.IsLetterOrDigit(c)))
            return true;

        // 2. 同一トークンの過度な頻度（最頻出単語が全体の75%以上）
        //    例: "the the the the the the", "() () () ()"
        //    保護: "Oh oh oh what a beautiful morning" → "oh"が3/7=43%なのでfalse
        var words = trimmed.Split([' ', '\u3000'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 4)
        {
            // 最頻出単語の出現回数
            var maxWordFreq = words.GroupBy(w => w, StringComparer.OrdinalIgnoreCase)
                .Max(g => g.Count());
            // 最頻出単語が全単語の75%以上を占める場合
            if ((double)maxWordFreq / words.Length >= 0.75)
                return true;
        }

        // 3. 出力が入力より極端に長い（膨張率チェック）
        //    CJK→アルファベットの自然膨張を考慮して閾値を設定
        if (!string.IsNullOrEmpty(sourceText) && sourceText.Length >= 2)
        {
            var ratio = (double)trimmed.Length / sourceText.Length;
            // CJK文字を含むソースの場合、膨張率の閾値を緩和（CJK→英語は3-4倍が正常）
            var hasCjk = sourceText.Any(c => c is (>= '\u4E00' and <= '\u9FFF')
                or (>= '\u3040' and <= '\u309F') or (>= '\u30A0' and <= '\u30FF')
                or (>= '\uAC00' and <= '\uD7AF'));
            var maxRatio = hasCjk ? 8.0 : 5.0;
            if (ratio > maxRatio)
                return true;
        }

        return false;
    }

    /// <summary>
    /// [Issue #466] 数字・記号のみで構成されたテキストを判定
    /// 例: "710-40", "1", "12:34", "100%"
    /// </summary>
    private static bool IsDigitOrPunctuationOnly(string text)
    {
        foreach (var c in text)
        {
            if (char.IsLetter(c))
                return false;
        }
        return true;
    }

    /// <summary>
    /// [Issue #466] キリル文字等の予期しないスクリプトが含まれているか判定
    /// OCR誤認識により英語テキストにキリル文字が混入するケースを検出
    /// </summary>
    private static bool ContainsUnexpectedScript(string text)
    {
        foreach (var c in text)
        {
            // キリル文字 (U+0400-U+04FF)
            if (c is >= '\u0400' and <= '\u04FF')
                return true;
        }
        return false;
    }

    /// <summary>
    /// [Issue #397] Gate C: CJK文字（漢字・ひらがな・カタカナ）を含むか判定
    /// </summary>
    private static bool HasCjkCharacter(string text)
    {
        foreach (var c in text)
        {
            if (c is (>= '\u4E00' and <= '\u9FFF')   // CJK統合漢字
                  or (>= '\u3040' and <= '\u309F')     // ひらがな
                  or (>= '\u30A0' and <= '\u30FF'))     // カタカナ
                return true;
        }
        return false;
    }

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
        IReadOnlyList<TranslatedTextItem> cloudTexts,
        int imageWidth,
        int imageHeight)
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

        // [Issue #380] 座標ベースマッチング用: 使用済みCloud AI結果を追跡
        var usedCloudTexts = new HashSet<TranslatedTextItem>();

        var matchedCount = 0;
        var normalizedMatchCount = 0;
        var partialMatchCount = 0;
        var coordinateMatchCount = 0;
        var notDetectedCount = 0;

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunkText = chunks[i].CombinedText ?? string.Empty;
            string translation;

            // 1. 完全一致
            if (exactMatchMap.TryGetValue(chunkText, out translation!))
            {
                results.Add(translation);
                matchedCount++;
                // 使用済みとしてマーク（完全一致の元を探す）
                var usedItem = cloudTexts.FirstOrDefault(t => t.Original == chunkText);
                if (usedItem != null) usedCloudTexts.Add(usedItem);
                continue;
            }

            // 2. 正規化一致
            var normalizedChunkText = NormalizeText(chunkText);
            if (!string.IsNullOrEmpty(normalizedChunkText) &&
                normalizedMap.TryGetValue(normalizedChunkText, out translation!))
            {
                results.Add(translation);
                normalizedMatchCount++;
                // 使用済みとしてマーク
                var usedItem = cloudTexts.FirstOrDefault(t => NormalizeText(t.Original) == normalizedChunkText);
                if (usedItem != null) usedCloudTexts.Add(usedItem);
                _logger?.LogDebug(
                    "🔍 [Issue #296] 正規化マッチ: Chunk[{Index}] '{ChunkText}' → '{Translation}'",
                    i, chunkText.Length > 30 ? chunkText[..30] + "..." : chunkText,
                    translation.Length > 30 ? translation[..30] + "..." : translation);
                continue;
            }

            // 3. 部分一致（正規化テキストで比較 - 空白・改行・句読点の差異を無視）
            var partialMatch = cloudTexts.FirstOrDefault(t =>
            {
                if (string.IsNullOrEmpty(t.Original)) return false;
                var normalizedCloudOriginal = NormalizeText(t.Original);
                // 正規化後のテキストで部分一致チェック
                return normalizedCloudOriginal.Contains(normalizedChunkText, StringComparison.OrdinalIgnoreCase) ||
                       normalizedChunkText.Contains(normalizedCloudOriginal, StringComparison.OrdinalIgnoreCase);
            });

            if (partialMatch != null)
            {
                results.Add(partialMatch.Translation ?? string.Empty);
                partialMatchCount++;
                usedCloudTexts.Add(partialMatch);
                _logger?.LogDebug(
                    "🔍 [Issue #296] 部分マッチ: Chunk[{Index}] '{ChunkText}' ⊂⊃ '{CloudOriginal}' → '{Translation}'",
                    i,
                    chunkText.Length > 20 ? chunkText[..20] + "..." : chunkText,
                    partialMatch.Original?.Length > 20 ? partialMatch.Original[..20] + "..." : partialMatch.Original,
                    partialMatch.Translation?.Length > 20 ? partialMatch.Translation[..20] + "..." : partialMatch.Translation);
                continue;
            }

            // 3.5. [Issue #380] 座標ベースフォールバックマッチング（テキスト一致失敗時）
            // Cloud AI BoundingBoxとチャンクCombinedBoundsのIoUで最も近いものを探す
            if (imageWidth > 0 && imageHeight > 0)
            {
                var coordinateMatch = FindBestCoordinateMatch(
                    chunks[i],
                    cloudTexts,
                    usedCloudTexts,
                    imageWidth,
                    imageHeight);

                if (coordinateMatch != null)
                {
                    results.Add(coordinateMatch.Translation ?? string.Empty);
                    coordinateMatchCount++;
                    usedCloudTexts.Add(coordinateMatch);
                    _logger?.LogDebug(
                        "🔍 [Issue #380] 座標フォールバックマッチ: Chunk[{Index}] '{ChunkText}' → '{Translation}'",
                        i,
                        chunkText.Length > 20 ? chunkText[..20] + "..." : chunkText,
                        coordinateMatch.Translation?.Length > 20 ? coordinateMatch.Translation[..20] + "..." : coordinateMatch.Translation);
                    continue;
                }
            }

            // 4. マッチなし: Cloud AIが検出しなかった → 翻訳不要と判断
            // Cloud AI (Gemini) は視覚的に理解し「意味のあるテキスト」のみ検出・翻訳する
            // ローカルOCRが検出してもCloud AIが検出しなかったものは装飾・ノイズの可能性が高い
            results.Add(string.Empty);
            notDetectedCount++;
            _logger?.LogDebug(
                "🔍 [Issue #296] Cloud AI未検出: Chunk[{Index}] '{ChunkText}' - オーバーレイ非表示",
                i, chunkText.Length > 50 ? chunkText[..50] + "..." : chunkText);
        }

        _logger?.LogInformation(
            "📊 [Issue #380] マッチング統計: 完全一致={Exact}, 正規化={Normalized}, 部分={Partial}, 座標={Coordinate}, 未検出={NotDetected}, 合計={Total}",
            matchedCount, normalizedMatchCount, partialMatchCount, coordinateMatchCount, notDetectedCount, chunks.Count);

#if DEBUG
        Console.WriteLine($"📊 [Issue #380] マッチング統計: 完全={matchedCount}, 正規化={normalizedMatchCount}, 部分={partialMatchCount}, 座標={coordinateMatchCount}, 未検出={notDetectedCount}");
#endif

        return results;
    }

    /// <summary>
    /// [Issue #380] 座標ベースで最も近いCloud AIテキストを探す
    /// </summary>
    /// <remarks>
    /// チャンクのCombinedBoundsとCloud AIのBoundingBoxのIoUを計算し、
    /// IoU >= 0.3の中で最も高いものを返す。
    /// テキストマッチングが失敗した場合のフォールバックとして使用。
    /// </remarks>
    private TranslatedTextItem? FindBestCoordinateMatch(
        TextChunk chunk,
        IReadOnlyList<TranslatedTextItem> cloudTexts,
        HashSet<TranslatedTextItem> usedCloudTexts,
        int imageWidth,
        int imageHeight)
    {
        TranslatedTextItem? bestMatch = null;
        float bestIoU = 0f;

        foreach (var cloudText in cloudTexts
            .Where(t => !usedCloudTexts.Contains(t) && t.HasBoundingBox))
        {
            var cloudBox = cloudText.BoundingBox!.Value;

            // Cloud AI BoundingBoxは0-1000正規化スケール → ピクセル座標に変換
            var scaledCloudRect = new System.Drawing.Rectangle(
                cloudBox.X * imageWidth / 1000,
                cloudBox.Y * imageHeight / 1000,
                cloudBox.Width * imageWidth / 1000,
                cloudBox.Height * imageHeight / 1000);

            var iou = CalculateRectangleIoU(chunk.CombinedBounds, scaledCloudRect);

            if (iou >= CoordinateMatchIoUThreshold && iou > bestIoU)
            {
                bestIoU = iou;
                bestMatch = cloudText;
                _logger?.LogDebug(
                    "🔍 [Issue #380] 座標マッチ候補: IoU={IoU:F2}, Cloud='{Text}' CloudBox=({CX},{CY},{CW},{CH})→Scaled=({SX},{SY},{SW},{SH}), Chunk=({ChX},{ChY},{ChW},{ChH})",
                    iou,
                    cloudText.Original?.Length > 30 ? cloudText.Original[..30] + "..." : cloudText.Original,
                    cloudBox.X, cloudBox.Y, cloudBox.Width, cloudBox.Height,
                    scaledCloudRect.X, scaledCloudRect.Y, scaledCloudRect.Width, scaledCloudRect.Height,
                    chunk.CombinedBounds.X, chunk.CombinedBounds.Y,
                    chunk.CombinedBounds.Width, chunk.CombinedBounds.Height);
            }
        }

        return bestMatch;
    }

    /// <summary>
    /// [Issue #380] 2つのRectangleのIoU（Intersection over Union）を計算
    /// </summary>
    private static float CalculateRectangleIoU(System.Drawing.Rectangle a, System.Drawing.Rectangle b)
    {
        var intersectX = Math.Max(a.X, b.X);
        var intersectY = Math.Max(a.Y, b.Y);
        var intersectRight = Math.Min(a.Right, b.Right);
        var intersectBottom = Math.Min(a.Bottom, b.Bottom);

        if (intersectRight <= intersectX || intersectBottom <= intersectY)
            return 0f;

        var intersectionArea = (float)(intersectRight - intersectX) * (intersectBottom - intersectY);
        var unionArea = (float)a.Width * a.Height + (float)b.Width * b.Height - intersectionArea;

        return unionArea > 0 ? intersectionArea / unionArea : 0f;
    }

    /// <summary>
    /// [Issue #478] SuryaチャンクのBBoxに一致するCloud翻訳結果を検索
    /// Cloud AI BoundingBox（0-1000正規化スケール）をピクセル座標に変換し、IoUで一致判定
    /// </summary>
    private TranslatedTextItem? FindMatchingCloudResult(
        System.Drawing.Rectangle suryaBounds,
        IReadOnlyList<TranslatedTextItem> cloudTexts,
        int imageWidth, int imageHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0)
            return null;

        foreach (var cloudText in cloudTexts)
        {
            if (!cloudText.HasBoundingBox) continue;
            var cloudBox = cloudText.BoundingBox!.Value;

            // Cloud AI BoundingBoxは0-1000正規化スケール → ピクセル座標に変換
            var cloudPixelRect = new System.Drawing.Rectangle(
                cloudBox.X * imageWidth / 1000,
                cloudBox.Y * imageHeight / 1000,
                cloudBox.Width * imageWidth / 1000,
                cloudBox.Height * imageHeight / 1000);

            var iou = CalculateRectangleIoU(cloudPixelRect, suryaBounds);
            if (iou >= CoordinateMatchIoUThreshold)
                return cloudText;
        }
        return null;
    }

    /// <summary>
    /// [Issue #387] Cloud結果主導のオーバーレイアイテム作成
    /// Cloud AIの翻訳結果を起点として、Suryaチャンクで検証し、
    /// Cloud BoundingBoxベースの座標でオーバーレイを配置する
    /// </summary>
    /// <remarks>
    /// 従来のSurya主導アプローチ（MatchCloudTranslationsToChunks）では、
    /// Suryaが「キャラクター名+セリフ」を1チャンクに結合した場合、
    /// Cloud結果の部分マッチで最初の結果のみが採用される問題があった。
    ///
    /// Cloud主導アプローチでは:
    /// 1. Cloud結果を直接イテレートし、各結果のBoundingBoxをオーバーレイ位置として使用
    /// 2. Suryaチャンクとの包含率で検証（ハルシネーションフィルタ）
    /// 3. Cloud座標をSurya矩形にクリッピング（表示位置の安定化）
    /// </remarks>
    private (List<TextChunk> overlayChunks, List<string> translations) CreateCloudDrivenOverlayItems(
        List<TextChunk> suryaChunks,
        IReadOnlyList<TranslatedTextItem> cloudTexts,
        int imageWidth,
        int imageHeight)
    {
        // [Issue #387] Cloud結果の包含率閾値
        const float containmentThreshold = 0.3f;

        var discardedCount = 0;
        var noBboxCount = 0;

        // ============================================================
        // Phase 1: 各Cloud結果をSuryaチャンクにマッチング
        // ============================================================
        // Key: Surya ChunkId, Value: マッチしたCloud結果のリスト
        var suryaGroupedItems = new Dictionary<int, List<(TranslatedTextItem cloudText, System.Drawing.Rectangle cloudPixelRect)>>();
        // Suryaチャンクの参照を保持
        var suryaChunkMap = suryaChunks.ToDictionary(c => c.ChunkId);

        for (int i = 0; i < cloudTexts.Count; i++)
        {
            var cloudText = cloudTexts[i];

            if (string.IsNullOrEmpty(cloudText.Translation))
                continue;

            if (!cloudText.HasBoundingBox)
            {
                noBboxCount++;
                _logger?.LogDebug(
                    "[Issue #387] Cloud結果スキップ（BoundingBoxなし）: '{Original}'",
                    cloudText.Original?.Length > 30 ? cloudText.Original[..30] + "..." : cloudText.Original);
                continue;
            }

            var cloudBox = cloudText.BoundingBox!.Value;

            // [Issue #398] BBox妥当性フィルタ: Width/Height=0 または座標飽和を破棄
            if (cloudBox.Width <= 0 || cloudBox.Height <= 0
                || (cloudBox.X >= 999 && cloudBox.Y >= 999))
            {
                discardedCount++;
                _logger?.LogDebug(
                    "[Issue #398] Cloud結果破棄（無効BBox）: '{Original}' BBox=({X},{Y},{W}x{H})",
                    cloudText.Original?.Length > 30 ? cloudText.Original[..30] + "..." : cloudText.Original,
                    cloudBox.X, cloudBox.Y, cloudBox.Width, cloudBox.Height);
                continue;
            }

            // Cloud 0-1000正規化スケール → 画像ピクセル座標に変換
            var cloudPixelRect = new System.Drawing.Rectangle(
                cloudBox.X * imageWidth / 1000,
                cloudBox.Y * imageHeight / 1000,
                cloudBox.Width * imageWidth / 1000,
                cloudBox.Height * imageHeight / 1000);

            // Suryaチャンクとの包含率で検証
            var (bestSuryaChunk, bestContainment) = FindBestContainingSuryaChunk(
                cloudPixelRect, suryaChunks, containmentThreshold);

            if (bestSuryaChunk == null)
            {
                // [Issue #391] OverlapRatio フォールバック: Surya面積ベースの重複率 + Y中心距離
                // Cloud AIのBBoxがSuryaより縦に大きい（マージン含む）場合、包含率では失敗するが
                // OverlapRatio（交差面積/Surya面積）ならSuryaが完全にカバーされていることを検出可能
                bestSuryaChunk = FindBestOverlapRatioSuryaChunk(cloudPixelRect, suryaChunks);

                if (bestSuryaChunk != null)
                {
                    _logger?.LogInformation(
                        "[Issue #391] OverlapRatioマッチングでSurya裏付け成功: '{Original}' → SuryaChunk={ChunkId}",
                        cloudText.Original?.Length > 30 ? cloudText.Original[..30] + "..." : cloudText.Original,
                        bestSuryaChunk.ChunkId);
                }
            }

            // [Issue #414] 対策A: 近接マージンマッチング（BBox間にギャップがある場合の救済）
            if (bestSuryaChunk == null)
            {
                bestSuryaChunk = FindBestProximityMarginSuryaChunk(cloudPixelRect, suryaChunks);
                if (bestSuryaChunk != null)
                {
                    _logger?.LogInformation(
                        "[Issue #414] 近接マージンマッチングでSurya裏付け成功: '{Original}' → SuryaChunk={ChunkId}",
                        cloudText.Original?.Length > 30 ? cloudText.Original[..30] + "..." : cloudText.Original,
                        bestSuryaChunk.ChunkId);
                }
            }

            if (bestSuryaChunk == null)
            {
                // [Issue #387] 座標マッチング失敗 → テキスト内容でフォールバックマッチング
                // Geminiのbounding boxは不正確な場合があるため、テキスト包含関係で検証
                var normalizedCloudOriginal = NormalizeText(cloudText.Original ?? string.Empty);
                if (!string.IsNullOrEmpty(normalizedCloudOriginal))
                {
                    bestSuryaChunk = suryaChunks.FirstOrDefault(chunk =>
                    {
                        var normalizedSuryaText = NormalizeText(chunk.CombinedText ?? string.Empty);
                        return normalizedSuryaText.Contains(normalizedCloudOriginal, StringComparison.OrdinalIgnoreCase) ||
                               normalizedCloudOriginal.Contains(normalizedSuryaText, StringComparison.OrdinalIgnoreCase);
                    });
                }

                // [Issue #414] 対策B: ファジーテキストマッチング（記号差異を吸収）
                if (bestSuryaChunk == null && _fuzzyTextMatcher != null)
                {
                    var coreCloud = ExtractCoreCharacters(cloudText.Original ?? string.Empty);
                    if (coreCloud.Length >= 2)
                    {
                        const float fuzzyThreshold = 0.8f;
                        TextChunk? bestFuzzyChunk = null;
                        var bestFuzzySimilarity = 0f;

                        foreach (var chunk in suryaChunks)
                        {
                            var coreSurya = ExtractCoreCharacters(chunk.CombinedText ?? string.Empty);
                            if (coreSurya.Length < 2)
                                continue;

                            var similarity = _fuzzyTextMatcher.CalculateSimilarity(coreCloud, coreSurya);
                            if (similarity >= fuzzyThreshold && similarity > bestFuzzySimilarity)
                            {
                                bestFuzzySimilarity = similarity;
                                bestFuzzyChunk = chunk;
                            }
                        }

                        if (bestFuzzyChunk != null)
                        {
                            bestSuryaChunk = bestFuzzyChunk;
                            _logger?.LogInformation(
                                "[Issue #414] ファジーテキストマッチングでSurya裏付け成功: '{Original}' → SuryaChunk={ChunkId} (類似度={Similarity:F3})",
                                cloudText.Original?.Length > 30 ? cloudText.Original[..30] + "..." : cloudText.Original,
                                bestFuzzyChunk.ChunkId, bestFuzzySimilarity);
                        }
                    }
                }

                if (bestSuryaChunk == null)
                {
                    discardedCount++;
                    _logger?.LogDebug(
                        "[Issue #387] Cloud結果破棄（座標・テキスト両方で裏付けなし）: '{Original}' CloudBox=({X},{Y},{W}x{H})",
                        cloudText.Original?.Length > 30 ? cloudText.Original[..30] + "..." : cloudText.Original,
                        cloudPixelRect.X, cloudPixelRect.Y, cloudPixelRect.Width, cloudPixelRect.Height);
                    continue;
                }

                _logger?.LogInformation(
                    "[Issue #387] テキストマッチングでSurya裏付け成功（座標不一致）: '{Original}' → SuryaChunk={ChunkId}",
                    cloudText.Original?.Length > 30 ? cloudText.Original[..30] + "..." : cloudText.Original,
                    bestSuryaChunk.ChunkId);
            }

            // Suryaチャンク別にグループ化
            if (!suryaGroupedItems.TryGetValue(bestSuryaChunk.ChunkId, out var group))
            {
                group = [];
                suryaGroupedItems[bestSuryaChunk.ChunkId] = group;
            }
            group.Add((cloudText, cloudPixelRect));
        }

        // ============================================================
        // Phase 2: グループごとにオーバーレイアイテムを作成
        // 同じSuryaチャンクに属する複数Cloud結果は翻訳を結合
        // ============================================================
        var overlayChunks = new List<TextChunk>();
        var translations = new List<string>();
        var chunkIndex = 0;

        foreach (var (suryaChunkId, items) in suryaGroupedItems)
        {
            var suryaChunk = suryaChunkMap[suryaChunkId];

            if (items.Count == 1)
            {
                // 単独 → Cloud BoundingBoxをSurya矩形にクリッピングして使用
                var (cloudText, cloudPixelRect) = items[0];
                var clippedRect = ClipToSuryaBounds(cloudPixelRect, suryaChunk.CombinedBounds);

                // [Issue #414] 対策C: クリッピング失敗またはクリッピング結果が小さすぎる → Surya座標を採用
                // マッチング済み＝同一テキスト確認済み。位置精度はピクセル解析のSuryaが上。
                // ClipToSuryaBoundsが元のCloud座標を返した場合＝矩形の交差なし（近接マージン等で
                // マッチしたがBBox自体は重なっていない）→ Surya座標の方が正確
                if (clippedRect == cloudPixelRect && clippedRect != suryaChunk.CombinedBounds)
                {
                    _logger?.LogInformation(
                        "[Issue #414] Cloud/Surya矩形に交差なし → Surya境界を採用: Cloud=({CX},{CY},{CW}x{CH}) Surya=({SX},{SY},{SW}x{SH})",
                        cloudPixelRect.X, cloudPixelRect.Y, cloudPixelRect.Width, cloudPixelRect.Height,
                        suryaChunk.CombinedBounds.X, suryaChunk.CombinedBounds.Y,
                        suryaChunk.CombinedBounds.Width, suryaChunk.CombinedBounds.Height);
                    clippedRect = suryaChunk.CombinedBounds;
                }
                else if (clippedRect.Height < suryaChunk.CombinedBounds.Height * 0.7f ||
                    clippedRect.Width < suryaChunk.CombinedBounds.Width * 0.7f)
                {
                    // [Issue #414] しきい値を0.3→0.7に引き上げ
                    // Cloud AI の 0-1000 座標系は粗いため、BBox高さがSuryaより大幅に小さくなるケースがある
                    // 例: Cloud高さ73px vs Surya高さ105px (69.5%) → 縦幅の狭いオーバーレイになる
                    // Suryaのピクセル精度の方が信頼できるため、30%以上の縮小でSurya座標にフォールバック
                    _logger?.LogInformation(
                        "[Issue #414] クリッピング結果がSurya境界比70%未満のためSurya境界を使用: Clipped=({CW}x{CH}) Surya=({SW}x{SH})",
                        clippedRect.Width, clippedRect.Height,
                        suryaChunk.CombinedBounds.Width, suryaChunk.CombinedBounds.Height);
                    clippedRect = suryaChunk.CombinedBounds;
                }

                overlayChunks.Add(new TextChunk
                {
                    ChunkId = CloudDrivenChunkIdOffset + chunkIndex,
                    TextResults = suryaChunk.TextResults,
                    CombinedBounds = clippedRect,
                    CombinedText = cloudText.Original ?? string.Empty,
                    TranslatedText = cloudText.Translation,
                    SourceWindowHandle = suryaChunk.SourceWindowHandle,
                    DetectedLanguage = suryaChunk.DetectedLanguage,
                    CaptureRegion = suryaChunk.CaptureRegion
                });
                translations.Add(cloudText.Translation);

                _logger?.LogDebug(
                    "[Issue #387] Cloud結果採用（単独）: '{Translation}' Bounds=({X},{Y},{W}x{H})",
                    cloudText.Translation?.Length > 40 ? cloudText.Translation[..40] + "..." : cloudText.Translation,
                    clippedRect.X, clippedRect.Y, clippedRect.Width, clippedRect.Height);
            }
            else
            {
                // 複数のCloud結果が同じSuryaチャンクに属する → 結合
                // Y座標順にソートして読み順を維持
                var sortedItems = items.OrderBy(item => item.cloudPixelRect.Y).ToList();

                var mergedTranslation = string.Join(" ", sortedItems.Select(item => item.cloudText.Translation));
                var mergedOriginal = string.Join("", sortedItems.Select(item => item.cloudText.Original));

                // 結合時はSuryaチャンクのCombinedBoundsを使用（全Cloud結果を包含する領域）
                overlayChunks.Add(new TextChunk
                {
                    ChunkId = CloudDrivenChunkIdOffset + chunkIndex,
                    TextResults = suryaChunk.TextResults,
                    CombinedBounds = suryaChunk.CombinedBounds,
                    CombinedText = mergedOriginal,
                    TranslatedText = mergedTranslation,
                    SourceWindowHandle = suryaChunk.SourceWindowHandle,
                    DetectedLanguage = suryaChunk.DetectedLanguage,
                    CaptureRegion = suryaChunk.CaptureRegion
                });
                translations.Add(mergedTranslation);

                _logger?.LogInformation(
                    "[Issue #387] Cloud結果結合（{Count}個→1個）: '{Translation}' SuryaBounds=({X},{Y},{W}x{H})",
                    items.Count,
                    mergedTranslation.Length > 50 ? mergedTranslation[..50] + "..." : mergedTranslation,
                    suryaChunk.CombinedBounds.X, suryaChunk.CombinedBounds.Y,
                    suryaChunk.CombinedBounds.Width, suryaChunk.CombinedBounds.Height);
            }
            chunkIndex++;
        }

        _logger?.LogInformation(
            "[Issue #387] Cloud結果主導マッチング完了: Groups={Groups}, Discarded={Discarded}, NoBBox={NoBBox}, CloudTotal={Total}",
            suryaGroupedItems.Count, discardedCount, noBboxCount, cloudTexts.Count);

#if DEBUG
        Console.WriteLine($"📊 [Issue #387] Cloud主導: グループ={suryaGroupedItems.Count}, 破棄={discardedCount}, BBox無し={noBboxCount}");
#endif

        return (overlayChunks, translations);
    }

    /// <summary>
    /// [Issue #387] Cloud BoundingBoxを最も包含するSuryaチャンクを探す
    /// </summary>
    /// <remarks>
    /// IoUではなく「包含率（intersection / cloudBoxArea）」を使用する。
    /// 理由: Cloudが意味的に分離した小さなBoundingBoxは、Suryaの大きな結合チャンクに
    /// 包含されるため、IoUでは低い値になり誤って棄却されてしまう。
    /// 包含率なら、Cloud boxの大部分がSuryaチャンク内にあれば有効と判定できる。
    /// </remarks>
    private (TextChunk? bestChunk, float bestContainment) FindBestContainingSuryaChunk(
        System.Drawing.Rectangle cloudPixelRect,
        List<TextChunk> suryaChunks,
        float threshold)
    {
        TextChunk? bestChunk = null;
        var bestContainment = 0f;

        var cloudArea = (float)cloudPixelRect.Width * cloudPixelRect.Height;
        if (cloudArea <= 0)
            return (null, 0f);

        foreach (var chunk in suryaChunks)
        {
            var suryaBounds = chunk.CombinedBounds;

            // 交差領域を計算
            var intersectX = Math.Max(cloudPixelRect.X, suryaBounds.X);
            var intersectY = Math.Max(cloudPixelRect.Y, suryaBounds.Y);
            var intersectRight = Math.Min(cloudPixelRect.Right, suryaBounds.Right);
            var intersectBottom = Math.Min(cloudPixelRect.Bottom, suryaBounds.Bottom);

            if (intersectRight <= intersectX || intersectBottom <= intersectY)
                continue;

            var intersectionArea = (float)(intersectRight - intersectX) * (intersectBottom - intersectY);

            // 包含率: Cloud boxの何%がSuryaチャンク内にあるか
            var containment = intersectionArea / cloudArea;

            if (containment >= threshold && containment > bestContainment)
            {
                bestContainment = containment;
                bestChunk = chunk;
            }
        }

        return (bestChunk, bestContainment);
    }

    /// <summary>
    /// [Issue #391] OverlapRatio + Y中心距離によるフォールバックマッチング
    /// </summary>
    /// <remarks>
    /// Cloud AIのBBoxがSuryaより縦に大きい場合（上下マージン含む）、
    /// 包含率（intersection/cloudArea）では低い値になり失敗する。
    /// OverlapRatio（intersection/suryaArea）を使用すれば、Suryaの領域が
    /// Cloud BBox内に完全に含まれていることを検出できる。
    /// Y中心距離条件を併用して、水平方向に離れた無関係な領域への誤マッチを防止。
    /// </remarks>
    private TextChunk? FindBestOverlapRatioSuryaChunk(
        System.Drawing.Rectangle cloudPixelRect,
        List<TextChunk> suryaChunks)
    {
        // [Issue #391] OverlapRatio閾値: Surya面積の50%以上がCloud BBox内にあればマッチ
        const float overlapRatioThreshold = 0.5f;
        // [Issue #391] Y中心距離: Surya高さの3倍以内なら近接と判定
        const float yCenterDistanceMultiplier = 3.0f;

        TextChunk? bestChunk = null;
        var bestOverlapRatio = 0f;

        var cloudCenterY = cloudPixelRect.Y + cloudPixelRect.Height / 2.0f;

        foreach (var chunk in suryaChunks)
        {
            var suryaBounds = chunk.CombinedBounds;
            var suryaArea = (float)suryaBounds.Width * suryaBounds.Height;
            if (suryaArea <= 0)
                continue;

            // Y中心距離チェック
            var suryaCenterY = suryaBounds.Y + suryaBounds.Height / 2.0f;
            var yCenterDistance = Math.Abs(cloudCenterY - suryaCenterY);
            var maxYDistance = suryaBounds.Height * yCenterDistanceMultiplier;
            if (yCenterDistance > maxYDistance)
                continue;

            // 交差領域を計算
            var intersectX = Math.Max(cloudPixelRect.X, suryaBounds.X);
            var intersectY = Math.Max(cloudPixelRect.Y, suryaBounds.Y);
            var intersectRight = Math.Min(cloudPixelRect.Right, suryaBounds.Right);
            var intersectBottom = Math.Min(cloudPixelRect.Bottom, suryaBounds.Bottom);

            if (intersectRight <= intersectX || intersectBottom <= intersectY)
                continue;

            var intersectionArea = (float)(intersectRight - intersectX) * (intersectBottom - intersectY);

            // OverlapRatio: Surya面積の何%がCloud BBox内にあるか
            var overlapRatio = intersectionArea / suryaArea;

            if (overlapRatio >= overlapRatioThreshold && overlapRatio > bestOverlapRatio)
            {
                bestOverlapRatio = overlapRatio;
                bestChunk = chunk;

                _logger?.LogDebug(
                    "[Issue #391] OverlapRatio候補: SuryaChunk={ChunkId}, Ratio={Ratio:F3}, YDist={YDist:F0}px (max={MaxY:F0}px)",
                    chunk.ChunkId, overlapRatio, yCenterDistance, maxYDistance);
            }
        }

        return bestChunk;
    }

    /// <summary>
    /// [Issue #414] 対策A: Cloud BBoxとSurya BBox間の最小辺間距離による近接マージンマッチング
    /// </summary>
    /// <remarks>
    /// Cloud BBoxとSurya BBoxが数ピクセルのギャップで離れている場合、
    /// 包含率やOverlapRatioでは交差面積がゼロとなりマッチングが失敗する。
    /// BBox間の最小辺間距離がSurya高さの一定割合以内であれば近接と判定する。
    /// Cloud AIはBBoxを上方に浮かせる傾向があるため、上方向のマージンを大きく取る。
    /// </remarks>
    private TextChunk? FindBestProximityMarginSuryaChunk(
        System.Drawing.Rectangle cloudPixelRect,
        List<TextChunk> suryaChunks)
    {
        TextChunk? bestChunk = null;
        var bestDistance = float.MaxValue;

        var cloudCenterY = cloudPixelRect.Y + cloudPixelRect.Height / 2.0f;

        foreach (var chunk in suryaChunks)
        {
            var suryaBounds = chunk.CombinedBounds;
            if (suryaBounds.Width <= 0 || suryaBounds.Height <= 0)
                continue;

            // X方向のギャップ（重なっている場合は0）
            var gapX = Math.Max(0, Math.Max(cloudPixelRect.X - suryaBounds.Right, suryaBounds.X - cloudPixelRect.Right));
            // Y方向のギャップ（重なっている場合は0）
            var gapY = Math.Max(0, Math.Max(cloudPixelRect.Y - suryaBounds.Bottom, suryaBounds.Y - cloudPixelRect.Bottom));

            // 最小辺間距離（ユークリッド距離の近似: X,Y両方にギャップがあれば対角距離）
            var distance = (float)Math.Sqrt((double)gapX * gapX + (double)gapY * gapY);

            // Cloud中心がSurya中心より上方向: BBoxが上に浮いている傾向 → マージンを大きく
            var suryaCenterY = suryaBounds.Y + suryaBounds.Height / 2.0f;
            var margin = cloudCenterY < suryaCenterY
                ? suryaBounds.Height * 0.25f  // 上方向: Surya高さの25%
                : suryaBounds.Height * 0.15f; // 下方向: Surya高さの15%

            if (distance <= margin && distance < bestDistance)
            {
                bestDistance = distance;
                bestChunk = chunk;

                _logger?.LogDebug(
                    "[Issue #414] 近接マージン候補: SuryaChunk={ChunkId}, Distance={Distance:F1}px, Margin={Margin:F1}px",
                    chunk.ChunkId, distance, margin);
            }
        }

        return bestChunk;
    }

    /// <summary>
    /// [Issue #387] Cloud BoundingBoxをSurya矩形にクリッピング
    /// </summary>
    /// <remarks>
    /// Cloud AIの0-1000座標は「緩い」傾向があり、テキスト領域から
    /// はみ出す場合がある。Suryaのピクセル精度の矩形をコンテナとして
    /// クリッピングすることで、表示位置の安定性を向上させる。
    /// クリッピング結果がゼロサイズになる場合は元のCloud座標を返す。
    /// </remarks>
    internal static System.Drawing.Rectangle ClipToSuryaBounds(
        System.Drawing.Rectangle cloudRect,
        System.Drawing.Rectangle suryaBounds)
    {
        var clippedX = Math.Max(cloudRect.X, suryaBounds.X);
        var clippedY = Math.Max(cloudRect.Y, suryaBounds.Y);
        var clippedRight = Math.Min(cloudRect.Right, suryaBounds.Right);
        var clippedBottom = Math.Min(cloudRect.Bottom, suryaBounds.Bottom);

        var clippedWidth = clippedRight - clippedX;
        var clippedHeight = clippedBottom - clippedY;

        // クリッピング結果がゼロサイズになる場合は元のCloud座標を返す
        if (clippedWidth <= 0 || clippedHeight <= 0)
            return cloudRect;

        return new System.Drawing.Rectangle(clippedX, clippedY, clippedWidth, clippedHeight);
    }

    /// <summary>
    /// [Issue #296] テキスト正規化（マッチング用）
    /// </summary>
    /// <remarks>
    /// 空白、改行、制御文字、および一般的な句読点を除去して
    /// テキストの実質的な内容のみを比較できるようにする。
    /// これにより、OCRの改行位置の違いやCloud AIの句読点の違いを吸収できる。
    /// </remarks>
    private static string NormalizeText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // 空白・改行・制御文字・句読点を除去
        // 日本語句読点: 。、！？・
        // 英語句読点: .!?,;:
        // 括弧類は意味があるので残す（「」『』など）
        var punctuationToRemove = new HashSet<char> { '。', '、', '！', '？', '・', '.', '!', '?', ',', ';', ':' };

        return new string(text
            .Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c) && !punctuationToRemove.Contains(c))
            .ToArray());
    }

    /// <summary>
    /// [Issue #414] 対策B: テキストからコア文字（ひらがな・カタカナ・CJK漢字・ASCII英数字）のみを抽出
    /// </summary>
    /// <remarks>
    /// OCRとCloud AIで括弧・句読点・記号の認識差異が生じるため、
    /// 意味を持つコア文字のみで比較することでファジーマッチングの精度を向上させる。
    /// NormalizeTextよりも積極的に記号を除去する（括弧類も除去対象）。
    /// </remarks>
    private static string ExtractCoreCharacters(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return new string(text
            .Where(c =>
                (c >= '\u3040' && c <= '\u309F') || // ひらがな
                (c >= '\u30A0' && c <= '\u30FF') || // カタカナ
                (c >= '\u4E00' && c <= '\u9FFF') || // CJK統合漢字
                (c >= '\u3400' && c <= '\u4DBF') || // CJK統合漢字拡張A
                (c >= 'A' && c <= 'Z') ||           // ASCII大文字
                (c >= 'a' && c <= 'z') ||           // ASCII小文字
                (c >= '0' && c <= '9'))             // ASCII数字
            .ToArray());
    }

    /// <summary>
    /// [Issue #414→#415] Cloud結果のサイクル間重複検出ログ（補助的な役割）
    /// Fork-Join段階（Issue #415）で画像ハッシュベースのAPIコール抑制を実施するため、
    /// ここでは結果数のログ記録のみ行う。
    /// </summary>
    private void UpdateCloudResultCache(List<TranslatedTextItem> cloudTexts)
    {
        _logger?.LogDebug(
            "[Issue #415] Cloud結果受信: {Count}件（APIコール抑制はFork-Join段階で実施済み）",
            cloudTexts.Count);
    }

    /// <summary>
    /// [Issue #293] Gate判定を適用してテキスト変化のないチャンクをフィルタリング
    /// </summary>
    /// <remarks>
    /// 各チャンクに対してテキスト変化検知を実行し、前回と同じテキストのチャンクをスキップします。
    /// ROIヒートマップ値を取得して動的閾値調整に活用します。
    /// </remarks>
    private async Task<List<TextChunk>> ApplyGateFilteringAsync(
        List<TextChunk> chunks,
        int imageWidth,
        int imageHeight,
        CancellationToken cancellationToken)
    {
        // サービスが利用不可能な場合はフィルタリングをスキップ
        if (_textChangeDetectionService == null)
        {
            _logger?.LogDebug("🚪 [Issue #293] Gate判定スキップ: ITextChangeDetectionService未登録");
            return chunks;
        }

        var gatedChunks = new List<TextChunk>();
        var gateBlockedCount = 0;
        var gatePassedCount = 0;

        // ROIマネージャーの状態をログ出力
        var roiEnabled = _roiManager?.IsEnabled ?? false;

        // [Issue #464] 安定したゾーン計算を保証
        // imageWidth/HeightはOriginalWidth/Height（フルウィンドウ）であるべき
        // 0やROIクロップサイズの場合はフォールバック
        var stableWidth = imageWidth > 0 ? imageWidth : 1920;
        var stableHeight = imageHeight > 0 ? imageHeight : 1080;
        if (imageWidth <= 0 || imageHeight <= 0)
        {
            _logger?.LogWarning(
                "[Issue #464] imageSize無効値を検出: {Width}x{Height} → フォールバック: {StableWidth}x{StableHeight}",
                imageWidth, imageHeight, stableWidth, stableHeight);
        }

        _logger?.LogInformation(
            "🚪 [Issue #293] Gate判定開始: ChunkCount={Count}, RoiManager={RoiEnabled}, ImageSize={Width}x{Height}",
            chunks.Count, roiEnabled, stableWidth, stableHeight);

        // [Issue #397] ゾーンベースSourceIDの事前計算
        // 同一ゾーン内の複数チャンクがGate状態を相互汚染する問題を防止
        // → ゾーンごとに最長テキストのチャンクのみGate評価、他は自動通過
        // 注: 変化検知グリッド(16x9)より粗い8x6を使用。OCRのチャンク境界揺れ（数ピクセル）で
        // 隣接ゾーンに振り分けられることを防ぎ、Gate状態の安定性を優先する設計。
        const int zoneColumns = 8;
        const int zoneRows = 6;
        var chunkZoneMap = new Dictionary<int, string>(); // chunkIndex → sourceId
        var zoneRepresentative = new Dictionary<string, int>(); // sourceId → longest chunk index

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            var text = chunk.CombinedText ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                chunkZoneMap[i] = string.Empty;
                continue;
            }

            // [Issue #464] CombinedBoundsがimageSize範囲内であることを検証
            var centerX = chunk.CombinedBounds.X + chunk.CombinedBounds.Width / 2;
            var centerY = chunk.CombinedBounds.Y + chunk.CombinedBounds.Height / 2;
            centerX = Math.Clamp(centerX, 0, stableWidth - 1);
            centerY = Math.Clamp(centerY, 0, stableHeight - 1);

            var zoneCol = Math.Clamp(centerX * zoneColumns / stableWidth, 0, zoneColumns - 1);
            var zoneRow = Math.Clamp(centerY * zoneRows / stableHeight, 0, zoneRows - 1);
            var sourceId = $"zone_{zoneRow}_{zoneCol}";

            chunkZoneMap[i] = sourceId;

            // 同一ゾーン内で最長テキストのチャンクを代表として記録
            if (!zoneRepresentative.TryGetValue(sourceId, out var existingIdx) ||
                text.Length > (chunks[existingIdx].CombinedText?.Length ?? 0))
            {
                zoneRepresentative[sourceId] = i;
            }
        }

        // [Issue #397] ゾーン重複検出のログ
        var duplicateZones = zoneRepresentative.Where(kv =>
            chunkZoneMap.Count(z => z.Value == kv.Key) > 1).ToList();
        if (duplicateZones.Count > 0)
        {
            foreach (var dz in duplicateZones)
            {
                var chunkCount = chunkZoneMap.Count(z => z.Value == dz.Key);
                _logger?.LogDebug(
                    "[Issue #397] ゾーン重複検出: {Zone} に{Count}チャンク → 代表チャンク(idx={RepIdx})のみGate評価",
                    dz.Key, chunkCount, dz.Value);
            }
        }

        // Gate評価済みゾーンのトラッキング
        var evaluatedZones = new HashSet<string>();

        for (int i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            if (cancellationToken.IsCancellationRequested)
                break;

            var text = chunk.CombinedText ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                gatedChunks.Add(chunk);
                continue;
            }

            var sourceId = chunkZoneMap[i];

            // [Issue #397] 同一ゾーンで既にGate評価済み → Gate状態汚染防止のため自動通過
            if (evaluatedZones.Contains(sourceId))
            {
                gatedChunks.Add(chunk);
                gatePassedCount++;
                _logger?.LogDebug(
                    "🚪 [Issue #397] Gate AUTO-PASS (同一ゾーン既評価): Zone={Zone}, Text='{Text}'",
                    sourceId, text.Length > 30 ? text[..30] + "..." : text);
                continue;
            }

            // [Issue #397] 代表チャンク以外はGate評価をスキップ（自動通過）
            // 代表チャンク = 同一ゾーン内で最長テキストを持つチャンク
            if (zoneRepresentative.TryGetValue(sourceId, out var repIdx) && repIdx != i)
            {
                gatedChunks.Add(chunk);
                gatePassedCount++;
                _logger?.LogDebug(
                    "🚪 [Issue #397] Gate AUTO-PASS (非代表チャンク): Zone={Zone}, Text='{Text}'",
                    sourceId, text.Length > 30 ? text[..30] + "..." : text);
                continue;
            }

            // 正規化座標を計算
            GateRegionInfo? regionInfo = null;
            if (imageWidth > 0 && imageHeight > 0)
            {
                var normalizedX = (float)chunk.CombinedBounds.X / imageWidth;
                var normalizedY = (float)chunk.CombinedBounds.Y / imageHeight;
                var normalizedWidth = (float)chunk.CombinedBounds.Width / imageWidth;
                var normalizedHeight = (float)chunk.CombinedBounds.Height / imageHeight;

                // ヒートマップ値を取得
                float? heatmapValue = null;
                if (_roiManager?.IsEnabled == true)
                {
                    var centerX = normalizedX + normalizedWidth / 2f;
                    var centerY = normalizedY + normalizedHeight / 2f;
                    heatmapValue = _roiManager.GetHeatmapValueAt(centerX, centerY);

                    _logger?.LogDebug(
                        "🗺️ [Issue #293] HeatmapValue取得: Center=({CenterX:F3},{CenterY:F3}), Value={Value:F3}",
                        centerX, centerY, heatmapValue);
                }

                regionInfo = heatmapValue.HasValue
                    ? GateRegionInfo.WithHeatmap(normalizedX, normalizedY, normalizedWidth, normalizedHeight, heatmapValue.Value)
                    : GateRegionInfo.FromCoordinates(normalizedX, normalizedY, normalizedWidth, normalizedHeight);

                // [Issue #491] OCR信頼度をGateRegionInfoに設定（CJK1文字Gate判定に使用）
                regionInfo = regionInfo with { ConfidenceScore = (float)chunk.AverageConfidence };
            }

            // Gate判定を実行（代表チャンクのみ）
            var gateResult = await _textChangeDetectionService.DetectChangeWithGateAsync(
                text,
                sourceId,
                regionInfo,
                cancellationToken).ConfigureAwait(false);

            evaluatedZones.Add(sourceId);

            if (gateResult.ShouldTranslate)
            {
                gatedChunks.Add(chunk);
                gatePassedCount++;
                _logger?.LogDebug(
                    "🚪 [Issue #293] Gate PASS: Decision={Decision}, ChangeRate={Change:P1}, Threshold={Threshold:P1}, HeatmapValue={Heatmap}, Text='{Text}'",
                    gateResult.Decision,
                    gateResult.ChangePercentage,
                    gateResult.AppliedThreshold,
                    regionInfo?.HeatmapValue?.ToString("F3") ?? "(null)",
                    text.Length > 30 ? text[..30] + "..." : text);
            }
            else
            {
                gateBlockedCount++;
                _logger?.LogInformation(
                    "🚪 [Issue #293] Gate BLOCK: Decision={Decision}, ChangeRate={Change:P1}, Threshold={Threshold:P1}, HeatmapValue={Heatmap}, Text='{Text}'",
                    gateResult.Decision,
                    gateResult.ChangePercentage,
                    gateResult.AppliedThreshold,
                    regionInfo?.HeatmapValue?.ToString("F3") ?? "(null)",
                    text.Length > 30 ? text[..30] + "..." : text);
            }
        }

        // [Issue #486] OCR確認ベースのテキスト安定性追跡:
        // Gate判定の結果に関わらず、OCRがテキストを検出した全ゾーンの存在確認を更新。
        // これにより、TextDisappearanceの誤判定でオーバーレイが不要に削除されるのを防止。
        foreach (var zoneId in evaluatedZones)
        {
            _textChangeDetectionService.ConfirmTextPresence(zoneId);
        }

        if (gateBlockedCount > 0 || gatePassedCount > 0)
        {
            Console.WriteLine($"🚪 [Issue #293] Gate判定完了: {gatePassedCount}件通過, {gateBlockedCount}件ブロック");
            _logger?.LogInformation(
                "🚪 [Issue #293] Gate判定完了: Passed={Passed}, Blocked={Blocked}, RoiEnabled={RoiEnabled}, PresenceConfirmed={ConfirmedZones}",
                gatePassedCount, gateBlockedCount, roiEnabled, evaluatedZones.Count);
        }

        // [Issue #491] OCR不検出ベースのオーバーレイ自動クリア
        var sourceWindowHandle = chunks.FirstOrDefault()?.SourceWindowHandle ?? IntPtr.Zero;
        await ProcessOcrAbsenceCleanupAsync(
            evaluatedZones, imageWidth, imageHeight, sourceWindowHandle, cancellationToken).ConfigureAwait(false);

        return gatedChunks;
    }

    /// <summary>
    /// [Issue #491] OCR不検出ベースのオーバーレイ自動クリア
    /// </summary>
    /// <remarks>
    /// 前回のOCRサイクルでテキストが検出されたゾーンのうち、今回検出されなかったゾーンの
    /// 不検出カウンターをインクリメント。連続OcrAbsenceThresholdサイクル不検出で
    /// TextDisappearanceEventを発行してオーバーレイを削除。
    /// evaluatedZonesはGate-BLOCKEDゾーンも含むため、OCRで検出されたゾーン全体を追跡できる。
    /// </remarks>
    private async Task ProcessOcrAbsenceCleanupAsync(
        HashSet<string> currentCycleZones,
        int imageWidth,
        int imageHeight,
        IntPtr sourceWindowHandle,
        CancellationToken cancellationToken)
    {
        // 前回存在→今回不存在のゾーンを特定
        var disappearedZones = _previousCycleZones.Except(currentCycleZones).ToList();
        var zonesToClear = new List<string>();

        foreach (var zoneId in disappearedZones)
        {
            var count = _zoneAbsenceCounter.AddOrUpdate(zoneId, 1, (_, c) => c + 1);
            if (count >= OcrAbsenceThreshold)
            {
                zonesToClear.Add(zoneId);
                _zoneAbsenceCounter.TryRemove(zoneId, out _);
            }
            else
            {
                _logger?.LogDebug(
                    "[Issue #491] OCR不検出カウント: Zone={Zone}, Count={Count}/{Threshold}",
                    zoneId, count, OcrAbsenceThreshold);
            }
        }

        // 今回検出されたゾーンの不検出カウンターをリセット
        foreach (var zoneId in currentCycleZones)
        {
            _zoneAbsenceCounter.TryRemove(zoneId, out _);
        }

        // 前回ゾーンを更新
        _previousCycleZones = [.. currentCycleZones];

        // 連続不検出ゾーンのオーバーレイをクリア
        if (zonesToClear.Count > 0)
        {
            _logger?.LogInformation(
                "[Issue #491] OCR不検出によるオーバーレイクリア: Zones={Zones}",
                string.Join(", ", zonesToClear));
            Console.WriteLine($"🧹 [Issue #491] OCR不検出クリア: {string.Join(", ", zonesToClear)}");

            // Gate状態をクリアして再検出時にFirstTextとして扱われるようにする
            foreach (var zoneId in zonesToClear)
            {
                _textChangeDetectionService?.ClearPreviousText(zoneId);
            }

            // ゾーンIDからキャプチャ画像座標の矩形を計算してTextDisappearanceEventを発行
            var stableWidth = imageWidth > 0 ? imageWidth : 1920;
            var stableHeight = imageHeight > 0 ? imageHeight : 1080;
            var regions = ConvertZonesToRectangles(zonesToClear, stableWidth, stableHeight);

            if (regions.Count > 0)
            {
                var disappearanceEvent = new Baketa.Core.Events.Capture.TextDisappearanceEvent(
                    regions,
                    sourceWindow: sourceWindowHandle,
                    regionId: "ocr_absence_cleanup",
                    confidenceScore: 1.0f,
                    captureImageSize: new System.Drawing.Size(stableWidth, stableHeight));

                try
                {
                    await _eventAggregator.PublishAsync(disappearanceEvent, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex,
                        "[Issue #491] TextDisappearanceEvent発行エラー: {Message}", ex.Message);
                }
            }
        }
    }

    /// <summary>
    /// [Issue #491] ゾーンIDリストからキャプチャ座標系の矩形リストに変換
    /// </summary>
    private static List<System.Drawing.Rectangle> ConvertZonesToRectangles(
        List<string> zoneIds, int imageWidth, int imageHeight)
    {
        const int zoneColumns = 8;
        const int zoneRows = 6;
        var zoneWidth = imageWidth / zoneColumns;
        var zoneHeight = imageHeight / zoneRows;
        var rectangles = new List<System.Drawing.Rectangle>();

        foreach (var zoneId in zoneIds)
        {
            // "zone_{row}_{col}" 形式を解析
            var parts = zoneId.Split('_');
            if (parts.Length == 3
                && int.TryParse(parts[1], out var row)
                && int.TryParse(parts[2], out var col))
            {
                rectangles.Add(new System.Drawing.Rectangle(
                    col * zoneWidth, row * zoneHeight, zoneWidth, zoneHeight));
            }
        }

        return rectangles;
    }

    // [Issue #482] HTML/LaTeXマークアップ検出用正規表現
    [GeneratedRegex(@"</?[a-zA-Z][^>]*>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\\[a-zA-Z]{2,}")]
    private static partial Regex LatexCommandRegex();

    /// <summary>
    /// ターゲット言語に応じたオーバーレイ用システムフォントファミリーを返す
    /// </summary>
    internal static string GetOverlayFontFamily(string targetLanguage)
    {
        return targetLanguage.ToLowerInvariant() switch
        {
            "ja" => "Yu Gothic UI",
            "zh-cn" or "zho_hans" => "Microsoft YaHei UI",
            "zh-tw" or "zho_hant" => "Microsoft JhengHei UI",
            "ko" or "kor_hang" => "Malgun Gothic",
            _ => "Segoe UI" // 英語・欧州言語
        };
    }
}
