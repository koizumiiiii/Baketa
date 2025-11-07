namespace Baketa.Core.Abstractions.UI.Overlays;

/// <summary>
/// オーバーレイインスタンスの抽象インターフェース
/// 個々のオーバーレイウィンドウ/コンポーネントを表現
/// </summary>
public interface IOverlay : IDisposable
{
    /// <summary>
    /// オーバーレイの一意識別子
    /// </summary>
    string Id { get; }

    /// <summary>
    /// オーバーレイを表示
    /// </summary>
    Task ShowAsync();

    /// <summary>
    /// オーバーレイを非表示
    /// </summary>
    Task HideAsync();

    /// <summary>
    /// オーバーレイが現在表示されているか
    /// </summary>
    bool IsVisible { get; }

    /// <summary>
    /// オーバーレイの位置情報
    /// </summary>
    OverlayPosition Position { get; }

    /// <summary>
    /// オーバーレイの表示内容
    /// </summary>
    OverlayContent Content { get; }
}
