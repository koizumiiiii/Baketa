using System;
using System.Drawing;
using System.Runtime.InteropServices;
using Baketa.Core.Abstractions.Services;
using Baketa.Infrastructure.Platform.Windows.NativeMethods; // 🔥 [PHASE3_DPI_AWARENESS] GetDpiForWindow用
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Platform.Windows.Services;

/// <summary>
/// 座標変換サービス実装
/// coordinate_test/Program.csの変換ロジックに基づく正確なROI→スクリーン座標変換
/// UltraThink P0: 座標変換問題修正 - DPIスケーリング・ウィンドウオフセット計算
/// </summary>
public sealed class CoordinateTransformationService : ICoordinateTransformationService
{
    private readonly ILogger<CoordinateTransformationService> _logger;

    // Win32 API declarations
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out WindowRect lpRect);

    // 🔥 [PHASE1_CLIENT_TO_SCREEN] クライアント座標→スクリーン座標変換API
    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref Point lpPoint);

    // 🔥 [PHASE2_MAXIMIZED_WINDOW] 最大化ウィンドウ検出API
    [DllImport("user32.dll")]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    // 🔥 [PHASE2_MONITOR_DETECTION] モニタ検出API
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint dwFlags);

    // 🔥 [PHASE2_MONITOR_INFO] モニタ情報取得API
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    // 🔥 [PHASE2.1_BORDERLESS_DETECTION] ボーダーレス/フルスクリーン検出API
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    // Win32 Constants
    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    private const uint SW_MAXIMIZE = 3;

    // 🔥 [PHASE2.1_BORDERLESS_DETECTION] DWM定数
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    // 🔥 [PHASE2.1_BORDERLESS_DETECTION] Window Style定数
    private const int GWL_STYLE = -16;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_SYSMENU = 0x00080000;

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    // 🔥 [PHASE2_MAXIMIZED_WINDOW] ウィンドウ配置情報構造体
    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public uint showCmd;
        public Point ptMinPosition;
        public Point ptMaxPosition;
        public Rectangle rcNormalPosition;
    }

    // 🔥 [PHASE2_MONITOR_INFO] モニタ情報構造体
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public Rectangle rcMonitor;
        public Rectangle rcWork;
        public uint dwFlags;
    }

    // 🔥 [PHASE2.1_BORDERLESS_DETECTION] DWM用RECT構造体
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    public CoordinateTransformationService(ILogger<CoordinateTransformationService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// ROI座標をスクリーン座標に変換
    /// coordinate_test/Program.csのConvertRoiToScreenCoordinatesと同じロジック
    /// Phase 2.1: ボーダーレス/フルスクリーンパラメータ追加
    /// </summary>
    public Rectangle ConvertRoiToScreenCoordinates(
        Rectangle roiBounds,
        IntPtr windowHandle,
        float roiScaleFactor = 1.0f,
        bool isBorderlessOrFullscreen = false)
    {
        try
        {
            _logger.LogDebug("🎯 [P0_COORDINATE_TRANSFORM] ROI→スクリーン座標変換開始: ROI=({X},{Y},{W},{H}), Handle={Handle}, ScaleFactor={ScaleFactor}",
                roiBounds.X, roiBounds.Y, roiBounds.Width, roiBounds.Height, windowHandle, roiScaleFactor);

            // ROIスケールファクタの逆数でスケーリング
            var inverseScale = 1.0f / roiScaleFactor;

            // 1. ROI座標を実際の画面座標にスケーリング（クライアント座標系）
            var scaledX = (int)(roiBounds.X * inverseScale);
            var scaledY = (int)(roiBounds.Y * inverseScale);
            // 🔥 [GEMINI_P2_FIX] scaledWidth/Heightが負の値にならないようにガード処理
            var scaledWidth = Math.Max(0, (int)(roiBounds.Width * inverseScale));
            var scaledHeight = Math.Max(0, (int)(roiBounds.Height * inverseScale));

            _logger.LogInformation("📐 [PHASE2_SCALED] スケーリング後 - Scaled=({ScaledX},{ScaledY})", scaledX, scaledY);

            // 🔥 [PHASE3_DPI_AWARENESS] DPI補正 - 高DPI環境（125%, 150%, 200%）対応
            //    Per-Monitor DPI V2により、各モニターごとの異なるDPI設定に対応
            //    物理ピクセル = 論理ピクセル * (DPI / 96.0)
            // 🔥 [OVERLAY_FIX] ボーダーレス/フルスクリーンの場合、キャプチャは既に物理ピクセルなのでDPI補正不要
            if (isBorderlessOrFullscreen)
            {
                _logger.LogInformation("🔍 [PHASE3_DPI] ボーダーレス/フルスクリーン検出 - DPI補正をスキップ（キャプチャは既に物理ピクセル）");
            }
            else if (windowHandle != IntPtr.Zero && IsWindow(windowHandle))
            {
                try
                {
                    uint dpi = LayeredWindowMethods.GetDpiForWindow(windowHandle);

                    // 🔥 [GEMINI_P0_FIX] DPI取得失敗時のフォールバック
                    // GetDpiForWindowが0を返す場合: Windows 10 1607以前、無効なハンドル、API失敗
                    if (dpi == 0)
                    {
                        _logger.LogWarning("⚠️ [PHASE3_DPI] GetDpiForWindow返り値が0 - DPI補正をスキップします（スケール1.0として継続）");
                        // DPI補正なしで継続（スケーリング後の座標をそのまま使用）
                    }
                    else
                    {
                        float dpiScale = dpi / 96.0f; // 96 = 100% DPI（基準値）

                        // DPI補正を適用
                        scaledX = (int)(scaledX * dpiScale);
                        scaledY = (int)(scaledY * dpiScale);
                        scaledWidth = (int)(scaledWidth * dpiScale);
                        scaledHeight = (int)(scaledHeight * dpiScale);

                        _logger.LogInformation("📐 [PHASE3_DPI] DPI補正後 - DPI={Dpi}, Scale={DpiScale:F2}, Corrected=({CorrectedX},{CorrectedY})",
                            dpi, dpiScale, scaledX, scaledY);
                    }
                }
                catch (Exception dpiEx)
                {
                    _logger.LogWarning(dpiEx, "⚠️ [PHASE3_DPI] GetDpiForWindow例外発生 - DPI補正をスキップします");
                    // DPI取得失敗時はスケーリング後の座標をそのまま使用（フォールバック）
                }
            }
            else
            {
                _logger.LogDebug("🔍 [PHASE3_DPI] 無効なウィンドウハンドル - DPI補正をスキップします");
            }

            // 2. 🔥 [PHASE1_CLIENT_TO_SCREEN] クライアント座標→スクリーン絶対座標変換
            //    ROI座標はクライアント領域内の相対座標（0,0起点）
            //    ClientToScreen APIで仮想スクリーン全体の絶対座標に変換
            var topLeft = new Point(scaledX, scaledY);
            if (!ClientToScreen(windowHandle, ref topLeft))
            {
                _logger.LogWarning("❌ [PHASE2_ERROR] ClientToScreen失敗 - ROI=({X},{Y}), スケーリング後の座標を返します", scaledX, scaledY);
                // 🔥 [GEMINI_P1_FIX] フォールバック: スケーリング後の座標を返す（ウィンドウオフセットなし）
                // IntPtr.Zero等の無効なハンドルでもスケーリングは適用すべき
                return new Rectangle(scaledX, scaledY, scaledWidth, scaledHeight);
            }
            _logger.LogInformation("✅ [PHASE1_CLIENT_TO_SCREEN] ClientToScreen成功 - Result=({X},{Y})", topLeft.X, topLeft.Y);

            // 🔥 [PHASE2_MAXIMIZED_WINDOW] 最大化ウィンドウの検出と補正
            var placement = new WINDOWPLACEMENT();
            placement.length = Marshal.SizeOf(typeof(WINDOWPLACEMENT));

            if (GetWindowPlacement(windowHandle, ref placement))
            {
                var isMaximized = placement.showCmd == SW_MAXIMIZE;
                _logger.LogInformation("🔍 [PHASE2_DEBUG] showCmd={ShowCmd}, IsMaximized={IsMaximized}, IsBorderless={IsBorderless}, ClientToScreen結果=({X},{Y})",
                    placement.showCmd, isMaximized, isBorderlessOrFullscreen, topLeft.X, topLeft.Y);

                // 🔥 [PHASE2.1] 最大化 OR ボーダーレス/フルスクリーンの場合、DWM座標補正を適用
                if (isMaximized || isBorderlessOrFullscreen)
                {
                    // 🔥 [PHASE2_MONITOR_DETECTION] ウィンドウが存在するモニタを特定
                    var hMonitor = MonitorFromWindow(windowHandle, MONITOR_DEFAULTTONEAREST);
                    if (hMonitor != IntPtr.Zero)
                    {
                        var monitorInfo = new MONITORINFO();
                        monitorInfo.cbSize = Marshal.SizeOf(typeof(MONITORINFO));

                        if (GetMonitorInfo(hMonitor, ref monitorInfo))
                        {
                            // 🔥 [PHASE2_MONITOR_INFO] モニタ情報を取得成功
                            _logger.LogInformation("📺 [PHASE2_MONITOR_INFO] モニタ境界: Monitor=({MonLeft},{MonTop},{MonRight},{MonBottom}), Work=({WorkLeft},{WorkTop},{WorkRight},{WorkBottom})",
                                monitorInfo.rcMonitor.Left, monitorInfo.rcMonitor.Top, monitorInfo.rcMonitor.Right, monitorInfo.rcMonitor.Bottom,
                                monitorInfo.rcWork.Left, monitorInfo.rcWork.Top, monitorInfo.rcWork.Right, monitorInfo.rcWork.Bottom);

                            // 最大化ウィンドウの場合、ClientToScreenで取得した座標は
                            // DWMによりモニタ境界から1ピクセルずれている可能性がある
                            // モニタの作業領域(rcWork)を基準に補正
                            var correctedX = topLeft.X;
                            var correctedY = topLeft.Y;

                            // X座標の補正（モニタ左端から1ピクセルずれている場合）
                            if (topLeft.X == monitorInfo.rcMonitor.Left - 1)
                            {
                                correctedX = monitorInfo.rcWork.Left;
                                _logger.LogInformation("🔧 [PHASE2_FIX] X座標補正: {OldX} → {NewX}", topLeft.X, correctedX);
                            }

                            // Y座標の補正（モニタ上端から1ピクセルずれている場合）
                            if (topLeft.Y == monitorInfo.rcMonitor.Top - 1)
                            {
                                correctedY = monitorInfo.rcWork.Top;
                                _logger.LogInformation("🔧 [PHASE2_FIX] Y座標補正: {OldY} → {NewY}", topLeft.Y, correctedY);
                            }

                            // 🔥 [GEMINI_P0_P1_FIX] ヘルパーメソッドを使用してクランプ処理
                            // DWM Extended Frame Boundsによる座標オーバーを防止
                            topLeft = ClampPointToMonitor(
                                correctedX,
                                correctedY,
                                scaledWidth,
                                scaledHeight,
                                monitorInfo,
                                out _); // wasClamped フラグは使用しない（ヘルパー内でログ出力）
                            _logger.LogInformation("✅ [PHASE2_RESULT] 補正後座標=({X},{Y})", topLeft.X, topLeft.Y);
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ [PHASE2_MONITOR_INFO] GetMonitorInfo失敗");
                        }
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ [PHASE2_MONITOR_DETECTION] MonitorFromWindow失敗");
                    }
                }
            }
            else
            {
                _logger.LogWarning("⚠️ [PHASE2_MAXIMIZED_WINDOW] GetWindowPlacement失敗");
            }

            // 3. スクリーン絶対座標のRectangleを構築
            var finalBounds = new Rectangle(
                topLeft.X,
                topLeft.Y,
                scaledWidth,
                scaledHeight
            );

            _logger.LogInformation("🎯 [PHASE1_CLIENT_TO_SCREEN] 座標変換完了: ROI=({RoiX},{RoiY}) → Scaled=({ScaledX},{ScaledY}) → ClientToScreen → Screen=({ScreenX},{ScreenY})",
                roiBounds.X, roiBounds.Y, scaledX, scaledY, finalBounds.X, finalBounds.Y);

            return finalBounds;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [P0_COORDINATE_TRANSFORM] 座標変換エラー: ROI=({X},{Y},{W},{H}), Handle={Handle}",
                roiBounds.X, roiBounds.Y, roiBounds.Width, roiBounds.Height, windowHandle);

            // フォールバック: 元の座標をそのまま返す
            return roiBounds;
        }
    }

    /// <summary>
    /// 複数のROI座標を一括変換
    /// 効率化のため、ウィンドウオフセットを一度だけ取得
    /// Phase 2.1: ボーダーレス/フルスクリーンパラメータ追加
    /// </summary>
    public Rectangle[] ConvertRoiToScreenCoordinatesBatch(
        Rectangle[] roiBounds,
        IntPtr windowHandle,
        float roiScaleFactor = 1.0f,
        bool isBorderlessOrFullscreen = false)
    {
        if (roiBounds == null || roiBounds.Length == 0)
            return [];

        try
        {
            _logger.LogDebug("🎯 [PHASE1_CLIENT_TO_SCREEN] 一括座標変換開始: Count={Count}, Handle={Handle}, ScaleFactor={ScaleFactor}",
                roiBounds.Length, windowHandle, roiScaleFactor);

            var inverseScale = 1.0f / roiScaleFactor;
            var results = new Rectangle[roiBounds.Length];

            // 🔥 [PHASE2_BATCH_OPTIMIZATION] 最大化ウィンドウ情報とモニタ情報を1回だけ取得
            var placement = new WINDOWPLACEMENT();
            placement.length = Marshal.SizeOf(typeof(WINDOWPLACEMENT));
            var isMaximized = false;
            MONITORINFO monitorInfo = default;
            var hasMonitorInfo = false;

            if (GetWindowPlacement(windowHandle, ref placement))
            {
                isMaximized = placement.showCmd == SW_MAXIMIZE;
                // 🔥 [PHASE2.1] 最大化 OR ボーダーレス/フルスクリーンの場合、モニタ情報を取得
                if (isMaximized || isBorderlessOrFullscreen)
                {
                    var hMonitor = MonitorFromWindow(windowHandle, MONITOR_DEFAULTTONEAREST);
                    if (hMonitor != IntPtr.Zero)
                    {
                        monitorInfo = new MONITORINFO();
                        monitorInfo.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                        hasMonitorInfo = GetMonitorInfo(hMonitor, ref monitorInfo);

                        if (hasMonitorInfo)
                        {
                            _logger.LogDebug("📺 [PHASE2_BATCH_OPTIMIZATION] バッチ変換用モニタ情報取得完了: IsMaximized={IsMaximized}, IsBorderless={IsBorderless}",
                                isMaximized, isBorderlessOrFullscreen);
                        }
                    }
                }
            }

            for (int i = 0; i < roiBounds.Length; i++)
            {
                var roi = roiBounds[i];

                // 1. ROI座標をスケーリング（クライアント座標系）
                var scaledX = (int)(roi.X * inverseScale);
                var scaledY = (int)(roi.Y * inverseScale);
                // 🔥 [GEMINI_P2_FIX] scaledWidth/Heightが負の値にならないようにガード処理
                var scaledWidth = Math.Max(0, (int)(roi.Width * inverseScale));
                var scaledHeight = Math.Max(0, (int)(roi.Height * inverseScale));

                // 2. 🔥 [PHASE1_CLIENT_TO_SCREEN] ClientToScreenで変換
                var topLeft = new Point(scaledX, scaledY);
                if (!ClientToScreen(windowHandle, ref topLeft))
                {
                    _logger.LogWarning("⚠️ [PHASE1_CLIENT_TO_SCREEN] ClientToScreen失敗 - Index={Index}, ROI=({X},{Y}), スケーリング後の座標を使用", i, scaledX, scaledY);
                    // 🔥 [GEMINI_P1_FIX] フォールバック: スケーリング後の座標を使用（ウィンドウオフセットなし）
                    // IntPtr.Zero等の無効なハンドルでもスケーリングは適用すべき
                    results[i] = new Rectangle(scaledX, scaledY, scaledWidth, scaledHeight);
                    continue;
                }

                // 🔥 [PHASE2_BATCH_OPTIMIZATION] 最大化 OR ボーダーレス/フルスクリーンの場合、座標を補正
                if ((isMaximized || isBorderlessOrFullscreen) && hasMonitorInfo)
                {
                    var correctedX = topLeft.X;
                    var correctedY = topLeft.Y;

                    if (topLeft.X == monitorInfo.rcMonitor.Left - 1)
                    {
                        correctedX = monitorInfo.rcWork.Left;
                    }

                    if (topLeft.Y == monitorInfo.rcMonitor.Top - 1)
                    {
                        correctedY = monitorInfo.rcWork.Top;
                    }

                    // 🔥 [GEMINI_P0_P1_FIX] ヘルパーメソッドを使用してクランプ処理
                    // DWM Extended Frame Boundsによる座標オーバーを防止
                    topLeft = ClampPointToMonitor(
                        correctedX,
                        correctedY,
                        scaledWidth,
                        scaledHeight,
                        monitorInfo,
                        out _); // wasClamped フラグは使用しない（ヘルパー内でログ出力）
                }

                // 3. スクリーン絶対座標のRectangleを構築
                results[i] = new Rectangle(
                    topLeft.X,
                    topLeft.Y,
                    scaledWidth,
                    scaledHeight
                );
            }

            _logger.LogDebug("🎯 [PHASE1_CLIENT_TO_SCREEN] 一括座標変換完了: Count={Count}",
                results.Length);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [P0_COORDINATE_TRANSFORM] 一括座標変換エラー: Count={Count}, Handle={Handle}",
                roiBounds.Length, windowHandle);

            // フォールバック: 元の座標をそのまま返す
            return roiBounds;
        }
    }

    /// <summary>
    /// 🔥 [PHASE2.1] ボーダーレス/フルスクリーンウィンドウ検出
    /// DWM Hybrid方式: DwmGetWindowAttribute（主）+ GetWindowLong（フォールバック）
    /// </summary>
    public bool DetectBorderlessOrFullscreen(IntPtr windowHandle)
    {
        try
        {
            // ウィンドウハンドルの有効性チェック
            if (windowHandle == IntPtr.Zero || !IsWindow(windowHandle))
            {
                _logger.LogWarning("⚠️ [PHASE2.1] 無効なウィンドウハンドル: {Handle}", windowHandle);
                return false;
            }

            // 🔥 [PHASE2.1_DWM_PRIMARY] DWM方式による検出（Primary）
            if (TryDetectByDwm(windowHandle, out var isDwmBorderless))
            {
                _logger.LogInformation("✅ [PHASE2.1_DWM] DWM検出成功 - Handle={Handle}, Borderless={IsBorderless}",
                    windowHandle, isDwmBorderless);
                return isDwmBorderless;
            }

            // 🔥 [PHASE2.1_FALLBACK] スタイルビット + サイズ比較による検出（Fallback）
            var isFallbackBorderless = DetectByStyleAndSize(windowHandle);
            _logger.LogInformation("✅ [PHASE2.1_FALLBACK] Fallback検出完了 - Handle={Handle}, Borderless={IsBorderless}",
                windowHandle, isFallbackBorderless);

            return isFallbackBorderless;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE2.1] ボーダーレス検出エラー: Handle={Handle}", windowHandle);
            return false; // エラー時はボーダーレスではないと判定
        }
    }

    /// <summary>
    /// 🔥 [PHASE2.1_DWM] DWM方式によるボーダーレス検出
    /// </summary>
    private bool TryDetectByDwm(IntPtr windowHandle, out bool isBorderless)
    {
        isBorderless = false;

        try
        {
            // DwmGetWindowAttributeでウィンドウの正確な境界を取得
            var hr = DwmGetWindowAttribute(
                windowHandle,
                DWMWA_EXTENDED_FRAME_BOUNDS,
                out RECT dwmRect,
                Marshal.SizeOf(typeof(RECT)));

            if (hr != 0) // HRESULT失敗
            {
                _logger.LogDebug("⚠️ [PHASE2.1_DWM] DwmGetWindowAttribute失敗 - HRESULT={HResult}", hr);
                return false;
            }

            // モニタ情報取得
            var hMonitor = MonitorFromWindow(windowHandle, MONITOR_DEFAULTTONEAREST);
            if (hMonitor == IntPtr.Zero)
            {
                _logger.LogDebug("⚠️ [PHASE2.1_DWM] MonitorFromWindow失敗");
                return false;
            }

            var monitorInfo = new MONITORINFO();
            monitorInfo.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (!GetMonitorInfo(hMonitor, ref monitorInfo))
            {
                _logger.LogDebug("⚠️ [PHASE2.1_DWM] GetMonitorInfo失敗");
                return false;
            }

            // 🔥 [PHASE2.1_SIZE_COMPARISON] ウィンドウサイズとモニタサイズの比較
            var monitorWidth = monitorInfo.rcMonitor.Width;
            var monitorHeight = monitorInfo.rcMonitor.Height;
            var windowWidth = dwmRect.Width;
            var windowHeight = dwmRect.Height;

            // 絶対値マッチング: ±10px許容
            var widthMatch = Math.Abs(windowWidth - monitorWidth) <= 10;
            var heightMatch = Math.Abs(windowHeight - monitorHeight) <= 10;

            // 相対値マッチング: 95%以上
            var widthRatio = (double)windowWidth / monitorWidth;
            var heightRatio = (double)windowHeight / monitorHeight;
            var widthRelativeMatch = widthRatio >= 0.95;
            var heightRelativeMatch = heightRatio >= 0.95;

            isBorderless = (widthMatch && heightMatch) || (widthRelativeMatch && heightRelativeMatch);

            _logger.LogDebug("🔍 [PHASE2.1_DWM] サイズ比較 - Window=({WinW}x{WinH}), Monitor=({MonW}x{MonH}), Match={Match}",
                windowWidth, windowHeight, monitorWidth, monitorHeight, isBorderless);

            return true; // DWM検出成功
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "⚠️ [PHASE2.1_DWM] DWM検出例外");
            return false;
        }
    }

    /// <summary>
    /// 🔥 [PHASE2.1_FALLBACK] スタイルビット + サイズ比較によるボーダーレス検出
    /// </summary>
    private bool DetectByStyleAndSize(IntPtr windowHandle)
    {
        try
        {
            // ウィンドウスタイル取得
            var style = GetWindowLong(windowHandle, GWL_STYLE);

            // タイトルバー、リサイズ枠、システムメニューのいずれかが存在する場合は通常ウィンドウ
            var hasCaption = (style & WS_CAPTION) == WS_CAPTION;
            var hasThickFrame = (style & WS_THICKFRAME) == WS_THICKFRAME;
            var hasSysMenu = (style & WS_SYSMENU) == WS_SYSMENU;

            if (hasCaption || hasThickFrame || hasSysMenu)
            {
                _logger.LogDebug("🔍 [PHASE2.1_FALLBACK] 通常ウィンドウ検出 - Caption={Caption}, ThickFrame={ThickFrame}, SysMenu={SysMenu}",
                    hasCaption, hasThickFrame, hasSysMenu);
                return false;
            }

            // GetWindowRectでウィンドウサイズ取得
            if (!GetWindowRect(windowHandle, out var windowRect))
            {
                _logger.LogDebug("⚠️ [PHASE2.1_FALLBACK] GetWindowRect失敗");
                return false;
            }

            var windowWidth = windowRect.Right - windowRect.Left;
            var windowHeight = windowRect.Bottom - windowRect.Top;

            // モニタ情報取得
            var hMonitor = MonitorFromWindow(windowHandle, MONITOR_DEFAULTTONEAREST);
            if (hMonitor == IntPtr.Zero)
            {
                return false;
            }

            var monitorInfo = new MONITORINFO();
            monitorInfo.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (!GetMonitorInfo(hMonitor, ref monitorInfo))
            {
                return false;
            }

            var monitorWidth = monitorInfo.rcMonitor.Width;
            var monitorHeight = monitorInfo.rcMonitor.Height;

            // サイズ比較（±10pxまたは95%以上）
            var widthMatch = Math.Abs(windowWidth - monitorWidth) <= 10;
            var heightMatch = Math.Abs(windowHeight - monitorHeight) <= 10;

            var widthRatio = (double)windowWidth / monitorWidth;
            var heightRatio = (double)windowHeight / monitorHeight;
            var widthRelativeMatch = widthRatio >= 0.95;
            var heightRelativeMatch = heightRatio >= 0.95;

            var isBorderless = (widthMatch && heightMatch) || (widthRelativeMatch && heightRelativeMatch);

            _logger.LogDebug("🔍 [PHASE2.1_FALLBACK] Fallback判定 - Window=({WinW}x{WinH}), Monitor=({MonW}x{MonH}), Borderless={IsBorderless}",
                windowWidth, windowHeight, monitorWidth, monitorHeight, isBorderless);

            return isBorderless;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "⚠️ [PHASE2.1_FALLBACK] Fallback検出例外");
            return false;
        }
    }

    /// <summary>
    /// ウィンドウオフセットを取得
    /// coordinate_test/Program.csのGetTargetWindowOffsetと同じロジック
    /// </summary>
    public Point GetWindowOffset(IntPtr windowHandle)
    {
        try
        {
            if (windowHandle == IntPtr.Zero)
            {
                _logger.LogDebug("⚠️ [P0_COORDINATE_TRANSFORM] ウィンドウハンドルが無効、(0,0)を使用");
                return Point.Empty;
            }

            // ウィンドウの矩形情報を取得
            if (GetWindowRect(windowHandle, out var rect))
            {
                var offset = new Point(rect.Left, rect.Top);

                // 🔥 [DEBUG] GetWindowRect結果を強制出力
                Console.WriteLine($"🎯 [WINDOW_RECT_DEBUG] Handle={windowHandle}, Left={rect.Left}, Top={rect.Top}, Right={rect.Right}, Bottom={rect.Bottom}");
                Console.WriteLine($"🎯 [WINDOW_RECT_DEBUG] Offset=({offset.X},{offset.Y}), Size=({rect.Right - rect.Left}x{rect.Bottom - rect.Top})");

                _logger.LogDebug("🎯 [P0_COORDINATE_TRANSFORM] ウィンドウオフセット取得成功: Handle={Handle}, Offset=({X},{Y})",
                    windowHandle, offset.X, offset.Y);
                return offset;
            }

            _logger.LogWarning("⚠️ [P0_COORDINATE_TRANSFORM] GetWindowRect失敗、(0,0)を使用: Handle={Handle}", windowHandle);
            return Point.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [P0_COORDINATE_TRANSFORM] ウィンドウオフセット取得エラー: Handle={Handle}", windowHandle);
            return Point.Empty;
        }
    }

    /// <summary>
    /// 🔥 [GEMINI_P0_P1_FIX] モニター範囲内に座標をクランプする共通ロジック
    /// オーバーレイサイズがモニターサイズより大きい場合でも、確実に範囲内に収める
    /// DRY原則に従い、重複コードを削減し、保守性を向上
    /// </summary>
    /// <param name="x">元のX座標</param>
    /// <param name="y">元のY座標</param>
    /// <param name="width">オーバーレイの幅</param>
    /// <param name="height">オーバーレイの高さ</param>
    /// <param name="monitorInfo">モニター情報</param>
    /// <param name="wasClamped">クランプが発生したかどうか</param>
    /// <returns>クランプ後の座標</returns>
    private Point ClampPointToMonitor(
        int x,
        int y,
        int width,
        int height,
        MONITORINFO monitorInfo,
        out bool wasClamped)
    {
        // 🔥 [GEMINI_P0_FIX] Math.MaxとMath.Minの組み合わせで確実にクランプ
        // オーバーレイサイズがモニターサイズより大きい場合でも正しく動作する
        // 例: モニター幅1920px、オーバーレイ幅2000pxの場合
        //     - Math.Min(x, Right - 2000) で右端を制限
        //     - Math.Max(Left, ...) で左端を制限
        //     → 結果的にLeft座標に固定され、画面外にはみ出さない
        var clampedX = Math.Max(monitorInfo.rcMonitor.Left,
                                Math.Min(x, monitorInfo.rcMonitor.Right - width));
        var clampedY = Math.Max(monitorInfo.rcMonitor.Top,
                                Math.Min(y, monitorInfo.rcMonitor.Bottom - height));

        wasClamped = (clampedX != x || clampedY != y);

        // 🔥 [GEMINI_P1_FIX] クランプ発生時のログ出力（一貫性確保）
        if (wasClamped)
        {
            _logger.LogWarning("🔧 [COORDINATE_CLAMP_FIX] 座標クランプ実行: ({OldX},{OldY}) → ({NewX},{NewY}) - モニター境界=({Left},{Top},{Right},{Bottom})",
                x, y, clampedX, clampedY,
                monitorInfo.rcMonitor.Left, monitorInfo.rcMonitor.Top, monitorInfo.rcMonitor.Right, monitorInfo.rcMonitor.Bottom);
        }

        return new Point(clampedX, clampedY);
    }
}
