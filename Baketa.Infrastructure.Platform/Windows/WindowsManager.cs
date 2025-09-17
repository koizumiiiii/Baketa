using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Baketa.Core.Abstractions.Platform.Windows;

namespace Baketa.Infrastructure.Platform.Windows;

    /// <summary>
    /// IWindowManagerインターフェースのWindows特化実装
    /// Win32 APIを使用して実際のウィンドウ情報を取得
    /// </summary>
    public class WindowsManager : IWindowManager
    {
        // P/Invoke宣言は NativeMethods.User32Methods を使用
        
        /// <summary>
        /// ウィンドウのサムネイル画像を取得
        /// </summary>
        /// <param name="handle">ウィンドウハンドル</param>
        /// <param name="maxWidth">最大幅</param>
        /// <param name="maxHeight">最大高さ</param>
        /// <returns>Base64エンコードされたサムネイル画像</returns>
        public string? GetWindowThumbnail(IntPtr handle, int maxWidth = 160, int maxHeight = 120)
        {
            try
            {
                if (!NativeMethods.User32Methods.GetWindowRect(handle, out NativeMethods.RECT rect))
                    return null;
                    
                int width = rect.right - rect.left;
                int height = rect.bottom - rect.top;
                
                if (width <= 0 || height <= 0)
                    return null;
                
                // サムネイルサイズの計算（アスペクト比を保持）
                double scale = Math.Min((double)maxWidth / width, (double)maxHeight / height);
                int thumbWidth = Math.Max(1, (int)(width * scale));
                int thumbHeight = Math.Max(1, (int)(height * scale));
                
                // デスクトップDCを取得
                IntPtr desktopDC = NativeMethods.User32Methods.GetDC(IntPtr.Zero);
                if (desktopDC == IntPtr.Zero)
                    return null;
                
                // 互換DCとビットマップを作成
                IntPtr memoryDC = NativeMethods.Gdi32Methods.CreateCompatibleDC(desktopDC);
                IntPtr bitmap = NativeMethods.Gdi32Methods.CreateCompatibleBitmap(desktopDC, width, height);
                IntPtr oldBitmap = NativeMethods.Gdi32Methods.SelectObject(memoryDC, bitmap);
                
                try
                {
                    // ウィンドウ画像をキャプチャ
                    if (NativeMethods.User32Methods.PrintWindow(handle, memoryDC, 0))
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
                    _ = NativeMethods.Gdi32Methods.SelectObject(memoryDC, oldBitmap);
                    _ = NativeMethods.Gdi32Methods.DeleteObject(bitmap);
                    _ = NativeMethods.Gdi32Methods.DeleteDC(memoryDC);
                    _ = NativeMethods.User32Methods.ReleaseDC(IntPtr.Zero, desktopDC);
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
                return NativeMethods.User32Methods.GetForegroundWindow();
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
                if (NativeMethods.User32Methods.GetWindowRect(handle, out NativeMethods.RECT rect))
                {
                    return new Rectangle(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);
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
                var titleBuffer = new StringBuilder(maxLength);
                var length = NativeMethods.User32Methods.GetWindowText(handle, titleBuffer, maxLength);
                return length > 0 ? titleBuffer.ToString() : "";
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
                return NativeMethods.User32Methods.IsIconic(handle);
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
            var visibleWindows = new List<IntPtr>();
            
            try
            {
                // 🚀 Gemini Expert Recommendation: EnumWindows軽量実装でProcess.GetProcesses()完全置き換え
                // メモリ競合回避 + 数十倍高速化で機能と安全性を両立
                
                System.Diagnostics.Debug.WriteLine("🚀 WindowsManager: EnumWindows軽量実装でウィンドウ列挙開始");
                Console.WriteLine("🚀 WindowsManager: EnumWindows軽量実装でウィンドウ列挙開始");
                
                uint currentProcessId = (uint)Environment.ProcessId;
                
                // EnumWindowsで全ウィンドウを軽量に列挙
                bool enumResult = NativeMethods.User32Methods.EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
                {
                    try
                    {
                        // 🚀 UltraThink根本修正: GetWindowTextLength制限を撤廃
                        // ゲーム系ウィンドウはタイトル長0でも有効な場合が多い
                        
                        // Step 1: 基本的なウィンドウ有効性チェックのみ
                        if (!NativeMethods.User32Methods.IsWindow(hWnd))
                        {
                            Console.WriteLine($"⚠️  WindowsManager: 無効ウィンドウをスキップ - ハンドル: {hWnd}");
                            return true; // 次のウィンドウへ
                        }
                        
                        // Step 2: 自プロセスのウィンドウは除外
                        uint threadId = NativeMethods.User32Methods.GetWindowThreadProcessId(hWnd, out uint windowProcessId);
                        if (windowProcessId == currentProcessId)
                        {
                            Console.WriteLine($"⚠️  WindowsManager: 自プロセスウィンドウをスキップ - ハンドル: {hWnd}, PID: {windowProcessId}");
                            return true; // 次のウィンドウへ
                        }
                        
                        // Step 3: 詳細デバッグログ with タイトル長情報とタイトル文字列
                        int titleLength = NativeMethods.User32Methods.GetWindowTextLength(hWnd);
                        string actualTitle = GetWindowTitle(hWnd);
                        Console.WriteLine($"🔍 WindowsManager: 候補ウィンドウ発見 - ハンドル: {hWnd}, PID: {windowProcessId}, タイトル長: {titleLength}, タイトル: '{actualTitle}'");
                        System.Diagnostics.Debug.WriteLine($"🔍 WindowsManager: 候補ウィンドウ発見 - ハンドル: {hWnd}, PID: {windowProcessId}, タイトル長: {titleLength}, タイトル: '{actualTitle}'");
                        
                        // Step 4: すべての候補をリストに追加（タイトル長に関係なく）
                        visibleWindows.Add(hWnd);
                        return true; // 列挙を続ける
                    }
                    catch (Exception ex)
                    {
                        // Win32 APIエラー時はログ出力してスキップ
                        Console.WriteLine($"❌ WindowsManager: EnumWindows例外 - ハンドル: {hWnd}, エラー: {ex.Message}");
                        return true;
                    }
                }, IntPtr.Zero);
                
                // 🎯 Gemini Expert推奨: EnumWindows結果検証とエラーハンドリング
                if (!enumResult)
                {
                    int lastError = Marshal.GetLastWin32Error();
                    Console.WriteLine($"⚠️  WindowsManager: EnumWindows失敗 - Win32エラーコード: {lastError}");
                    System.Diagnostics.Debug.WriteLine($"⚠️  WindowsManager: EnumWindows失敗 - Win32エラーコード: {lastError}");
                    // 部分的な結果でも処理を継続（完全失敗ではない場合）
                }
                
                Console.WriteLine($"✅ WindowsManager: EnumWindows完了 - 候補ウィンドウ数: {visibleWindows.Count}");
                
                // 各ウィンドウのタイトルを取得
                foreach (var handle in visibleWindows)
                {
                    try
                    {
                        string title = GetWindowTitle(handle);
                        
                        // 🚀 UltraThink緩和: 空タイトルには代替表示名を付与
                        string displayTitle = string.IsNullOrEmpty(title) ? $"<無題ウィンドウ {handle}>" : title;
                        Console.WriteLine($"🔍 WindowsManager: ハンドル {handle} のタイトル: '{title}' → 表示名: '{displayTitle}'");
                        
                        // IsValidApplicationWindowの判定を実行（デバッグのため）
                        bool isValid = IsValidApplicationWindow(title, handle);
                        
                        if (isValid)
                        {
                            windows[handle] = displayTitle;  // 表示名を使用
                            Console.WriteLine($"✅ WindowsManager: 有効ウィンドウ追加 - {displayTitle}");
                        }
                        else
                        {
                            Console.WriteLine($"❌ WindowsManager: ウィンドウ除外 - タイトル: '{title}', 表示名: '{displayTitle}', 有効性: {isValid}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ WindowsManager: タイトル取得エラー - ハンドル: {handle}, エラー: {ex.Message}");
                    }
                }
                
                System.Diagnostics.Debug.WriteLine($"✅ WindowsManager: ウィンドウ列挙完了 - {windows.Count}個のウィンドウを検出");
                Console.WriteLine($"✅ WindowsManager: ウィンドウ列挙完了 - {windows.Count}個のウィンドウを検出");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ WindowsManager: EnumWindowsエラー - {ex.Message}");
                Console.WriteLine($"❌ WindowsManager: EnumWindowsエラー - {ex.Message}");
            }
            
            return windows;
        }
        
        /// <summary>
        /// アプリケーションウィンドウとして有効か判定
        /// </summary>
        private bool IsValidApplicationWindow(string title, IntPtr handle)
        {
            // 🚀 UltraThink緩和: 空のタイトルも一時的に許可（ゲーム系対応）
            Console.WriteLine($"🔍 IsValidApplicationWindow: 判定開始 - ハンドル: {handle}, タイトル: '{title}'");
            
            // Baketaアプリケーションを除外（これは必須）
            if (!string.IsNullOrEmpty(title))
            {
                if (title.Contains("Baketa", StringComparison.OrdinalIgnoreCase) ||
                    title.Contains("WindowSelectionDialog", StringComparison.OrdinalIgnoreCase) ||
                    title.Contains("MainOverlay", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"❌ IsValidApplicationWindow: Baketaアプリ除外 - タイトル: '{title}'");
                    return false;
                }
                
                // 明らかなシステムウィンドウは除外
                var systemWindowTitles = new[]
                {
                    "Program Manager", "デスクトップ", "タスクバー",
                    "Desktop Window Manager", "Windows Shell Experience Host"
                };
                
                foreach (var systemTitle in systemWindowTitles)
                {
                    if (title.Contains(systemTitle, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"❌ IsValidApplicationWindow: システムウィンドウ除外 - タイトル: '{title}' (除外理由: '{systemTitle}')");
                        return false;
                    }
                }
            }
            
            // 🎯 追加検証: ウィンドウ可視性とスタイル
            bool isVisible = NativeMethods.User32Methods.IsWindowVisible(handle);
            Console.WriteLine($"🔍 IsValidApplicationWindow: 可視性チェック - ハンドル: {handle}, 可視: {isVisible}");
            
            // 可視性に関係なく一旦通す（最小化ウィンドウ対応）
            Console.WriteLine($"✅ IsValidApplicationWindow: 有効ウィンドウ判定 - タイトル: '{title}', 可視: {isVisible}");
            return true;
        }
    }
