using System.Collections.Concurrent;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.UI;
using Baketa.Core.Abstractions.UI.Overlays; // 🔧 [OVERLAY_UNIFICATION]
using Baketa.Core.Events.EventTypes;
using Baketa.Core.Models.OCR; // 🔥 [FIX7_STEP3] OcrContext統合
using Baketa.Core.Settings;
using Baketa.Core.Translation.Models;
using Baketa.Core.Utilities;
using Baketa.Infrastructure.OCR.PostProcessing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ITranslationService = Baketa.Core.Abstractions.Translation.ITranslationService;

namespace Baketa.Infrastructure.OCR.BatchProcessing;

/// <summary>
/// TimedChunkAggregator統合型バッチOCRサービス
/// 戦略書設計: translation-quality-improvement-strategy.md 完全準拠
/// UltraThink Phase 26-2: ITextChunkAggregatorService実装による Clean Architecture準拠
/// </summary>
public sealed class EnhancedBatchOcrIntegrationService : ITextChunkAggregatorService, IDisposable
{
    private readonly BatchOcrIntegrationService _baseBatchService;
    private readonly TimedChunkAggregator _timedChunkAggregator;
    private readonly ITranslationService _translationService;
    // 🔧 [OVERLAY_UNIFICATION] IInPlaceTranslationOverlayManager → IOverlayManager に統一
    private readonly IOverlayManager _overlayManager;
    private readonly IEventAggregator _eventAggregator;
    private readonly IUnifiedSettingsService _unifiedSettingsService;
    private readonly ILogger<EnhancedBatchOcrIntegrationService> _logger;
    private readonly TimedAggregatorSettings _settings;
    private readonly ILanguageConfigurationService _languageConfig;

    // パフォーマンス監視用
    private readonly ConcurrentDictionary<string, ProcessingStatistics> _processingStats;
    private long _totalProcessedImages;
    // NOTE: readonlyを付けられない - Interlocked.Read(ref _totalAggregatedChunks)で使用するため
#pragma warning disable IDE0044 // Interlocked操作でref引数として使用するため読み取り専用にできない
    private long _totalAggregatedChunks;
#pragma warning restore IDE0044
    private bool _disposed;

    public EnhancedBatchOcrIntegrationService(
        BatchOcrIntegrationService baseBatchService,
        TimedChunkAggregator timedChunkAggregator,
        ITranslationService translationService,
        // 🔧 [OVERLAY_UNIFICATION] IInPlaceTranslationOverlayManager → IOverlayManager に統一
        IOverlayManager overlayManager,
        IEventAggregator eventAggregator,
        IUnifiedSettingsService unifiedSettingsService,
        IOptionsMonitor<TimedAggregatorSettings> settings,
        ILogger<EnhancedBatchOcrIntegrationService> logger,
        ILanguageConfigurationService languageConfig)
    {
        _baseBatchService = baseBatchService ?? throw new ArgumentNullException(nameof(baseBatchService));
        _timedChunkAggregator = timedChunkAggregator ?? throw new ArgumentNullException(nameof(timedChunkAggregator));
        _translationService = translationService ?? throw new ArgumentNullException(nameof(translationService));
        _overlayManager = overlayManager ?? throw new ArgumentNullException(nameof(overlayManager));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _unifiedSettingsService = unifiedSettingsService ?? throw new ArgumentNullException(nameof(unifiedSettingsService));
        _settings = settings?.CurrentValue ?? TimedAggregatorSettings.Development;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _languageConfig = languageConfig ?? throw new ArgumentNullException(nameof(languageConfig));

        _processingStats = new ConcurrentDictionary<string, ProcessingStatistics>();

        // 🚀 [PHASE12.2_MIGRATION] Gemini推奨: 後方互換コールバックを無効化
        // TimedChunkAggregatorのイベントハンドラ設定
        // _timedChunkAggregator.OnChunksAggregated = OnChunksAggregatedHandler;
        _logger.LogInformation("🔥 [PHASE12.2_MIGRATION] OnChunksAggregatedコールバックの登録を意図的にスキップ。新アーキテクチャ（AggregatedChunksReadyEvent）を使用");
        Console.WriteLine("🔥 [PHASE12.2_MIGRATION] 旧ルート（OnChunksAggregatedコールバック）無効化 - 新イベント駆動アーキテクチャに移行");

        _logger.LogInformation("🚀 EnhancedBatchOcrIntegrationService初期化完了 - TimedAggregator: {Enabled}",
            _settings.IsFeatureEnabled);
    }

    /// <summary>
    /// 拡張統合OCR処理 - TimedChunkAggregator統合版
    /// 戦略書フィードバック反映: 時間軸統合による翻訳品質向上40-60%
    /// FIX7 Step3: OcrContext対応
    /// </summary>
    public async Task<IReadOnlyList<TextChunk>> ProcessWithEnhancedOcrAsync(
        OcrContext context)
    {
        ThrowIfDisposed();

        var operationId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        _logger.LogDebug("🔍 拡張OCR処理開始 - Image: {Width}x{Height}, OperationId: {OperationId}",
            context.Image.Width, context.Image.Height, operationId);

        _logger.LogInformation("🔥 [FIX7_STEP3] ProcessWithEnhancedOcrAsync開始 - CaptureRegion: {HasCaptureRegion}",
            context.HasCaptureRegion);

        try
        {
            // 1. 既存BatchOcrIntegrationServiceでOCR実行
            var ocrChunks = await _baseBatchService.ProcessWithIntegratedOcrAsync(context).ConfigureAwait(false);

            if (ocrChunks.Count == 0)
            {
                _logger.LogWarning("⚠️ OCR結果なし - OperationId: {OperationId}", operationId);
                return ocrChunks;
            }

            // 2. TimedChunkAggregator統合処理
            if (_settings.IsFeatureEnabled)
            {
                var aggregationResults = new List<TextChunk>();

                foreach (var chunk in ocrChunks)
                {
                    // TimedChunkAggregatorにチャンクを追加
                    var added = await _timedChunkAggregator.TryAddChunkAsync(chunk, context.CancellationToken).ConfigureAwait(false);

                    if (!added)
                    {
                        // Feature Flag無効またはエラー時は直接結果に追加
                        aggregationResults.Add(chunk);
                    }
                }

                // TimedAggregatorが無効の場合は元のchunksをそのまま返す
                if (aggregationResults.Count > 0)
                {
                    _logger.LogInformation("📊 TimedAggregator無効 - 直接処理: {ChunkCount}個", aggregationResults.Count);
                    return aggregationResults;
                }

                // TimedAggregatorに追加されたチャンクは集約後に別途処理される
                _logger.LogDebug("⏱️ チャンク集約待機中 - {ChunkCount}個がTimedAggregatorに追加済み", ocrChunks.Count);
            }
            else
            {
                _logger.LogDebug("🚫 TimedAggregator機能無効 - 直接処理実行");
                return ocrChunks;
            }

            // 統計情報更新
            Interlocked.Increment(ref _totalProcessedImages);
            UpdateProcessingStatistics(operationId, startTime, ocrChunks.Count);

            // TimedAggregator有効時は空リストを返す（集約後の処理は別途実行）
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 拡張OCR処理エラー - OperationId: {OperationId}", operationId);
            throw;
        }
    }

    /// <summary>
    /// 複数画像の拡張並列処理
    /// FIX7 Step3: OcrContext対応
    /// </summary>
    public async Task<IReadOnlyList<IReadOnlyList<TextChunk>>> ProcessMultipleImagesWithEnhancedOcrAsync(
        IReadOnlyList<OcrContext> contexts,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (contexts.Count == 0)
            return [];

        _logger.LogInformation("📦 拡張複数画像処理開始 - 画像数: {ImageCount}, TimedAggregator: {Enabled}",
            contexts.Count, _settings.IsFeatureEnabled);

        // 並列処理タスクを作成
        var tasks = contexts.Select(async context =>
        {
            try
            {
                return await ProcessWithEnhancedOcrAsync(context).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ 画像処理エラー - サイズ: {Width}x{Height}",
                    context.Image.Width, context.Image.Height);
                return (IReadOnlyList<TextChunk>)[];
            }
        });

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        var totalChunks = results.Sum(r => r.Count);
        _logger.LogInformation("✅ 拡張複数画像処理完了 - 総チャンク数: {TotalChunks}", totalChunks);

        return results;
    }


    /// <summary>
    /// 🚀 Phase 22: CaptureCompletedHandlerからの個別TextChunk送信メソッド
    /// TimedChunkAggregatorに直接チャンクを送信し、集約処理を開始
    /// </summary>
    public async Task<bool> TryAddTextChunkDirectlyAsync(
        TextChunk chunk,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // 🔥 [PHASE22_ENTRY] メソッド実行開始診断
        _logger?.LogDebug(
            $"🔥🔥🔥 [PHASE22_ENTRY] TryAddTextChunkDirectlyAsync実行開始 - " +
            $"ChunkId: {chunk.ChunkId}, Text: \"{chunk.CombinedText}\", " +
            $"TimedChunkAggregator is null: {_timedChunkAggregator == null}"
        );

        _logger.LogCritical(
            "🔥🔥🔥 [PHASE22_ENTRY] TryAddTextChunkDirectlyAsync実行開始 - " +
            "ChunkId: {ChunkId}, Text: \"{Text}\", " +
            "TimedChunkAggregator is null: {IsNull}",
            chunk.ChunkId,
            chunk.CombinedText,
            _timedChunkAggregator == null
        );

        try
        {
            _logger.LogDebug("📥 [PHASE22] 個別TextChunk受信 - ID: {ChunkId}, テキスト: '{Text}'",
                chunk.ChunkId, chunk.CombinedText);

            if (!_settings.IsFeatureEnabled)
            {
                _logger?.LogDebug("🔥 [PHASE22_DISABLED] Feature無効により早期リターン");
                _logger.LogCritical("🔥 [PHASE22_DISABLED] Feature無効により早期リターン");
                _logger.LogInformation("⚠️ [PHASE22] TimedAggregator機能無効 - チャンク送信スキップ");
                return false;
            }

            if (_timedChunkAggregator == null)
            {
                _logger?.LogDebug("🔥 [PHASE22_NULL] TimedChunkAggregator is NULL - 返却: False");
                _logger.LogCritical("🔥 [PHASE22_NULL] TimedChunkAggregator is NULL - 返却: False");
                return false;
            }

            // 🔥 TimedChunkAggregator呼び出し前
            _logger?.LogDebug("🔥 [PHASE22_BEFORE_CALL] TimedChunkAggregator.TryAddChunkAsync呼び出し直前");
            _logger.LogCritical("🔥 [PHASE22_BEFORE_CALL] TimedChunkAggregator.TryAddChunkAsync呼び出し直前");

            // TimedChunkAggregatorに直接送信
            var added = await _timedChunkAggregator.TryAddChunkAsync(chunk, cancellationToken).ConfigureAwait(false);

            // 🔥 TimedChunkAggregator呼び出し後
            _logger?.LogDebug(
                $"🔥 [PHASE22_AFTER_CALL] TimedChunkAggregator.TryAddChunkAsync実行完了 - Result: {added}"
            );
            _logger.LogCritical(
                "🔥 [PHASE22_AFTER_CALL] TimedChunkAggregator.TryAddChunkAsync実行完了 - Result: {Result}",
                added
            );

            if (added)
            {
                _logger.LogInformation("✅ [PHASE22] TextChunk → TimedChunkAggregator送信成功 - ID: {ChunkId}",
                    chunk.ChunkId);
                Console.WriteLine($"📥 [PHASE22] TimedChunkAggregator: '{chunk.CombinedText}' 受信完了");
            }
            else
            {
                _logger.LogWarning("⚠️ [PHASE22] TextChunk送信失敗 - TimedAggregator処理エラー");
            }

            return added;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug($"🔥 [PHASE22_EXCEPTION] 例外発生: {ex.GetType().Name} - {ex.Message}");
            _logger.LogCritical(ex, "🔥 [PHASE22_EXCEPTION] 例外発生");
            _logger.LogError(ex, "❌ [PHASE22] TextChunk送信エラー - ChunkId: {ChunkId}", chunk.ChunkId);
            return false;
        }
    }

    /// <summary>
    /// パフォーマンス最適化設定の委譲
    /// </summary>
    public async Task OptimizeEnhancedPerformanceAsync(
        int imageWidth,
        int imageHeight,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // 既存BatchOcrIntegrationServiceの最適化処理を委譲
        await _baseBatchService.OptimizeBatchPerformanceAsync(imageWidth, imageHeight, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogDebug("⚙️ 拡張パフォーマンス最適化完了 - 画像: {Width}x{Height}", imageWidth, imageHeight);
    }

    /// <summary>
    /// 処理統計情報の更新
    /// </summary>
    private void UpdateProcessingStatistics(string operationId, DateTime startTime, int chunkCount)
    {
        var processingTime = DateTime.UtcNow - startTime;
        var stats = new ProcessingStatistics
        {
            OperationId = operationId,
            ProcessingTime = processingTime,
            ChunkCount = chunkCount,
            Timestamp = DateTime.UtcNow
        };

        _processingStats.TryAdd(operationId, stats);

        // 古い統計情報のクリーンアップ（メモリリーク防止）
        if (_processingStats.Count > 1000)
        {
            var oldEntries = _processingStats
                .Where(kvp => kvp.Value.Timestamp < DateTime.UtcNow.AddMinutes(-10))
                .Take(100)
                .ToList();

            foreach (var entry in oldEntries)
            {
                _processingStats.TryRemove(entry.Key, out _);
            }
        }
    }

    /// <summary>
    /// 現在の処理統計情報を取得
    /// </summary>
    public (long TotalImages, long TotalAggregatedChunks, TimeSpan AverageProcessingTime) GetEnhancedStatistics()
    {
        ThrowIfDisposed();

        var totalImages = Interlocked.Read(ref _totalProcessedImages);
        var totalChunks = Interlocked.Read(ref _totalAggregatedChunks);

        var avgProcessingTime = _processingStats.Values.Count > 0
            ? TimeSpan.FromTicks((long)_processingStats.Values.Average(s => s.ProcessingTime.Ticks))
            : TimeSpan.Zero;

        return (totalImages, totalChunks, avgProcessingTime);
    }

    /// <summary>
    /// TimedChunkAggregatorの統計情報を取得
    /// </summary>
    public (long TotalChunksProcessed, long TotalAggregationEvents) GetAggregatorStatistics()
    {
        ThrowIfDisposed();
        return _timedChunkAggregator.GetStatistics();
    }

    // ============================================
    // ITextChunkAggregatorService インターフェース実装
    // Phase 26-2: Clean Architecture準拠の抽象化実装
    // ============================================

    /// <inheritdoc />
    public async Task<bool> TryAddTextChunkAsync(TextChunk chunk, CancellationToken cancellationToken = default)
    {
        // 既存のTryAddTextChunkDirectlyAsyncメソッドに委譲
        return await TryAddTextChunkDirectlyAsync(chunk, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public bool IsFeatureEnabled => _settings.IsFeatureEnabled;

    /// <inheritdoc />
    public int PendingChunksCount => 0; // TODO: TimedChunkAggregatorにPendingChunksCount実装後に修正

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;

        // 統計情報ログ出力
        if (_settings.EnablePerformanceLogging)
        {
            var (totalImages, totalChunks, avgTime) = GetEnhancedStatistics();
            var (timedChunks, timedEvents) = GetAggregatorStatistics();

            _logger.LogInformation("📊 EnhancedBatchOcrIntegrationService最終統計 - " +
                "処理画像: {Images}, 集約チャンク: {Chunks}, 平均処理時間: {AvgTime}ms, " +
                "TimedAggregator - チャンク: {TimedChunks}, イベント: {TimedEvents}",
                totalImages, totalChunks, avgTime.TotalMilliseconds,
                timedChunks, timedEvents);
        }

        // リソース解放
        _baseBatchService?.Dispose();
        _timedChunkAggregator?.Dispose();
        _processingStats.Clear();

        _disposed = true;

        _logger.LogInformation("🧹 EnhancedBatchOcrIntegrationService disposed");
    }
}

/// <summary>
/// 処理統計情報を格納する内部クラス
/// </summary>
internal sealed class ProcessingStatistics
{
    public required string OperationId { get; init; }
    public required TimeSpan ProcessingTime { get; init; }
    public required int ChunkCount { get; init; }
    public required DateTime Timestamp { get; init; }
}
