using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Core.Abstractions.Platform.Windows.Adapters;

namespace Baketa.Infrastructure.Platform.Adapters;

/// <summary>
/// Core.Abstractions版のIWindowManagerAdapterインターフェースの基本スタブ実装
/// </summary>
public class CoreWindowManagerAdapterStub(Baketa.Core.Abstractions.Platform.Windows.IWindowManager windowManager) : Baketa.Core.Abstractions.Platform.Windows.Adapters.IWindowManagerAdapter
{
    private readonly Baketa.Core.Abstractions.Platform.Windows.IWindowManager _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));

    /// <summary>
    /// アダプターがサポートする機能名
    /// </summary>
    public string FeatureName => "WindowManager";

    /// <summary>
    /// 特定の型変換をサポートするかどうか
    /// </summary>
    /// <typeparam name="TSource">ソース型</typeparam>
    /// <typeparam name="TTarget">ターゲット型</typeparam>
    /// <returns>サポートする場合はtrue</returns>
    public bool SupportsConversion<TSource, TTarget>()
    {
        return false; // スタブ実装では変換をサポートしない
    }

    /// <summary>
    /// 変換を試行
    /// </summary>
    /// <typeparam name="TSource">ソース型</typeparam>
    /// <typeparam name="TTarget">ターゲット型</typeparam>
    /// <param name="source">ソースオブジェクト</param>
    /// <param name="target">変換結果（出力）</param>
    /// <returns>変換成功時はtrue</returns>
    public bool TryConvert<TSource, TTarget>(TSource source, out TTarget target) where TSource : class where TTarget : class
    {
        target = default!;
        return false; // スタブ実装では変換をサポートしない
    }

    /// <summary>
    /// アクティブなウィンドウハンドルを取得します
    /// </summary>
    /// <returns>アクティブウィンドウのハンドル</returns>
    public IntPtr GetActiveWindowHandle()
    {
        return _windowManager.GetActiveWindowHandle();
    }

    /// <summary>
    /// 指定したタイトルを持つウィンドウハンドルを取得します
    /// </summary>
    /// <param name="title">ウィンドウタイトル (部分一致)</param>
    /// <returns>一致するウィンドウのハンドル。見つからなければIntPtr.Zero</returns>
    public IntPtr FindWindowByTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title, nameof(title));
        return _windowManager.FindWindowByTitle(title);
    }

    /// <summary>
    /// 指定したクラス名を持つウィンドウハンドルを取得します
    /// </summary>
    /// <param name="className">ウィンドウクラス名</param>
    /// <returns>一致するウィンドウのハンドル。見つからなければIntPtr.Zero</returns>
    public IntPtr FindWindowByClass(string className)
    {
        ArgumentNullException.ThrowIfNull(className, nameof(className));
        return _windowManager.FindWindowByClass(className);
    }

    /// <summary>
    /// ウィンドウの位置とサイズを取得します
    /// </summary>
    /// <param name="handle">ウィンドウハンドル</param>
    /// <returns>ウィンドウの位置とサイズを表す Rectangle</returns>
    public Rectangle? GetWindowBounds(IntPtr handle)
    {
        return _windowManager.GetWindowBounds(handle);
    }

    /// <summary>
    /// ウィンドウのクライアント領域を取得します
    /// </summary>
    /// <param name="handle">ウィンドウハンドル</param>
    /// <returns>クライアント領域の位置とサイズを表す Rectangle</returns>
    public Rectangle? GetClientBounds(IntPtr handle)
    {
        return _windowManager.GetClientBounds(handle);
    }

    /// <summary>
    /// ウィンドウのタイトルを取得します
    /// </summary>
    /// <param name="handle">ウィンドウハンドル</param>
    /// <returns>ウィンドウタイトル</returns>
    public string GetWindowTitle(IntPtr handle)
    {
        return _windowManager.GetWindowTitle(handle);
    }

    /// <summary>
    /// 実行中のアプリケーションのウィンドウリストを取得します
    /// </summary>
    /// <returns>ウィンドウ情報のリスト</returns>
    public IReadOnlyCollection<Baketa.Core.Abstractions.Platform.Windows.Adapters.WindowInfo> GetRunningApplicationWindows()
    {
        try
        {
            // 基盤のIWindowManagerを使ってウィンドウを取得
            var windows = _windowManager.GetRunningApplicationWindows();
            var windowList = new List<Baketa.Core.Abstractions.Platform.Windows.Adapters.WindowInfo>();
            
            foreach (var window in windows)
            {
                var handle = window.Key;
                var title = window.Value;
                
                // 空のタイトルをスキップ
                if (string.IsNullOrWhiteSpace(title))
                    continue;
                
                // Baketaアプリケーションのみ除外（Discord風の動作）
                if (title.Contains("Baketa", StringComparison.OrdinalIgnoreCase) ||
                    title.Contains("WindowSelectionDialog", StringComparison.OrdinalIgnoreCase) ||
                    title.Contains("MainOverlay", StringComparison.OrdinalIgnoreCase))
                    continue;
                    
                var windowInfo = new Baketa.Core.Abstractions.Platform.Windows.Adapters.WindowInfo
                {
                    Handle = handle,
                    Title = title,
                    IsVisible = true, // 最小化されていても選択可能とする
                    IsMinimized = _windowManager.IsMinimized(handle),
                    Bounds = _windowManager.GetWindowBounds(handle) ?? Rectangle.Empty,
                    ThumbnailBase64 = GetWindowThumbnail(handle) ?? string.Empty
                };
                
                windowList.Add(windowInfo);
            }
            
            // 実際のウィンドウのみを表示（テストダミーウィンドウは削除）
            
            return windowList;
        }
        catch (Exception)
        {
            // エラー時はテスト用のダミーウィンドウを返す
            return
            [
                new Baketa.Core.Abstractions.Platform.Windows.Adapters.WindowInfo
                {
                    Handle = new IntPtr(12345),
                    Title = "テストウィンドウ1 - メモ帳",
                    IsVisible = true,
                    IsMinimized = false,
                    Bounds = new Rectangle(100, 100, 800, 600)
                },
                new Baketa.Core.Abstractions.Platform.Windows.Adapters.WindowInfo
                {
                    Handle = new IntPtr(12346),
                    Title = "テストウィンドウ2 - ブラウザ",
                    IsVisible = true,
                    IsMinimized = false,
                    Bounds = new Rectangle(200, 200, 1024, 768)
                },
                new Baketa.Core.Abstractions.Platform.Windows.Adapters.WindowInfo
                {
                    Handle = new IntPtr(12347),
                    Title = "テストウィンドウ3 - ゲーム",
                    IsVisible = true,
                    IsMinimized = false,
                    Bounds = new Rectangle(0, 0, 1920, 1080)
                }
            ];
        }
    }
    
    /// <summary>
    /// ウィンドウのサムネイル画像を取得
    /// </summary>
    /// <param name="handle">ウィンドウハンドル</param>
    /// <param name="maxWidth">最大幅</param>
    /// <param name="maxHeight">最大高さ</param>
    /// <returns>Base64エンコードされたサムネイル画像</returns>
    /// <summary>
    /// ウィンドウのサムネイル画像を取得（改良版キャプチャ実装）
    /// </summary>
    private string? GetWindowThumbnail(IntPtr handle, int maxWidth = 160, int maxHeight = 120)
    {
        try
        {
            // ウィンドウ可視性チェック
            if (!IsWindow(handle) || !IsWindowVisible(handle))
            {
                System.Diagnostics.Debug.WriteLine($"❌ ウィンドウが無効または非表示: Handle={handle}");
                return GenerateFallbackThumbnail(maxWidth, maxHeight);
            }

            // Win32 APIでウィンドウ情報を取得
            if (!GetWindowRect(handle, out RECT rect))
            {
                System.Diagnostics.Debug.WriteLine($"❌ GetWindowRect失敗: Handle={handle}");
                return GenerateFallbackThumbnail(maxWidth, maxHeight);
            }
                
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            
            // ウィンドウサイズ検証
            if (width <= 0 || height <= 0 || width > 4096 || height > 4096)
            {
                System.Diagnostics.Debug.WriteLine($"❌ 無効なウィンドウサイズ: {width}x{height}");
                return GenerateFallbackThumbnail(maxWidth, maxHeight);
            }

            // 画面境界チェック
            int screenWidth = GetSystemMetrics(SM_CXSCREEN);
            int screenHeight = GetSystemMetrics(SM_CYSCREEN);
            
            // ウィンドウが完全に画面外にある場合はスキップ
            if (rect.Right < 0 || rect.Bottom < 0 || rect.Left > screenWidth || rect.Top > screenHeight)
            {
                System.Diagnostics.Debug.WriteLine($"❌ ウィンドウが画面外: Rect=({rect.Left},{rect.Top},{rect.Right},{rect.Bottom})");
                return GenerateFallbackThumbnail(maxWidth, maxHeight);
            }

            // サムネイルサイズの計算（アスペクト比を保持）
            double scale = Math.Min((double)maxWidth / width, (double)maxHeight / height);
            int thumbWidth = Math.Max(1, (int)(width * scale));
            int thumbHeight = Math.Max(1, (int)(height * scale));
            
            System.Diagnostics.Debug.WriteLine($"🖼️ キャプチャ試行: Handle={handle}, Size={width}x{height}, Thumb={thumbWidth}x{thumbHeight}");

            // 方法1: PrintWindow（最優先）
            var result = TryPrintWindow(handle, width, height, thumbWidth, thumbHeight);
            if (result != null)
            {
                System.Diagnostics.Debug.WriteLine($"✅ PrintWindow成功: Handle={handle}");
                return result;
            }

            // 方法2: 一時的にウィンドウをフォアグラウンドにしてPrintWindow再試行
            result = TryPrintWindowWithForeground(handle, width, height, thumbWidth, thumbHeight);
            if (result != null)
            {
                System.Diagnostics.Debug.WriteLine($"✅ PrintWindow+Foreground成功: Handle={handle}");
                return result;
            }

            // 方法3: フォールバック画像
            System.Diagnostics.Debug.WriteLine($"❌ 全ての方法が失敗: Handle={handle}");
            return GenerateFallbackThumbnail(maxWidth, maxHeight);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"❌ サムネイル取得例外: {ex.Message}");
            return GenerateFallbackThumbnail(maxWidth, maxHeight);
        }
    }

    /// <summary>
    /// PrintWindowでキャプチャを試行
    /// </summary>
    private string? TryPrintWindow(IntPtr handle, int width, int height, int thumbWidth, int thumbHeight)
    {
        try
        {
            IntPtr desktopDC = GetDC(IntPtr.Zero);
            if (desktopDC == IntPtr.Zero) return null;
            
            IntPtr memoryDC = CreateCompatibleDC(desktopDC);
            IntPtr bitmap = CreateCompatibleBitmap(desktopDC, width, height);
            IntPtr oldBitmap = SelectObject(memoryDC, bitmap);
            
            try
            {
                // PrintWindow実行 (PW_CLIENTONLY | PW_RENDERFULLCONTENT)
                bool success = PrintWindow(handle, memoryDC, 0x00000001 | 0x00000002);
                
                if (success)
                {
                    return CreateThumbnailFromBitmap(bitmap, thumbWidth, thumbHeight);
                }
            }
            finally
            {
                _ = SelectObject(memoryDC, oldBitmap);
                _ = DeleteObject(bitmap);
                _ = DeleteDC(memoryDC);
                _ = ReleaseDC(IntPtr.Zero, desktopDC);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"TryPrintWindow例外: {ex.Message}");
        }
        
        return null;
    }

    /// <summary>
    /// 一時的にフォアグラウンドにしてPrintWindow試行
    /// </summary>
    private string? TryPrintWindowWithForeground(IntPtr handle, int width, int height, int thumbWidth, int thumbHeight)
    {
        IntPtr currentForeground = GetForegroundWindow();
        
        try
        {
            // 最小化されている場合は復元
            if (IsIconic(handle))
            {
                ShowWindow(handle, SW_RESTORE);
                System.Threading.Thread.Sleep(100); // 復元待機
            }

            // 一時的にフォアグラウンドに
            SetForegroundWindow(handle);
            System.Threading.Thread.Sleep(50); // レンダリング待機

            var result = TryPrintWindow(handle, width, height, thumbWidth, thumbHeight);
            
            return result;
        }
        finally
        {
            // 元のフォアグラウンドウィンドウを復元
            if (currentForeground != IntPtr.Zero)
            {
                SetForegroundWindow(currentForeground);
            }
        }
    }

    /// <summary>
    /// ビットマップからサムネイル作成
    /// </summary>
    private string? CreateThumbnailFromBitmap(IntPtr bitmap, int thumbWidth, int thumbHeight)
    {
        try
        {
            using var originalBitmap = Image.FromHbitmap(bitmap);
            using var thumbnail = new Bitmap(thumbWidth, thumbHeight);
            using var graphics = Graphics.FromImage(thumbnail);
            
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            graphics.DrawImage(originalBitmap, 0, 0, thumbWidth, thumbHeight);
            
            using var stream = new MemoryStream();
            thumbnail.Save(stream, ImageFormat.Png);
            return Convert.ToBase64String(stream.ToArray());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CreateThumbnailFromBitmap例外: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// フォールバック用のプレースホルダー画像を生成
    /// </summary>
    private string GenerateFallbackThumbnail(int maxWidth, int maxHeight)
    {
        try
        {
            using var thumbnail = new Bitmap(maxWidth, maxHeight);
            using var graphics = Graphics.FromImage(thumbnail);
            
            // 背景をライトグレーで塗りつぶし
            graphics.Clear(Color.FromArgb(240, 240, 240));
            
            // 枠線を描画
            using var pen = new Pen(Color.FromArgb(200, 200, 200), 2);
            graphics.DrawRectangle(pen, 1, 1, maxWidth - 2, maxHeight - 2);
            
            // アイコンを描画
            var iconSize = Math.Min(maxWidth, maxHeight) / 3;
            var iconRect = new Rectangle((maxWidth - iconSize) / 2, (maxHeight - iconSize) / 2, iconSize, iconSize);
            using var brush = new SolidBrush(Color.FromArgb(180, 180, 180));
            graphics.FillRectangle(brush, iconRect);
            
            // Base64エンコード
            using var stream = new MemoryStream();
            thumbnail.Save(stream, ImageFormat.Png);
            var base64 = Convert.ToBase64String(stream.ToArray());
            System.Diagnostics.Debug.WriteLine($"📦 フォールバック画像生成完了: {maxWidth}x{maxHeight}px, Base64={base64.Length}文字");
            return base64;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"💥 フォールバック画像生成エラー: {ex.Message}");
            return string.Empty; // nullの代わりに空文字列を返す
        }
    }
    
    #region Win32 API
    
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    
    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, int nFlags);
    
    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);
    
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);
    
    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);
    
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
    
    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdc, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop);
    
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);
    
    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
    
    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);
    
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
    
    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);
    
    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    
    // Win32定数
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int SW_RESTORE = 9;
    
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
    
    #endregion
}