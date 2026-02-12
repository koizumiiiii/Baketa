using System.Reactive.Linq;
using System.Reactive.Subjects;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Platform.Windows.Adapters;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.Services.UI;

/// <summary>
/// ウィンドウ管理統一サービス実装
/// MainOverlayViewModelから抽出されたウィンドウ選択・管理ロジックを統一化
/// 注: ダイアログ表示はUI層の責務として委譲し、Clean Architecture原則を維持
/// </summary>
public sealed class WindowManagementService : IWindowManagementService, IDisposable
{
    private readonly IWindowManagerAdapter _windowManager;
    private readonly IEventAggregator _eventAggregator;
    private readonly ILogger<WindowManagementService> _logger;
    private readonly IWindowSelectionDialogService? _dialogService;

    private readonly Subject<WindowSelectionChanged> _windowSelectionSubject = new();
    private readonly Subject<bool> _windowSelectionEnabledSubject = new();

    private WindowInfo? _selectedWindow;
    private bool _isWindowSelectionEnabled = true;
    private bool _disposed;

    public WindowManagementService(
        IWindowManagerAdapter windowManager,
        IEventAggregator eventAggregator,
        ILogger<WindowManagementService> logger,
        IWindowSelectionDialogService? dialogService = null)
    {
        _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dialogService = dialogService; // Optional dependency for UI dialog
    }

    /// <inheritdoc />
    public WindowInfo? SelectedWindow => _selectedWindow;

    /// <inheritdoc />
    public bool IsWindowSelected => _selectedWindow != null;

    /// <inheritdoc />
    public bool IsWindowSelectionEnabled => _isWindowSelectionEnabled;

    /// <inheritdoc />
    public IObservable<WindowSelectionChanged> WindowSelectionChanged => _windowSelectionSubject.AsObservable();

    /// <inheritdoc />
    public IObservable<bool> WindowSelectionEnabledChanged => _windowSelectionEnabledSubject.AsObservable();

    /// <inheritdoc />
    public async Task<WindowInfo?> ShowWindowSelectionAsync()
    {
        if (_disposed) return null;

        try
        {
            _logger.LogInformation("ウィンドウ選択ダイアログ表示要求");
            Console.WriteLine("🔧 WindowManagementService.ShowWindowSelectionAsync開始");

            // UI層のダイアログサービスがある場合は使用、ない場合はnullを返す
            if (_dialogService != null)
            {
                Console.WriteLine("🔧 _dialogService != null - ShowWindowSelectionDialogAsync呼び出し開始");
                // 🔥 [ISSUE#171] 現在選択中のウィンドウハンドルを渡す（選択済みウィンドウに枠表示用）
                var currentHandle = _selectedWindow?.Handle ?? IntPtr.Zero;
                _logger.LogDebug("[BORDER_DEBUG] Passing currentHandle to dialog: {Handle} (from _selectedWindow: {Title})", currentHandle, _selectedWindow?.Title ?? "null");
                var result = await _dialogService.ShowWindowSelectionDialogAsync(currentHandle);
                Console.WriteLine($"🔧 _dialogService.ShowWindowSelectionDialogAsync完了: result={result != null}");

                if (result != null)
                {
                    _logger.LogInformation("ウィンドウ選択完了: '{Title}' (Handle={Handle})",
                        result.Title, result.Handle);
                    Console.WriteLine($"✅ ウィンドウ選択完了: '{result.Title}' (Handle={result.Handle})");

                    // 🔥 [ISSUE#171] 選択されたウィンドウを保存（次回のダイアログ表示時に枠表示するため）
                    await SelectWindowAsync(result).ConfigureAwait(false);
                    _logger.LogDebug("[BORDER_DEBUG] SelectWindowAsync called - _selectedWindow updated to: {Handle}", result.Handle);
                }
                else
                {
                    _logger.LogDebug("ウィンドウ選択がキャンセルされました");
                    Console.WriteLine("❌ ウィンドウ選択がキャンセルされました");
                }

                return result;
            }
            else
            {
                _logger.LogWarning("ダイアログサービスが利用できません - UI層での実装が必要");
                Console.WriteLine("❌ _dialogService == null - ダイアログサービスが利用できません");
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ウィンドウ選択ダイアログ処理中にエラーが発生");
            Console.WriteLine($"💥 WindowManagementService.ShowWindowSelectionAsyncエラー: {ex.Message}");
            Console.WriteLine($"💥 スタックトレース: {ex.StackTrace}");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SelectWindowAsync(WindowInfo windowInfo)
    {
        if (_disposed) return;
        ArgumentNullException.ThrowIfNull(windowInfo);

        try
        {
            var previousWindow = _selectedWindow;

            // ウィンドウの有効性を検証
            var isValid = await ValidateWindowAsync(windowInfo);
            if (!isValid)
            {
                _logger.LogWarning("無効なウィンドウが選択されました: '{Title}' (Handle={Handle})",
                    windowInfo.Title, windowInfo.Handle);
                return;
            }

            // ウィンドウ選択状態を更新
            _selectedWindow = windowInfo;

            _logger.LogInformation("ウィンドウ選択完了: '{Title}' (Handle={Handle})",
                windowInfo.Title, windowInfo.Handle);

            // 変更通知を発行
            var changeEvent = new WindowSelectionChanged(
                previousWindow,
                windowInfo,
                true,
                DateTime.UtcNow,
                "SelectWindowAsync"
            );

            _windowSelectionSubject.OnNext(changeEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ウィンドウ選択処理中にエラーが発生");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task ClearWindowSelectionAsync()
    {
        if (_disposed) return;

        try
        {
            var previousWindow = _selectedWindow;
            if (previousWindow == null) return; // 既に未選択状態

            _selectedWindow = null;

            _logger.LogInformation("ウィンドウ選択解除");

            // 変更通知を発行
            var changeEvent = new WindowSelectionChanged(
                previousWindow,
                null,
                false,
                DateTime.UtcNow,
                "ClearWindowSelectionAsync"
            );

            _windowSelectionSubject.OnNext(changeEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ウィンドウ選択解除処理中にエラーが発生");
            throw;
        }

        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<bool> ValidateSelectedWindowAsync()
    {
        if (_disposed) return false;
        if (_selectedWindow == null) return false;

        return await ValidateWindowAsync(_selectedWindow);
    }

    /// <summary>
    /// ウィンドウ選択可能状態を設定します
    /// </summary>
    /// <param name="enabled">選択可能かどうか</param>
    public void SetWindowSelectionEnabled(bool enabled)
    {
        if (_disposed) return;
        if (_isWindowSelectionEnabled == enabled) return;

        _isWindowSelectionEnabled = enabled;
        _windowSelectionEnabledSubject.OnNext(enabled);

        _logger.LogDebug("ウィンドウ選択可能状態変更: {Enabled}", enabled);
    }

    /// <summary>
    /// 指定されたウィンドウの有効性を検証します
    /// </summary>
    private async Task<bool> ValidateWindowAsync(WindowInfo windowInfo)
    {
        try
        {
            if (windowInfo.Handle == IntPtr.Zero || string.IsNullOrEmpty(windowInfo.Title))
                return false;

            // [Issue #389] IWindowManagerAdapter.GetWindowBounds() でウィンドウの実在を確認
            // ウィンドウが閉じられている場合、GetWindowBounds() は null を返す
            var bounds = _windowManager.GetWindowBounds(windowInfo.Handle);
            if (bounds == null)
            {
                _logger.LogInformation("[Issue #389] ウィンドウが存在しません: '{Title}' (Handle={Handle})",
                    windowInfo.Title, windowInfo.Handle);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ウィンドウ有効性検証中にエラーが発生: '{Title}' (Handle={Handle})",
                windowInfo.Title, windowInfo.Handle);
            return false;
        }
    }

    /// <summary>
    /// リソースを解放します
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _windowSelectionSubject?.OnCompleted();
        _windowSelectionSubject?.Dispose();
        _windowSelectionEnabledSubject?.OnCompleted();
        _windowSelectionEnabledSubject?.Dispose();

        _disposed = true;
    }
}
