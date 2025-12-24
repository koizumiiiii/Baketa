using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Baketa.UI.Configuration;
using Baketa.UI.Views;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.UI.Services;

/// <summary>
/// Avalonia用通知サービスの実装（カスタムトースト使用）
/// </summary>
public sealed class AvaloniaNotificationService : INotificationService, IDisposable
{
    private readonly ILogger<AvaloniaNotificationService> _logger;
    private readonly TranslationUIOptions _options;
    private readonly List<ToastNotificationWindow> _activeToasts = [];
    private readonly object _toastLock = new();
    private bool _disposed;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="logger">ロガー</param>
    /// <param name="options">UI設定オプション</param>
    public AvaloniaNotificationService(
        ILogger<AvaloniaNotificationService> logger,
        IOptions<TranslationUIOptions> options)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);

        _logger = logger;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task ShowSuccessAsync(string title, string message, int duration = 3000)
    {
        if (!_options.EnableNotifications)
            return;

        _logger.LogInformation("Success notification: {Title} - {Message}", title, message);

        await ShowNotificationAsync(NotificationType.Success, title, message, duration).ConfigureAwait(false);

        NotificationShown?.Invoke(this, new NotificationEventArgs(NotificationType.Success, title, message, duration));
    }

    /// <inheritdoc />
    public async Task ShowInformationAsync(string title, string message, int duration = 4000)
    {
        if (!_options.EnableNotifications)
            return;

        _logger.LogInformation("Information notification: {Title} - {Message}", title, message);

        await ShowNotificationAsync(NotificationType.Information, title, message, duration).ConfigureAwait(false);

        NotificationShown?.Invoke(this, new NotificationEventArgs(NotificationType.Information, title, message, duration));
    }

    /// <inheritdoc />
    public async Task ShowInfoAsync(string title, string message, int duration = 4000)
    {
        // ShowInformationAsync への委譲
        await ShowInformationAsync(title, message, duration).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ShowWarningAsync(string title, string message, int duration = 5000)
    {
        if (!_options.EnableNotifications)
            return;

        _logger.LogWarning("Warning notification: {Title} - {Message}", title, message);

        await ShowNotificationAsync(NotificationType.Warning, title, message, duration).ConfigureAwait(false);

        NotificationShown?.Invoke(this, new NotificationEventArgs(NotificationType.Warning, title, message, duration));
    }

    /// <inheritdoc />
    public async Task ShowErrorAsync(string title, string message, int duration = 0)
    {
        if (!_options.EnableNotifications)
            return;

        _logger.LogError("Error notification: {Title} - {Message}", title, message);

        await ShowNotificationAsync(NotificationType.Error, title, message, duration).ConfigureAwait(false);

        NotificationShown?.Invoke(this, new NotificationEventArgs(NotificationType.Error, title, message, duration));
    }

    /// <inheritdoc />
    public async Task ShowFallbackNotificationAsync(string fromEngine, string toEngine, string reason)
    {
        if (!_options.EnableNotifications || !_options.ShowFallbackInformation)
            return;

        var title = "翻訳エンジン切り替え";
        var message = $"{fromEngine} から {toEngine} に切り替えました。理由: {reason}";

        _logger.LogInformation("Fallback notification: {FromEngine} -> {ToEngine}, Reason: {Reason}",
            fromEngine, toEngine, reason);

        await ShowInformationAsync(title, message, 4000).ConfigureAwait(false);

        NotificationShown?.Invoke(this, new NotificationEventArgs(NotificationType.FallbackNotification, title, message, 4000));
    }

    /// <inheritdoc />
    public async Task ShowEngineStatusChangeAsync(string engineName, string status)
    {
        if (!_options.EnableNotifications)
            return;

        var title = "エンジン状態変更";
        var message = $"{engineName}: {status}";

        _logger.LogDebug("Engine status change notification: {Engine} - {Status}", engineName, status);

        await ShowInformationAsync(title, message, 3000).ConfigureAwait(false);

        NotificationShown?.Invoke(this, new NotificationEventArgs(NotificationType.EngineStatusChange, title, message, 3000));
    }

    /// <inheritdoc />
    public async Task<bool> ShowConfirmationAsync(string title, string message,
        string confirmText = "OK", string cancelText = "キャンセル")
    {
        _logger.LogInformation("Confirmation dialog: {Title} - {Message}", title, message);

        try
        {
            // UIスレッドで確認ダイアログを表示
            var result = Dispatcher.UIThread.CheckAccess()
                ? await ShowConfirmationDialogAsync(title, message, confirmText, cancelText).ConfigureAwait(true)
                : await Dispatcher.UIThread.InvokeAsync(async () =>
                    await ShowConfirmationDialogAsync(title, message, confirmText, cancelText).ConfigureAwait(true)).ConfigureAwait(true);

            return result;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "確認ダイアログの表示でInvalidOperationExceptionが発生しました");
            return false;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "確認ダイアログの表示がキャンセルされました");
            return false;
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "確認ダイアログの表示でArgumentExceptionが発生しました");
            return false;
        }
    }

    /// <inheritdoc />
    public event EventHandler<NotificationEventArgs>? NotificationShown;

    /// <summary>
    /// 実際の通知を表示（カスタムトースト使用）
    /// </summary>
    /// <param name="type">通知タイプ</param>
    /// <param name="title">タイトル</param>
    /// <param name="message">メッセージ</param>
    /// <param name="duration">表示時間</param>
    private async Task ShowNotificationAsync(NotificationType type, string title, string message, int duration)
    {
        try
        {
            // UIスレッドでトーストウィンドウを表示
            if (Dispatcher.UIThread.CheckAccess())
            {
                ShowToastWindow(type, title, message, duration);
            }
            else
            {
                await Dispatcher.UIThread.InvokeAsync(() => ShowToastWindow(type, title, message, duration));
            }

            if (_options.EnableVerboseLogging)
            {
                _logger.LogDebug("トースト通知を表示: Type={Type}, Title={Title}, Duration={Duration}ms",
                    type, title, duration);
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "トースト通知の表示でInvalidOperationExceptionが発生しました: {Type} - {Title}", type, title);
            LogNotificationAsFallback(type, title, message);
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "トースト通知の表示でArgumentExceptionが発生しました: {Type} - {Title}", type, title);
            LogNotificationAsFallback(type, title, message);
        }
        catch (NotSupportedException ex)
        {
            _logger.LogError(ex, "トースト通知の表示でNotSupportedExceptionが発生しました: {Type} - {Title}", type, title);
            LogNotificationAsFallback(type, title, message);
        }
    }

    /// <summary>
    /// トーストウィンドウを表示
    /// </summary>
    private void ShowToastWindow(NotificationType type, string title, string message, int duration)
    {
        try
        {
            var toast = new ToastNotificationWindow(type, title, message, duration);

            // クローズ時にリストから削除
            toast.Closed += (_, _) =>
            {
                lock (_toastLock)
                {
                    _activeToasts.Remove(toast);
                }
            };

            lock (_toastLock)
            {
                _activeToasts.Add(toast);
            }

            toast.Show();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "トーストウィンドウの作成に失敗");
        }
    }

    /// <summary>
    /// 確認ダイアログを表示
    /// </summary>
    /// <param name="title">タイトル</param>
    /// <param name="message">メッセージ</param>
    /// <param name="confirmText">確認ボタンテキスト</param>
    /// <param name="cancelText">キャンセルボタンテキスト</param>
    /// <returns>ユーザーが確認を選択した場合true</returns>
    private async Task<bool> ShowConfirmationDialogAsync(string title, string message, string confirmText, string cancelText)
    {
        try
        {
            var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (mainWindow == null)
            {
                _logger.LogWarning("メインウィンドウが見つからないため、確認ダイアログを表示できません");
                return true; // デフォルトでtrueを返す
            }

            // シンプルな確認ダイアログを作成
            var dialog = new Window
            {
                Title = title,
                Width = 400,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = false,
                ShowInTaskbar = false,
                Content = CreateConfirmationDialogContent(message, confirmText, cancelText)
            };

            return await dialog.ShowDialog<bool>(mainWindow).ConfigureAwait(true);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "確認ダイアログの表示でInvalidOperationExceptionが発生しました");
            return false;
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "確認ダイアログの表示でArgumentExceptionが発生しました");
            return false;
        }
        catch (NotSupportedException ex)
        {
            _logger.LogError(ex, "確認ダイアログの表示でNotSupportedExceptionが発生しました");
            return false;
        }
    }

    /// <summary>
    /// 確認ダイアログのコンテンツを作成
    /// </summary>
    /// <param name="message">メッセージ</param>
    /// <param name="confirmText">確認ボタンテキスト</param>
    /// <param name="cancelText">キャンセルボタンテキスト</param>
    /// <returns>ダイアログコンテンツ</returns>
    private static StackPanel CreateConfirmationDialogContent(string message, string confirmText, string cancelText)
    {
        var stackPanel = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 20
        };

        // メッセージテキスト
        var messageTextBlock = new TextBlock
        {
            Text = message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        // ボタンパネル
        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Spacing = 10
        };

        // 確認ボタン
        var confirmButton = new Button
        {
            Content = confirmText,
            Padding = new Thickness(20, 5),
            IsDefault = true
        };
        confirmButton.Click += (_, _) =>
        {
            if (confirmButton.Parent is Panel panel &&
                panel.Parent is StackPanel sp &&
                sp.Parent is Window window)
            {
                window.Close(true);
            }
        };

        // キャンセルボタン
        var cancelButton = new Button
        {
            Content = cancelText,
            Padding = new Thickness(20, 5),
            IsCancel = true
        };
        cancelButton.Click += (_, _) =>
        {
            if (cancelButton.Parent is Panel panel &&
                panel.Parent is StackPanel sp &&
                sp.Parent is Window window)
            {
                window.Close(false);
            }
        };

        buttonPanel.Children.Add(confirmButton);
        buttonPanel.Children.Add(cancelButton);

        stackPanel.Children.Add(messageTextBlock);
        stackPanel.Children.Add(buttonPanel);

        return stackPanel;
    }

    /// <summary>
    /// フォールバックとして通知をログに出力
    /// </summary>
    /// <param name="type">通知タイプ</param>
    /// <param name="title">タイトル</param>
    /// <param name="message">メッセージ</param>
    private void LogNotificationAsFallback(NotificationType type, string title, string message)
    {
        var logLevel = type switch
        {
            NotificationType.Error => LogLevel.Error,
            NotificationType.Warning => LogLevel.Warning,
            _ => LogLevel.Information
        };

        _logger.Log(logLevel, "[通知] {Type}: {Title} - {Message}", type, title, message);
    }

    /// <summary>
    /// リソースを解放
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            try
            {
                // アクティブなトーストをすべて閉じる
                lock (_toastLock)
                {
                    foreach (var toast in _activeToasts.ToArray())
                    {
                        try
                        {
                            Dispatcher.UIThread.Post(() => toast.Close());
                        }
                        catch
                        {
                            // 閉じ済みの場合は無視
                        }
                    }
                    _activeToasts.Clear();
                }

                _disposed = true;
                _logger.LogDebug("AvaloniaNotificationService が正常に解放されました");
            }
            catch (ObjectDisposedException ex)
            {
                _logger.LogWarning(ex, "AvaloniaNotificationService の解放中にObjectDisposedExceptionが発生しました（既に解放済み）");
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "AvaloniaNotificationService の解放中にInvalidOperationExceptionが発生しました");
            }
        }
    }
}
