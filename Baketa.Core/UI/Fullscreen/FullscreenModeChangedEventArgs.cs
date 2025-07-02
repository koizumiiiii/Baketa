using Baketa.Core.UI.Monitors;

namespace Baketa.Core.UI.Fullscreen;

/// <summary>
/// フルスクリーンモード変更イベント引数
/// </summary>
/// <param name="IsExclusiveFullscreen">排他的フルスクリーンモードかどうか</param>
/// <param name="IsBorderlessFullscreen">ボーダレスフルスクリーンモードかどうか</param>
/// <param name="CanShowOverlay">オーバーレイ表示可能かどうか</param>
/// <param name="RecommendationMessage">ユーザーへの推奨メッセージ</param>
/// <param name="AffectedMonitor">対象のモニター</param>
public readonly record struct FullscreenModeChangedEventArgs(
    bool IsExclusiveFullscreen,
    bool IsBorderlessFullscreen,
    bool CanShowOverlay,
    string RecommendationMessage,
    MonitorInfo? AffectedMonitor)
{
    /// <summary>
    /// 検出実行時刻
    /// </summary>
    public DateTime DetectionTime { get; init; } = DateTime.UtcNow;
    /// <summary>
    /// フルスクリーンモードの種別
    /// </summary>
    public FullscreenModeType ModeType => (IsExclusiveFullscreen, IsBorderlessFullscreen) switch
    {
        (true, false) => FullscreenModeType.Exclusive,
        (false, true) => FullscreenModeType.Borderless,
        (false, false) => FullscreenModeType.Windowed,
        (true, true) => FullscreenModeType.Unknown // 通常はありえない組み合わせ
    };
    
    /// <summary>
    /// ユーザーアクションが必要かどうか
    /// </summary>
    public bool RequiresUserAction => IsExclusiveFullscreen && !string.IsNullOrEmpty(RecommendationMessage);
    
    /// <summary>
    /// イベント概要の文字列表現
    /// </summary>
    public override string ToString() => ModeType switch
    {
        FullscreenModeType.Exclusive => $"Exclusive Fullscreen (Overlay: {(CanShowOverlay ? "可能" : "不可")})",
        FullscreenModeType.Borderless => $"Borderless Fullscreen (Overlay: 可能)",
        FullscreenModeType.Windowed => "Windowed Mode (Overlay: 可能)",
        FullscreenModeType.Unknown => "Unknown Fullscreen Mode",
        _ => "Invalid Mode"
    };
}

/// <summary>
/// フルスクリーンモードの種別
/// </summary>
public enum FullscreenModeType
{
    /// <summary>
    /// ウィンドウモード
    /// </summary>
    Windowed,
    
    /// <summary>
    /// ボーダレスフルスクリーンモード
    /// </summary>
    Borderless,
    
    /// <summary>
    /// 排他的フルスクリーンモード
    /// </summary>
    Exclusive,
    
    /// <summary>
    /// 不明なモード
    /// </summary>
    Unknown
}
