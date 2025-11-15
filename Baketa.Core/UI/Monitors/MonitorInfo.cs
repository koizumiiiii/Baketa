using Baketa.Core.UI.Geometry;
using CorePoint = Baketa.Core.UI.Geometry.Point;
using CoreRect = Baketa.Core.UI.Geometry.Rect;

namespace Baketa.Core.UI.Monitors;

/// <summary>
/// モニター情報を表すレコード（不変データ）
/// DPI詳細対応とマルチモニター機能のサポート
/// </summary>
/// <param name="Handle">モニターハンドル</param>
/// <param name="Name">モニター名</param>
/// <param name="DeviceId">デバイス一意識別子</param>
/// <param name="Bounds">モニターのスクリーン領域</param>
/// <param name="WorkArea">モニターの作業領域（タスクバー等を除く）</param>
/// <param name="IsPrimary">プライマリモニターかどうか</param>
/// <param name="DpiX">水平DPI</param>
/// <param name="DpiY">垂直DPI</param>
public readonly record struct MonitorInfo(
    nint Handle,
    string Name,
    string DeviceId,
    CoreRect Bounds,
    CoreRect WorkArea,
    bool IsPrimary,
    double DpiX,
    double DpiY)
{
    /// <summary>
    /// 水平スケールファクター（Avalonia UI Screens.Scalingとの整合性確保）
    /// 96 DPIを基準とした倍率
    /// </summary>
    public double ScaleFactorX => DpiX / 96.0;

    /// <summary>
    /// 垂直スケールファクター（X軸Y軸で異なるDPI設定対応）
    /// 96 DPIを基準とした倍率  
    /// </summary>
    public double ScaleFactorY => DpiY / 96.0;

    /// <summary>
    /// 指定したウィンドウがこのモニターにどの程度含まれるかを計算
    /// 複数モニターにまたがる場合のアクティブモニター判定に使用
    /// </summary>
    /// <param name="windowRect">ウィンドウの矩形</param>
    /// <returns>含まれる面積（0.0～1.0）</returns>
    public double CalculateOverlapRatio(CoreRect windowRect)
    {
        var intersection = Bounds.Intersect(windowRect);
        if (intersection.IsEmpty)
            return 0.0;

        var windowArea = windowRect.Width * windowRect.Height;
        if (windowArea <= 0)
            return 0.0;

        var overlapArea = intersection.Width * intersection.Height;
        return overlapArea / windowArea;
    }

    /// <summary>
    /// 物理ピクセル座標をDPI非依存論理座標に変換
    /// </summary>
    /// <param name="physicalPoint">物理ピクセル座標</param>
    /// <returns>論理座標</returns>
    public CorePoint PhysicalToLogical(CorePoint physicalPoint) => new(
        physicalPoint.X / ScaleFactorX,
        physicalPoint.Y / ScaleFactorY);

    /// <summary>
    /// DPI非依存論理座標を物理ピクセル座標に変換
    /// </summary>
    /// <param name="logicalPoint">論理座標</param>
    /// <returns>物理ピクセル座標</returns>
    public CorePoint LogicalToPhysical(CorePoint logicalPoint) => new(
        logicalPoint.X * ScaleFactorX,
        logicalPoint.Y * ScaleFactorY);

    /// <summary>
    /// 座標がこのモニター範囲内に含まれるかチェック
    /// </summary>
    /// <param name="point">チェックする座標</param>
    /// <returns>含まれる場合true</returns>
    public bool Contains(CorePoint point) => Bounds.Contains(point);

    /// <summary>
    /// 表示名を取得（デバッグ・ログ用）
    /// </summary>
    /// <returns>モニター情報の文字列表現</returns>
    public override string ToString() =>
        $"{Name} ({Bounds.Width}x{Bounds.Height}, DPI: {DpiX:F0}x{DpiY:F0}{(IsPrimary ? ", Primary" : "")})";
}
