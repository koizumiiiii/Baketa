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
    private readonly ILoggerFactory _loggerFactory;

    public WindowSelectionDialogService(
        IEventAggregator eventAggregator,
        IWindowManagerAdapter windowManager,
        ILogger<WindowSelectionDialogService> logger,
        ILoggerFactory loggerFactory)
    {
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    /// <inheritdoc />
    public async Task<WindowInfo?> ShowWindowSelectionDialogAsync(IntPtr currentlySelectedWindowHandle = default)
    {
        try
        {
            _logger.LogDebug("[DIALOG_START] Window selection dialog started");
            Console.WriteLine("[DIALOG_START] Window selection dialog started");

            // Gemini推奨: HomeViewModelパターンによる簡素化実装
            // 🔥 [ISSUE#171] 実際のLoggerを使用（NullLoggerではログが出力されない）
            var viewModelLogger = _loggerFactory.CreateLogger<WindowSelectionDialogViewModel>();
            _logger.LogDebug("[BORDER_DEBUG] Creating ViewModel with PreviouslySelectedWindowHandle: {Handle}", currentlySelectedWindowHandle);
            var dialogViewModel = new WindowSelectionDialogViewModel(_eventAggregator, viewModelLogger, _windowManager)
            {
                // 🔥 [ISSUE#171] 前回選択したウィンドウハンドルを設定（選択済みウィンドウに枠表示用）
                PreviouslySelectedWindowHandle = currentlySelectedWindowHandle
            };
            _logger.LogDebug("[BORDER_DEBUG] ViewModel created - PreviouslySelectedWindowHandle confirmed: {Handle}", dialogViewModel.PreviouslySelectedWindowHandle);
            var dialog = new WindowSelectionDialogView
            {
                DataContext = dialogViewModel
            };
            _logger.LogDebug("[BORDER_DEBUG] Dialog created with DataContext set");

            // メインウィンドウを取得
            var owner = Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow : null;

            if (owner != null)
            {
                _logger.LogDebug("[DIALOG_SHOW] Showing window selection dialog with progressive loading");

                // 🚀 段階的ローディング: ダイアログを先に表示し、バックグラウンドでウィンドウリスト読み込み
                // ダイアログ表示開始（ローディング状態で表示）
                var dialogTask = dialog.ShowDialog(owner);

                // バックグラウンドでウィンドウリスト読み込み開始（UIコンテキストを保持）
                _logger.LogDebug("[PROGRESSIVE_LOADING] Starting background window list loading");
                Console.WriteLine("[PROGRESSIVE_LOADING] Starting background window list loading");
                _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    try
                    {
                        _logger.LogDebug("[PROGRESSIVE_LOADING] Before Task.Delay(100)");
                        Console.WriteLine("[PROGRESSIVE_LOADING] Before Task.Delay(100)");
                        await Task.Delay(100); // UI表示を確実にするための短い遅延

                        _logger.LogDebug("[PROGRESSIVE_LOADING] Calling ExecuteRefreshAsync");
                        Console.WriteLine("[PROGRESSIVE_LOADING] Calling ExecuteRefreshAsync");
                        await dialogViewModel.ExecuteRefreshAsync();

                        _logger.LogDebug("[PROGRESSIVE_LOADING] Background window list loading completed");
                        Console.WriteLine("[PROGRESSIVE_LOADING] Background window list loading completed");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[PROGRESSIVE_LOADING] Error during background loading");
                        Console.WriteLine($"[PROGRESSIVE_LOADING] Error: {ex.Message}");
                    }
                });

                // ダイアログの結果待ち
                await dialogTask;
                _logger.LogDebug("[DIALOG_RESULT] Dialog completed, result: {HasResult}", dialogViewModel.DialogResult != null);
                return dialogViewModel.DialogResult;
            }
            else
            {
                _logger.LogWarning("[DIALOG_ERROR] Main window not found");
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DIALOG_ERROR] Window selection dialog failed");
            Console.WriteLine($"[DIALOG_ERROR] Window selection dialog failed: {ex.Message}");
            return null;
        }
    }
}
