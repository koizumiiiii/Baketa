namespace Baketa.Core.Abstractions.UI.Overlays;

/// <summary>
/// オーバーレイマネージャーの統一インターフェース
/// Win32/Avaloniaの実装を抽象化し、Application層からの依存を分離
/// </summary>
public interface IOverlayManager
{
    /// <summary>
    /// 新しいオーバーレイを作成して表示
    /// </summary>
    /// <param name="content">表示内容（テキスト、スタイル情報）</param>
    /// <param name="position">表示位置とサイズ</param>
    /// <returns>作成されたオーバーレイインスタンス</returns>
    Task<IOverlay> ShowAsync(OverlayContent content, OverlayPosition position);

    /// <summary>
    /// 指定されたオーバーレイを非表示にして破棄
    /// </summary>
    /// <param name="overlay">非表示にするオーバーレイ</param>
    Task HideAsync(IOverlay overlay);

    /// <summary>
    /// 管理下の全オーバーレイを非表示にして破棄
    /// </summary>
    Task HideAllAsync();

    /// <summary>
    /// 現在表示中のオーバーレイ数を取得
    /// </summary>
    int ActiveOverlayCount { get; }
}
