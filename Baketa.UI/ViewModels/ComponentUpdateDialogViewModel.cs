using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using Baketa.Core.Abstractions.Services;
using Baketa.UI.Helpers;
using Baketa.UI.Resources;
using ReactiveUI;

namespace Baketa.UI.ViewModels;

/// <summary>
/// [Issue #256] コンポーネント更新ダイアログViewModel
/// Geminiフィードバック反映: ReactiveObject継承、進捗状態管理
/// </summary>
public sealed class ComponentUpdateDialogViewModel : ReactiveObject, IDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private bool _disposed;

    private string _statusMessage = string.Empty;
    private bool _isUpdating;
    private bool _isComplete;

    public ComponentUpdateDialogViewModel(IReadOnlyList<IComponentUpdateCheckResult> availableUpdates)
    {
        ArgumentNullException.ThrowIfNull(availableUpdates);

        // 更新可能なコンポーネントをViewModelに変換
        AvailableUpdates = new ObservableCollection<ComponentUpdateItem>(
            availableUpdates
                .Where(u => u.UpdateAvailable && u.MeetsAppVersionRequirement)
                .Select(u => new ComponentUpdateItem
                {
                    ComponentId = u.ComponentId,
                    DisplayName = u.DisplayName,
                    CurrentVersion = u.CurrentVersion,
                    LatestVersion = u.LatestVersion,
                    DownloadSizeBytes = u.TotalDownloadSize,
                    Changelog = u.Changelog,
                    IsSelected = true
                }));

        // コマンド初期化
        var canUpdate = this.WhenAnyValue(
            x => x.IsUpdating,
            x => x.IsComplete,
            (updating, complete) => !updating && !complete);

        UpdateNowCommand = ReactiveCommand.Create(OnUpdateNow, canUpdate);
        RemindLaterCommand = ReactiveCommand.Create(OnRemindLater, canUpdate);
        SkipCommand = ReactiveCommand.Create(OnSkip, canUpdate);
        CancelCommand = ReactiveCommand.Create(OnCancel);
        CloseCommand = ReactiveCommand.Create(OnClose);

        _disposables.Add(UpdateNowCommand);
        _disposables.Add(RemindLaterCommand);
        _disposables.Add(SkipCommand);
        _disposables.Add(CancelCommand);
        _disposables.Add(CloseCommand);
    }

    /// <summary>
    /// 更新可能なコンポーネントリスト
    /// </summary>
    public ObservableCollection<ComponentUpdateItem> AvailableUpdates { get; }

    /// <summary>
    /// 選択されたコンポーネント
    /// </summary>
    public IEnumerable<ComponentUpdateItem> SelectedUpdates =>
        AvailableUpdates.Where(u => u.IsSelected);

    /// <summary>
    /// 合計ダウンロードサイズ（選択されたもののみ）
    /// </summary>
    public long TotalDownloadSizeBytes =>
        SelectedUpdates.Sum(u => u.DownloadSizeBytes);

    /// <summary>
    /// 合計ダウンロードサイズ（表示用）
    /// </summary>
#pragma warning disable CA1863 // リソース文字列のフォーマットはキャッシュ不要
    public string TotalDownloadSizeText =>
        string.Format(Strings.ComponentUpdate_TotalSize, ByteFormatter.Format(TotalDownloadSizeBytes));
#pragma warning restore CA1863

    /// <summary>
    /// 更新処理中フラグ
    /// </summary>
    public bool IsUpdating
    {
        get => _isUpdating;
        private set => this.RaiseAndSetIfChanged(ref _isUpdating, value);
    }

    /// <summary>
    /// 更新完了フラグ
    /// </summary>
    public bool IsComplete
    {
        get => _isComplete;
        private set => this.RaiseAndSetIfChanged(ref _isComplete, value);
    }

    /// <summary>
    /// ステータスメッセージ
    /// </summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    /// <summary>
    /// ダイアログ結果
    /// </summary>
    public ComponentUpdateDialogResult Result { get; private set; } = ComponentUpdateDialogResult.None;

    // ローカライズ文字列
    public string WindowTitle => Strings.ComponentUpdate_WindowTitle;
    public string HeaderText => Strings.ComponentUpdate_HeaderText;
    public string DescriptionText => Strings.ComponentUpdate_Description;
    public string UpdateNowButtonText => Strings.ComponentUpdate_UpdateNowButton;
    public string RemindLaterButtonText => Strings.ComponentUpdate_RemindLaterButton;
    public string SkipButtonText => Strings.ComponentUpdate_SkipButton;
    public string CancelButtonText => Strings.ComponentUpdate_CancelButton;
    public string CloseButtonText => Strings.ComponentUpdate_CloseButton;

    // コマンド
    public ReactiveCommand<Unit, Unit> UpdateNowCommand { get; }
    public ReactiveCommand<Unit, Unit> RemindLaterCommand { get; }
    public ReactiveCommand<Unit, Unit> SkipCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    /// <summary>
    /// ウィンドウを閉じるリクエスト
    /// </summary>
    public event EventHandler<ComponentUpdateDialogResult>? CloseRequested;

    private void OnUpdateNow()
    {
        Result = ComponentUpdateDialogResult.UpdateNow;
        CloseRequested?.Invoke(this, Result);
    }

    private void OnRemindLater()
    {
        Result = ComponentUpdateDialogResult.RemindLater;
        CloseRequested?.Invoke(this, Result);
    }

    private void OnSkip()
    {
        Result = ComponentUpdateDialogResult.Skip;
        CloseRequested?.Invoke(this, Result);
    }

    private void OnCancel()
    {
        Result = ComponentUpdateDialogResult.Cancelled;
        CloseRequested?.Invoke(this, Result);
    }

    private void OnClose()
    {
        Result = ComponentUpdateDialogResult.Closed;
        CloseRequested?.Invoke(this, Result);
    }

    /// <summary>
    /// 更新処理開始時に呼び出す
    /// </summary>
    public void StartUpdating()
    {
        IsUpdating = true;
        StatusMessage = string.Empty;
    }

    /// <summary>
    /// 更新処理完了時に呼び出す
    /// </summary>
    public void CompleteUpdating(bool allSucceeded)
    {
        IsUpdating = false;
        IsComplete = true;
        StatusMessage = allSucceeded
            ? Strings.ComponentUpdate_AllCompleted
            : Strings.ComponentUpdate_SomeFailed;
    }

    /// <summary>
    /// リソースを解放
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _disposables.Dispose();
    }
}

/// <summary>
/// ダイアログの結果
/// </summary>
public enum ComponentUpdateDialogResult
{
    None,
    UpdateNow,
    RemindLater,
    Skip,
    Cancelled,
    Closed
}

/// <summary>
/// [Issue #256] コンポーネント更新アイテムViewModel
/// Geminiフィードバック反映: ReactiveObject継承、進捗状態プロパティ追加
/// </summary>
public sealed class ComponentUpdateItem : ReactiveObject
{
    private bool _isSelected = true;
    private double _downloadProgress;
    private ComponentUpdateStatus _status = ComponentUpdateStatus.Pending;
    private string? _errorMessage;

    public required string ComponentId { get; init; }
    public required string DisplayName { get; init; }
    public string? CurrentVersion { get; init; }
    public required string LatestVersion { get; init; }
    public required long DownloadSizeBytes { get; init; }
    public string? Changelog { get; init; }

    /// <summary>
    /// 選択状態
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }

    /// <summary>
    /// ダウンロード進捗（0.0〜1.0）
    /// </summary>
    public double DownloadProgress
    {
        get => _downloadProgress;
        set => this.RaiseAndSetIfChanged(ref _downloadProgress, value);
    }

    /// <summary>
    /// 更新ステータス
    /// </summary>
    public ComponentUpdateStatus Status
    {
        get => _status;
        set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    /// <summary>
    /// エラーメッセージ
    /// </summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    /// <summary>
    /// バージョン表示テキスト
    /// </summary>
#pragma warning disable CA1863 // リソース文字列のフォーマットはキャッシュ不要
    public string VersionText => CurrentVersion == null
        ? string.Format(Strings.ComponentUpdate_NotInstalled, LatestVersion)
        : string.Format(Strings.ComponentUpdate_VersionFormat, CurrentVersion, LatestVersion);
#pragma warning restore CA1863

    /// <summary>
    /// サイズ表示テキスト
    /// </summary>
#pragma warning disable CA1863 // リソース文字列のフォーマットはキャッシュ不要
    public string SizeText => string.Format(Strings.ComponentUpdate_SizeFormat, ByteFormatter.Format(DownloadSizeBytes));
#pragma warning restore CA1863

    /// <summary>
    /// ステータス表示テキスト
    /// </summary>
    public string StatusText => Status switch
    {
        ComponentUpdateStatus.Pending => Strings.ComponentUpdate_Status_Pending,
        ComponentUpdateStatus.Downloading => Strings.ComponentUpdate_Status_Downloading,
        ComponentUpdateStatus.Installing => Strings.ComponentUpdate_Status_Installing,
        ComponentUpdateStatus.Completed => Strings.ComponentUpdate_Status_Completed,
        ComponentUpdateStatus.Failed => Strings.ComponentUpdate_Status_Failed,
        ComponentUpdateStatus.Skipped => Strings.ComponentUpdate_Status_Skipped,
        _ => string.Empty
    };
}

/// <summary>
/// コンポーネント更新ステータス
/// </summary>
public enum ComponentUpdateStatus
{
    Pending,
    Downloading,
    Installing,
    Completed,
    Skipped,
    Failed
}
