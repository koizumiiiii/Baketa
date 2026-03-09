using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using Baketa.Infrastructure.Platform.Windows.NativeMethods;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Platform.Windows.Overlay;

/// <summary>
/// DWM Composition + Blurベースのオーバーレイウィンドウ実装
/// </summary>
/// <remarks>
/// 🎯 [DWM_BLUR_IMPLEMENTATION] Phase 2: すりガラス効果対応オーバーレイ
///
/// 🔥 [KEY_DIFFERENCES] LayeredOverlayWindow との違い:
/// 1. WS_EX_LAYERED 不使用 → UpdateLayeredWindow 不使用
/// 2. DwmExtendFrameIntoClientArea でウィンドウ全体をガラス化
/// 3. DwmEnableBlurBehindWindow でブラー効果適用
/// 4. WM_PAINT ハンドラでGDI+描画（透過背景 + テキスト）
/// 5. SetLayeredWindowAttributes 不要（DWM Compositionが透過処理）
///
/// アーキテクチャ:
/// - STAスレッド + メッセージループ（LayeredOverlayWindow と同様）
/// - スレッドセーフなメッセージキュー
/// - GDI+ でビットマップ描画 → WM_PAINT で描画
/// - DWM Composition による透過とブラー効果
///
/// 対応OS:
/// - Windows Vista以降（DWM Composition必須）
/// - Windows 10/11 推奨（安定したブラー効果）
/// </remarks>
[SupportedOSPlatform("windows6.0")] // Windows Vista+
public sealed class CompositionOverlayWindow : ILayeredOverlayWindow
{
    private readonly ILogger<CompositionOverlayWindow> _logger;
    private readonly bool _enableBlur;
    private readonly byte _blurOpacity;

    // 🔥 [GEMINI_RECOMMENDATION] STAスレッド関連
    private readonly Thread? _windowThread;
    private IntPtr _hwnd = IntPtr.Zero;
    private readonly ManualResetEventSlim _windowCreatedEvent = new(false);
    private bool _disposed;

    // 🔥 [GEMINI_RECOMMENDATION] スレッドセーフなメッセージキュー
    private readonly BlockingCollection<Action> _messageQueue = new();

    // ウィンドウ状態
    private bool _isVisible;
    private string _currentText = string.Empty;
    private int _currentX;
    private int _currentY;
    private int _currentWidth = 200;
    private int _currentHeight = 50;
    private int _originalHeight = 50;
    private Color _backgroundColor = Color.FromArgb(200, 240, 240, 240); // 半透明白（ブラー用）
    private float _fontSize = 14f;
    private string _fontFamily = "Segoe UI"; // フォントファミリー（言語別に設定可能）

    // 🔥 [MESSAGE_COALESCING] メッセージ集約用フラグ
    private bool _updatePending;

    // ウィンドウクラス名
    private const string WINDOW_CLASS_NAME = "BaketaCompositionOverlay";
    private static ushort _windowClassAtom;
    private static readonly object _classLock = new();

    // カスタムメッセージ定義
    private const uint WM_USER = 0x0400;
    private const uint WM_PROCESS_QUEUE = WM_USER + 1;
    private const uint WM_PAINT = 0x000F;
    private const uint WM_ERASEBKGND = 0x0014;
    private const uint WM_NCHITTEST = 0x0084;
    private const nint HTTRANSPARENT = -1;

    // 🔥 [P0_GC_FIX] WndProcDelegateをstaticフィールドで保持してGCから保護
    private static NativeMethods.WndProcDelegate? _wndProcDelegate;

    // 🎯 [DWM_BLUR] 各インスタンスのWndProcを識別するためのマップ
    private static readonly ConcurrentDictionary<IntPtr, CompositionOverlayWindow> _instanceMap = new();

    public CompositionOverlayWindow(
        ILogger<CompositionOverlayWindow> logger,
        bool enableBlur = true,
        byte blurOpacity = 200)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _enableBlur = enableBlur;
        _blurOpacity = blurOpacity;

        _logger.LogDebug("🚀 [DWM_OVERLAY] CompositionOverlayWindow コンストラクター開始 (Blur: {EnableBlur}, Opacity: {Opacity})",
            _enableBlur, _blurOpacity);

        // 🔥 [GEMINI_CRITICAL] STAスレッド起動
        _windowThread = new Thread(WindowThreadProc)
        {
            Name = "Win32 Composition Overlay Thread",
            IsBackground = true
        };
        _windowThread.SetApartmentState(ApartmentState.STA);
        _windowThread.Start();

        // ウィンドウ作成完了を待機（タイムアウト5秒）
        if (!_windowCreatedEvent.Wait(TimeSpan.FromSeconds(5)))
        {
            _logger.LogError("❌ [DWM_OVERLAY] ウィンドウ作成タイムアウト - STAスレッド起動失敗");
            throw new InvalidOperationException("Failed to create DWM composition window - STA thread timeout");
        }

        if (_hwnd == IntPtr.Zero)
        {
            _logger.LogError("❌ [DWM_OVERLAY] ウィンドウハンドル取得失敗");
            throw new InvalidOperationException("Failed to create DWM composition window - HWND is null");
        }

        _logger.LogInformation("✅ [DWM_OVERLAY] CompositionOverlayWindow 作成完了 - HWND: 0x{Hwnd:X}", _hwnd.ToInt64());
    }

    /// <summary>
    /// STAスレッドのメインプロシージャ
    /// </summary>
    private void WindowThreadProc()
    {
        try
        {
            _logger.LogDebug("🔄 [STA_THREAD] WindowThreadProc 開始");

            // ウィンドウクラス登録（初回のみ）
            RegisterWindowClass();

            // ウィンドウ作成
            CreateWindow();

            if (_hwnd == IntPtr.Zero)
            {
                _logger.LogError("❌ [STA_THREAD] CreateWindow失敗");
                _windowCreatedEvent.Set();
                return;
            }

            // 🎯 [DWM_BLUR] インスタンスマップに登録（WndProcから参照）
            _instanceMap.TryAdd(_hwnd, this);

            // 🔥 [DWM_BLUR] DWM Compositionとブラー効果を適用
            ApplyDwmEffects();

            _logger.LogDebug("✅ [STA_THREAD] ウィンドウ作成成功 - HWND: 0x{Hwnd:X}", _hwnd.ToInt64());
            _windowCreatedEvent.Set();

            // 🔥 [GEMINI_CRITICAL] メッセージループ
            _logger.LogDebug("🔄 [STA_THREAD] メッセージループ開始");

            while (LayeredWindowMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0))
            {
                LayeredWindowMethods.TranslateMessage(ref msg);
                LayeredWindowMethods.DispatchMessage(ref msg);

                // カスタムメッセージキュー処理
                while (_messageQueue.TryTake(out var action, 0))
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ [STA_THREAD] メッセージキュー処理中に例外発生");
                    }
                }

                // 🔥 [MESSAGE_COALESCING] キュー処理完了後にフラグをリセット
                _updatePending = false;
            }

            _logger.LogDebug("🔄 [STA_THREAD] メッセージループ終了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [STA_THREAD] WindowThreadProc で例外発生");
            _windowCreatedEvent.Set();
        }
        finally
        {
            // インスタンスマップから削除
            if (_hwnd != IntPtr.Zero)
            {
                _instanceMap.TryRemove(_hwnd, out _);
            }
        }
    }

    /// <summary>
    /// ウィンドウクラスを登録
    /// </summary>
    private void RegisterWindowClass()
    {
        lock (_classLock)
        {
            if (_windowClassAtom != 0)
            {
                _logger.LogDebug("ℹ️ [STA_THREAD] ウィンドウクラス既に登録済み - Atom: {Atom}", _windowClassAtom);
                return;
            }

            // 🔥 [P0_GC_FIX] デリゲートをstaticフィールドで保持（GC保護）
            _wndProcDelegate = new NativeMethods.WndProcDelegate(StaticWndProc);
            var wndProcPtr = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);

            var wndClass = new WNDCLASS
            {
                lpfnWndProc = wndProcPtr,
                hInstance = User32Methods.GetModuleHandle(null),
                lpszClassName = WINDOW_CLASS_NAME,
                hCursor = IntPtr.Zero,
                hbrBackground = (IntPtr)5, // 🎯 [ACRYLIC_BLUR] NULL_BRUSH (5) - 背景描画を抑制
                style = 0,
                cbClsExtra = 0,
                cbWndExtra = 0,
                hIcon = IntPtr.Zero,
                lpszMenuName = null
            };

            _windowClassAtom = User32Methods.RegisterClass(ref wndClass);

            if (_windowClassAtom == 0)
            {
                var error = Marshal.GetLastWin32Error();
                _logger.LogError("❌ [STA_THREAD] RegisterClass失敗 - Error Code: {ErrorCode}", error);
                throw new InvalidOperationException($"Failed to register window class - Error: {error}");
            }

            _logger.LogInformation("✅ [STA_THREAD] ウィンドウクラス登録成功 - Atom: {Atom}", _windowClassAtom);
        }
    }

    /// <summary>
    /// ウィンドウを作成
    /// </summary>
    private void CreateWindow()
    {
        // 🔧 [White Background Fix] WS_EX_LAYEREDを削除
        // WS_EX_LAYEREDはSetLayeredWindowAttributesまたはUpdateLayeredWindowが必要で、
        // どちらもDWM Compositionのブラー効果と競合する
        // クリックスルーはWS_EX_TRANSPARENT + WM_NCHITTEST(HTTRANSPARENT)のみで対応
        const uint exStyle = (uint)ExtendedWindowStyles.WS_EX_TRANSPARENT
                           | LayeredWindowMethods.WS_EX_NOACTIVATE
                           | (uint)ExtendedWindowStyles.WS_EX_TOPMOST;

        const uint style = (uint)WindowStyles.WS_POPUP;

        _hwnd = User32Methods.CreateWindowEx(
            exStyle,
            _windowClassAtom,
            "Baketa Overlay (DWM)",
            style,
            0, 0,
            _currentWidth, _currentHeight,
            IntPtr.Zero,
            IntPtr.Zero,
            User32Methods.GetModuleHandle(null),
            IntPtr.Zero
        );

        if (_hwnd == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            _logger.LogError("❌ [STA_THREAD] CreateWindowEx失敗 - Error Code: {ErrorCode}", error);
            throw new InvalidOperationException($"CreateWindowEx failed - Error: {error}");
        }

        // 🔧 [White Background Fix] SetLayeredWindowAttributesはApplyDwmEffects()後に呼び出す
        // DWM Composition適用前に呼び出すと白背景問題が発生する
    }

    /// <summary>
    /// DWM CompositionとBlur効果を適用
    /// </summary>
    private void ApplyDwmEffects()
    {
        if (_hwnd == IntPtr.Zero) return;

        try
        {
            // 1. DWM Compositionが有効か確認
            if (!DwmApiMethods.IsCompositionSupported())
            {
                _logger.LogWarning("⚠️ [DWM_BLUR] DWM Composition未サポート - ブラー効果なし");
                return;
            }

            // 2. ウィンドウ全体をガラス化（DWM Compositionに参加）
            // 🔥 [CRITICAL_FIX] DwmExtendFrameIntoClientAreaとSetWindowCompositionAttributeが競合する
            // SetWindowCompositionAttributeを使用する場合は、DWMガラス化は不要
            // var margins = DwmApiMethods.MARGINS.CreateFullWindow();
            // var hr = DwmApiMethods.DwmExtendFrameIntoClientArea(_hwnd, ref margins);
            //
            // if (DwmApiMethods.FAILED(hr))
            // {
            //     _logger.LogError("❌ [DWM_BLUR] DwmExtendFrameIntoClientArea失敗 - HRESULT: 0x{Hr:X}", hr);
            //     return;
            // }
            //
            // _logger.LogDebug("✅ [DWM_BLUR] ウィンドウガラス化成功");
            _logger.LogDebug("🔥 [ACRYLIC_FIX] DWMガラス化をスキップ（SetWindowCompositionAttribute使用のため）");

            // 3. ブラー効果適用（SetWindowCompositionAttribute使用）
            if (_enableBlur)
            {
                ApplyWindowsBlurEffect();
            }

            // 4. 🔧 [White Background Fix] WS_EX_LAYERED未使用のためSetLayeredWindowAttributesも不要
            // DWM CompositionはWS_EX_LAYERED無しで正しく動作する
            _logger.LogDebug("✅ [DWM_BLUR] WS_EX_LAYERED未使用（DWM Compositionのみ）");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [DWM_BLUR] DWM効果適用中に例外発生");
        }
    }

    /// <summary>
    /// SetWindowCompositionAttributeを使用してWindows 10/11のブラー効果を適用
    /// </summary>
    /// <remarks>
    /// 🔥 [ACRYLIC_BLUR] Windows 10/11専用の非公式API
    /// DwmEnableBlurBehindWindowはWindows 8以降非機能のため、この方法を使用
    /// </remarks>
    private void ApplyWindowsBlurEffect()
    {
        try
        {
            // AccentPolicyを作成
            // GradientColor: AABGR format (0xAABBGGRR)
            // 🔥 黒ベースのオーバーレイ（ライト/ダークモード共通）
            // ACCENT_ENABLE_BLURBEHIND: ガウスぼかし風のブラー効果
            var accent = new AccentPolicy
            {
                AccentState = AccentState.ACCENT_ENABLE_BLURBEHIND,  // ガウスぼかし風ブラー
                AccentFlags = 0,  // No additional flags
                GradientColor = 0x60000000,  // 約38%不透明の黒
                AnimationId = 0
            };

            // アンマネージドメモリにマーシャリング
            var accentStructSize = Marshal.SizeOf<AccentPolicy>();
            var accentPtr = Marshal.AllocHGlobal(accentStructSize);

            try
            {
                Marshal.StructureToPtr(accent, accentPtr, false);

                // WindowCompositionAttributeDataを作成
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                    Data = accentPtr,
                    SizeOfData = accentStructSize
                };

                // SetWindowCompositionAttributeを呼び出し
                var result = User32Methods.SetWindowCompositionAttribute(_hwnd, ref data);

                if (result != 0)
                {
                    _logger.LogInformation("✅ [ACRYLIC_BLUR] SetWindowCompositionAttribute成功 - すりガラス効果適用");
                }
                else
                {
                    _logger.LogWarning("⚠️ [ACRYLIC_BLUR] SetWindowCompositionAttribute失敗 - Windows 10/11でない可能性");
                }
            }
            finally
            {
                // アンマネージドメモリの解放
                Marshal.FreeHGlobal(accentPtr);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [ACRYLIC_BLUR] すりガラス効果適用中に例外発生");
        }
    }

    /// <summary>
    /// 静的ウィンドウプロシージャ（全インスタンス共有）
    /// </summary>
    private static IntPtr StaticWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        // 🔧 [Issue #340] クリックスルー - インスタンス登録前でも確実に処理
        // WM_NCHITTESTを最優先で処理してマウスイベントを背後のウィンドウに透過
        if (msg == WM_NCHITTEST)
        {
            return HTTRANSPARENT;
        }

        // インスタンスマップからウィンドウに対応するインスタンスを取得
        if (_instanceMap.TryGetValue(hwnd, out var instance))
        {
            return instance.WndProc(hwnd, msg, wParam, lParam);
        }

        return User32Methods.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    /// <summary>
    /// インスタンス固有のウィンドウプロシージャ
    /// </summary>
    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_NCHITTEST:
                // 🔧 [Issue #340] クリックスルー - マウスイベントを背後のウィンドウに透過
                // WS_EX_TRANSPARENTだけではDWM Compositionと干渉するため明示的に処理
                return HTTRANSPARENT;

            case WM_PAINT:
                return HandlePaint(hwnd);

            case WM_ERASEBKGND:
                // 背景消去不要（DWM Compositionが処理）
                return new IntPtr(1);

            default:
                return User32Methods.DefWindowProc(hwnd, msg, wParam, lParam);
        }
    }

    /// <summary>
    /// WM_PAINT メッセージ処理
    /// </summary>
    private IntPtr HandlePaint(IntPtr hwnd)
    {
        try
        {
            // BeginPaint / EndPaint
            var ps = new PAINTSTRUCT();
            var hdc = User32Methods.BeginPaint(hwnd, ref ps);

            if (hdc == IntPtr.Zero)
            {
                _logger.LogError("❌ [WM_PAINT] BeginPaint失敗");
                return IntPtr.Zero;
            }

            try
            {
                // GDI+ で描画
                PaintContent(hdc);
            }
            finally
            {
                User32Methods.EndPaint(hwnd, ref ps);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [WM_PAINT] 描画中に例外発生");
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// ウィンドウコンテンツを描画
    /// </summary>
    private void PaintContent(IntPtr hdc)
    {
        using var graphics = Graphics.FromHdc(hdc);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        // 🔧 [White Background Fix] WS_EX_LAYERED未使用のため背景クリア不要
        // DWM Compositionがブラー効果で背景を処理する
        // 背景色は使用しない（SetWindowCompositionAttributeのGradientColorが背景）

        // テキスト描画（白テキスト、左寄せ・垂直中央）
        if (!string.IsNullOrWhiteSpace(_currentText))
        {
            // 🔥 白テキスト + 黒の影（可読性向上）
            using var shadowBrush = new SolidBrush(Color.FromArgb(180, 0, 0, 0)); // 影（黒）
            using var textBrush = new SolidBrush(Color.FromArgb(255, 255, 255, 255)); // テキスト（白）
            using var font = new Font(_fontFamily, _fontSize, FontStyle.Regular);

            var padding = 8f;
            var textWidth = _currentWidth - padding * 2;

            var lines = GetWrappedTextLines(graphics, _currentText, font, textWidth);
            var lineHeight = font.GetHeight(graphics) * 1.1f;

            using var format = new StringFormat(StringFormat.GenericTypographic)
            {
                FormatFlags = StringFormatFlags.NoWrap,
                Trimming = StringTrimming.None
            };

            // 🔥 テキスト全体の高さを計算して垂直中央に配置
            var totalTextHeight = lines.Count * lineHeight;
            var y = (_currentHeight - totalTextHeight) / 2f; // 垂直中央
            y = Math.Max(padding, y); // 最低でもpaddingは確保

            const float shadowOffset = 1.0f; // ドロップシャドウのオフセット（控えめ）

            foreach (var line in lines)
            {
                if ((y + lineHeight) > _currentHeight) break;

                // 薄い白の影を先に描画（白背景上では控えめに）
                graphics.DrawString(line, font, shadowBrush, new PointF(padding + shadowOffset, y + shadowOffset), format);
                // テキスト本体（黒）- 左寄せ
                graphics.DrawString(line, font, textBrush, new PointF(padding, y), format);
                y += lineHeight;
            }
        }
    }

    // ========================================
    // ILayeredOverlayWindow実装
    // ========================================

    public void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _messageQueue.Add(() =>
        {
            if (!_isVisible && _hwnd != IntPtr.Zero)
            {
                LayeredWindowMethods.ShowWindow(_hwnd, ShowWindowCommands.SW_SHOWNOACTIVATE);
                _isVisible = true;
                _logger.LogDebug("👁️ [DWM_OVERLAY] ウィンドウ表示");
            }
        });

        TriggerMessageQueueProcessing();
    }

    public void Hide()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _messageQueue.Add(() =>
        {
            if (_isVisible && _hwnd != IntPtr.Zero)
            {
                LayeredWindowMethods.ShowWindow(_hwnd, ShowWindowCommands.SW_HIDE);
                _isVisible = false;
                _logger.LogDebug("🙈 [DWM_OVERLAY] ウィンドウ非表示");
            }
        });

        TriggerMessageQueueProcessing();
    }

    public void Close()
    {
        if (_disposed) return;

        _logger.LogDebug("🚪 [DWM_OVERLAY] Close呼び出し");

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
    }

    public void SetText(string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(text)) return;

        _currentText = text;

        _messageQueue.Add(() =>
        {
            if (_hwnd == IntPtr.Zero) return;

            // 高さ自動調整
            AdjustHeightForText();

            // WM_PAINTメッセージを送信して再描画
            User32Methods.InvalidateRect(_hwnd, IntPtr.Zero, false);
            User32Methods.UpdateWindow(_hwnd);
        });

        TriggerMessageQueueProcessing();
    }

    public void SetFontSize(float fontSize)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (fontSize <= 0) return;

        _fontSize = fontSize;

        if (!string.IsNullOrWhiteSpace(_currentText))
        {
            _messageQueue.Add(() =>
            {
                if (_hwnd == IntPtr.Zero) return;

                AdjustHeightForText();
                User32Methods.InvalidateRect(_hwnd, IntPtr.Zero, false);
                User32Methods.UpdateWindow(_hwnd);
            });

            TriggerMessageQueueProcessing();
        }
    }

    public void SetFontFamily(string fontFamily)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (string.IsNullOrWhiteSpace(fontFamily)) return;

        _fontFamily = fontFamily;

        if (!string.IsNullOrWhiteSpace(_currentText))
        {
            _messageQueue.Add(() =>
            {
                if (_hwnd == IntPtr.Zero) return;

                AdjustHeightForText();
                User32Methods.InvalidateRect(_hwnd, IntPtr.Zero, false);
                User32Methods.UpdateWindow(_hwnd);
            });

            TriggerMessageQueueProcessing();
        }
    }

    public void SetPosition(int x, int y)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _currentX = x;
        _currentY = y;

        _messageQueue.Add(() =>
        {
            if (_hwnd == IntPtr.Zero) return;

            LayeredWindowMethods.SetWindowPos(
                _hwnd,
                LayeredWindowMethods.HWND_TOPMOST,
                x, y,
                _currentWidth, _currentHeight,
                SetWindowPosFlags.SWP_NOACTIVATE | SetWindowPosFlags.SWP_NOSIZE
            );
        });

        TriggerMessageQueueProcessing();
    }

    public void SetSize(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (width <= 0 || height <= 0) return;

        _currentWidth = width;
        _currentHeight = height;
        _originalHeight = height;

        _messageQueue.Add(() =>
        {
            if (_hwnd == IntPtr.Zero) return;

            LayeredWindowMethods.SetWindowPos(
                _hwnd,
                LayeredWindowMethods.HWND_TOPMOST,
                _currentX, _currentY,
                width, height,
                SetWindowPosFlags.SWP_NOACTIVATE | SetWindowPosFlags.SWP_NOMOVE
            );

            User32Methods.InvalidateRect(_hwnd, IntPtr.Zero, false);
            User32Methods.UpdateWindow(_hwnd);
        });

        TriggerMessageQueueProcessing();
    }

    public void SetBackgroundColor(byte a, byte r, byte g, byte b)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _backgroundColor = Color.FromArgb(a, r, g, b);

        _messageQueue.Add(() =>
        {
            if (_hwnd == IntPtr.Zero) return;

            User32Methods.InvalidateRect(_hwnd, IntPtr.Zero, false);
            User32Methods.UpdateWindow(_hwnd);
        });

        TriggerMessageQueueProcessing();
    }

    public bool IsVisible => _isVisible;

    public IntPtr WindowHandle => _hwnd;

    // ========================================
    // ヘルパーメソッド
    // ========================================

    /// <summary>
    /// テキストサイズに応じてウィンドウ高さを自動調整
    /// </summary>
    private void AdjustHeightForText()
    {
        if (string.IsNullOrWhiteSpace(_currentText)) return;

        try
        {
            using var tempBitmap = new Bitmap(1, 1);
            using var tempGraphics = Graphics.FromImage(tempBitmap);
            using var font = new Font(_fontFamily, _fontSize, FontStyle.Regular);

            var padding = 8f;
            var textWidth = _currentWidth - padding * 2;

            var lines = GetWrappedTextLines(tempGraphics, _currentText, font, textWidth);
            var lineHeight = font.GetHeight(tempGraphics) * 1.1f;
            var textHeight = lines.Count * lineHeight;
            var requiredHeight = (int)(textHeight + padding * 2);

            requiredHeight = Math.Max(_originalHeight, requiredHeight);

            if (requiredHeight != _currentHeight)
            {
                _currentHeight = requiredHeight;

                // 画面境界チェック
                var screenHeight = System.Windows.Forms.Screen.FromPoint(
                    new System.Drawing.Point(_currentX, _currentY)).Bounds.Height;

                if (_currentY + _currentHeight > screenHeight)
                {
                    _currentY = Math.Max(0, screenHeight - _currentHeight);
                }

                LayeredWindowMethods.SetWindowPos(
                    _hwnd,
                    LayeredWindowMethods.HWND_TOPMOST,
                    _currentX, _currentY,
                    _currentWidth, _currentHeight,
                    SetWindowPosFlags.SWP_NOACTIVATE
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [DWM_OVERLAY] 高さ調整中に例外発生");
        }
    }

    /// <summary>
    /// テキストを折り返して行に分割
    /// </summary>
    private List<string> GetWrappedTextLines(Graphics g, string text, Font font, float maxWidth)
    {
        var lines = new List<string>();
        var paragraphs = text.Split(new[] { '\n' }, StringSplitOptions.None);

        foreach (var paragraph in paragraphs)
        {
            if (string.IsNullOrEmpty(paragraph))
            {
                lines.Add(string.Empty);
                continue;
            }

            var words = paragraph.Split(' ');
            var wrappedLine = new System.Text.StringBuilder();

            foreach (var word in words)
            {
                if (wrappedLine.Length > 0)
                {
                    var testLine = wrappedLine.ToString() + " " + word;
                    if (g.MeasureString(testLine, font).Width > maxWidth)
                    {
                        lines.Add(wrappedLine.ToString());
                        wrappedLine.Clear();
                        wrappedLine.Append(word);
                    }
                    else
                    {
                        wrappedLine.Append(" " + word);
                    }
                }
                else
                {
                    wrappedLine.Append(word);
                }
            }

            if (wrappedLine.Length > 0)
            {
                lines.Add(wrappedLine.ToString());
            }
        }

        return lines;
    }

    /// <summary>
    /// メッセージキュー処理をトリガー
    /// </summary>
    private void TriggerMessageQueueProcessing()
    {
        if (_hwnd != IntPtr.Zero && !_updatePending)
        {
            _updatePending = true;
            LayeredWindowMethods.PostMessage(_hwnd, WM_PROCESS_QUEUE, IntPtr.Zero, IntPtr.Zero);
        }
    }

    // ========================================
    // IDisposable実装
    // ========================================

    public void Dispose()
    {
        if (_disposed) return;

        _logger.LogDebug("🗑️ [DWM_OVERLAY] Dispose開始");

        Close();

        _windowThread?.Join(TimeSpan.FromSeconds(3));

        _windowCreatedEvent.Dispose();
        _messageQueue.Dispose();

        _disposed = true;

        _logger.LogInformation("✅ [DWM_OVERLAY] Dispose完了");
    }

    /// <summary>
    /// 静的リソースのクリーンアップ
    /// </summary>
    public static void CleanupStaticResources()
    {
        lock (_classLock)
        {
            if (_windowClassAtom != 0)
            {
                var hInstance = User32Methods.GetModuleHandle(null);
                if (User32Methods.UnregisterClass(WINDOW_CLASS_NAME, hInstance))
                {
                    _windowClassAtom = 0;
                    _wndProcDelegate = null;
                }
            }
        }

        _instanceMap.Clear();
    }
}
