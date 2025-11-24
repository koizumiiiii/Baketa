using System;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Services;
using Baketa.UI.ViewModels;
using Baketa.UI.Views;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Services;

/// <summary>
/// エラー通知サービスの実装
/// ErrorNotificationViewのシングルトンインスタンスを管理し、エラーメッセージを表示
/// </summary>
public sealed class ErrorNotificationService : IErrorNotificationService, IDisposable
{
    private readonly IEventAggregator _eventAggregator;
    private readonly ILogger<ErrorNotificationService> _logger;
    private readonly ILoggerFactory _loggerFactory;

    private ErrorNotificationView? _notificationView;
    private ErrorNotificationViewModel? _notificationViewModel;
    private readonly object _lock = new();

    public ErrorNotificationService(
        IEventAggregator eventAggregator,
        ILogger<ErrorNotificationService> logger,
        ILoggerFactory loggerFactory)
    {
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    /// <inheritdoc />
    public async Task ShowErrorAsync(string message)
    {
        try
        {
            // UIスレッドで実行
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                EnsureNotificationViewCreated();

                if (_notificationViewModel != null)
                {
                    await _notificationViewModel.ShowErrorAsync(message).ConfigureAwait(false);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "エラー通知の表示に失敗: {Message}", ex.Message);
        }
    }

    /// <inheritdoc />
    public void HideError()
    {
        try
        {
            // UIスレッドで実行
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                _notificationViewModel?.HideError();
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "エラー通知の非表示に失敗: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// 通知ウィンドウとViewModelを作成（遅延初期化）
    /// </summary>
    private void EnsureNotificationViewCreated()
    {
        lock (_lock)
        {
            if (_notificationView == null || _notificationViewModel == null)
            {
                _logger.LogDebug("ErrorNotificationViewを作成します");

                // ViewModelを作成
                var viewModelLogger = _loggerFactory.CreateLogger<ErrorNotificationViewModel>();
                _notificationViewModel = new ErrorNotificationViewModel(_eventAggregator, viewModelLogger);

                // Viewを作成
                _notificationView = new ErrorNotificationView
                {
                    DataContext = _notificationViewModel
                };

                _logger.LogInformation("ErrorNotificationViewの作成が完了しました");
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_lock)
        {
            // UIリソースの破棄（UIスレッドで実行）
            if (_notificationView != null)
            {
                try
                {
                    // UIスレッドで確実にウィンドウを閉じる
                    if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                    {
                        _notificationView.Close();
                    }
                    else
                    {
                        // UIスレッド外から呼ばれた場合は、UIスレッドにポスト
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            try
                            {
                                _notificationView?.Close();
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "ErrorNotificationViewのClose中にエラー: {Message}", ex.Message);
                            }
                        });
                    }

                    _notificationView = null;
                    _logger.LogDebug("ErrorNotificationViewを破棄しました");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "ErrorNotificationView破棄中にエラー: {Message}", ex.Message);
                }
            }

            // ViewModelの破棄
            if (_notificationViewModel != null)
            {
                try
                {
                    _notificationViewModel.Dispose();
                    _notificationViewModel = null;
                    _logger.LogDebug("ErrorNotificationViewModelを破棄しました");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "ErrorNotificationViewModel破棄中にエラー: {Message}", ex.Message);
                }
            }
        }
    }
}
