using Baketa.Core.UI.Monitors;

namespace Baketa.Core.UI.Fullscreen;

/// <summary>
/// フルスクリーンモード検出・対応サービスインターフェース
/// 排他的フルスクリーンとボーダレスフルスクリーンの検出、オーバーレイ表示可能性の判定
/// </summary>
public interface IFullscreenModeService : IDisposable
{
    /// <summary>
    /// 排他的フルスクリーンモードかどうか
    /// DirectX/OpenGL等による排他的モード
    /// </summary>
    bool IsExclusiveFullscreen { get; }

    /// <summary>
    /// ボーダレスフルスクリーンモードかどうか
    /// ウィンドウモードでフルスクリーンサイズ
    /// </summary>
    bool IsBorderlessFullscreen { get; }

    /// <summary>
    /// オーバーレイ表示可能かどうか
    /// 排他的フルスクリーン以外では通常true
    /// </summary>
    bool CanShowOverlay { get; }

    /// <summary>
    /// 現在のフルスクリーンモード種別
    /// </summary>
    FullscreenModeType CurrentModeType { get; }

    /// <summary>
    /// 監視中のターゲットウィンドウハンドル
    /// </summary>
    nint TargetWindowHandle { get; }

    /// <summary>
    /// フルスクリーンモード変更イベント
    /// モード変更時に通知される
    /// </summary>
    event EventHandler<FullscreenModeChangedEventArgs>? FullscreenModeChanged;

    /// <summary>
    /// 指定されたウィンドウのフルスクリーンモードを検出
    /// </summary>
    /// <param name="windowHandle">ウィンドウハンドル</param>
    /// <param name="targetMonitor">対象モニター（省略可）</param>
    /// <returns>フルスクリーンモード情報</returns>
    FullscreenModeChangedEventArgs DetectFullscreenMode(nint windowHandle, MonitorInfo? targetMonitor = null);

    /// <summary>
    /// ターゲットウィンドウを設定してモード監視を開始
    /// </summary>
    /// <param name="windowHandle">監視対象ウィンドウハンドル</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>監視開始タスク</returns>
    Task StartMonitoringAsync(nint windowHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// モード監視を停止
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>監視停止タスク</returns>
    Task StopMonitoringAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// ユーザーへの推奨表示（ボーダレスフルスクリーン推奨等）
    /// 排他的フルスクリーンでオーバーレイが表示できない場合に使用
    /// </summary>
    /// <param name="currentMode">現在のフルスクリーンモード</param>
    /// <returns>推奨表示タスク</returns>
    Task ShowRecommendationAsync(FullscreenModeChangedEventArgs currentMode);

    /// <summary>
    /// フルスクリーンモード情報を手動更新
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>更新タスク</returns>
    Task RefreshModeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// フルスクリーンモードサービスの拡張メソッド
/// </summary>
public static class FullscreenModeServiceExtensions
{
    /// <summary>
    /// オーバーレイ表示の安全性をチェック
    /// </summary>
    /// <param name="service">フルスクリーンモードサービス</param>
    /// <returns>オーバーレイ表示が安全な場合true</returns>
    public static bool IsOverlaySafe(this IFullscreenModeService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        return service.CanShowOverlay && !service.IsExclusiveFullscreen;
    }

    /// <summary>
    /// ユーザーアクションが必要かチェック
    /// </summary>
    /// <param name="service">フルスクリーンモードサービス</param>
    /// <returns>ユーザーアクションが必要な場合true</returns>
    public static bool RequiresUserAction(this IFullscreenModeService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        return service.IsExclusiveFullscreen && !service.CanShowOverlay;
    }

    /// <summary>
    /// 推奨メッセージを生成
    /// </summary>
    /// <param name="service">フルスクリーンモードサービス</param>
    /// <returns>推奨メッセージ</returns>
    public static string GenerateRecommendationMessage(this IFullscreenModeService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        return service.CurrentModeType switch
        {
            FullscreenModeType.Exclusive => "オーバーレイ表示のため、ボーダレスフルスクリーンモードへの変更を推奨します。",
            FullscreenModeType.Borderless => "最適な設定です。オーバーレイが正常に表示されます。",
            FullscreenModeType.Windowed => "ウィンドウモードです。オーバーレイが正常に表示されます。",
            FullscreenModeType.Unknown => "フルスクリーンモードを検出できませんでした。",
            _ => "モード情報が不正です。"
        };
    }
}
