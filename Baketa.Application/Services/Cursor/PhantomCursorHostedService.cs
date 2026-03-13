using System;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Application.Services.UI;
using Baketa.Core.Abstractions.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.Services.Cursor;

/// <summary>
/// [Issue #497] ファントムカーソル監視HostedService
/// ゲームがシステムカーソルを非表示にしている場合に代替カーソルを表示する
/// </summary>
/// <remarks>
/// 監視ループで以下の3条件をチェック:
/// 1. システムカーソルが非表示（CURSORINFO.flags に CURSOR_SHOWING なし）
/// 2. 翻訳対象ウィンドウまたはBaketa自身がフォアグラウンド
/// 3. マウス座標がゲームウィンドウのクライアント領域内
/// </remarks>
public sealed class PhantomCursorHostedService : BackgroundService, IPhantomCursorService
{
    private readonly IWindowManagementService? _windowManagementService;
    private readonly ICursorStateProvider? _cursorStateProvider;
    private readonly ILogger<PhantomCursorHostedService> _logger;

    private readonly Func<ILogger, IPhantomCursorWindowAdapter>? _windowFactory;
    private IPhantomCursorWindowAdapter? _cursorWindow;

    private nint _targetWindowHandle;
    private bool _isEnabled = true; // デフォルトON
    private bool _wasShowing;

    // マウス静止検出
    private int _lastMouseX;
    private int _lastMouseY;
    private int _staticFrameCount;
    private const int StaticThresholdFrames = 6; // ~100ms at 16ms interval

    private const int PollingIntervalMs = 16; // ~60FPS

    public PhantomCursorHostedService(
        IWindowManagementService? windowManagementService,
        ICursorStateProvider? cursorStateProvider,
        Func<ILogger, IPhantomCursorWindowAdapter>? windowFactory,
        ILogger<PhantomCursorHostedService> logger)
    {
        _windowManagementService = windowManagementService;
        _cursorStateProvider = cursorStateProvider;
        _windowFactory = windowFactory;
        _logger = logger;

        _windowManagementService?.WindowSelectionChanged.Subscribe(OnWindowSelectionChanged);
    }

    public bool IsEnabled => _isEnabled;

    public void SetTargetWindow(nint windowHandle)
    {
        _targetWindowHandle = windowHandle;
        _logger.LogDebug("[Issue #497] Target window set: 0x{Handle:X}", windowHandle);
    }

    public void Enable()
    {
        _isEnabled = true;
        _logger.LogInformation("[Issue #497] PhantomCursor enabled");
    }

    public void Disable()
    {
        _isEnabled = false;
        _cursorWindow?.Hide();
        _logger.LogInformation("[Issue #497] PhantomCursor disabled");
    }

    private void OnWindowSelectionChanged(WindowSelectionChanged change)
    {
        if (change.CurrentWindow is not null)
        {
            _logger.LogInformation("[Issue #497] Target window: \"{Title}\" (0x{Handle:X})",
                change.CurrentWindow.Title, change.CurrentWindow.Handle);
            SetTargetWindow(change.CurrentWindow.Handle);
        }
        else
        {
            _targetWindowHandle = IntPtr.Zero;
            _cursorWindow?.Hide();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[Issue #497] PhantomCursorHostedService starting");

        if (_cursorStateProvider is null)
        {
            _logger.LogWarning("[Issue #497] ICursorStateProvider not available, PhantomCursor disabled");
            return;
        }

        // 初期化遅延（アプリケーション起動を妨げない）
        await Task.Delay(2000, stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_isEnabled && _targetWindowHandle != IntPtr.Zero)
                {
                    ProcessCursorState();
                }

                await Task.Delay(PollingIntervalMs, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Issue #497] PhantomCursor monitoring error");
                await Task.Delay(1000, stoppingToken).ConfigureAwait(false);
            }
        }

        _cursorWindow?.Dispose();
        _cursorWindow = null;

        _logger.LogInformation("[Issue #497] PhantomCursorHostedService stopped");
    }

    private void ProcessCursorState()
    {
        var shouldShow = ShouldShowPhantomCursor(out var screenX, out var screenY);

        if (shouldShow)
        {
            if (screenX == _lastMouseX && screenY == _lastMouseY)
            {
                _staticFrameCount++;
                if (_staticFrameCount > StaticThresholdFrames)
                    return; // 静止中は描画更新スキップ
            }
            else
            {
                _staticFrameCount = 0;
                _lastMouseX = screenX;
                _lastMouseY = screenY;
            }

            EnsureCursorWindow();
            _cursorWindow?.UpdatePosition(screenX, screenY);
            _cursorWindow?.Show();

            if (!_wasShowing)
            {
                var state = _cursorStateProvider!.GetCursorState();
                _logger.LogInformation(
                    "[Issue #497] PhantomCursor shown - HiddenType: {HiddenType}, flags: 0x{Flags:X}, hCursor: 0x{Cursor:X}",
                    state.HiddenType, state.Flags, state.CursorHandle);
                _wasShowing = true;
            }
        }
        else
        {
            _cursorWindow?.Hide();
            _staticFrameCount = 0;

            if (_wasShowing)
            {
                _logger.LogDebug("[Issue #497] PhantomCursor hidden");
                _wasShowing = false;
            }
        }
    }

    private bool ShouldShowPhantomCursor(out int screenX, out int screenY)
    {
        screenX = 0;
        screenY = 0;

        if (_cursorStateProvider is null)
            return false;

        var state = _cursorStateProvider.GetCursorState();

        // 条件1: システムカーソルが非表示か
        if (!state.IsHidden)
        {
            screenX = state.ScreenX;
            screenY = state.ScreenY;
            return false;
        }

        screenX = state.ScreenX;
        screenY = state.ScreenY;

        // 条件2: 翻訳対象ウィンドウまたはBaketaがフォアグラウンドか
        if (!_cursorStateProvider.IsWindowForeground(_targetWindowHandle))
            return false;

        // 条件3: マウスが対象ウィンドウのクライアント領域内か
        if (!_cursorStateProvider.IsPointInClientArea(_targetWindowHandle, screenX, screenY))
            return false;

        return true;
    }

    private void EnsureCursorWindow()
    {
        if (_cursorWindow is not null) return;

        try
        {
            _cursorWindow = _windowFactory?.Invoke(_logger);
            _logger.LogInformation("[Issue #497] PhantomCursorWindow created");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Issue #497] Failed to create PhantomCursorWindow");
        }
    }

    public override void Dispose()
    {
        _cursorWindow?.Dispose();
        _cursorWindow = null;
        base.Dispose();
    }
}
