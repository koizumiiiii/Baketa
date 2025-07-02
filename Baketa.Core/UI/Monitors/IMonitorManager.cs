using Baketa.Core.UI.Geometry;
using CorePoint = Baketa.Core.UI.Geometry.Point;
using CoreRect = Baketa.Core.UI.Geometry.Rect;
using CoreSize = Baketa.Core.UI.Geometry.Size;

namespace Baketa.Core.UI.Monitors;

/// <summary>
/// モニターマネージャーインターフェース
/// マルチモニター環境の検出・管理・監視機能を提供
/// </summary>
public interface IMonitorManager : IDisposable
{
    /// <summary>
    /// 利用可能なモニターのコレクション（読み取り専用）
    /// </summary>
    IReadOnlyList<MonitorInfo> Monitors { get; }
    
    /// <summary>
    /// プライマリモニター情報
    /// </summary>
    MonitorInfo? PrimaryMonitor { get; }
    
    /// <summary>
    /// アクティブなモニター数
    /// </summary>
    int MonitorCount => Monitors.Count;
    
    /// <summary>
    /// モニター監視が開始されているかどうか
    /// </summary>
    bool IsMonitoring { get; }
    
    /// <summary>
    /// モニター設定変更イベント
    /// 接続・切断・設定変更・プライマリ変更を通知
    /// </summary>
    event EventHandler<MonitorChangedEventArgs>? MonitorChanged;
    
    /// <summary>
    /// ウィンドウが表示されているモニターを取得
    /// </summary>
    /// <param name="windowHandle">ウィンドウハンドル</param>
    /// <returns>モニター情報、見つからない場合はnull</returns>
    MonitorInfo? GetMonitorFromWindow(nint windowHandle);
    
    /// <summary>
    /// 座標が含まれるモニターを取得
    /// </summary>
    /// <param name="point">スクリーン座標</param>
    /// <returns>モニター情報、見つからない場合はnull</returns>
    MonitorInfo? GetMonitorFromPoint(CorePoint point);
    
    /// <summary>
    /// 矩形と重複するモニターを取得（重複面積順）
    /// </summary>
    /// <param name="rect">矩形</param>
    /// <returns>重複面積の大きい順のモニターリスト</returns>
    IReadOnlyList<MonitorInfo> GetMonitorsFromRect(CoreRect rect);
    
    /// <summary>
    /// 指定されたハンドルのモニターを取得
    /// </summary>
    /// <param name="handle">モニターハンドル</param>
    /// <returns>モニター情報、見つからない場合はnull</returns>
    MonitorInfo? GetMonitorByHandle(nint handle);
    
    /// <summary>
    /// モニター間で座標を変換（DPIスケール考慮）
    /// </summary>
    /// <param name="point">変換する座標</param>
    /// <param name="sourceMonitor">元のモニター</param>
    /// <param name="targetMonitor">対象のモニター</param>
    /// <returns>変換後の座標</returns>
    CorePoint TransformPointBetweenMonitors(
        CorePoint point, 
        MonitorInfo sourceMonitor, 
        MonitorInfo targetMonitor);
    
    /// <summary>
    /// モニター間で矩形を変換（DPIスケール考慮）
    /// </summary>
    /// <param name="rect">変換する矩形</param>
    /// <param name="sourceMonitor">元のモニター</param>
    /// <param name="targetMonitor">対象のモニター</param>
    /// <returns>変換後の矩形</returns>
    CoreRect TransformRectBetweenMonitors(
        CoreRect rect,
        MonitorInfo sourceMonitor,
        MonitorInfo targetMonitor);
    
    /// <summary>
    /// モニター情報を手動更新
    /// 通常は自動更新されるが、必要に応じて強制更新
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>更新タスク</returns>
    Task RefreshMonitorsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// モニター監視を開始
    /// システムイベントを監視してモニター変更を検出
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>監視開始タスク</returns>
    Task StartMonitoringAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// モニター監視を停止
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>監視停止タスク</returns>
    Task StopMonitoringAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// モニターマネージャーの拡張メソッド
/// 便利な操作を提供
/// </summary>
public static class MonitorManagerExtensions
{
    /// <summary>
    /// ウィンドウに最適なモニターを決定
    /// 1. ウィンドウ中心点を含むモニター優先
    /// 2. 複数モニターにまたがる場合は表示面積最大のモニター選択
    /// 3. 完全に画面外の場合はプライマリモニター使用
    /// </summary>
    /// <param name="manager">モニターマネージャー</param>
    /// <param name="windowRect">ウィンドウ矩形</param>
    /// <returns>最適なモニター</returns>
    public static MonitorInfo DetermineOptimalMonitor(
        this IMonitorManager manager, 
        CoreRect windowRect)
    {
        ArgumentNullException.ThrowIfNull(manager);
        
        // 1. ウィンドウ中心点を含むモニターをチェック
        var centerPoint = new CorePoint(
            windowRect.X + windowRect.Width / 2,
            windowRect.Y + windowRect.Height / 2);
            
        var centerMonitor = manager.GetMonitorFromPoint(centerPoint);
        if (centerMonitor.HasValue)
            return centerMonitor.Value;
        
        // 2. 表示面積が最大のモニターを検索
        var overlappingMonitors = manager.GetMonitorsFromRect(windowRect);
        if (overlappingMonitors.Count > 0)
        {
            var bestMonitor = overlappingMonitors
                .Select(monitor => new { Monitor = monitor, Overlap = monitor.CalculateOverlapRatio(windowRect) })
                .Where(x => x.Overlap > 0.1) // 10%以上の重複があれば有効
                .MaxBy(x => x.Overlap);
                
            if (bestMonitor is not null)
                return bestMonitor.Monitor;
        }
        
        // 3. 適切なモニターが見つからない場合はプライマリモニターを使用
        return manager.PrimaryMonitor ?? throw new InvalidOperationException("プライマリモニターが見つかりません");
    }
    
    /// <summary>
    /// ウィンドウハンドルから最適なモニターを決定
    /// </summary>
    /// <param name="manager">モニターマネージャー</param>
    /// <param name="windowHandle">ウィンドウハンドル</param>
    /// <returns>最適なモニター</returns>
    public static MonitorInfo DetermineOptimalMonitor(
        this IMonitorManager manager,
        nint windowHandle)
    {
        ArgumentNullException.ThrowIfNull(manager);
        
        // まずウィンドウから直接モニターを取得を試行
        var directMonitor = manager.GetMonitorFromWindow(windowHandle);
        if (directMonitor.HasValue)
            return directMonitor.Value;
        
        // 取得できない場合はプライマリモニターを返す
        return manager.PrimaryMonitor ?? throw new InvalidOperationException("プライマリモニターが見つかりません");
    }
    
    /// <summary>
    /// DPI変更が発生したかチェック
    /// </summary>
    /// <param name="oldMonitor">変更前のモニター</param>
    /// <param name="newMonitor">変更後のモニター</param>
    /// <param name="threshold">閾値（デフォルト: 0.01）</param>
    /// <returns>DPI変更があった場合true</returns>
    public static bool HasDpiChanged(
        MonitorInfo oldMonitor, 
        MonitorInfo newMonitor, 
        double threshold = 0.01) =>
        Math.Abs(oldMonitor.ScaleFactorX - newMonitor.ScaleFactorX) > threshold ||
        Math.Abs(oldMonitor.ScaleFactorY - newMonitor.ScaleFactorY) > threshold;
}
