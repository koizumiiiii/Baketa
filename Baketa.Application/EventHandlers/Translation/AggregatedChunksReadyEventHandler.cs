using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Services; // 🔥 [COORDINATE_FIX] ICoordinateTransformationService用
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.UI;
using Baketa.Core.Abstractions.UI.Overlays; // 🔧 [OVERLAY_UNIFICATION] IOverlayManager統一インターフェース用
using Baketa.Core.Events.EventTypes; // 🔥 [INDIVIDUAL_TRANSLATION_EVENT] TranslationWithBoundsCompletedEvent用
using Baketa.Core.Events.Translation;
using Baketa.Core.Models.Translation;
using Baketa.Core.Translation.Models;
using Baketa.Core.Utilities;
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

    public AggregatedChunksReadyEventHandler(
        Baketa.Core.Abstractions.Translation.ITranslationService translationService,
        // 🔧 [OVERLAY_UNIFICATION] IInPlaceTranslationOverlayManager → IOverlayManager に統一
        IOverlayManager overlayManager,
        ILanguageConfigurationService languageConfig,
        IEventAggregator eventAggregator,
        ILogger<AggregatedChunksReadyEventHandler> logger,
        ICoordinateTransformationService coordinateTransformationService, // 🔥 [COORDINATE_FIX]
        Core.Abstractions.Settings.IUnifiedSettingsService unifiedSettingsService,
        IStreamingTranslationService? streamingTranslationService = null)
    {
        _translationService = translationService ?? throw new ArgumentNullException(nameof(translationService));
        _overlayManager = overlayManager ?? throw new ArgumentNullException(nameof(overlayManager));
        _languageConfig = languageConfig ?? throw new ArgumentNullException(nameof(languageConfig));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _coordinateTransformationService = coordinateTransformationService ?? throw new ArgumentNullException(nameof(coordinateTransformationService)); // 🔥 [COORDINATE_FIX]
        _unifiedSettingsService = unifiedSettingsService ?? throw new ArgumentNullException(nameof(unifiedSettingsService));
        _streamingTranslationService = streamingTranslationService;
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
    public async Task HandleAsync(AggregatedChunksReadyEvent eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);

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

            _logger.LogInformation("🔥 [PHASE12.2] 集約チャンク受信 - {Count}個, SessionId: {SessionId}",
                eventData.AggregatedChunks.Count, eventData.SessionId);
            _logger.LogCritical("✅✅✅ [PHASE12.2_NEW_ARCH] AggregatedChunksReadyEventHandler開始. SessionId: {SessionId}", eventData.SessionId);

            // 集約されたチャンクをリストに変換
            var aggregatedChunks = eventData.AggregatedChunks.ToList();

            // 空でないチャンクのみフィルタリング
            var nonEmptyChunks = aggregatedChunks
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

            // バッチ翻訳実行
            _logger?.LogDebug($"🚀🚀🚀 [PHASE12.2_HANDLER] ExecuteBatchTranslationAsync呼び出し直前 - ChunkCount: {nonEmptyChunks.Count}");
            Console.WriteLine($"🚀🚀🚀 [PHASE12.2_HANDLER] ExecuteBatchTranslationAsync呼び出し直前 - ChunkCount: {nonEmptyChunks.Count}");

            var translationResults = await ExecuteBatchTranslationAsync(
                nonEmptyChunks,
                CancellationToken.None).ConfigureAwait(false);

            _logger?.LogDebug($"✅✅✅ [PHASE12.2_HANDLER] ExecuteBatchTranslationAsync完了 - 結果数: {translationResults.Count}");
            Console.WriteLine($"✅✅✅ [PHASE12.2_HANDLER] ExecuteBatchTranslationAsync完了 - 結果数: {translationResults.Count}");

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
                _logger?.LogDebug($"🔧 [PHASE12.2_HANDLER] チャンク{i}翻訳結果設定: '{nonEmptyChunks[i].CombinedText}' → '{translationResults[i]}'");
            }

#if DEBUG
            // 🚨 [ULTRATHINK_TRACE2] 翻訳結果設定完了トレースログ
            var timestamp2 = DateTime.Now.ToString("HH:mm:ss.fff");
            var threadId2 = Environment.CurrentManagedThreadId;
            System.IO.File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "baketa_debug.log"),
                $"[{timestamp2}][T{threadId2:D2}] 🚨 [ULTRATHINK_TRACE2] 翻訳結果設定完了 - チャンク数: {nonEmptyChunks.Count}\r\n");
#endif

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

                _logger.LogDebug("🔥 [FIX4_FULLSCREEN_COORD] 座標変換実行 - 画像座標:({X},{Y}) → スクリーン座標:({SX},{SY})",
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
        _logger.LogInformation("ℹ️ [COORD_FIX] 座標正規化は不要です。座標は既に絶対値のはずです: ({X},{Y})",
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

                _logger?.LogDebug($"✅ [PHASE12.2_BATCH] TranslateBatchWithStreamingAsync完了 - 結果数: {results.Count}");
                return results;
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

    /// <summary>
    /// オーバーレイ表示処理
    /// CoordinateBasedTranslationService.ProcessBatchTranslationAsync()のオーバーレイ表示処理相当
    /// </summary>
    private async Task DisplayTranslationOverlayAsync(
        List<TextChunk> translatedChunks,
        IntPtr windowHandle,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger?.LogDebug($"🎯🎯🎯 [PHASE12.2_OVERLAY] DisplayTranslationOverlayAsync メソッド開始 - ChunkCount: {translatedChunks.Count}");
            Console.WriteLine($"🎯🎯🎯 [PHASE12.2_OVERLAY] DisplayTranslationOverlayAsync メソッド開始 - ChunkCount: {translatedChunks.Count}");

            _logger.LogInformation("🎯 [PHASE12.2] インプレースオーバーレイ表示開始 - チャンク数: {Count}",
                translatedChunks.Count);

            // 翻訳結果の詳細ログ
            for (int i = 0; i < translatedChunks.Count; i++)
            {
                var chunk = translatedChunks[i];
                _logger?.LogDebug($"   🔍 [PHASE12.2_OVERLAY] チャンク[{i}]: '{chunk.CombinedText}' → '{chunk.TranslatedText}'");
                _logger.LogDebug("   [{Index}] '{Original}' → '{Translated}'",
                    i, chunk.CombinedText, chunk.TranslatedText);
            }

            // 各チャンクをインプレース表示
            int displayedCount = 0;
            foreach (var chunk in translatedChunks)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger?.LogDebug($"⚠️ [PHASE12.2_OVERLAY] キャンセル要求検出 - 表示中断");
                    break;
                }

                if (chunk.CanShowInPlace() && !string.IsNullOrWhiteSpace(chunk.TranslatedText))
                {
                    _logger?.LogDebug($"🔧 [OVERLAY_UNIFICATION] ShowAsync実行開始 - ChunkId: {chunk.ChunkId}");
                    _logger.LogDebug("🔧 [OVERLAY_UNIFICATION] ShowAsync実行 - ChunkId: {ChunkId}",
                        chunk.ChunkId);

                    // 🔧 [OVERLAY_UNIFICATION] 統一IOverlayManager.ShowAsync()でオーバーレイ表示
                    var translationSettings = _unifiedSettingsService.GetTranslationSettings();
                    var content = new OverlayContent
                    {
                        Text = chunk.TranslatedText,
                        OriginalText = chunk.CombinedText,
                        FontSize = translationSettings.OverlayFontSize
                    };

                    var position = new OverlayPosition
                    {
                        X = chunk.CombinedBounds.X,
                        Y = chunk.CombinedBounds.Y,
                        Width = chunk.CombinedBounds.Width,
                        Height = chunk.CombinedBounds.Height
                    };

                    await _overlayManager.ShowAsync(content, position).ConfigureAwait(false);

                    displayedCount++;
                    _logger?.LogDebug($"   ✅ [OVERLAY_UNIFICATION] ShowAsync完了 - ChunkId: {chunk.ChunkId}, 累計表示: {displayedCount}個");
                    _logger.LogDebug("   ✅ [OVERLAY_UNIFICATION] インプレース表示完了 - ChunkId: {ChunkId}",
                        chunk.ChunkId);
                }
                else
                {
                    _logger?.LogDebug($"⚠️ [PHASE12.2_OVERLAY] スキップ - ChunkId: {chunk.ChunkId}, CanShowInPlace: {chunk.CanShowInPlace()}, HasTranslation: {!string.IsNullOrWhiteSpace(chunk.TranslatedText)}");
                }
            }

            _logger?.LogDebug($"🎉🎉🎉 [PHASE12.2_OVERLAY] DisplayTranslationOverlayAsync完了 - 表示数: {displayedCount}/{translatedChunks.Count}");
            Console.WriteLine($"🎉🎉🎉 [PHASE12.2_OVERLAY] DisplayTranslationOverlayAsync完了 - 表示数: {displayedCount}/{translatedChunks.Count}");

            _logger.LogInformation("🎉 [PHASE12.2] 座標ベース翻訳処理完了 - オーバーレイ表示成功");
        }
        catch (Exception ex)
        {
            _logger?.LogDebug($"❌❌❌ [PHASE12.2_OVERLAY] DisplayTranslationOverlayAsync例外: {ex.GetType().Name} - {ex.Message}");
            Console.WriteLine($"❌❌❌ [PHASE12.2_OVERLAY] DisplayTranslationOverlayAsync例外: {ex.GetType().Name} - {ex.Message}");
            _logger.LogError(ex, "❌ [PHASE12.2] オーバーレイ表示エラー");
            throw;
        }
    }
}
