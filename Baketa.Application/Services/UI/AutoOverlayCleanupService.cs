using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Processing;
using Baketa.Core.Abstractions.UI;
using Baketa.Core.Abstractions.UI.Overlays; // 🔧 [OVERLAY_UNIFICATION]
using Baketa.Core.Events.Capture;
using Baketa.Core.Settings;
// using Baketa.UI.Services; // UI層への直接参照は避ける（Clean Architecture違反）
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Application.Services.UI;

/// <summary>
/// オーバーレイ自動削除サービス実装
/// UltraThink Phase 1: オーバーレイ自動消去システム
/// 
/// TextDisappearanceEventを受信してInPlaceTranslationOverlayManagerの削除機能を呼び出す
/// Circuit Breaker パターンによる誤検知防止機能付き
/// Gemini Review: IHostedService実装により初期化自動化
/// </summary>
public sealed class AutoOverlayCleanupService : IAutoOverlayCleanupService, IEventProcessor<TextDisappearanceEvent>, IHostedService
{
    // 🔧 [OVERLAY_UNIFICATION] IInPlaceTranslationOverlayManager → IOverlayManager に統一
    private readonly IOverlayManager _overlayManager;
    private readonly IEventAggregator _eventAggregator;
    private readonly ILogger<AutoOverlayCleanupService> _logger;
    private readonly IOptionsMonitor<AutoOverlayCleanupSettings> _settings;
    // [Issue #407] オーバーレイ削除時のGate状態リセット用（オプショナル）
    private readonly ITextChangeDetectionService? _textChangeDetectionService;

    // Circuit Breaker設定（IOptions経由で動的取得）
    private float MinConfidenceScore => _settings.CurrentValue.MinConfidenceScore;
    private int MaxCleanupPerSecond => _settings.CurrentValue.MaxCleanupPerSecond;

    // 統計・監視用
    private readonly object _statsLock = new();
    private int _totalEventsProcessed;
    private int _overlaysCleanedUp;
    private int _rejectedByConfidence;
    private int _rejectedByRateLimit;
    private double _totalProcessingTime;
    private DateTime? _lastEventProcessedAt;
    private int _errorCount;

    // レート制限用
    private readonly Queue<DateTime> _recentCleanups = new();

    // 初期化状態
    private volatile bool _isInitialized = false;
    private bool _disposed = false;

    // IEventProcessor<T>の必須プロパティ
    /// <summary>イベント処理優先度（高優先度でオーバーレイを迅速に削除）</summary>
    public int Priority => 100;

    /// <summary>同期実行（UI操作のため非同期実行を使用）</summary>
    public bool SynchronousExecution => false;

    public AutoOverlayCleanupService(
        // 🔧 [OVERLAY_UNIFICATION] IInPlaceTranslationOverlayManager → IOverlayManager に統一
        IOverlayManager overlayManager,
        IEventAggregator eventAggregator,
        ILogger<AutoOverlayCleanupService> logger,
        IOptionsMonitor<AutoOverlayCleanupSettings> settings,
        ITextChangeDetectionService? textChangeDetectionService = null) // [Issue #407] Gate状態リセット用
    {
        _overlayManager = overlayManager ?? throw new ArgumentNullException(nameof(overlayManager));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _textChangeDetectionService = textChangeDetectionService;
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
        {
            _logger.LogWarning("AutoOverlayCleanupServiceは既に初期化済みです");
            return;
        }

        try
        {
            // TextDisappearanceEventイベント購読
            _eventAggregator.Subscribe<TextDisappearanceEvent>(this);

            _isInitialized = true;
            _logger.LogInformation("🎯 AutoOverlayCleanupService初期化完了 - 信頼度閾値: {MinConfidence:F2}, 最大削除レート: {MaxRate}/秒, 設定外部化: 有効",
                MinConfidenceScore, MaxCleanupPerSecond);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ AutoOverlayCleanupService初期化エラー");
            throw;
        }
    }

    /// <inheritdoc />
    /// <summary>
    /// TextDisappearanceEventハンドラー（IEventProcessorとして実装）
    /// Circuit Breaker パターンによる安全な自動削除処理
    /// </summary>
    public async Task HandleAsync(TextDisappearanceEvent eventData, CancellationToken cancellationToken = default)
    {
        if (_disposed || eventData == null)
            return;

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // 統計更新
            Interlocked.Increment(ref _totalEventsProcessed);

            _logger.LogDebug("🔍 テキスト消失イベント受信 - RegionId: {RegionId}, 信頼度: {Confidence:F3}, 領域数: {RegionCount}",
                eventData.RegionId ?? "未指定",
                eventData.ConfidenceScore,
                eventData.DisappearedRegions.Count);

            // Circuit Breaker: 信頼度チェック
            if (eventData.ConfidenceScore < MinConfidenceScore)
            {
                Interlocked.Increment(ref _rejectedByConfidence);
                _logger.LogDebug("⚠️ 信頼度不足により削除要求を却下 - 信頼度: {Confidence:F3} < 閾値: {Threshold:F3}",
                    eventData.ConfidenceScore, MinConfidenceScore);
                return;
            }

            // Circuit Breaker: レート制限チェック
            if (!IsWithinRateLimit())
            {
                Interlocked.Increment(ref _rejectedByRateLimit);
                _logger.LogDebug("🚦 レート制限により削除要求を却下 - 最大レート: {MaxRate}/秒", MaxCleanupPerSecond);
                return;
            }

            // 実際のオーバーレイ削除実行
            var cleanedCount = await CleanupOverlaysInRegionAsync(
                eventData.SourceWindowHandle,
                eventData.DisappearedRegions).ConfigureAwait(false);

            // 削除成功時の統計更新
            if (cleanedCount > 0)
            {
                Interlocked.Add(ref _overlaysCleanedUp, cleanedCount);
                RecordCleanupTime();

                _logger.LogInformation("✅ オーバーレイ自動削除完了 - RegionId: {RegionId}, 削除数: {CleanedCount}, 処理時間: {ProcessingTime}ms",
                    eventData.RegionId ?? "未指定", cleanedCount, stopwatch.ElapsedMilliseconds);
            }
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _errorCount);
            _logger.LogError(ex, "❌ テキスト消失イベント処理エラー - RegionId: {RegionId}",
                eventData.RegionId ?? "未指定");
        }
        finally
        {
            stopwatch.Stop();
            UpdateProcessingTime(stopwatch.Elapsed.TotalMilliseconds);

            lock (_statsLock)
            {
                _lastEventProcessedAt = DateTime.UtcNow;
            }
        }
    }

    /// <inheritdoc />
    public async Task<int> CleanupOverlaysInRegionAsync(
        IntPtr windowHandle,
        IReadOnlyList<Rectangle> regions,
        CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
        {
            _logger.LogWarning("サービス未初期化のため削除要求をスキップ");
            return 0;
        }

        if (regions == null || !regions.Any())
        {
            _logger.LogDebug("削除対象領域が指定されていません");
            return 0;
        }

        int totalCleaned = 0;

        try
        {
            var beforeCount = _overlayManager.ActiveOverlayCount;

            // [Issue #408] 領域指定オーバーレイ削除
            foreach (var region in regions)
            {
                await _overlayManager.HideOverlaysInAreaAsync(region, excludeChunkId: -1, cancellationToken).ConfigureAwait(false);
                totalCleaned++;
            }

            // [Issue #481] 座標系不一致フォールバック: 領域指定削除でオーバーレイが1つも消えなかった場合、
            // キャプチャ相対座標とスクリーン絶対座標の不一致が原因の可能性があるため全消去にフォールバック
            var afterCount = _overlayManager.ActiveOverlayCount;
            if (beforeCount > 0 && afterCount == beforeCount)
            {
                _logger.LogInformation("[Issue #481] 領域指定削除で交差なし（座標系不一致の可能性） - HideAllAsyncにフォールバック (ActiveOverlays={Count})",
                    afterCount);
                await _overlayManager.HideAllAsync(cancellationToken).ConfigureAwait(false);
                totalCleaned = beforeCount;
            }

            // [Issue #408] ゾーン特定Gate状態クリア（全リセットではなく消失領域のゾーンのみ）
            if (_textChangeDetectionService != null)
            {
                ClearGateForRegions(regions, windowHandle);
            }

            _logger.LogDebug("[Issue #408] 領域指定オーバーレイ削除完了 - WindowHandle: {WindowHandle}, 対象領域数: {RegionCount}",
                windowHandle, regions.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "領域指定オーバーレイ削除エラー - WindowHandle: {WindowHandle}", windowHandle);
            throw;
        }

        return totalCleaned;
    }

    /// <inheritdoc />
    public AutoOverlayCleanupStatistics GetStatistics()
    {
        lock (_statsLock)
        {
            var avgProcessingTime = _totalEventsProcessed > 0
                ? _totalProcessingTime / _totalEventsProcessed
                : 0.0;

            return new AutoOverlayCleanupStatistics
            {
                TotalEventsProcessed = _totalEventsProcessed,
                OverlaysCleanedUp = _overlaysCleanedUp,
                RejectedByConfidence = _rejectedByConfidence,
                RejectedByRateLimit = _rejectedByRateLimit,
                AverageProcessingTimeMs = avgProcessingTime,
                LastEventProcessedAt = _lastEventProcessedAt,
                ErrorCount = _errorCount
            };
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Gemini Review: 実行時設定更新はIOptionsMonitor.CurrentValue経由となったため、
    /// このメソッドは設定検証のみ行い、実際の設定更新はappsettings.jsonの変更で行う
    /// </remarks>
    public void UpdateCircuitBreakerSettings(float minConfidenceScore, int maxCleanupRate)
    {
        if (minConfidenceScore < 0.0f || minConfidenceScore > 1.0f)
            throw new ArgumentOutOfRangeException(nameof(minConfidenceScore), "信頼度は0.0-1.0の範囲で指定してください");

        if (maxCleanupRate < 1 || maxCleanupRate > 100)
            throw new ArgumentOutOfRangeException(nameof(maxCleanupRate), "削除レートは1-100の範囲で指定してください");

        _logger.LogWarning("⚠️ UpdateCircuitBreakerSettings呼び出し検出 - 設定外部化により、appsettings.jsonでの設定変更を推奨します。" +
            "要求値: 信頼度閾値={MinConfidence:F2}, 最大削除レート={MaxRate}/秒", minConfidenceScore, maxCleanupRate);
    }

    /// <summary>
    /// [Issue #408] 消失領域からゾーンIDを計算し、該当ゾーンのGate状態をクリア
    /// AggregatedChunksReadyEventHandlerと同じ8x6グリッドを使用
    /// </summary>
    private void ClearGateForRegions(IEnumerable<Rectangle> regions, nint windowHandle)
    {
        // デフォルト解像度（実際のウィンドウサイズは取得困難なためフォールバック値を使用）
        const int defaultWidth = 1920;
        const int defaultHeight = 1080;
        const int zoneColumns = 8;
        const int zoneRows = 6;

        var clearedZones = new HashSet<string>();

        foreach (var region in regions)
        {
            // 領域中心からゾーンIDを計算
            var centerX = region.X + region.Width / 2;
            var centerY = region.Y + region.Height / 2;
            var zoneCol = Math.Clamp(centerX * zoneColumns / defaultWidth, 0, zoneColumns - 1);
            var zoneRow = Math.Clamp(centerY * zoneRows / defaultHeight, 0, zoneRows - 1);
            var zoneId = $"zone_{zoneRow}_{zoneCol}";

            if (clearedZones.Add(zoneId))
            {
                _textChangeDetectionService!.ClearPreviousText(zoneId);
            }
        }

        if (clearedZones.Count > 0)
        {
            _logger.LogInformation(
                "[Issue #408] ゾーン特定Gate状態クリア - Zones: [{Zones}]",
                string.Join(", ", clearedZones));
        }
    }

    /// <summary>
    /// レート制限チェック
    /// </summary>
    private bool IsWithinRateLimit()
    {
        var now = DateTime.UtcNow;
        var oneSecondAgo = now.AddSeconds(-1);

        lock (_recentCleanups)
        {
            // 1秒以前のレコードを削除
            while (_recentCleanups.Count > 0 && _recentCleanups.Peek() < oneSecondAgo)
            {
                _recentCleanups.Dequeue();
            }

            return _recentCleanups.Count < MaxCleanupPerSecond;
        }
    }

    /// <summary>
    /// 削除時刻記録（レート制限用）
    /// </summary>
    private void RecordCleanupTime()
    {
        var now = DateTime.UtcNow;

        lock (_recentCleanups)
        {
            _recentCleanups.Enqueue(now);
        }
    }

    /// <summary>
    /// 処理時間統計更新
    /// </summary>
    private void UpdateProcessingTime(double processingTimeMs)
    {
        lock (_statsLock)
        {
            _totalProcessingTime += processingTimeMs;
        }
    }

    /// <summary>
    /// IHostedService実装: アプリケーション開始時の初期化処理
    /// Gemini Review: InitializeAsync呼び出し保証のためのパターン
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("🚀 AutoOverlayCleanupService開始完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ AutoOverlayCleanupService開始エラー");
            throw;
        }
    }

    /// <summary>
    /// IHostedService実装: アプリケーション終了時の終了処理
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            Dispose();
            _logger.LogInformation("🛑 AutoOverlayCleanupService停止完了");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ AutoOverlayCleanupService停止エラー");
            return Task.FromException(ex);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            if (_isInitialized)
            {
                _eventAggregator.Unsubscribe<TextDisappearanceEvent>(this);
                _logger.LogInformation("🔌 AutoOverlayCleanupService購読解除完了");
            }

            lock (_recentCleanups)
            {
                _recentCleanups.Clear();
            }

            _disposed = true;
            _logger.LogInformation("🛑 AutoOverlayCleanupService破棄完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ AutoOverlayCleanupService破棄エラー");
        }
    }
}
