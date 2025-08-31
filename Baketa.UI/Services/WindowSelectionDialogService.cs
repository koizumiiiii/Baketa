using Baketa.Application.Services.UI;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Platform.Windows.Adapters;
using Baketa.UI.ViewModels;
using Baketa.UI.Views;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Services;

/// <summary>
/// ウィンドウ選択ダイアログサービス実装（UIレイヤー）
/// Clean Architecture原則に従い、UI固有のダイアログ表示責務を担当
/// </summary>
public sealed class WindowSelectionDialogService : IWindowSelectionDialogService
{
    private readonly IEventAggregator _eventAggregator;
    private readonly IWindowManagerAdapter _windowManager;
    private readonly ILogger<WindowSelectionDialogService> _logger;

    public WindowSelectionDialogService(
        IEventAggregator eventAggregator,
        IWindowManagerAdapter windowManager,
        ILogger<WindowSelectionDialogService> logger)
    {
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<WindowInfo?> ShowWindowSelectionDialogAsync()
    {
        try
        {
            _logger.LogDebug("ウィンドウ選択ダイアログ表示開始");

            // ダイアログViewModelを作成
            var dialogViewModel = new WindowSelectionDialogViewModel(
                _eventAggregator,
                Microsoft.Extensions.Logging.LoggerFactoryExtensions.CreateLogger<WindowSelectionDialogViewModel>(
                    Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance),
                _windowManager);

            // ダイアログViewを作成
            var dialog = new WindowSelectionDialogView
            {
                DataContext = dialogViewModel
            };

            // UIスレッドで安全にメインウィンドウを取得
            var owner = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                return Avalonia.Application.Current?.ApplicationLifetime
                    is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow : null;
            });

            _logger.LogDebug("オーナーウィンドウ取得: {OwnerExists}", owner != null);

            WindowInfo? result = null;
            if (owner != null)
            {
                // ダイアログを表示して結果を取得
                await dialog.ShowDialog(owner);
                result = dialogViewModel.DialogResult;
                
                if (result != null)
                {
                    _logger.LogInformation("ダイアログ結果取得: '{Title}' (Handle={Handle})", 
                        result.Title, result.Handle);
                }
                else
                {
                    _logger.LogDebug("ダイアログがキャンセルされました");
                }
                
                dialog.Close();
            }
            else
            {
                _logger.LogWarning("オーナーウィンドウが取得できませんでした");
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ウィンドウ選択ダイアログ表示中にエラーが発生");
            return null;
        }
    }
}