namespace Baketa.Core.UI.Monitors;

/// <summary>
/// モニター変更イベント引数（不変データ）
/// マルチモニター環境での変更通知に使用
/// </summary>
/// <param name="ChangeType">変更タイプ</param>
/// <param name="AffectedMonitor">追加/削除/変更されたモニター情報（該当しない場合はnull）</param>
/// <param name="Monitors">更新後の全モニターリスト</param>
public readonly record struct MonitorChangedEventArgs(
    MonitorChangeType ChangeType,
    MonitorInfo? AffectedMonitor,
    IReadOnlyList<MonitorInfo> Monitors)
{
    /// <summary>
    /// プライマリモニターを取得
    /// </summary>
    public MonitorInfo? PrimaryMonitor
    {
        get
        {
            var primary = Monitors.FirstOrDefault(m => m.IsPrimary);
            return primary.Handle != nint.Zero ? primary : null;
        }
    }
    
    /// <summary>
    /// アクティブなモニター数
    /// </summary>
    public int MonitorCount => Monitors.Count;
    
    /// <summary>
    /// 指定されたハンドルのモニターを検索
    /// </summary>
    /// <param name="handle">モニターハンドル</param>
    /// <returns>見つかったモニター情報、見つからない場合はnull</returns>
    public MonitorInfo? FindMonitorByHandle(nint handle)
    {
        var monitor = Monitors.FirstOrDefault(m => m.Handle == handle);
        return monitor.Handle != nint.Zero ? monitor : null;
    }
    
    /// <summary>
    /// 指定されたデバイスIDのモニターを検索
    /// </summary>
    /// <param name="deviceId">デバイス識別子</param>
    /// <returns>見つかったモニター情報、見つからない場合はnull</returns>
    public MonitorInfo? FindMonitorByDeviceId(string deviceId)
    {
        var monitor = Monitors.FirstOrDefault(m => m.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase));
        return monitor.Handle != nint.Zero ? monitor : null;
    }
    
    /// <summary>
    /// イベント概要の文字列表現
    /// </summary>
    /// <returns>変更内容の要約</returns>
    public override string ToString() => ChangeType switch
    {
        MonitorChangeType.Added => $"Monitor Added: {AffectedMonitor?.Name ?? "Unknown"} (Total: {MonitorCount})",
        MonitorChangeType.Removed => $"Monitor Removed: {AffectedMonitor?.Name ?? "Unknown"} (Total: {MonitorCount})",
        MonitorChangeType.Changed => $"Monitor Changed: {AffectedMonitor?.Name ?? "Unknown"}",
        MonitorChangeType.PrimaryChanged => $"Primary Monitor Changed to: {PrimaryMonitor?.Name ?? "Unknown"}",
        MonitorChangeType.RefreshAll => $"All Monitors Refreshed (Total: {MonitorCount})",
        _ => $"Unknown Change Type: {ChangeType}"
    };
}
