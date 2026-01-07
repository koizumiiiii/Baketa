using System;
using System.Reactive;
using System.Reactive.Disposables;
using Baketa.UI.Resources;
using ReactiveUI;

namespace Baketa.UI.ViewModels;

/// <summary>
/// [Issue #249] ダウンロード進行ダイアログViewModel
/// i18n対応
/// </summary>
public sealed class DownloadProgressViewModel : ReactiveObject, IDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private bool _disposed;
    private string _statusText = string.Empty;
    private double _progressValue;
    private bool _isDownloading = true;
    private bool _isActionButtonEnabled;
    private string _errorMessage = string.Empty;
    private bool _hasError;

    public DownloadProgressViewModel(string appVersion)
    {
        AppVersion = appVersion;
#pragma warning disable CA1863 // リソース文字列のフォーマットはキャッシュ不要
        StatusText = string.Format(Strings.Update_DownloadProgress_Downloading, $"v{appVersion}");
#pragma warning restore CA1863

        // コマンド初期化
        ActionCommand = ReactiveCommand.Create(OnAction);
        CancelCommand = ReactiveCommand.Create(OnCancel);

        _disposables.Add(ActionCommand);
        _disposables.Add(CancelCommand);
    }

    /// <summary>
    /// アプリバージョン
    /// </summary>
    public string AppVersion { get; }

    /// <summary>
    /// ウィンドウタイトル
    /// </summary>
    public string WindowTitle => Strings.Update_DownloadProgress_Title;

    /// <summary>
    /// ステータステキスト
    /// </summary>
    public string StatusText
    {
        get => _statusText;
        set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    /// <summary>
    /// 進行率 (0-100)
    /// </summary>
    public double ProgressValue
    {
        get => _progressValue;
        set => this.RaiseAndSetIfChanged(ref _progressValue, value);
    }

    /// <summary>
    /// ダウンロード中かどうか
    /// </summary>
    public bool IsDownloading
    {
        get => _isDownloading;
        set => this.RaiseAndSetIfChanged(ref _isDownloading, value);
    }

    /// <summary>
    /// アクションボタンが有効かどうか
    /// </summary>
    public bool IsActionButtonEnabled
    {
        get => _isActionButtonEnabled;
        set => this.RaiseAndSetIfChanged(ref _isActionButtonEnabled, value);
    }

    /// <summary>
    /// エラーメッセージ
    /// </summary>
    public string ErrorMessage
    {
        get => _errorMessage;
        set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    /// <summary>
    /// エラーがあるかどうか
    /// </summary>
    public bool HasError
    {
        get => _hasError;
        set => this.RaiseAndSetIfChanged(ref _hasError, value);
    }

    /// <summary>
    /// アクションボタンテキスト（ダウンロード完了後は「インストールと再起動」）
    /// </summary>
    public string ActionButtonText => Strings.Update_DownloadProgress_InstallAndRelaunch;

    /// <summary>
    /// キャンセルボタンテキスト
    /// </summary>
    public string CancelButtonText => Strings.Update_DownloadProgress_Cancel;

    /// <summary>
    /// アクションコマンド（インストールと再起動）
    /// </summary>
    public ReactiveCommand<Unit, Unit> ActionCommand { get; }

    /// <summary>
    /// キャンセルコマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    /// <summary>
    /// アクション実行時イベント
    /// </summary>
    public event EventHandler<bool>? ActionRequested;

    /// <summary>
    /// ダウンロード完了を設定
    /// </summary>
    public void SetDownloadComplete(bool isValid)
    {
        IsDownloading = false;
        ProgressValue = 100;

        if (isValid)
        {
            StatusText = Strings.Update_DownloadProgress_Complete;
            IsActionButtonEnabled = true;
        }
        else
        {
            HasError = true;
            ErrorMessage = "Download verification failed";
            IsActionButtonEnabled = false;
        }
    }

    /// <summary>
    /// 進行状況を更新
    /// </summary>
    public void UpdateProgress(long bytesReceived, long totalBytes, int percentage)
    {
        ProgressValue = percentage;

        var receivedMB = bytesReceived / (1024.0 * 1024.0);
        var totalMB = totalBytes / (1024.0 * 1024.0);
#pragma warning disable CA1863 // リソース文字列のフォーマットはキャッシュ不要
        StatusText = string.Format(
            Strings.Update_DownloadProgress_Downloading,
            $"v{AppVersion}") + $"\n({receivedMB:F2} MB / {totalMB:F2} MB)";
#pragma warning restore CA1863
    }

    /// <summary>
    /// エラーを表示
    /// </summary>
    public void ShowError(string message)
    {
        HasError = true;
        ErrorMessage = message;
        IsDownloading = false;
        IsActionButtonEnabled = false;
    }

    private void OnAction()
    {
        ActionRequested?.Invoke(this, true);
    }

    private void OnCancel()
    {
        ActionRequested?.Invoke(this, false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _disposables.Dispose();
    }
}
