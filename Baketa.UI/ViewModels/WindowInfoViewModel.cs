using Baketa.Core.Abstractions.Platform.Windows.Adapters;
using ReactiveUI;

namespace Baketa.UI.ViewModels;

/// <summary>
/// ウィンドウ選択ダイアログ用のWindowInfo表示ViewModelクラス
/// </summary>
/// <remarks>
/// 🔥 [ISSUE#171] 選択済みウィンドウの枠表示のため、IsCurrentlySelectedプロパティを追加
/// Core層のWindowInfoはドメインモデルなので、UI層の表示状態を含むべきではない。
/// そのため、ViewModelレイヤーでラップして表示状態を管理する。
/// </remarks>
public sealed class WindowInfoViewModel : ReactiveObject
{
    private bool _isCurrentlySelected;

    /// <summary>
    /// 元のWindowInfo
    /// </summary>
    public WindowInfo WindowInfo { get; }

    /// <summary>
    /// 現在選択されているウィンドウかどうか（前回選択したウィンドウに枠を表示するため）
    /// </summary>
    public bool IsCurrentlySelected
    {
        get => _isCurrentlySelected;
        set => this.RaiseAndSetIfChanged(ref _isCurrentlySelected, value);
    }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="windowInfo">元のWindowInfo</param>
    /// <param name="isCurrentlySelected">現在選択されているかどうか</param>
    public WindowInfoViewModel(WindowInfo windowInfo, bool isCurrentlySelected = false)
    {
        WindowInfo = windowInfo;
        _isCurrentlySelected = isCurrentlySelected;
    }

    // WindowInfoのプロパティを直接公開（バインディング用）
    public nint Handle => WindowInfo.Handle;
    public string Title => WindowInfo.Title;
    public string? ThumbnailBase64 => WindowInfo.ThumbnailBase64;
    public bool IsVisible => WindowInfo.IsVisible;
    public bool IsMinimized => WindowInfo.IsMinimized;
}
