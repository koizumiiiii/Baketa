using System;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.UI.Framework;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace Baketa.UI.ViewModels;

/// <summary>
/// エラー通知ウィンドウのViewModel
/// アプリ全体のエラーメッセージを画面中央最下部に表示
/// </summary>
public sealed class ErrorNotificationViewModel : ViewModelBase
{
    private string _errorMessage = string.Empty;
    private bool _isVisible;
    private CancellationTokenSource? _autoHideCts;

    public ErrorNotificationViewModel(
        IEventAggregator eventAggregator,
        ILogger<ErrorNotificationViewModel> logger)
        : base(eventAggregator, logger)
    {
        InitializeCommands();
    }

    #region Properties

    /// <summary>
    /// 表示するメッセージ本文
    /// </summary>
    public string DisplayMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    /// <summary>
    /// ウィンドウの表示状態
    /// </summary>
    public bool IsVisible
    {
        get => _isVisible;
        private set => this.RaiseAndSetIfChanged(ref _isVisible, value);
    }

    #endregion

    #region Commands

    /// <summary>
    /// エラーを閉じるコマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> CloseCommand { get; private set; } = null!;

    #endregion

    #region Initialization

    private void InitializeCommands()
    {
        CloseCommand = ReactiveCommand.Create(ExecuteClose);
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// エラーメッセージを表示し、5秒後に自動で非表示にする
    /// </summary>
    /// <param name="message">エラーメッセージ</param>
    public async Task ShowErrorAsync(string message)
    {
        try
        {
            // 既存の自動非表示タイマーをキャンセル
            _autoHideCts?.Cancel();
            _autoHideCts?.Dispose();

            // エラーメッセージを設定して表示
            DisplayMessage = message;
            IsVisible = true;

            Logger?.LogInformation("エラー通知表示: {Message}", message);

            // 5秒後に自動非表示
            _autoHideCts = new CancellationTokenSource();
            await Task.Delay(TimeSpan.FromSeconds(5), _autoHideCts.Token).ConfigureAwait(false);

            // タイマー完了後、非表示
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsVisible = false;
                Logger?.LogDebug("エラー通知自動非表示");
            });
        }
        catch (TaskCanceledException)
        {
            // タイマーがキャンセルされた場合（新しいエラー表示時など）
            Logger?.LogDebug("エラー通知タイマーがキャンセルされました");
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "エラー通知表示中にエラーが発生: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// エラーメッセージを即座に非表示にする
    /// </summary>
    public void HideError()
    {
        _autoHideCts?.Cancel();
        _autoHideCts?.Dispose();
        _autoHideCts = null;

        IsVisible = false;
        Logger?.LogDebug("エラー通知手動非表示");
    }

    #endregion

    #region Command Implementations

    private void ExecuteClose()
    {
        HideError();
    }

    #endregion

    #region Dispose

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _autoHideCts?.Cancel();
            _autoHideCts?.Dispose();
        }

        base.Dispose(disposing);
    }

    #endregion
}
