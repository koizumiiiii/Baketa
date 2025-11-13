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
/// Win32 Layered Windowベースのオーバーレイウィンドウ実装
/// </summary>
/// <remarks>
/// 🎯 [WIN32_OVERLAY_MIGRATION] Phase 1: STAスレッド + メッセージループ実装
///
/// 🔥 [GEMINI_CRITICAL_REQUIREMENT] Win32ウィンドウはSTAスレッド上で動作必須
/// - 専用STAスレッドでメッセージループを実行
/// - スレッドセーフなメッセージキューで外部からの操作を受付
/// - UpdateLayeredWindow によるピクセル単位のアルファブレンディング
///
/// アーキテクチャ:
/// - メインスレッド（呼び出し側） → メッセージキュー → STAスレッド（Win32操作）
/// - GDI32でビットマップ描画 → UpdateLayeredWindow で透過表示
/// - Dispose時にPostQuitMessageでメッセージループ終了
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class LayeredOverlayWindow : ILayeredOverlayWindow
{
    private readonly ILogger<LayeredOverlayWindow> _logger;

    // 🔥 [GEMINI_RECOMMENDATION] STAスレッド関連
    private Thread? _windowThread;
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
    private int _originalHeight = 50; // 🔧 [MIN_HEIGHT] 元のテキスト領域の高さを保持
    private Color _backgroundColor = Color.FromArgb(240, 255, 255, 255); // 半透明白

    // 🔥 [MESSAGE_COALESCING] メッセージ集約用フラグ
    // PostMessage()が既に送信済みかを追跡し、重複送信を防ぐ
    private bool _updatePending;

    // GDI リソース
    private IntPtr _hdcScreen = IntPtr.Zero;
    private IntPtr _hdcMem = IntPtr.Zero;
    private IntPtr _hBitmap = IntPtr.Zero;
    private IntPtr _hOldBitmap = IntPtr.Zero;
    private IntPtr _ppvBits = IntPtr.Zero;

    // ウィンドウクラス名
    private const string WINDOW_CLASS_NAME = "BaketaLayeredOverlay";
    private static ushort _windowClassAtom;

    // 🔥 [MESSAGE_QUEUE_FIX] カスタムメッセージ定義 - メッセージキュー処理をトリガー
    private const uint WM_USER = 0x0400;
    private const uint WM_PROCESS_QUEUE = WM_USER + 1;
    private static readonly object _classLock = new();

    // 🔥 [P0_GC_FIX] WndProcDelegateをstaticフィールドで保持してGCから保護
    // 問題: ローカル変数のデリゲートはメソッド終了後にGC対象 → Win32からの呼び出しでクラッシュ
    // 解決策: staticフィールドで保持してプロセス終了まで生存保証
    private static NativeMethods.WndProcDelegate? _wndProcDelegate;

    public LayeredOverlayWindow(ILogger<LayeredOverlayWindow> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogDebug("🚀 [WIN32_OVERLAY] LayeredOverlayWindow コンストラクター開始");

        // 🔥 [GEMINI_CRITICAL] STAスレッド起動
        _windowThread = new Thread(WindowThreadProc)
        {
            Name = "Win32 Layered Overlay Thread",
            IsBackground = true
        };
        _windowThread.SetApartmentState(ApartmentState.STA);
        _windowThread.Start();

        // ウィンドウ作成完了を待機（タイムアウト5秒）
        if (!_windowCreatedEvent.Wait(TimeSpan.FromSeconds(5)))
        {
            _logger.LogError("❌ [WIN32_OVERLAY] ウィンドウ作成タイムアウト - STAスレッド起動失敗");
            throw new InvalidOperationException("Failed to create Win32 layered window - STA thread timeout");
        }

        if (_hwnd == IntPtr.Zero)
        {
            _logger.LogError("❌ [WIN32_OVERLAY] ウィンドウハンドル取得失敗");
            throw new InvalidOperationException("Failed to create Win32 layered window - HWND is null");
        }

        _logger.LogInformation("✅ [WIN32_OVERLAY] LayeredOverlayWindow 作成完了 - HWND: 0x{Hwnd:X}", _hwnd.ToInt64());
    }

    /// <summary>
    /// STAスレッドのメインプロシージャ
    /// </summary>
    /// <remarks>
    /// 🔥 [GEMINI_CRITICAL] Win32メッセージループの実装
    /// - ウィンドウクラス登録
    /// - ウィンドウ作成
    /// - GetMessage/DispatchMessage ループ
    /// - カスタムメッセージキューの処理
    /// </remarks>
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

            _logger.LogDebug("✅ [STA_THREAD] ウィンドウ作成成功 - HWND: 0x{Hwnd:X}", _hwnd.ToInt64());
            _windowCreatedEvent.Set();

            // 🔥 [GEMINI_CRITICAL] メッセージループ
            _logger.LogDebug("🔄 [STA_THREAD] メッセージループ開始");

            while (LayeredWindowMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0))
            {
                LayeredWindowMethods.TranslateMessage(ref msg);
                LayeredWindowMethods.DispatchMessage(ref msg);

                // 🔥 [GEMINI_RECOMMENDATION] カスタムメッセージキュー処理
                // メッセージキューが空になるまで処理
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
                // 次の更新で再度PostMessage()可能にする
                _updatePending = false;
            }

            _logger.LogDebug("🔄 [STA_THREAD] メッセージループ終了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [STA_THREAD] WindowThreadProc で例外発生");
            _windowCreatedEvent.Set(); // エラーでもイベントを設定してブロックを解除
        }
        finally
        {
            CleanupGdiResources();
        }
    }

    /// <summary>
    /// ウィンドウクラスを登録
    /// </summary>
    /// <remarks>
    /// 🔥 [GEMINI_RECOMMENDATION] プロセス内で1度のみ登録（static）
    /// </remarks>
    private void RegisterWindowClass()
    {
        lock (_classLock)
        {
            if (_windowClassAtom != 0)
            {
                _logger.LogDebug("ℹ️ [STA_THREAD] ウィンドウクラス既に登録済み - Atom: {Atom}", _windowClassAtom);
                return; // 既に登録済み
            }

            // 🔥 [P0_GC_FIX] デリゲートをstaticフィールドで保持（GC保護）
            _wndProcDelegate = new NativeMethods.WndProcDelegate(WndProc);
            var wndProcPtr = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);

            var wndClass = new WNDCLASS
            {
                lpfnWndProc = wndProcPtr,
                hInstance = User32Methods.GetModuleHandle(null),
                lpszClassName = WINDOW_CLASS_NAME,
                hCursor = IntPtr.Zero,
                hbrBackground = IntPtr.Zero,
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
        // 🔥 [WIN32_OVERLAY] WS_EX_LAYERED + WS_EX_TRANSPARENT + WS_EX_NOACTIVATE
        const uint exStyle = LayeredWindowMethods.WS_EX_LAYERED
                           | (uint)ExtendedWindowStyles.WS_EX_TRANSPARENT
                           | LayeredWindowMethods.WS_EX_NOACTIVATE
                           | (uint)ExtendedWindowStyles.WS_EX_TOPMOST;

        const uint style = (uint)WindowStyles.WS_POPUP;

        _hwnd = User32Methods.CreateWindowEx(
            exStyle,
            _windowClassAtom,
            "Baketa Overlay",
            style,
            0, 0, // 初期位置
            _currentWidth, _currentHeight,
            IntPtr.Zero, // 親ウィンドウなし
            IntPtr.Zero, // メニューなし
            User32Methods.GetModuleHandle(null),
            IntPtr.Zero
        );

        if (_hwnd == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            _logger.LogError("❌ [STA_THREAD] CreateWindowEx失敗 - Error Code: {ErrorCode}", error);
            throw new InvalidOperationException($"CreateWindowEx failed - Error: {error}");
        }
    }

    /// <summary>
    /// ウィンドウプロシージャ
    /// </summary>
    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        // 基本的なメッセージ処理（必要に応じて拡張）
        return User32Methods.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    // ========================================
    // ILayeredOverlayWindow実装
    // ========================================

    public void Show()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LayeredOverlayWindow));

        _messageQueue.Add(() =>
        {
            if (!_isVisible && _hwnd != IntPtr.Zero)
            {
                LayeredWindowMethods.ShowWindow(_hwnd, ShowWindowCommands.SW_SHOWNOACTIVATE);
                _isVisible = true;
                _logger.LogDebug("👁️ [WIN32_OVERLAY] ウィンドウ表示 - HWND: 0x{Hwnd:X}", _hwnd.ToInt64());
            }
        });

        // 🔥 [MESSAGE_QUEUE_FIX] PostMessage()でメッセージキュー処理をトリガー
        TriggerMessageQueueProcessing();
    }

    public void Hide()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LayeredOverlayWindow));

        _messageQueue.Add(() =>
        {
            if (_isVisible && _hwnd != IntPtr.Zero)
            {
                LayeredWindowMethods.ShowWindow(_hwnd, ShowWindowCommands.SW_HIDE);
                _isVisible = false;
                _logger.LogDebug("🙈 [WIN32_OVERLAY] ウィンドウ非表示 - HWND: 0x{Hwnd:X}", _hwnd.ToInt64());
            }
        });
    }

    public void Close()
    {
        if (_disposed) return;

        _logger.LogDebug("🚪 [WIN32_OVERLAY] Close呼び出し");

        // STAスレッドにPostQuitMessage送信
        _messageQueue.Add(() =>
        {
            if (_hwnd != IntPtr.Zero)
            {
                User32Methods.DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
            LayeredWindowMethods.PostQuitMessage(0);
        });

        // 🔥 [CLOSE_FIX] メッセージキュー処理をトリガーしてDestroyWindow()を確実に実行
        TriggerMessageQueueProcessing();

        // メッセージキューをクローズして追加の操作を防ぐ
        _messageQueue.CompleteAdding();
    }

    public void SetText(string text)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LayeredOverlayWindow));
        if (string.IsNullOrWhiteSpace(text)) return;

        _currentText = text;

        _messageQueue.Add(() =>
        {
            if (_hwnd == IntPtr.Zero) return;

            // GDI描画とUpdateLayeredWindowで更新
            UpdateWindowContent();
        });

        // 🔥 [MESSAGE_QUEUE_FIX] PostMessage()でメッセージキュー処理をトリガー
        TriggerMessageQueueProcessing();
    }

    public void SetPosition(int x, int y)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LayeredOverlayWindow));

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

            _logger.LogDebug("📍 [WIN32_OVERLAY] 位置更新 - X: {X}, Y: {Y}", x, y);
        });

        // 🔥 [MESSAGE_QUEUE_FIX] PostMessage()でメッセージキュー処理をトリガー
        TriggerMessageQueueProcessing();
    }

    public void SetSize(int width, int height)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LayeredOverlayWindow));
        if (width <= 0 || height <= 0) return;

        _currentWidth = width;
        _currentHeight = height;
        _originalHeight = height; // 🔧 [MIN_HEIGHT] 元の高さを保存

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

            _logger.LogDebug("📏 [WIN32_OVERLAY] サイズ更新 - Width: {Width}, Height: {Height}", width, height);
        });

        // 🔥 [MESSAGE_QUEUE_FIX] PostMessage()でメッセージキュー処理をトリガー
        TriggerMessageQueueProcessing();
    }

    public void SetBackgroundColor(byte a, byte r, byte g, byte b)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LayeredOverlayWindow));

        _backgroundColor = Color.FromArgb(a, r, g, b);

        // テキスト再描画で背景色も更新
        _messageQueue.Add(() =>
        {
            if (_hwnd == IntPtr.Zero) return;
            UpdateWindowContent();
        });

        // 🔥 [MESSAGE_QUEUE_FIX] PostMessage()でメッセージキュー処理をトリガー
        TriggerMessageQueueProcessing();
    }

    public bool IsVisible => _isVisible;

    public IntPtr WindowHandle => _hwnd;

    // ========================================
    // GDI描画とUpdateLayeredWindow
    // ========================================

    /// <summary>
    /// ウィンドウコンテンツを更新（GDI描画 + UpdateLayeredWindow）
    /// </summary>
    /// <remarks>
    /// 🔥 [GEMINI_RECOMMENDATION] 32bit ARGB ビットマップによるper-pixel alpha
    /// - CreateDIBSection で DIB作成
    /// - GDI32 で描画
    /// - UpdateLayeredWindow で転送
    /// </remarks>
    private void UpdateWindowContent()
    {
        try
        {
            // 🔧 [HEIGHT_AUTO] テキストサイズを事前測定して高さを調整
            if (!string.IsNullOrWhiteSpace(_currentText))
            {
                var originalHeight = _currentHeight;

                // 一時的なBitmapとGraphicsを作成してテキストサイズを測定
                using var tempBitmap = new Bitmap(1, 1);
                using var tempGraphics = Graphics.FromImage(tempBitmap);
                using var font = new Font("Segoe UI", 14, FontStyle.Regular);

                var padding = 8f;
                var textWidth = _currentWidth - padding * 2;

                // 🔧 [LINE_SPACING] テキストを行ごとに分割して110%の行間で高さを計算
                var lines = GetWrappedTextLines(tempGraphics, _currentText, font, textWidth);
                var lineHeight = font.GetHeight(tempGraphics) * 1.1f;
                var textHeight = lines.Count * lineHeight;
                var requiredHeight = (int)(textHeight + padding * 2);

                // 🔧 [MIN_HEIGHT] 元の高さを最小値として保証
                requiredHeight = Math.Max(_originalHeight, requiredHeight);

                // 高さが変わった場合のみ更新
                if (requiredHeight != _currentHeight)
                {
                    _currentHeight = requiredHeight;

                    // 🔧 [BOUNDARY_CHECK] 画面境界チェック
                    var screenHeight = System.Windows.Forms.Screen.FromPoint(
                        new System.Drawing.Point(_currentX, _currentY)).Bounds.Height;

                    var overlayBottom = _currentY + _currentHeight;

                    // 画面下端を超える場合、Y座標を上方向にシフト
                    if (overlayBottom > screenHeight)
                    {
                        var originalY = _currentY;
                        var adjustedY = Math.Max(0, screenHeight - _currentHeight);

                        _currentY = adjustedY;
                        _logger.LogDebug("🔧 [BOUNDARY_CHECK] Y座標調整: {OriginalY} → {AdjustedY} (画面高さ: {ScreenHeight})",
                            originalY, adjustedY, screenHeight);
                    }

                    _logger.LogDebug("📏 [HEIGHT_AUTO] 高さ調整: {OriginalHeight} → {NewHeight}", originalHeight, _currentHeight);
                }
            }

            // 既存のGDIリソースをクリーンアップ
            CleanupGdiResources();

            // スクリーンDC取得
            _hdcScreen = User32Methods.GetDC(IntPtr.Zero);
            if (_hdcScreen == IntPtr.Zero)
            {
                _logger.LogError("❌ [GDI] GetDC(screen)失敗");
                return;
            }

            // メモリDC作成
            _hdcMem = LayeredWindowMethods.CreateCompatibleDC(_hdcScreen);
            if (_hdcMem == IntPtr.Zero)
            {
                _logger.LogError("❌ [GDI] CreateCompatibleDC失敗");
                return;
            }

            // 32bit ARGB DIB作成
            var bmi = new BITMAPINFO
            {
                bmiHeader = BITMAPINFOHEADER.Create32BitARGB(_currentWidth, _currentHeight),
                bmiColors = new uint[1]
            };

            _hBitmap = LayeredWindowMethods.CreateDIBSection(
                _hdcMem,
                ref bmi,
                LayeredWindowMethods.DIB_RGB_COLORS,
                out _ppvBits,
                IntPtr.Zero,
                0
            );

            if (_hBitmap == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                _logger.LogError("❌ [GDI] CreateDIBSection失敗 - Error: {Error}", error);
                return;
            }

            _hOldBitmap = LayeredWindowMethods.SelectObject(_hdcMem, _hBitmap);

            // 🎨 GDI描画: 背景塗りつぶし
            using (var g = Graphics.FromHdc(_hdcMem))
            {
                g.Clear(_backgroundColor);

                // テキスト描画
                if (!string.IsNullOrWhiteSpace(_currentText))
                {
                    using var brush = new SolidBrush(Color.FromArgb(255, 45, 45, 45)); // 濃いグレー
                    using var font = new Font("Segoe UI", 14, FontStyle.Regular);

                    var padding = 8f;
                    var textWidth = _currentWidth - padding * 2;

                    // 🔧 [LINE_SPACING] テキストを行ごとに分割して、110%の行間で描画
                    var lines = GetWrappedTextLines(g, _currentText, font, textWidth);
                    var lineHeight = font.GetHeight(g) * 1.1f;

                    // 1行ずつ描画（自動折り返しは無効）
                    using var format = new StringFormat(StringFormat.GenericTypographic)
                    {
                        FormatFlags = StringFormatFlags.NoWrap,
                        Trimming = StringTrimming.None
                    };

                    var y = padding;
                    foreach (var line in lines)
                    {
                        // 描画領域の高さを超える場合は描画を停止
                        if ((y + lineHeight) > _currentHeight)
                        {
                            break;
                        }

                        g.DrawString(line, font, brush, new PointF(padding, y), format);
                        y += lineHeight;
                    }
                }
            }

            // 🔥 [CRITICAL] UpdateLayeredWindow で透過ウィンドウ更新
            var pptDst = new NativeMethods.POINT(_currentX, _currentY);
            var psize = new NativeMethods.SIZE(_currentWidth, _currentHeight);
            var pptSrc = new NativeMethods.POINT(0, 0);

            // BLENDFUNCTIONの作成（CreateDefault()メソッドが使えないため直接作成）
            var blend = new NativeMethods.BLENDFUNCTION
            {
                BlendOp = 0, // AC_SRC_OVER
                BlendFlags = 0,
                SourceConstantAlpha = 255, // 不透明度100%
                AlphaFormat = 1 // AC_SRC_ALPHA - per-pixel alpha使用
            };

            var result = LayeredWindowMethods.UpdateLayeredWindow(
                _hwnd,
                _hdcScreen,
                ref pptDst,
                ref psize,
                _hdcMem,
                ref pptSrc,
                0,
                ref blend,
                UpdateLayeredWindowFlags.ULW_ALPHA
            );

            if (!result)
            {
                var error = Marshal.GetLastWin32Error();
                _logger.LogError("❌ [GDI] UpdateLayeredWindow失敗 - Error: {Error}", error);
            }
            else
            {
                _logger.LogDebug("✅ [GDI] UpdateLayeredWindow成功 - Text: '{Text}'", _currentText);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [GDI] UpdateWindowContent で例外発生");
        }
    }

    /// <summary>
    /// GDIリソースをクリーンアップ
    /// </summary>
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
            User32Methods.ReleaseDC(IntPtr.Zero, _hdcScreen);
            _hdcScreen = IntPtr.Zero;
        }
    }

    /// <summary>
    /// カスタムメッセージキューの処理をトリガー
    /// </summary>
    /// <remarks>
    /// 🔥 [MESSAGE_QUEUE_FIX] PostMessage()でGetMessage()のブロックを解除
    /// 問題: GetMessage()はWin32メッセージが来るまでブロックし、_messageQueueが処理されない
    /// 解決策: カスタムメッセージを送ってGetMessage()を起こし、_messageQueueを処理させる
    ///
    /// 🔥 [MESSAGE_COALESCING] メッセージ集約による最適化
    /// 1チャンク内の複数メソッド呼び出し（SetText, SetPosition, SetSize, Show等）で
    /// PostMessage()を1回のみ実行することで、不要なメッセージループ回転を削減
    /// 効果: 75回 → 15回（15チャンクの場合）
    /// </remarks>
    private void TriggerMessageQueueProcessing()
    {
        if (_hwnd != IntPtr.Zero && !_updatePending)
        {
            _updatePending = true;
            LayeredWindowMethods.PostMessage(_hwnd, WM_PROCESS_QUEUE, IntPtr.Zero, IntPtr.Zero);
        }
    }

    // ========================================
    // テキスト折り返しヘルパー
    // ========================================

    /// <summary>
    /// 指定された最大幅に基づいて、文字列を複数行に分割します
    /// </summary>
    /// <param name="g">Graphics オブジェクト</param>
    /// <param name="text">分割するテキスト</param>
    /// <param name="font">使用するフォント</param>
    /// <param name="maxWidth">最大幅（ピクセル）</param>
    /// <returns>分割された行のリスト</returns>
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

    // ========================================
    // IDisposable実装
    // ========================================

    public void Dispose()
    {
        if (_disposed) return;

        _logger.LogDebug("🗑️ [WIN32_OVERLAY] Dispose開始");

        Close();

        // STAスレッド終了を待機（タイムアウト3秒）
        _windowThread?.Join(TimeSpan.FromSeconds(3));

        _windowCreatedEvent.Dispose();
        _messageQueue.Dispose();

        _disposed = true;

        _logger.LogInformation("✅ [WIN32_OVERLAY] Dispose完了");
    }

    /// <summary>
    /// アプリケーション終了時のクリーンアップ処理（静的リソース解放）
    /// </summary>
    /// <remarks>
    /// 🔥 [P0_GC_FIX] Win32ウィンドウクラスとWndProcDelegateの完全クリーンアップ
    /// - 全LayeredOverlayWindowインスタンスが破棄された後に呼び出す
    /// - アプリケーション終了時（MainWindow.OnClosing等）から明示的に呼び出すことを推奨
    /// </remarks>
    public static void CleanupStaticResources()
    {
        lock (_classLock)
        {
            if (_windowClassAtom != 0)
            {
                // ウィンドウクラスの登録解除
                var hInstance = User32Methods.GetModuleHandle(null);
                if (User32Methods.UnregisterClass(WINDOW_CLASS_NAME, hInstance))
                {
                    _windowClassAtom = 0;
                    _wndProcDelegate = null; // デリゲート参照を解放（GC可能に）
                }
            }
        }
    }
}
