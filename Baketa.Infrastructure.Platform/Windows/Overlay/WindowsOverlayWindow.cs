using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Baketa.Core.UI.Overlay;
using CoreGeometry = Baketa.Core.UI.Geometry;

namespace Baketa.Infrastructure.Platform.Windows.Overlay;

/// <summary>
/// Windows透過オーバーレイウィンドウの実装
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsOverlayWindow : IOverlayWindow
{
    private readonly ILogger<WindowsOverlayWindow>? _logger;
    private readonly List<CoreGeometry.Rect> _hitTestAreas = [];
    private readonly object _lock = new();
    
    private bool _isClickThrough = true;
    private CoreGeometry.Point _position;
    private CoreGeometry.Size _size;
    private nint _targetWindowHandle;
    private bool _disposed;
    
    // Windows API デリゲート
    private readonly WindowProc _windowProc;
    private readonly GCHandle _windowProcHandle;
    
    // 固定設定
    private const double DefaultOpacity = 0.9;
    private const string WindowClassName = "BaketaOverlayWindow";
    
    // プロパティ
    private nint WindowHandle { get; set; }
    private bool IsVisibleInternal { get; set; }
    
    /// <summary>
    /// ウィンドウプロシージャのデリゲート
    /// </summary>
    private delegate nint WindowProc(nint hWnd, uint msg, nint wParam, nint lParam);
    
    /// <summary>
    /// オブジェクトが破棄されている場合に例外をスローします
    /// </summary>
    /// <param name="disposed">破棄状態</param>
    /// <param name="instance">オブジェクトインスタンス</param>
    private static void ThrowIfDisposed(bool disposed, object instance)
    {
        if (disposed)
        {
            ThrowObjectDisposedException(instance);
        }
    }
    
    /// <summary>
    /// ObjectDisposedException をスローします
    /// </summary>
    /// <param name="instance">オブジェクトインスタンス</param>
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void ThrowObjectDisposedException(object instance)
    {
        throw new ObjectDisposedException(instance.GetType().Name);
    }
    
    public WindowsOverlayWindow(
        CoreGeometry.Size initialSize,
        CoreGeometry.Point initialPosition,
        nint targetWindowHandle,
        ILogger<WindowsOverlayWindow>? logger = null)
    {
        _logger = logger;
        _size = initialSize;
        _position = initialPosition;
        _targetWindowHandle = targetWindowHandle;
        
        // ウィンドウプロシージャのデリゲートを作成してGCから保護
        _windowProc = WndProc;
        _windowProcHandle = GCHandle.Alloc(_windowProc);
        
        InitializeWindow();
        
        _logger?.LogDebug("WindowsOverlayWindow created. Handle: {Handle}, Size: {Size}, Position: {Position}", 
            WindowHandle, _size, _position);
    }
    
    // === IOverlayWindow プロパティ実装 ===
    
    public bool IsVisible => IsVisibleInternal;
    
    nint IOverlayWindow.Handle => WindowHandle;
    
    public double Opacity => DefaultOpacity;
    
    public bool IsClickThrough
    {
        get => _isClickThrough;
        set
        {
            if (_isClickThrough != value)
            {
                _isClickThrough = value;
                UpdateWindowStyles();
                _logger?.LogDebug("Click-through changed to: {IsClickThrough}", _isClickThrough);
            }
        }
    }
    
    public IReadOnlyList<CoreGeometry.Rect> HitTestAreas
    {
        get
        {
            lock (_lock)
            {
                return [.. _hitTestAreas];
            }
        }
    }
    
    public CoreGeometry.Point Position
    {
        get => _position;
        set
        {
            if (_position != value)
            {
                _position = value;
                UpdateWindowPosition();
            }
        }
    }
    
    public CoreGeometry.Size Size
    {
        get => _size;
        set
        {
            if (_size != value)
            {
                _size = value;
                UpdateWindowSize();
            }
        }
    }
    
    public nint TargetWindowHandle
    {
        get => _targetWindowHandle;
        set
        {
            if (_targetWindowHandle != value)
            {
                _targetWindowHandle = value;
                _logger?.LogDebug("Target window handle changed to: {Handle}", _targetWindowHandle);
            }
        }
    }
    
    // === IOverlayWindow メソッド実装 ===
    
    public void Show()
    {
        ThrowIfDisposed(_disposed, this);
        
        if (!IsVisibleInternal)
        {
            if (OverlayInterop.ShowWindow(WindowHandle, OverlayInterop.SW_SHOWNOACTIVATE))
            {
                IsVisibleInternal = true;
                _logger?.LogDebug("Overlay window shown");
            }
            else
            {
                var error = OverlayInterop.GetLastError();
                _logger?.LogError("Failed to show overlay window. Error: {Error}", error);
            }
        }
    }
    
    public void Hide()
    {
        ThrowIfDisposed(_disposed, this);
        
        if (IsVisibleInternal)
        {
            if (OverlayInterop.ShowWindow(WindowHandle, OverlayInterop.SW_HIDE))
            {
                IsVisibleInternal = false;
                _logger?.LogDebug("Overlay window hidden");
            }
            else
            {
                var error = OverlayInterop.GetLastError();
                _logger?.LogError("Failed to hide overlay window. Error: {Error}", error);
            }
        }
    }
    
    public void AddHitTestArea(CoreGeometry.Rect area)
    {
        ThrowIfDisposed(_disposed, this);
        
        lock (_lock)
        {
            _hitTestAreas.Add(area);
        }
        
        _logger?.LogDebug("Hit test area added: {Area}", area);
    }
    
    public void RemoveHitTestArea(CoreGeometry.Rect area)
    {
        ThrowIfDisposed(_disposed, this);
        
        lock (_lock)
        {
            _hitTestAreas.Remove(area);
        }
        
        _logger?.LogDebug("Hit test area removed: {Area}", area);
    }
    
    public void ClearHitTestAreas()
    {
        ThrowIfDisposed(_disposed, this);
        
        lock (_lock)
        {
            _hitTestAreas.Clear();
        }
        
        _logger?.LogDebug("All hit test areas cleared");
    }
    
    public void UpdateContent(object? content = null)
    {
        ThrowIfDisposed(_disposed, this);
        
        // MVP実装: 基本的なビットマップ転送
        try
        {
            UpdateLayeredWindowFromContent(content);
            _logger?.LogDebug("Content updated successfully");
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogError(ex, "Failed to update content due to invalid operation");
            throw;
        }
        catch (ExternalException ex)
        {
            _logger?.LogError(ex, "Failed to update content due to external error");
            throw;
        }
    }
    
    public void AdjustToTargetWindow()
    {
        ThrowIfDisposed(_disposed, this);
        
        if (_targetWindowHandle == nint.Zero)
        {
            return;
        }
        
        if (OverlayInterop.GetWindowRect(_targetWindowHandle, out var targetRect))
        {
            // ターゲットウィンドウの位置に合わせて調整
            var newPosition = new CoreGeometry.Point(targetRect.Left, targetRect.Top);
            Position = newPosition;
            
            _logger?.LogDebug("Adjusted to target window. New position: {Position}", newPosition);
        }
        else
        {
            var error = OverlayInterop.GetLastError();
            _logger?.LogWarning("Failed to get target window rect. Error: {Error}", error);
        }
    }
    
    public void Close()
    {
        ThrowIfDisposed(_disposed, this);
        
        try
        {
            Hide();
            
            if (WindowHandle != nint.Zero)
            {
                if (OverlayInterop.DestroyWindow(WindowHandle))
                {
                    WindowHandle = nint.Zero;
                    _logger?.LogDebug("Overlay window closed successfully");
                }
                else
                {
                    var error = OverlayInterop.GetLastError();
                    _logger?.LogError("Failed to destroy window. Error: {Error}", error);
                }
            }
        }
        catch (ExternalException ex)
        {
            _logger?.LogError(ex, "External exception while closing overlay window");
            throw;
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogError(ex, "Invalid operation while closing overlay window");
            throw;
        }
    }
    
    // === プライベートメソッド ===
    
    private void InitializeWindow()
    {
        // ウィンドウクラスを登録
        RegisterWindowClass();
        
        // ウィンドウを作成
        CreateWindow();
        
        // レイヤードウィンドウの属性を設定
        SetupLayeredWindow();
    }
    
    private void RegisterWindowClass()
    {
        var hInstance = OverlayInterop.GetModuleHandleW(null);
        var hCursor = OverlayInterop.LoadCursorW(nint.Zero, new nint(32512)); // IDC_ARROW
        
        var wndClass = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_windowProc),
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = hInstance,
            hIcon = nint.Zero,
            hCursor = hCursor,
            hbrBackground = nint.Zero,
            lpszMenuName = null,
            lpszClassName = WindowClassName,
            hIconSm = nint.Zero
        };
        
        var result = OverlayInterop.RegisterClassExW(ref wndClass);
        if (result == 0)
        {
            var error = OverlayInterop.GetLastError();
            if (error != 1410) // クラスが既に存在する場合のエラーコードは無視
            {
                _logger?.LogError("Failed to register window class. Error: {Error}", error);
                throw new InvalidOperationException($"Failed to register window class. Error: {error}");
            }
        }
    }
    
    private void CreateWindow()
    {
        var hInstance = OverlayInterop.GetModuleHandleW(null);
        
        var exStyle = OverlayInterop.WS_EX_LAYERED | 
                     OverlayInterop.WS_EX_TOPMOST | 
                     OverlayInterop.WS_EX_TOOLWINDOW |
                     OverlayInterop.WS_EX_NOACTIVATE;
        
        if (_isClickThrough)
        {
            exStyle |= OverlayInterop.WS_EX_TRANSPARENT;
        }
        
        WindowHandle = OverlayInterop.CreateWindowExW(
            exStyle,
            WindowClassName,
            "Baketa Overlay",
            OverlayInterop.WS_POPUP,
            (int)_position.X,
            (int)_position.Y,
            (int)_size.Width,
            (int)_size.Height,
            nint.Zero,
            nint.Zero,
            hInstance,
            nint.Zero);
        
        if (WindowHandle == nint.Zero)
        {
            var error = OverlayInterop.GetLastError();
            _logger?.LogError("Failed to create window. Error: {Error}", error);
            throw new InvalidOperationException($"Failed to create window. Error: {error}");
        }
    }
    
    private void SetupLayeredWindow()
    {
        // 固定透明度を設定
        var alpha = (byte)(DefaultOpacity * 255);
        
        if (!OverlayInterop.SetLayeredWindowAttributes(WindowHandle, 0, alpha, OverlayInterop.LWA_ALPHA))
        {
            var error = OverlayInterop.GetLastError();
            _logger?.LogError("Failed to set layered window attributes. Error: {Error}", error);
        }
    }
    
    private void UpdateWindowStyles()
    {
        var currentExStyle = OverlayInterop.GetWindowLongW(WindowHandle, OverlayInterop.GWL_EXSTYLE);
        
        if (_isClickThrough)
        {
            currentExStyle |= OverlayInterop.WS_EX_TRANSPARENT;
        }
        else
        {
            currentExStyle &= ~(uint)OverlayInterop.WS_EX_TRANSPARENT;
        }
        
        var result = OverlayInterop.SetWindowLongW(WindowHandle, OverlayInterop.GWL_EXSTYLE, currentExStyle);
        if (result == 0)
        {
            var error = OverlayInterop.GetLastError();
            _logger?.LogWarning("SetWindowLongW returned 0. Error: {Error}", error);
        }
    }
    
    private void UpdateWindowPosition()
    {
        if (!OverlayInterop.SetWindowPos(
            WindowHandle,
            OverlayInterop.HWND_TOPMOST,
            (int)_position.X,
            (int)_position.Y,
            0,
            0,
            OverlayInterop.SWP_NOSIZE | OverlayInterop.SWP_NOACTIVATE))
        {
            var error = OverlayInterop.GetLastError();
            _logger?.LogWarning("Failed to update window position. Error: {Error}", error);
        }
    }
    
    private void UpdateWindowSize()
    {
        if (!OverlayInterop.SetWindowPos(
            WindowHandle,
            nint.Zero,
            0,
            0,
            (int)_size.Width,
            (int)_size.Height,
            OverlayInterop.SWP_NOMOVE | OverlayInterop.SWP_NOZORDER | OverlayInterop.SWP_NOACTIVATE))
        {
            var error = OverlayInterop.GetLastError();
            _logger?.LogWarning("Failed to update window size. Error: {Error}", error);
        }
    }
    
    private void UpdateLayeredWindowFromContent(object? _)
    {
        // MVP実装: 基本的なテストコンテンツを Win32 レイヤードウィンドウに描画
        try
        {
            var pixelWidth = (int)_size.Width;
            var pixelHeight = (int)_size.Height;
            
            // 基本的なテストビットマップを作成して表示
            UpdateLayeredWindowFromTestBitmap(pixelWidth, pixelHeight);
            
            _logger?.LogDebug("Content updated successfully. Size: {Width}x{Height}", pixelWidth, pixelHeight);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to update layered window from content");
            throw;
        }
    }
    
    private void UpdateLayeredWindowFromTestBitmap(int width, int height)
    {
        // Win32 UpdateLayeredWindow APIを使用してビットマップを表示
        nint screenDc = nint.Zero;
        nint memoryDc = nint.Zero;
        nint hBitmap = nint.Zero;
        nint oldBitmap = nint.Zero;
        
        try
        {
            // 画面DCを取得
            screenDc = OverlayInterop.GetDC(nint.Zero);
            if (screenDc == nint.Zero)
            {
                throw new InvalidOperationException("Failed to get screen DC");
            }
            
            // メモリDCを作成
            memoryDc = OverlayInterop.CreateCompatibleDC(screenDc);
            if (memoryDc == nint.Zero)
            {
                throw new InvalidOperationException("Failed to create compatible DC");
            }
            
            // 基本的なテストビットマップを作成
            hBitmap = CreateTestBitmap(screenDc, width, height);
            if (hBitmap == nint.Zero)
            {
                throw new InvalidOperationException("Failed to create test bitmap");
            }
            
            // ビットマップをメモリDCに選択
            oldBitmap = OverlayInterop.SelectObject(memoryDc, hBitmap);
            
            // UpdateLayeredWindow構造体を準備
            var windowPos = new POINT((int)_position.X, (int)_position.Y);
            var windowSize = new SIZE((int)_size.Width, (int)_size.Height);
            var sourcePos = new POINT(0, 0);
            
            var blendFunction = new BLENDFUNCTION
            {
                BlendOp = 0, // AC_SRC_OVER
                BlendFlags = 0,
                SourceConstantAlpha = (byte)(DefaultOpacity * 255),
                AlphaFormat = 1 // AC_SRC_ALPHA
            };
            
            // レイヤードウィンドウを更新
            var result = OverlayInterop.UpdateLayeredWindow(
                WindowHandle,
                screenDc,
                ref windowPos,
                ref windowSize,
                memoryDc,
                ref sourcePos,
                0, // 色キーなし
                ref blendFunction,
                OverlayInterop.ULW_ALPHA);
            
            if (!result)
            {
                var error = OverlayInterop.GetLastError();
                _logger?.LogError("UpdateLayeredWindow failed with error: {Error}", error);
                throw new InvalidOperationException($"UpdateLayeredWindow failed. Error: {error}");
            }
            
            _logger?.LogDebug("Layered window updated successfully");
        }
        finally
        {
            // リソースをクリーンアップ
            if (oldBitmap != nint.Zero)
            {
                OverlayInterop.SelectObject(memoryDc, oldBitmap);
            }
            
            if (hBitmap != nint.Zero)
            {
                OverlayInterop.DeleteObject(hBitmap);
            }
            
            if (memoryDc != nint.Zero)
            {
                OverlayInterop.DeleteDC(memoryDc);
            }
            
            if (screenDc != nint.Zero)
            {
                _ = OverlayInterop.ReleaseDC(nint.Zero, screenDc);
            }
        }
    }
    
    private nint CreateTestBitmap(nint screenDc, int width, int height)
    {
        // MVP実装: シンプルなテストビットマップを作成
        try
        {
            // シンプルなグラデーションビットマップを作成
            var bitmap = OverlayInterop.CreateCompatibleBitmap(screenDc, width, height);
            
            if (bitmap != nint.Zero)
            {
                _logger?.LogDebug("Test bitmap created. Size: {Width}x{Height}", width, height);
            }
            else
            {
                var error = OverlayInterop.GetLastError();
                _logger?.LogError("Failed to create compatible bitmap. Error: {Error}", error);
            }
            
            return bitmap;
        }
        catch (ExternalException ex)
        {
            _logger?.LogError(ex, "External exception while creating test bitmap");
            return nint.Zero;
        }
        catch (OutOfMemoryException ex)
        {
            _logger?.LogError(ex, "Out of memory while creating test bitmap");
            return nint.Zero;
        }
    }
    
    // === ウィンドウプロシージャ ===
    
    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            return msg switch
            {
                OverlayInterop.WM_NCHITTEST => HandleHitTest(lParam),
                OverlayInterop.WM_DESTROY => HandleDestroy(),
                _ => OverlayInterop.DefWindowProcW(hWnd, msg, wParam, lParam)
            };
        }
        catch (AccessViolationException ex)
        {
            _logger?.LogError(ex, "Access violation in window procedure");
            return OverlayInterop.DefWindowProcW(hWnd, msg, wParam, lParam);
        }
        catch (ExternalException ex)
        {
            _logger?.LogError(ex, "External exception in window procedure");
            return OverlayInterop.DefWindowProcW(hWnd, msg, wParam, lParam);
        }
    }
    
    private nint HandleHitTest(nint lParam)
    {
        if (_isClickThrough)
        {
            // クリックスルーが有効な場合、ヒットテスト領域をチェック
            var screenX = (short)(lParam.ToInt64() & 0xFFFF);
            var screenY = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
            
            var point = new POINT(screenX, screenY);
            OverlayInterop.ScreenToClient(WindowHandle, ref point);
            
            var clientPoint = new CoreGeometry.Point(point.X, point.Y);
            
            lock (_lock)
            {
                foreach (var area in _hitTestAreas)
                {
                    if (area.Contains(clientPoint))
                    {
                        return new nint(OverlayInterop.HTCLIENT);
                    }
                }
            }
            
            return new nint(OverlayInterop.HTTRANSPARENT);
        }
        
        return new nint(OverlayInterop.HTCLIENT);
    }
    
    private nint HandleDestroy()
    {
        IsVisibleInternal = false;
        _logger?.LogDebug("WM_DESTROY received");
        return nint.Zero;
    }
    
    // === IDisposable実装 ===
    
    public void Dispose()
    {
        if (!_disposed)
        {
            Hide();
            
            if (WindowHandle != nint.Zero)
            {
                OverlayInterop.DestroyWindow(WindowHandle);
                WindowHandle = nint.Zero;
            }
            
            if (_windowProcHandle.IsAllocated)
            {
                _windowProcHandle.Free();
            }
            
            _disposed = true;
            _logger?.LogDebug("WindowsOverlayWindow disposed");
        }
    }
}