using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.UI.Overlay;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.Services.UI.Overlay;

/// <summary>
/// オーバーレイ位置計算のスタブ実装
/// UI層の実装が完成するまでの一時的な実装
/// Phase 15 動作確認・テスト用
/// </summary>
public class StubOverlayPositionCalculator : IOverlayPositionCalculator
{
    private readonly ILogger<StubOverlayPositionCalculator> _logger;

    /// <summary>
    /// スタブで管理するモニター情報
    /// </summary>
    private readonly List<MonitorInfo> _stubMonitors;

    /// <summary>
    /// 位置計算統計情報
    /// </summary>
    private long _totalCalculations = 0;
    private long _collisionAvoidanceCount = 0;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public StubOverlayPositionCalculator(ILogger<StubOverlayPositionCalculator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // スタブモニター情報を初期化
        _stubMonitors = new List<MonitorInfo>
        {
            new MonitorInfo
            {
                Id = 0,
                Name = "Primary Monitor (Stub)",
                WorkingArea = new Rectangle(0, 0, 1920, 1040), // タスクバー分を除いた領域
                FullArea = new Rectangle(0, 0, 1920, 1080),
                DpiScale = 1.0,
                IsPrimary = true,
                ColorDepth = 32,
                RefreshRate = 60
            }
        };

        _logger.LogInformation("🎭 [STUB_POSITION] StubOverlayPositionCalculator 初期化");
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🚀 [STUB_POSITION] スタブ位置計算器初期化開始");

        // スタブでは実際のモニター検出は行わない
        _totalCalculations = 0;
        _collisionAvoidanceCount = 0;

        _logger.LogInformation("✅ [STUB_POSITION] スタブ位置計算器初期化完了 - モニター数: {MonitorCount}", _stubMonitors.Count);
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<Rectangle> CalculateOptimalPositionAsync(PositionCalculationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            _totalCalculations++;

            _logger.LogDebug("🎭 [STUB_POSITION] 最適位置計算シミュレート - ID: {Id}, DesiredArea: {Area}, Strategy: {Strategy}",
                request.Id, request.DesiredArea, request.Strategy);

            var optimizedArea = request.DesiredArea;

            // 基本的な画面境界チェック
            var primaryMonitor = _stubMonitors.First(m => m.IsPrimary);
            optimizedArea = await AdjustToScreenBoundsAsync(optimizedArea, primaryMonitor.Id, cancellationToken);

            // 戦略別の簡単な位置調整
            switch (request.Strategy)
            {
                case PositionStrategy.CenterScreen:
                    optimizedArea = CenterOnScreen(optimizedArea, primaryMonitor);
                    break;

                case PositionStrategy.AvoidCollision:
                    optimizedArea = await AvoidCollisionStub(optimizedArea, request, cancellationToken);
                    break;

                case PositionStrategy.KeepOriginal:
                default:
                    // 元位置を維持
                    break;
            }

            _logger.LogDebug("✅ [STUB_POSITION] 最適位置計算完了 - ID: {Id}, OptimizedArea: {Area}",
                request.Id, optimizedArea);

            return optimizedArea;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [STUB_POSITION] 位置計算中にエラー - ID: {Id}", request.Id);
            return request.DesiredArea; // エラー時は元位置を返す
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Rectangle>> CalculateBatchPositionsAsync(IEnumerable<PositionCalculationRequest> requests, CancellationToken cancellationToken = default)
    {
        var results = new List<Rectangle>();

        foreach (var request in requests ?? Enumerable.Empty<PositionCalculationRequest>())
        {
            var optimizedPosition = await CalculateOptimalPositionAsync(request, cancellationToken);
            results.Add(optimizedPosition);
        }

        _logger.LogDebug("🎭 [STUB_POSITION] バッチ位置計算完了 - 処理数: {Count}", results.Count);
        return results;
    }

    /// <inheritdoc />
    public async Task<bool> DetectCollisionAsync(Rectangle area, IEnumerable<OverlayPositionInfo> existingOverlays, IEnumerable<string>? excludeIds = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var excludeIdSet = excludeIds?.ToHashSet() ?? new HashSet<string>();

            var hasCollision = existingOverlays?.Any(overlay =>
                !excludeIdSet.Contains(overlay.Id) &&
                overlay.Area.IntersectsWith(area)) ?? false;

            await Task.CompletedTask;
            return hasCollision;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [STUB_POSITION] 衝突検出中にエラー - Area: {Area}", area);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<Rectangle> AdjustToScreenBoundsAsync(Rectangle area, int? targetMonitor = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var monitor = targetMonitor.HasValue ?
                _stubMonitors.FirstOrDefault(m => m.Id == targetMonitor.Value) ?? _stubMonitors.First() :
                _stubMonitors.First(m => m.IsPrimary);

            var workingArea = monitor.WorkingArea;
            var adjustedArea = area;

            // 画面境界調整
            if (adjustedArea.Right > workingArea.Right)
                adjustedArea.X = workingArea.Right - adjustedArea.Width;
            if (adjustedArea.Bottom > workingArea.Bottom)
                adjustedArea.Y = workingArea.Bottom - adjustedArea.Height;
            if (adjustedArea.X < workingArea.X)
                adjustedArea.X = workingArea.X;
            if (adjustedArea.Y < workingArea.Y)
                adjustedArea.Y = workingArea.Y;

            _logger.LogDebug("🎭 [STUB_POSITION] 画面境界調整 - Original: {Original}, Adjusted: {Adjusted}", area, adjustedArea);

            return await Task.FromResult(adjustedArea);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [STUB_POSITION] 画面境界調整中にエラー - Area: {Area}", area);
            return area;
        }
    }

    /// <inheritdoc />
    public async Task<MonitorInfo?> GetMonitorFromPointAsync(Point point, CancellationToken cancellationToken = default)
    {
        try
        {
            var monitor = _stubMonitors.FirstOrDefault(m => m.FullArea.Contains(point)) ?? _stubMonitors.First();
            return await Task.FromResult(monitor);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [STUB_POSITION] モニター検出中にエラー - Point: {Point}", point);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<MonitorInfo>> GetAvailableMonitorsAsync(CancellationToken cancellationToken = default)
    {
        return await Task.FromResult(_stubMonitors);
    }

    /// <inheritdoc />
    public async Task<Rectangle> ConvertLogicalToPhysicalAsync(Rectangle logicalArea, int? targetMonitor = null, CancellationToken cancellationToken = default)
    {
        // スタブではDPIスケーリングなしとして同じ値を返す
        return await Task.FromResult(logicalArea);
    }

    /// <summary>
    /// 画面中央配置
    /// </summary>
    private Rectangle CenterOnScreen(Rectangle area, MonitorInfo monitor)
    {
        var workingArea = monitor.WorkingArea;
        var centerX = workingArea.X + (workingArea.Width - area.Width) / 2;
        var centerY = workingArea.Y + (workingArea.Height - area.Height) / 2;

        return new Rectangle(centerX, centerY, area.Width, area.Height);
    }

    /// <summary>
    /// 衝突回避のスタブ実装
    /// </summary>
    private async Task<Rectangle> AvoidCollisionStub(Rectangle area, PositionCalculationRequest request, CancellationToken cancellationToken)
    {
        // スタブでは簡単なオフセット調整のみ実装
        var adjustedArea = area;

        // 少しずらして衝突回避をシミュレート
        if (request.MaxDisplacement > 0)
        {
            adjustedArea.X += 10; // 10ピクセルずらし
            adjustedArea.Y += 10;
            _collisionAvoidanceCount++;
        }

        return await Task.FromResult(adjustedArea);
    }

    /// <summary>
    /// スタブ位置計算器の統計情報取得
    /// </summary>
    public PositionCalculationStatistics GetStatistics()
    {
        return new PositionCalculationStatistics
        {
            TotalCalculations = _totalCalculations,
            CollisionAvoidanceCount = _collisionAvoidanceCount,
            AverageCalculationTime = 1.0, // スタブでは固定値
            MaxCalculationTime = 2.0,
            OffScreenCorrectionCount = 0,
            MultiMonitorPlacementCount = 0
        };
    }
}
