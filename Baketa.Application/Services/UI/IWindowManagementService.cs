using System.Reactive;
using Baketa.Core.Abstractions.Platform.Windows.Adapters;

namespace Baketa.Application.Services.UI;

/// <summary>
/// ウィンドウ管理統一サービス
/// MainOverlayViewModelから抽出されたウィンドウ選択・管理機能を統一化
/// </summary>
public interface IWindowManagementService
{
    /// <summary>
    /// 現在選択されているウィンドウ
    /// </summary>
    WindowInfo? SelectedWindow { get; }

    /// <summary>
    /// ウィンドウが選択されているかどうか
    /// </summary>
    bool IsWindowSelected { get; }

    /// <summary>
    /// ウィンドウ選択が有効かどうか
    /// </summary>
    bool IsWindowSelectionEnabled { get; }

    /// <summary>
    /// ウィンドウ選択ダイアログを表示し、結果を返します
    /// </summary>
    /// <returns>選択されたウィンドウ情報（キャンセル時はnull）</returns>
    Task<WindowInfo?> ShowWindowSelectionAsync();

    /// <summary>
    /// 指定されたウィンドウを選択状態にします
    /// </summary>
    /// <param name="windowInfo">選択するウィンドウ情報</param>
    Task SelectWindowAsync(WindowInfo windowInfo);

    /// <summary>
    /// ウィンドウ選択を解除します
    /// </summary>
    Task ClearWindowSelectionAsync();

    /// <summary>
    /// 選択されたウィンドウの有効性を検証します
    /// </summary>
    /// <returns>ウィンドウが有効かどうか</returns>
    Task<bool> ValidateSelectedWindowAsync();

    /// <summary>
    /// ウィンドウ選択状態変更の通知
    /// </summary>
    IObservable<WindowSelectionChanged> WindowSelectionChanged { get; }

    /// <summary>
    /// ウィンドウ選択可能状態変更の通知
    /// </summary>
    IObservable<bool> WindowSelectionEnabledChanged { get; }
}

/// <summary>
/// ウィンドウ選択変更イベント
/// </summary>
public sealed record WindowSelectionChanged(
    WindowInfo? PreviousWindow,
    WindowInfo? CurrentWindow,
    bool IsSelected,
    DateTime ChangedAt,
    string Source
);
