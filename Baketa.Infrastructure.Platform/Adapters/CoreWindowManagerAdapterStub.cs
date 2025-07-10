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
                    ThumbnailBase64 = string.Empty // 一時的に無効化してFormatException回避
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
    /// ウィンドウのサムネイル画像を取得（実際のキャプチャ実装）
    /// </summary>
    private string? GetWindowThumbnail(IntPtr handle, int maxWidth = 160, int maxHeight = 120)
    {
        try
        {
            // Win32 APIでウィンドウ情報を取得
            if (!GetWindowRect(handle, out RECT rect))
            {
                return GenerateFallbackThumbnail(maxWidth, maxHeight);
            }
                
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            
            if (width <= 0 || height <= 0)
            {
                return GenerateFallbackThumbnail(maxWidth, maxHeight);
            }

            // サムネイルサイズの計算（アスペクト比を保持）
            double scale = Math.Min((double)maxWidth / width, (double)maxHeight / height);
            int thumbWidth = Math.Max(1, (int)(width * scale));
            int thumbHeight = Math.Max(1, (int)(height * scale));
            
            // デスクトップDCを取得
            IntPtr desktopDC = GetDC(IntPtr.Zero);
            if (desktopDC == IntPtr.Zero)
            {
                return GenerateFallbackThumbnail(maxWidth, maxHeight);
            }
            
            // 互換DCとビットマップを作成
            IntPtr memoryDC = CreateCompatibleDC(desktopDC);
            IntPtr bitmap = CreateCompatibleBitmap(desktopDC, width, height);
            IntPtr oldBitmap = SelectObject(memoryDC, bitmap);
            
            try
            {
                // PrintWindow を試行
                System.Diagnostics.Debug.WriteLine($"Attempting PrintWindow for handle {handle}");
                bool printWindowSuccess = PrintWindow(handle, memoryDC, 0);
                
                if (!printWindowSuccess)
                {
                    // PrintWindow失敗時はBitBltを使用してスクリーンキャプチャ
                    System.Diagnostics.Debug.WriteLine("PrintWindow failed, trying BitBlt");
                    const uint SRCCOPY = 0x00CC0020;
                    printWindowSuccess = BitBlt(memoryDC, 0, 0, width, height, desktopDC, rect.Left, rect.Top, SRCCOPY);
                    System.Diagnostics.Debug.WriteLine($"BitBlt result: {printWindowSuccess}");
                }
                
                if (printWindowSuccess)
                {
                    System.Diagnostics.Debug.WriteLine("Window capture succeeded");
                    // Bitmapオブジェクトを作成
                    using var originalBitmap = Image.FromHbitmap(bitmap);
                    System.Diagnostics.Debug.WriteLine($"Original bitmap size: {originalBitmap.Width}x{originalBitmap.Height}");
                    
                    using var thumbnail = new Bitmap(thumbWidth, thumbHeight);
                    using var graphics = Graphics.FromImage(thumbnail);
                    
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(originalBitmap, 0, 0, thumbWidth, thumbHeight);
                    
                    // Base64エンコード
                    using var stream = new MemoryStream();
                    thumbnail.Save(stream, ImageFormat.Png);
                    var base64 = Convert.ToBase64String(stream.ToArray());
                    
                    System.Diagnostics.Debug.WriteLine($"✅ ウィンドウキャプチャ成功: Base64長={base64.Length}文字");
                    System.Diagnostics.Debug.WriteLine($"🖼️ サムネイル情報: {thumbWidth}x{thumbHeight}px");
                    return base64;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"❌ PrintWindowとBitBlt両方が失敗: Handle={handle}");
                    var fallback = GenerateFallbackThumbnail(maxWidth, maxHeight);
                    System.Diagnostics.Debug.WriteLine($"🔄 フォールバック画像生成: Base64長={fallback.Length}文字");
                    return fallback;
                }
            }
            finally
            {
                // リソースを解放
                _ = SelectObject(memoryDC, oldBitmap);
                _ = DeleteObject(bitmap);
                _ = DeleteDC(memoryDC);
                _ = ReleaseDC(IntPtr.Zero, desktopDC);
            }
        }
        catch (Exception)
        {
            return GenerateFallbackThumbnail(maxWidth, maxHeight);
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