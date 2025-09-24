using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Baketa.Core.UI.Monitors;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Services.Monitor;

/// <summary>
/// 高度なモニター情報管理・DPI補正サービス実装
/// Phase 1: Avalonia Screen API優先活用による安全な基盤システム
/// Gemini推奨: Win32直接呼び出し最小化、フレームワーク協調重視
/// </summary>
public sealed class AdvancedMonitorService : IAdvancedMonitorService
{
    private readonly ILogger<AdvancedMonitorService> _logger;

    // DPI情報キャッシュシステム（Gemini推奨：パフォーマンス最適化）
    private readonly ConcurrentDictionary<string, AdvancedDpiInfo> _dpiCache = new();
    private readonly object _cacheLock = new();

    /// <inheritdoc />
    public event EventHandler<MonitorConfigurationChangedEventArgs>? MonitorConfigurationChanged;

    public AdvancedMonitorService(ILogger<AdvancedMonitorService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogInformation("🖥️ [ADVANCED_MONITOR] AdvancedMonitorService初期化 - Avalonia Screen API優先戦略");

        // システムDPI変更監視（Windows 10 1903+対応）
        InitializeDpiChangeMonitoring();
    }

    /// <inheritdoc />
    public MonitorType DetectMonitorType(MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        var width = monitor.Bounds.Width;
        var height = monitor.Bounds.Height;

        // Avalonia Screen APIからDPI情報取得
        var avaloniaScreen = GetAvaloniaScreenForMonitor(monitor);
        var dpiScaling = avaloniaScreen?.Scaling ?? 1.0;

        _logger.LogDebug("🖥️ [MONITOR_DETECTION] モニター判定 - Resolution: {Width}x{Height}, DPI: {DpiScaling}",
            width, height, dpiScaling);

        // 解像度×DPI組み合わせでモニター種別判定
        return (width, height, dpiScaling) switch
        {
            // フルHD系
            (1920, 1080, <= 1.1) => MonitorType.FullHD_100DPI,
            (1920, 1080, <= 1.35) => MonitorType.FullHD_125DPI,

            // ウルトラワイド系（現在環境）
            (2560, 1080, <= 1.1) => MonitorType.UltraWide_100DPI,
            (2560, 1080, <= 1.35) => MonitorType.UltraWide_125DPI,

            // 4K系
            (3840, 2160, <= 1.6) => MonitorType.FourK_150DPI,
            (3840, 2160, <= 1.85) => MonitorType.FourK_175DPI,
            (3840, 2160, <= 2.1) => MonitorType.FourK_200DPI,

            // その他
            _ => MonitorType.Custom
        };
    }

    /// <inheritdoc />
    public AdvancedDpiInfo GetAdvancedDpiInfo(MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        var cacheKey = GetMonitorCacheKey(monitor);

        // キャッシュから取得を試行
        if (_dpiCache.TryGetValue(cacheKey, out var cachedInfo))
        {
            _logger.LogTrace("📦 [DPI_CACHE] キャッシュヒット - Monitor: {MonitorName}", monitor.Name);
            return cachedInfo;
        }

        // キャッシュミス：新規計算
        lock (_cacheLock)
        {
            // ダブルチェックロッキング
            if (_dpiCache.TryGetValue(cacheKey, out cachedInfo))
                return cachedInfo;

            var dpiInfo = CalculateAdvancedDpiInfo(monitor);
            _dpiCache[cacheKey] = dpiInfo;

            _logger.LogInformation("🆕 [DPI_CALCULATION] 新規DPI情報計算 - {MonitorType}, Scaling: {AvaloniaScaling}, Compensation: {CompensationFactor}",
                dpiInfo.MonitorType, dpiInfo.AvaloniaScaling, dpiInfo.CompensationFactor);

            return dpiInfo;
        }
    }

    /// <inheritdoc />
    public System.Drawing.Point CompensateCoordinatesForAvalonia(System.Drawing.Point logicalCoordinates, AdvancedDpiInfo dpiInfo)
    {
        ArgumentNullException.ThrowIfNull(dpiInfo);

        if (!dpiInfo.RequiresAvaloniaCompensation)
        {
            _logger.LogTrace("🎯 [COORDINATE_COMPENSATION] 補正不要 - MonitorType: {MonitorType}", dpiInfo.MonitorType);
            return logicalCoordinates;
        }

        // Avalonia二重スケーリング打ち消し処理
        var compensatedX = (int)Math.Round(logicalCoordinates.X * dpiInfo.CompensationFactor);
        var compensatedY = (int)Math.Round(logicalCoordinates.Y * dpiInfo.CompensationFactor);

        var compensatedCoordinates = new System.Drawing.Point(compensatedX, compensatedY);

        _logger.LogDebug("🎯 [COORDINATE_COMPENSATION] 座標補正実施 - Original: ({OriginalX},{OriginalY}) → Compensated: ({CompensatedX},{CompensatedY}), Factor: {Factor}",
            logicalCoordinates.X, logicalCoordinates.Y, compensatedX, compensatedY, dpiInfo.CompensationFactor);

        return compensatedCoordinates;
    }

    /// <inheritdoc />
    public System.Drawing.Size CompensateSize(System.Drawing.Size logicalSize, AdvancedDpiInfo dpiInfo)
    {
        ArgumentNullException.ThrowIfNull(dpiInfo);

        if (!dpiInfo.RequiresAvaloniaCompensation)
            return logicalSize;

        var compensatedWidth = (int)Math.Round(logicalSize.Width * dpiInfo.CompensationFactor);
        var compensatedHeight = (int)Math.Round(logicalSize.Height * dpiInfo.CompensationFactor);

        return new System.Drawing.Size(compensatedWidth, compensatedHeight);
    }

    /// <summary>
    /// Avalonia Screen APIからモニター情報取得
    /// Gemini推奨: Avalonia native API優先使用
    /// </summary>
    private Screen? GetAvaloniaScreenForMonitor(MonitorInfo monitor)
    {
        try
        {
            // TODO: [PHASE1_AVALONIA_SCREEN] Avalonia Screen API統合 (現在は無効化)
            IReadOnlyList<Screen>? screens = null;
            if (screens == null) return null;

            // モニター境界との一致でスクリーンを特定
            return screens.FirstOrDefault(screen =>
                Math.Abs(screen.Bounds.X - monitor.Bounds.X) < 10 &&
                Math.Abs(screen.Bounds.Y - monitor.Bounds.Y) < 10 &&
                Math.Abs(screen.Bounds.Width - monitor.Bounds.Width) < 10 &&
                Math.Abs(screen.Bounds.Height - monitor.Bounds.Height) < 10);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [AVALONIA_SCREEN] Avaloniaスクリーン取得失敗 - Monitor: {MonitorName}", monitor.Name);
            return null;
        }
    }

    /// <summary>
    /// 高度DPI情報計算
    /// </summary>
    private AdvancedDpiInfo CalculateAdvancedDpiInfo(MonitorInfo monitor)
    {
        var monitorType = DetectMonitorType(monitor);
        var avaloniaScreen = GetAvaloniaScreenForMonitor(monitor);

        var avaloniaScaling = avaloniaScreen?.Scaling ?? 1.0;
        var systemDpiScaling = monitor.ScaleFactorX; // 既存システムから取得

        // Avalonia補正が必要かどうかの判定
        var requiresCompensation = ShouldApplyAvaloniaCompensation(monitorType, avaloniaScaling);

        // 補正係数計算（二重スケーリング打ち消し）
        var compensationFactor = requiresCompensation
            ? CalculateCompensationFactor(avaloniaScaling, systemDpiScaling)
            : 1.0;

        return new AdvancedDpiInfo
        {
            MonitorType = monitorType,
            AvaloniaScaling = avaloniaScaling,
            SystemDpiScaling = systemDpiScaling,
            RequiresAvaloniaCompensation = requiresCompensation,
            CompensationFactor = compensationFactor,
            PhysicalResolution = new System.Drawing.Size(
                (int)(monitor.Bounds.Width * systemDpiScaling),
                (int)(monitor.Bounds.Height * systemDpiScaling)),
            LogicalResolution = new System.Drawing.Size((int)monitor.Bounds.Width, (int)monitor.Bounds.Height)
        };
    }

    /// <summary>
    /// Avalonia補正必要判定
    /// モニター種別に基づく補正要否の決定
    /// </summary>
    private static bool ShouldApplyAvaloniaCompensation(MonitorType monitorType, double avaloniaScaling)
    {
        // 現在問題が確認されている環境では補正を適用
        // 他環境は将来のテスト結果に基づいて調整
        return monitorType switch
        {
            MonitorType.UltraWide_100DPI => true,  // 現在の問題環境
            MonitorType.FourK_150DPI => true,      // 高DPI環境では補正が必要になる可能性が高い
            MonitorType.FourK_175DPI => true,
            MonitorType.FourK_200DPI => true,
            _ => avaloniaScaling > 1.1 // その他は高DPIの場合のみ補正
        };
    }

    /// <summary>
    /// 補正係数計算
    /// Avalonia二重スケーリング打ち消し用
    /// </summary>
    private static double CalculateCompensationFactor(double avaloniaScaling, double systemDpiScaling)
    {
        // 基本的にはAvalonia内部スケーリングを打ち消す逆数
        // ただし、システムDPIとの関係も考慮
        if (Math.Abs(avaloniaScaling - systemDpiScaling) < 0.01)
        {
            // AvaloniaとシステムDPIが一致する場合：単純な逆数
            return 1.0 / avaloniaScaling;
        }
        else
        {
            // 不一致の場合：より複雑な計算（将来調整予定）
            return 1.0 / avaloniaScaling;
        }
    }

    /// <summary>
    /// モニターキャッシュキー生成
    /// </summary>
    private static string GetMonitorCacheKey(MonitorInfo monitor)
    {
        return $"{monitor.Name}_{monitor.Bounds.Width}x{monitor.Bounds.Height}_{monitor.ScaleFactorX:F2}";
    }

    /// <summary>
    /// システムDPI変更監視初期化
    /// </summary>
    private void InitializeDpiChangeMonitoring()
    {
        try
        {
            // 将来実装：システムDPI変更イベントの監視
            // Windows 10 1903+ の動的DPI変更対応
            _logger.LogDebug("🔍 [DPI_MONITORING] DPI変更監視初期化（将来実装）");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [DPI_MONITORING] DPI変更監視初期化失敗");
        }
    }

    /// <summary>
    /// キャッシュクリア（テスト・デバッグ用）
    /// </summary>
    public void ClearDpiCache()
    {
        lock (_cacheLock)
        {
            var cachedCount = _dpiCache.Count;
            _dpiCache.Clear();
            _logger.LogInformation("🧹 [DPI_CACHE] キャッシュクリア - 削除済みエントリ: {Count}", cachedCount);
        }
    }
}