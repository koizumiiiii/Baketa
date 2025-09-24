using System;
using System.Drawing;
using Baketa.Core.UI.Monitors;

namespace Baketa.UI.Services.Monitor;

/// <summary>
/// 高度なモニター情報管理・DPI補正サービス
/// Phase 1: Avalonia Screen API優先活用による安全な基盤システム
/// </summary>
public interface IAdvancedMonitorService
{
    /// <summary>
    /// モニター種別を判定
    /// </summary>
    MonitorType DetectMonitorType(MonitorInfo monitor);

    /// <summary>
    /// Avalonia DPI補正情報を取得
    /// </summary>
    AdvancedDpiInfo GetAdvancedDpiInfo(MonitorInfo monitor);

    /// <summary>
    /// 座標のAvalonia DPI補正を実行
    /// </summary>
    System.Drawing.Point CompensateCoordinatesForAvalonia(System.Drawing.Point logicalCoordinates, AdvancedDpiInfo dpiInfo);

    /// <summary>
    /// サイズのAvalonia DPI補正を実行
    /// </summary>
    System.Drawing.Size CompensateSize(System.Drawing.Size logicalSize, AdvancedDpiInfo dpiInfo);

    /// <summary>
    /// モニター構成変更イベント通知
    /// </summary>
    event EventHandler<MonitorConfigurationChangedEventArgs> MonitorConfigurationChanged;
}

/// <summary>
/// モニター種別定義
/// 解像度×DPI組み合わせの体系的分類
/// </summary>
public enum MonitorType
{
    /// <summary>フルHD 100% DPI (1920×1080, DPI=1.0)</summary>
    FullHD_100DPI,

    /// <summary>フルHD 125% DPI (1920×1080, DPI=1.25)</summary>
    FullHD_125DPI,

    /// <summary>ウルトラワイド 100% DPI (2560×1080, DPI=1.0) - 現在環境</summary>
    UltraWide_100DPI,

    /// <summary>ウルトラワイド 125% DPI (2560×1080, DPI=1.25)</summary>
    UltraWide_125DPI,

    /// <summary>4K 150% DPI (3840×2160, DPI=1.5)</summary>
    FourK_150DPI,

    /// <summary>4K 175% DPI (3840×2160, DPI=1.75)</summary>
    FourK_175DPI,

    /// <summary>4K 200% DPI (3840×2160, DPI=2.0)</summary>
    FourK_200DPI,

    /// <summary>その他の解像度・DPI組み合わせ</summary>
    Custom
}

/// <summary>
/// 高度DPI情報
/// Avalonia内部DPI処理との協調用
/// </summary>
public sealed class AdvancedDpiInfo
{
    /// <summary>モニター種別</summary>
    public required MonitorType MonitorType { get; init; }

    /// <summary>Avaloniaスケーリング係数 (Screen.Scaling)</summary>
    public required double AvaloniaScaling { get; init; }

    /// <summary>システムDPIスケーリング係数</summary>
    public required double SystemDpiScaling { get; init; }

    /// <summary>Avalonia DPI補正が必要かどうか</summary>
    public required bool RequiresAvaloniaCompensation { get; init; }

    /// <summary>補正係数（Avalonia二重スケーリング打ち消し用）</summary>
    public required double CompensationFactor { get; init; }

    /// <summary>物理解像度</summary>
    public required System.Drawing.Size PhysicalResolution { get; init; }

    /// <summary>論理解像度</summary>
    public required System.Drawing.Size LogicalResolution { get; init; }
}

/// <summary>
/// モニター構成変更イベント引数
/// </summary>
public sealed class MonitorConfigurationChangedEventArgs : EventArgs
{
    /// <summary>変更されたモニター</summary>
    public required MonitorInfo ChangedMonitor { get; init; }

    /// <summary>変更タイプ</summary>
    public required MonitorChangeType ChangeType { get; init; }

    /// <summary>変更前のDPI情報（更新・削除時のみ）</summary>
    public AdvancedDpiInfo? PreviousDpiInfo { get; init; }

    /// <summary>変更後のDPI情報（追加・更新時のみ）</summary>
    public AdvancedDpiInfo? NewDpiInfo { get; init; }
}

/// <summary>
/// モニター変更タイプ
/// </summary>
public enum MonitorChangeType
{
    /// <summary>モニター追加</summary>
    Added,

    /// <summary>モニター削除</summary>
    Removed,

    /// <summary>DPI設定変更</summary>
    DpiChanged,

    /// <summary>解像度変更</summary>
    ResolutionChanged
}