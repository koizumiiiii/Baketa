using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using Baketa.Infrastructure.Platform.Windows.NativeMethods;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Platform.Windows.Overlay;

/// <summary>
/// [Issue #497] ファントムカーソル描画用の軽量LayeredWindowオーバーレイ
/// </summary>
/// <remarks>
/// ゲーム側がシステムカーソルを非表示にしている場合に、
/// 半透明の円形カーソルを代替表示する。
/// LayeredOverlayWindowパターンを踏襲: STAスレッド + GDI32描画 + UpdateLayeredWindow
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class PhantomCursorWindow : IDisposable
{
    private readonly ILogger _logger;

    private readonly Thread _windowThread;
    private IntPtr _hwnd = IntPtr.Zero;
    private readonly ManualResetEventSlim _windowCreatedEvent = new(false);
    private readonly BlockingCollection<Action> _messageQueue = new();
    private bool _disposed;
    private bool _updatePending;
    private bool _isVisible;

    // カーソル描画パラメータ
    private const int CursorDiameter = 30;
    private const int BitmapSize = CursorDiameter + 4; // 外側境界線のマージン
    private const byte CursorAlpha = 153; // ~60% 不透明度

    // GDIリソース
    private IntPtr _hdcScreen;
    private IntPtr _hdcMem;
    private IntPtr _hBitmap;
    private IntPtr _hOldBitmap;

    // ウィンドウクラス
    private const string WindowClassName = "BaketaPhantomCursor";
    private static ushort _windowClassAtom;
    private static readonly object _classLock = new();
    private static WndProcDelegate? _wndProcDelegate;

    private const uint WM_USER = 0x0400;
    private const uint WM_PROCESS_QUEUE = WM_USER + 2;
    private const uint WM_NCHITTEST = 0x0084;
    private const nint HTTRANSPARENT = -1;

    public PhantomCursorWindow(ILogger logger)
    {
        _logger = logger;

        _windowThread = new Thread(WindowThreadProc)
        {
            Name = "PhantomCursor STA Thread",
            IsBackground = true
        };
        _windowThread.SetApartmentState(ApartmentState.STA);
        _windowThread.Start();

        if (!_windowCreatedEvent.Wait(TimeSpan.FromSeconds(5)))
            throw new InvalidOperationException("PhantomCursorWindow creation timeout");

        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException("PhantomCursorWindow HWND is null");

        // 初回描画（円形カーソルのビットマップを一度だけ作成）
        _messageQueue.Add(DrawCursorBitmap);
        TriggerMessageQueueProcessing();

        _logger.LogDebug("[Issue #497] PhantomCursorWindow created - HWND: 0x{Hwnd:X}", _hwnd.ToInt64());
    }

    private void WindowThreadProc()
    {
        try
        {
            RegisterWindowClass();
            CreateWindow();

            if (_hwnd == IntPtr.Zero)
            {
                _windowCreatedEvent.Set();
                return;
            }

            _windowCreatedEvent.Set();

            while (LayeredWindowMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0))
            {
                LayeredWindowMethods.TranslateMessage(ref msg);
                LayeredWindowMethods.DispatchMessage(ref msg);

                while (_messageQueue.TryTake(out var action, 0))
                {
                    try { action(); }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[Issue #497] PhantomCursor message queue error");
                    }
                }
                _updatePending = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Issue #497] PhantomCursor thread error");
            _windowCreatedEvent.Set();
        }
        finally
        {
            CleanupGdiResources();
        }
    }

    private void RegisterWindowClass()
    {
        lock (_classLock)
        {
            if (_windowClassAtom != 0) return;

            _wndProcDelegate = new WndProcDelegate(WndProc);
            var wndProcPtr = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);

            var wndClass = new WNDCLASS
            {
                lpfnWndProc = wndProcPtr,
                hInstance = User32Methods.GetModuleHandle(null),
                lpszClassName = WindowClassName,
                hCursor = IntPtr.Zero,
                hbrBackground = IntPtr.Zero,
            };

            _windowClassAtom = User32Methods.RegisterClass(ref wndClass);
            if (_windowClassAtom == 0)
                throw new InvalidOperationException($"RegisterClass failed for {WindowClassName}");
        }
    }

    private void CreateWindow()
    {
        const uint exStyle = LayeredWindowMethods.WS_EX_LAYERED
                           | (uint)ExtendedWindowStyles.WS_EX_TRANSPARENT
                           | LayeredWindowMethods.WS_EX_NOACTIVATE
                           | (uint)ExtendedWindowStyles.WS_EX_TOPMOST
                           | (uint)ExtendedWindowStyles.WS_EX_TOOLWINDOW;

        _hwnd = User32Methods.CreateWindowEx(
            exStyle,
            _windowClassAtom,
            "Baketa PhantomCursor",
            (uint)WindowStyles.WS_POPUP,
            0, 0, BitmapSize, BitmapSize,
            IntPtr.Zero, IntPtr.Zero,
            User32Methods.GetModuleHandle(null),
            IntPtr.Zero);
    }

    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_NCHITTEST)
            return HTTRANSPARENT;
        return User32Methods.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    /// <summary>
    /// カーソルビットマップを描画（円形二重縁取り）
    /// </summary>
    private void DrawCursorBitmap()
    {
        if (_hwnd == IntPtr.Zero) return;

        try
        {
            CleanupGdiResources();

            _hdcScreen = User32Methods.GetDC(IntPtr.Zero);
            if (_hdcScreen == IntPtr.Zero) return;

            _hdcMem = LayeredWindowMethods.CreateCompatibleDC(_hdcScreen);
            if (_hdcMem == IntPtr.Zero) return;

            var bmi = new BITMAPINFO
            {
                bmiHeader = BITMAPINFOHEADER.Create32BitARGB(BitmapSize, BitmapSize),
                bmiColors = new uint[1]
            };

            _hBitmap = LayeredWindowMethods.CreateDIBSection(
                _hdcMem, ref bmi, LayeredWindowMethods.DIB_RGB_COLORS,
                out _, IntPtr.Zero, 0);
            if (_hBitmap == IntPtr.Zero) return;

            _hOldBitmap = LayeredWindowMethods.SelectObject(_hdcMem, _hBitmap);

            using (var g = Graphics.FromHdc(_hdcMem))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;

                var offset = (BitmapSize - CursorDiameter) / 2;
                var outerRect = new Rectangle(offset, offset, CursorDiameter - 1, CursorDiameter - 1);
                var innerRect = new Rectangle(offset + 2, offset + 2, CursorDiameter - 5, CursorDiameter - 5);

                // 外側: 黒縁（1px）
                using var outerPen = new Pen(Color.FromArgb(CursorAlpha, 0, 0, 0), 1.5f);
                g.DrawEllipse(outerPen, outerRect);

                // 内側: 白縁（1px）
                using var innerPen = new Pen(Color.FromArgb(CursorAlpha, 255, 255, 255), 1.5f);
                g.DrawEllipse(innerPen, innerRect);

                // 中央点
                var centerSize = 4;
                var cx = BitmapSize / 2 - centerSize / 2;
                var cy = BitmapSize / 2 - centerSize / 2;
                using var centerBrush = new SolidBrush(Color.FromArgb(CursorAlpha, 255, 255, 255));
                g.FillEllipse(centerBrush, cx, cy, centerSize, centerSize);
            }

            // UpdateLayeredWindowで描画内容を適用（位置は後で更新するので0,0）
            ApplyLayeredContent(0, 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Issue #497] DrawCursorBitmap error");
        }
    }

    /// <summary>
    /// カーソル位置を更新（スクリーン座標）
    /// </summary>
    public void UpdatePosition(int screenX, int screenY)
    {
        if (_disposed || _hwnd == IntPtr.Zero) return;

        // カーソルの中心がマウス位置に来るようにオフセット
        var x = screenX - BitmapSize / 2;
        var y = screenY - BitmapSize / 2;

        _messageQueue.Add(() =>
        {
            if (_hwnd == IntPtr.Zero) return;
            ApplyLayeredContent(x, y);
        });
        TriggerMessageQueueProcessing();
    }

    private void ApplyLayeredContent(int x, int y)
    {
        if (_hdcMem == IntPtr.Zero || _hdcScreen == IntPtr.Zero) return;

        var pptDst = new NativeMethods.POINT(x, y);
        var psize = new NativeMethods.SIZE(BitmapSize, BitmapSize);
        var pptSrc = new NativeMethods.POINT(0, 0);

        var blend = new NativeMethods.BLENDFUNCTION
        {
            BlendOp = 0,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = 1
        };

        LayeredWindowMethods.UpdateLayeredWindow(
            _hwnd, _hdcScreen, ref pptDst, ref psize,
            _hdcMem, ref pptSrc, 0, ref blend,
            UpdateLayeredWindowFlags.ULW_ALPHA);
    }

    public void Show()
    {
        if (_disposed || _isVisible) return;

        _messageQueue.Add(() =>
        {
            if (_hwnd != IntPtr.Zero && !_isVisible)
            {
                LayeredWindowMethods.ShowWindow(_hwnd, ShowWindowCommands.SW_SHOWNOACTIVATE);
                _isVisible = true;
            }
        });
        TriggerMessageQueueProcessing();
    }

    public void Hide()
    {
        if (_disposed || !_isVisible) return;

        _messageQueue.Add(() =>
        {
            if (_hwnd != IntPtr.Zero && _isVisible)
            {
                LayeredWindowMethods.ShowWindow(_hwnd, ShowWindowCommands.SW_HIDE);
                _isVisible = false;
            }
        });
        TriggerMessageQueueProcessing();
    }

    public bool IsVisible => _isVisible;

    private void TriggerMessageQueueProcessing()
    {
        if (_hwnd != IntPtr.Zero && !_updatePending)
        {
            _updatePending = true;
            LayeredWindowMethods.PostMessage(_hwnd, WM_PROCESS_QUEUE, IntPtr.Zero, IntPtr.Zero);
        }
    }

    private void CleanupGdiResources()
    {
        if (_hOldBitmap != IntPtr.Zero && _hdcMem != IntPtr.Zero)
        {
            LayeredWindowMethods.SelectObject(_hdcMem, _hOldBitmap);
            _hOldBitmap = IntPtr.Zero;
        }
        if (_hBitmap != IntPtr.Zero)
        {
            LayeredWindowMethods.DeleteObject(_hBitmap);
            _hBitmap = IntPtr.Zero;
        }
        if (_hdcMem != IntPtr.Zero)
        {
            LayeredWindowMethods.DeleteDC(_hdcMem);
            _hdcMem = IntPtr.Zero;
        }
        if (_hdcScreen != IntPtr.Zero)
        {
#pragma warning disable CA1806
            User32Methods.ReleaseDC(IntPtr.Zero, _hdcScreen);
#pragma warning restore CA1806
            _hdcScreen = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _messageQueue.Add(() =>
        {
            if (_hwnd != IntPtr.Zero)
            {
                User32Methods.DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
            LayeredWindowMethods.PostQuitMessage(0);
        });
        TriggerMessageQueueProcessing();
        _messageQueue.CompleteAdding();

        _windowThread.Join(TimeSpan.FromSeconds(3));
        _windowCreatedEvent.Dispose();
        _messageQueue.Dispose();

        _disposed = true;
        _logger.LogDebug("[Issue #497] PhantomCursorWindow disposed");
    }
}
