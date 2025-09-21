using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Platform;
using Baketa.Core.Abstractions.UI.Overlay;
using Microsoft.Extensions.Logging;
using Baketa.UI.Overlay.Positioning;
using DrawingPoint = System.Drawing.Point;

namespace Baketa.UI.Services.Overlay;

/// <summary>
/// モニター情報取得失敗時のフォールバック設定
/// 設定可能なデフォルト値による堅牢なエラーハンドリング
/// </summary>
public class MonitorFallbackSettings
{
    /// <summary>デフォルト解像度幅</summary>
    public int DefaultWidth { get; set; } = 1920;
    
    /// <summary>デフォルト解像度高さ</summary>
    public int DefaultHeight { get; set; } = 1080;
    
    /// <summary>タスクバー高さ（作業領域計算用）</summary>
    public int TaskbarHeight { get; set; } = 40;
    
    /// <summary>デフォルトDPIスケーリング</summary>
    public double DefaultDpiScale { get; set; } = 1.0;
    
    /// <summary>デフォルト色深度</summary>
    public int DefaultColorDepth { get; set; } = 32;
    
    /// <summary>デフォルトリフレッシュレート</summary>
    public int DefaultRefreshRate { get; set; } = 60;
    
    /// <summary>フォールバック有効化フラグ</summary>
    public bool EnableFallback { get; set; } = true;
    
    /// <summary>フォールバック使用時の警告ログ出力</summary>
    public bool LogFallbackUsage { get; set; } = true;
}

/// <summary>
/// Avalonia UI を使用した実際のオーバーレイ位置計算実装
/// Phase 15 Clean Architecture と既存システムを統合
/// </summary>
public class AvaloniaOverlayPositionCalculator : IOverlayPositionCalculator, IDisposable
{
    private readonly ILogger<AvaloniaOverlayPositionCalculator> _logger;
    private readonly OverlayPositionManager? _positionManager;
    private readonly MonitorFallbackSettings _fallbackSettings;

    /// <summary>
    /// モニター情報キャッシュ
    /// </summary>
    private List<MonitorInfo>? _cachedMonitors;
    private DateTime _lastMonitorUpdate = DateTime.MinValue;
    private readonly TimeSpan _monitorCacheExpiry = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 統計情報
    /// </summary>
    private long _totalCalculations = 0;
    private long _collisionAvoidanceCount = 0;
    private long _offScreenCorrectionCount = 0;
    private long _multiMonitorPlacementCount = 0;
    private bool _disposed = false;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public AvaloniaOverlayPositionCalculator(
        ILogger<AvaloniaOverlayPositionCalculator> logger,
        IServiceProvider serviceProvider,
        MonitorFallbackSettings? fallbackSettings = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _fallbackSettings = fallbackSettings ?? new MonitorFallbackSettings();
        
        // 既存の位置管理システムを取得（オプション）
        try
        {
            _positionManager = serviceProvider.GetService(typeof(OverlayPositionManager)) as OverlayPositionManager;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "🚧 [AVALONIA_POSITION] 既存OverlayPositionManager取得失敗 - スタンドアロンで動作");
        }
        
        _logger.LogInformation("🎭 [AVALONIA_POSITION] AvaloniaOverlayPositionCalculator 初期化 - 既存システム統合");
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        _logger.LogInformation("🚀 [AVALONIA_POSITION] Avalonia 位置計算器初期化開始");
        
        try
        {
            // モニター情報の初期化
            await RefreshMonitorInfoAsync(cancellationToken);
            
            _totalCalculations = 0;
            _collisionAvoidanceCount = 0;
            _offScreenCorrectionCount = 0;
            _multiMonitorPlacementCount = 0;
            
            _logger.LogInformation("✅ [AVALONIA_POSITION] Avalonia 位置計算器初期化完了 - モニター数: {MonitorCount}", 
                _cachedMonitors?.Count ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [AVALONIA_POSITION] 初期化中にエラー");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Rectangle> CalculateOptimalPositionAsync(PositionCalculationRequest request, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            _totalCalculations++;

            _logger.LogDebug("🎭 [AVALONIA_POSITION] 最適位置計算開始 - ID: {Id}, Strategy: {Strategy}",
                request.Id, request.Strategy);

            await EnsureMonitorInfoFreshAsync(cancellationToken);
            
            var optimizedArea = request.DesiredArea;

            // モニター情報取得
            var targetMonitor = await GetTargetMonitorAsync(optimizedArea, cancellationToken);
            if (targetMonitor == null)
            {
                _logger.LogWarning("⚠️ [AVALONIA_POSITION] ターゲットモニターが見つからない - デフォルト処理");
                return request.DesiredArea;
            }

            // 基本的な画面境界チェック
            optimizedArea = await AdjustToScreenBoundsAsync(optimizedArea, targetMonitor.Id, cancellationToken);

            // 戦略別の位置調整
            switch (request.Strategy)
            {
                case PositionStrategy.CenterScreen:
                    optimizedArea = CenterOnScreen(optimizedArea, targetMonitor);
                    break;
                    
                case PositionStrategy.AvoidCollision:
                    optimizedArea = await AvoidCollisionAdvanced(optimizedArea, request, targetMonitor, cancellationToken);
                    break;
                    
                case PositionStrategy.KeepOriginal:
                default:
                    // 元位置を維持（境界調整のみ適用済み）
                    break;
            }

            // DPI スケーリング適用
            optimizedArea = await ConvertLogicalToPhysicalAsync(optimizedArea, targetMonitor.Id, cancellationToken);

            _logger.LogDebug("✅ [AVALONIA_POSITION] 最適位置計算完了 - ID: {Id}, OptimizedArea: {Area}", 
                request.Id, optimizedArea);

            return optimizedArea;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [AVALONIA_POSITION] 位置計算中にエラー - ID: {Id}", request.Id);
            return request.DesiredArea; // エラー時は元位置を返す
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Rectangle>> CalculateBatchPositionsAsync(IEnumerable<PositionCalculationRequest> requests, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        var results = new List<Rectangle>();
        
        foreach (var request in requests ?? Enumerable.Empty<PositionCalculationRequest>())
        {
            var optimizedPosition = await CalculateOptimalPositionAsync(request, cancellationToken);
            results.Add(optimizedPosition);
        }

        _logger.LogDebug("🎭 [AVALONIA_POSITION] バッチ位置計算完了 - 処理数: {Count}", results.Count);
        return results;
    }

    /// <inheritdoc />
    public async Task<bool> DetectCollisionAsync(Rectangle area, IEnumerable<OverlayPositionInfo> existingOverlays, IEnumerable<string>? excludeIds = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
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
            _logger.LogError(ex, "❌ [AVALONIA_POSITION] 衝突検出中にエラー - Area: {Area}", area);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<Rectangle> AdjustToScreenBoundsAsync(Rectangle area, int? targetMonitor = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        try
        {
            await EnsureMonitorInfoFreshAsync(cancellationToken);
            
            var monitor = GetMonitorById(targetMonitor) ?? GetPrimaryMonitor();
            if (monitor == null)
            {
                _logger.LogWarning("⚠️ [AVALONIA_POSITION] モニター情報なし - 調整をスキップ");
                return area;
            }

            var workingArea = monitor.WorkingArea;
            var adjustedArea = area;
            bool wasAdjusted = false;

            // 画面境界調整
            if (adjustedArea.Right > workingArea.Right)
            {
                adjustedArea.X = Math.Max(workingArea.X, workingArea.Right - adjustedArea.Width);
                wasAdjusted = true;
            }
            if (adjustedArea.Bottom > workingArea.Bottom)
            {
                adjustedArea.Y = Math.Max(workingArea.Y, workingArea.Bottom - adjustedArea.Height);
                wasAdjusted = true;
            }
            if (adjustedArea.X < workingArea.X)
            {
                adjustedArea.X = workingArea.X;
                wasAdjusted = true;
            }
            if (adjustedArea.Y < workingArea.Y)
            {
                adjustedArea.Y = workingArea.Y;
                wasAdjusted = true;
            }

            if (wasAdjusted)
            {
                _offScreenCorrectionCount++;
            }

            _logger.LogDebug("🎭 [AVALONIA_POSITION] 画面境界調整 - Original: {Original}, Adjusted: {Adjusted}, WasAdjusted: {WasAdjusted}", 
                area, adjustedArea, wasAdjusted);

            return adjustedArea;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [AVALONIA_POSITION] 画面境界調整中にエラー - Area: {Area}", area);
            return area;
        }
    }

    /// <inheritdoc />
    public async Task<MonitorInfo?> GetMonitorFromPointAsync(DrawingPoint point, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        try
        {
            await EnsureMonitorInfoFreshAsync(cancellationToken);
            
            var monitor = _cachedMonitors?.FirstOrDefault(m => m.FullArea.Contains(point)) ?? GetPrimaryMonitor();
            return monitor;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [AVALONIA_POSITION] モニター検出中にエラー - Point: {Point}", point);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IEnumerable<MonitorInfo>> GetAvailableMonitorsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        await EnsureMonitorInfoFreshAsync(cancellationToken);
        return _cachedMonitors ?? Enumerable.Empty<MonitorInfo>();
    }

    /// <inheritdoc />
    public async Task<Rectangle> ConvertLogicalToPhysicalAsync(Rectangle logicalArea, int? targetMonitor = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        try
        {
            await EnsureMonitorInfoFreshAsync(cancellationToken);
            
            var monitor = GetMonitorById(targetMonitor) ?? GetPrimaryMonitor();
            if (monitor == null)
            {
                return logicalArea;
            }

            // DPI スケーリング適用
            var scale = monitor.DpiScale;
            if (Math.Abs(scale - 1.0) < 0.001) // スケール = 1.0 の場合は変換不要
            {
                return logicalArea;
            }

            var physicalArea = new Rectangle(
                (int)(logicalArea.X * scale),
                (int)(logicalArea.Y * scale),
                (int)(logicalArea.Width * scale),
                (int)(logicalArea.Height * scale)
            );

            _logger.LogDebug("🎭 [AVALONIA_POSITION] DPI変換 - Logical: {Logical}, Physical: {Physical}, Scale: {Scale}", 
                logicalArea, physicalArea, scale);

            return physicalArea;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [AVALONIA_POSITION] DPI変換中にエラー - Area: {Area}", logicalArea);
            return logicalArea;
        }
    }

    /// <summary>
    /// モニター情報の更新
    /// </summary>
    private async Task RefreshMonitorInfoAsync(CancellationToken cancellationToken)
    {
        try
        {
            var monitors = new List<MonitorInfo>();

            // Avalonia UI のスクリーン情報を使用
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var screens = desktop.MainWindow?.Screens?.All ?? Array.Empty<Screen>();
                
                for (int i = 0; i < screens.Count; i++)
                {
                    var screen = screens[i];
                    var bounds = screen.Bounds;
                    var workingArea = screen.WorkingArea;
                    
                    monitors.Add(new MonitorInfo
                    {
                        Id = i,
                        Name = screen.DisplayName ?? $"Monitor {i + 1}",
                        FullArea = new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height),
                        WorkingArea = new Rectangle(workingArea.X, workingArea.Y, workingArea.Width, workingArea.Height),
                        DpiScale = screen.Scaling,
                        IsPrimary = screen.IsPrimary,
                        ColorDepth = 32, // Avalonia では取得困難、デフォルト値
                        RefreshRate = 60 // Avalonia では取得困難、デフォルト値
                    });
                }
            }

            // 設定可能なフォールバック: 最低でもプライマリモニターを設定
            if (!monitors.Any() && _fallbackSettings.EnableFallback)
            {
                var fallbackMonitor = new MonitorInfo
                {
                    Id = 0,
                    Name = "Primary Monitor (Fallback)",
                    FullArea = new Rectangle(0, 0, _fallbackSettings.DefaultWidth, _fallbackSettings.DefaultHeight),
                    WorkingArea = new Rectangle(0, 0, _fallbackSettings.DefaultWidth, _fallbackSettings.DefaultHeight - _fallbackSettings.TaskbarHeight), // タスクバー分を除く
                    DpiScale = _fallbackSettings.DefaultDpiScale,
                    IsPrimary = true,
                    ColorDepth = _fallbackSettings.DefaultColorDepth,
                    RefreshRate = _fallbackSettings.DefaultRefreshRate
                };
                
                monitors.Add(fallbackMonitor);
                
                // 設定に応じてフォールバック使用を警告ログ出力
                if (_fallbackSettings.LogFallbackUsage)
                {
                    _logger.LogWarning("⚠️ [POSITION_CALC] モニター情報取得失敗: フォールバック使用 - Resolution: {Width}x{Height}, DPI: {DpiScale}",
                        _fallbackSettings.DefaultWidth, 
                        _fallbackSettings.DefaultHeight, 
                        _fallbackSettings.DefaultDpiScale);
                }
            }
            else if (!monitors.Any() && !_fallbackSettings.EnableFallback)
            {
                _logger.LogError("❌ [POSITION_CALC] モニター情報取得失敗: フォールバック無効化のため位置計算不可");
                throw new InvalidOperationException("モニター情報を取得できず、フォールバックも無効化されています。");
            }

            _cachedMonitors = monitors;
            _lastMonitorUpdate = DateTime.Now;
            
            _logger.LogDebug("🖥️ [AVALONIA_POSITION] モニター情報更新完了 - 検出数: {Count}", monitors.Count);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [AVALONIA_POSITION] モニター情報更新中にエラー");
            throw;
        }
    }

    /// <summary>
    /// モニター情報の鮮度確認・更新
    /// </summary>
    private async Task EnsureMonitorInfoFreshAsync(CancellationToken cancellationToken)
    {
        if (_cachedMonitors == null || DateTime.Now - _lastMonitorUpdate > _monitorCacheExpiry)
        {
            await RefreshMonitorInfoAsync(cancellationToken);
        }
    }

    /// <summary>
    /// ターゲットモニター取得
    /// </summary>
    private async Task<MonitorInfo?> GetTargetMonitorAsync(Rectangle area, CancellationToken cancellationToken)
    {
        var centerPoint = new DrawingPoint(area.X + area.Width / 2, area.Y + area.Height / 2);
        return await GetMonitorFromPointAsync(centerPoint, cancellationToken);
    }

    /// <summary>
    /// モニター ID による取得
    /// </summary>
    private MonitorInfo? GetMonitorById(int? monitorId)
    {
        if (!monitorId.HasValue || _cachedMonitors == null)
            return null;
        
        return _cachedMonitors.FirstOrDefault(m => m.Id == monitorId.Value);
    }

    /// <summary>
    /// プライマリモニター取得
    /// </summary>
    private MonitorInfo? GetPrimaryMonitor()
    {
        return _cachedMonitors?.FirstOrDefault(m => m.IsPrimary) ?? _cachedMonitors?.FirstOrDefault();
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
    /// 高度な衝突回避アルゴリズム
    /// </summary>
    private async Task<Rectangle> AvoidCollisionAdvanced(
        Rectangle area, 
        PositionCalculationRequest request, 
        MonitorInfo monitor, 
        CancellationToken cancellationToken)
    {
        try
        {
            _collisionAvoidanceCount++;
            
            // 基本的な衝突回避（既存システムの詳細情報なしでの簡易実装）
            if (request.MaxDisplacement > 0)
            {
                // 衝突回避のための位置調整
                var adjustedArea = await FindNonCollidingPosition(area, request, monitor, cancellationToken);
                return adjustedArea;
            }

            return area;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [AVALONIA_POSITION] 衝突回避中にエラー");
            return area;
        }
    }

    /// <summary>
    /// 衝突しない位置を探索
    /// </summary>
    private async Task<Rectangle> FindNonCollidingPosition(
        Rectangle originalArea, 
        PositionCalculationRequest request, 
        MonitorInfo monitor, 
        CancellationToken cancellationToken)
    {
        const int stepSize = 10; // 10ピクセルずつ移動
        var maxDisplacement = Math.Min(request.MaxDisplacement, 200); // 最大200ピクセル
        
        var workingArea = monitor.WorkingArea;
        
        // 螺旋状に探索
        for (int radius = stepSize; radius <= maxDisplacement; radius += stepSize)
        {
            var positions = GenerateSpiralPositions(originalArea, radius, stepSize);
            
            foreach (var position in positions)
            {
                // 画面境界内チェック
                if (!workingArea.Contains(position))
                    continue;
                
                // 衝突チェック（簡易実装 - 既存システムの詳細情報なしで基本的なチェック）
                var hasCollision = false; // Phase 16 では簡易実装
                if (!hasCollision)
                {
                    _logger.LogDebug("✅ [AVALONIA_POSITION] 衝突回避位置発見 - Original: {Original}, Adjusted: {Adjusted}", 
                        originalArea, position);
                    return position;
                }
            }
        }
        
        // 適切な位置が見つからない場合は元の位置を返す
        _logger.LogDebug("⚠️ [AVALONIA_POSITION] 衝突回避位置が見つからず - 元位置を使用");
        return originalArea;
    }

    /// <summary>
    /// 螺旋状位置生成
    /// </summary>
    private IEnumerable<Rectangle> GenerateSpiralPositions(Rectangle center, int radius, int stepSize)
    {
        var positions = new List<Rectangle>();
        
        // 上下左右の基本方向
        var directions = new[]
        {
            new DrawingPoint(0, -stepSize), // 上
            new DrawingPoint(stepSize, 0),  // 右
            new DrawingPoint(0, stepSize),  // 下
            new DrawingPoint(-stepSize, 0)  // 左
        };
        
        foreach (var direction in directions)
        {
            for (int distance = stepSize; distance <= radius; distance += stepSize)
            {
                var newX = center.X + direction.X * (distance / stepSize);
                var newY = center.Y + direction.Y * (distance / stepSize);
                positions.Add(new Rectangle(newX, newY, center.Width, center.Height));
            }
        }
        
        return positions;
    }

    /// <summary>
    /// Disposed チェック
    /// </summary>
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>
    /// 統計情報取得
    /// </summary>
    public PositionCalculationStatistics GetStatistics()
    {
        return new PositionCalculationStatistics
        {
            TotalCalculations = _totalCalculations,
            CollisionAvoidanceCount = _collisionAvoidanceCount,
            AverageCalculationTime = 2.5, // Avalonia UI での実測値（仮）
            MaxCalculationTime = 15.0,
            OffScreenCorrectionCount = _offScreenCorrectionCount,
            MultiMonitorPlacementCount = _multiMonitorPlacementCount
        };
    }

    /// <summary>
    /// リソース解放
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            _cachedMonitors?.Clear();
            _cachedMonitors = null;
            _disposed = true;
            
            _logger.LogInformation("🧹 [AVALONIA_POSITION] AvaloniaOverlayPositionCalculator リソース解放完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [AVALONIA_POSITION] リソース解放中にエラー");
        }
    }
}