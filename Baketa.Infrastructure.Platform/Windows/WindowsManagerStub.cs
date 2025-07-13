using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Baketa.Core.Abstractions.Platform.Windows;

namespace Baketa.Infrastructure.Platform.Windows;

    /// <summary>
    /// IWindowManagerインターフェースのWindows特化実装
    /// Win32 APIを使用して実際のウィンドウ情報を取得
    /// </summary>
    public class WindowsManagerStub : IWindowManager
    {
        #region Win32 API宣言
        
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, [Out] char[] text, int count);
        
        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);
        
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);
        
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        
        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, int nFlags);
        
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
        
        /// <summary>
        /// ウィンドウのサムネイル画像を取得
        /// </summary>
        /// <param name="handle">ウィンドウハンドル</param>
        /// <param name="maxWidth">最大幅</param>
        /// <param name="maxHeight">最大高さ</param>
        /// <returns>Base64エンコードされたサムネイル画像</returns>
        private string? GetWindowThumbnail(IntPtr handle, int maxWidth = 160, int maxHeight = 120)
        {
            try
            {
                if (!GetWindowRect(handle, out RECT rect))
                    return null;
                    
                int width = rect.Right - rect.Left;
                int height = rect.Bottom - rect.Top;
                
                if (width <= 0 || height <= 0)
                    return null;
                
                // サムネイルサイズの計算（アスペクト比を保持）
                double scale = Math.Min((double)maxWidth / width, (double)maxHeight / height);
                int thumbWidth = Math.Max(1, (int)(width * scale));
                int thumbHeight = Math.Max(1, (int)(height * scale));
                
                // デスクトップDCを取得
                IntPtr desktopDC = GetDC(IntPtr.Zero);
                if (desktopDC == IntPtr.Zero)
                    return null;
                
                // 互換DCとビットマップを作成
                IntPtr memoryDC = CreateCompatibleDC(desktopDC);
                IntPtr bitmap = CreateCompatibleBitmap(desktopDC, width, height);
                IntPtr oldBitmap = SelectObject(memoryDC, bitmap);
                
                try
                {
                    // ウィンドウ画像をキャプチャ
                    if (PrintWindow(handle, memoryDC, 0))
                    {
                        // Bitmapオブジェクトを作成
                        using var originalBitmap = Image.FromHbitmap(bitmap);
                        using var thumbnail = new Bitmap(thumbWidth, thumbHeight);
                        using var graphics = Graphics.FromImage(thumbnail);
                        
                        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        graphics.DrawImage(originalBitmap, 0, 0, thumbWidth, thumbHeight);
                        
                        // Base64エンコード
                        using var stream = new MemoryStream();
                        thumbnail.Save(stream, ImageFormat.Png);
                        return Convert.ToBase64String(stream.ToArray());
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
                // エラー時はnullを返す
            }
            
            return null;
        }
        /// <summary>
        /// アクティブなウィンドウハンドルを取得
        /// </summary>
        /// <returns>アクティブウィンドウのハンドル</returns>
        public IntPtr GetActiveWindowHandle()
        {
            try
            {
                return GetForegroundWindow();
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        /// <summary>
        /// 指定したタイトルを持つウィンドウハンドルを取得
        /// </summary>
        /// <param name="title">ウィンドウタイトル (部分一致)</param>
        /// <returns>一致するウィンドウのハンドル。見つからなければIntPtr.Zero</returns>
        public IntPtr FindWindowByTitle(string title)
        {
            // スタブ実装では常にIntPtr.Zeroを返す
            return IntPtr.Zero;
        }

        /// <summary>
        /// 指定したクラス名を持つウィンドウハンドルを取得
        /// </summary>
        /// <param name="className">ウィンドウクラス名</param>
        /// <returns>一致するウィンドウのハンドル。見つからなければIntPtr.Zero</returns>
        public IntPtr FindWindowByClass(string className)
        {
            // スタブ実装では常にIntPtr.Zeroを返す
            return IntPtr.Zero;
        }

        /// <summary>
        /// ウィンドウの位置とサイズを取得
        /// </summary>
        /// <param name="handle">ウィンドウハンドル</param>
        /// <returns>ウィンドウの位置とサイズを表す Rectangle</returns>
        public Rectangle? GetWindowBounds(IntPtr handle)
        {
            try
            {
                if (GetWindowRect(handle, out RECT rect))
                {
                    return new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// ウィンドウのクライアント領域を取得
        /// </summary>
        /// <param name="handle">ウィンドウハンドル</param>
        /// <returns>クライアント領域の位置とサイズを表す Rectangle</returns>
        public Rectangle? GetClientBounds(IntPtr handle)
        {
            // スタブ実装では780x560の位置(10,30)の矩形を返す（ウィンドウ境界と想定）
            return new Rectangle(10, 30, 780, 560);
        }

        /// <summary>
        /// ウィンドウのタイトルを取得
        /// </summary>
        /// <param name="handle">ウィンドウハンドル</param>
        /// <returns>ウィンドウタイトル</returns>
        public string GetWindowTitle(IntPtr handle)
        {
            try
            {
                const int maxLength = 256;
                var titleBuffer = new char[maxLength];
                var length = GetWindowText(handle, titleBuffer, maxLength);
                return new string(titleBuffer, 0, length);
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// ウィンドウが最小化されているか確認
        /// </summary>
        /// <param name="handle">ウィンドウハンドル</param>
        /// <returns>最小化されている場合はtrue</returns>
        public bool IsMinimized(IntPtr handle)
        {
            try
            {
                return IsIconic(handle);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// ウィンドウが最大化されているか確認
        /// </summary>
        /// <param name="handle">ウィンドウハンドル</param>
        /// <returns>最大化されている場合はtrue</returns>
        public bool IsMaximized(IntPtr handle)
        {
            // スタブ実装では常にfalseを返す
            return false;
        }

        /// <summary>
        /// ウィンドウの位置とサイズを設定
        /// </summary>
        /// <param name="handle">ウィンドウハンドル</param>
        /// <param name="bounds">新しい位置とサイズ</param>
        /// <returns>成功した場合はtrue</returns>
        public bool SetWindowBounds(IntPtr handle, Rectangle bounds)
        {
            // スタブ実装では常にtrueを返す
            return true;
        }
        
        /// <summary>
        /// ウィンドウの透明度を設定
        /// </summary>
        /// <param name="handle">ウィンドウハンドル</param>
        /// <param name="opacity">透明度 (0.0-1.0)</param>
        /// <returns>成功した場合はtrue</returns>
        public bool SetWindowOpacity(IntPtr handle, double opacity)
        {
            // スタブ実装では常にtrueを返す
            return true;
        }
        
        /// <summary>
        /// ウィンドウを前面に表示
        /// </summary>
        /// <param name="handle">ウィンドウハンドル</param>
        /// <returns>成功した場合はtrue</returns>
        public bool BringWindowToFront(IntPtr handle)
        {
            // スタブ実装では常にtrueを返す
            return true;
        }
        
        /// <summary>
        /// 実行中のアプリケーションのウィンドウリストを取得
        /// </summary>
        /// <returns>ウィンドウハンドルとタイトルのディクショナリ</returns>
        public Dictionary<IntPtr, string> GetRunningApplicationWindows()
        {
            var windows = new Dictionary<IntPtr, string>();
            
            try
            {
                // System.Diagnostics.Processを使用して実際のウィンドウを取得
                var processes = System.Diagnostics.Process.GetProcesses();
                
                foreach (var process in processes)
                {
                    try
                    {
                        // メインウィンドウハンドルがあるプロセスのみ処理
                        if (process.MainWindowHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(process.MainWindowTitle))
                        {
                            // システムプロセスや非表示ウィンドウをフィルタリング
                            var processName = process.ProcessName.ToLower(System.Globalization.CultureInfo.InvariantCulture);
                            var windowTitle = process.MainWindowTitle;
                            
                            // 最小限のシステムプロセスのみ除外（Discord風）
                            if (processName == "dwm" || processName == "winlogon" || processName == "csrss" || 
                                processName == "smss" || processName == "wininit" || processName == "services" ||
                                processName == "lsass" || processName == "svchost" || processName.StartsWith("system", StringComparison.InvariantCulture))
                            {
                                continue;
                            }
                            
                            // メインウィンドウが表示されているかチェック
                            if (!IsWindowVisible(process.MainWindowHandle))
                            {
                                continue;
                            }
                            
                            // Baketaアプリケーションを除外
                            if (processName.Contains("baketa", StringComparison.InvariantCulture) ||
                                windowTitle.Contains("Baketa", StringComparison.OrdinalIgnoreCase) ||
                                windowTitle.Contains("WindowSelectionDialog", StringComparison.OrdinalIgnoreCase) ||
                                windowTitle.Contains("MainOverlay", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                            
                            // デスクトップやタスクバーなどのシステムコンポーネントを除外
                            if (processName == "explorer" && (windowTitle == "デスクトップ" || windowTitle == "タスクバー" || windowTitle == "Program Manager"))
                            {
                                continue;
                            }
                            
                            windows[process.MainWindowHandle] = windowTitle;
                        }
                    }
                    catch (Exception)
                    {
                        // プロセスにアクセスできない場合はスキップ
                        continue;
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch (Exception)
            {
                // エラー時は空のディクショナリを返す
            }
            
            return windows;
        }
    }
