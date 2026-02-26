using System.Drawing;

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
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task HideAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// [Issue #408] 指定領域内のオーバーレイを非表示にして破棄
    /// </summary>
    /// <param name="area">対象領域</param>
    /// <param name="excludeChunkId">除外するChunkID（-1で除外なし）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task HideOverlaysInAreaAsync(Rectangle area, int excludeChunkId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 管理下の全オーバーレイの可視性を設定（破棄せずに表示/非表示を切り替え）
    /// </summary>
    /// <param name="isVisible">true: 表示, false: 非表示</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task SetAllVisibilityAsync(bool isVisible, CancellationToken cancellationToken = default);

    /// <summary>
    /// 現在表示中のオーバーレイ数を取得
    /// </summary>
    int ActiveOverlayCount { get; }
}
