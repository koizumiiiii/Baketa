using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Platform.Windows;

namespace Baketa.Infrastructure.Platform.Windows;

/// <summary>
/// IWindowManagerインターフェースのWindows特化実装
/// Win32 APIを使用して実際のウィンドウ情報を取得
/// </summary>
public class WindowsManager : IWindowManager, Baketa.Core.Abstractions.Platform.IWindowManager
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
        try
        {
            if (NativeMethods.User32Methods.GetClientRect(handle, out NativeMethods.RECT clientRect))
            {
                return new Rectangle(0, 0, clientRect.right - clientRect.left, clientRect.bottom - clientRect.top);
            }
            return null;
        }
        catch
        {
            return null;
        }
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
            // 🛠️ ハング防止: タイムアウト保護付きでGetWindowText実行
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)); // 100ms timeout
            var task = Task.Run(() =>
            {
                const int maxLength = 256;
                var titleBuffer = new StringBuilder(maxLength);
                var length = NativeMethods.User32Methods.GetWindowText(handle, titleBuffer, maxLength);
                return length > 0 ? titleBuffer.ToString() : "";
            }, cts.Token);

            if (task.Wait(100)) // 100ms wait
            {
                return task.Result;
            }
            else
            {
                Console.WriteLine($"⚠️ GetWindowTitle タイムアウト: Handle={handle}");
                return ""; // タイムアウト時は空文字列を返す
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine($"⚠️ GetWindowTitle キャンセル: Handle={handle}");
            return "";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ GetWindowTitle エラー: Handle={handle}, Error={ex.Message}");
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

            // 🛡️ UltraThink修正: EnumWindows APIタイムアウト保護実装
            var enumTask = Task.Run(() =>
            {
                try
                {
                    Console.WriteLine("🛡️ WindowsManager: タイムアウト保護付きEnumWindows開始");

                    return NativeMethods.User32Methods.EnumWindows(delegate (IntPtr hWnd, IntPtr lParam)
                    {
                        try
                        {
                            // Step 1: 基本的なウィンドウ有効性チェック（タイムアウト保護）
                            var isWindowTask = Task.Run(() => NativeMethods.User32Methods.IsWindow(hWnd));
                            if (!isWindowTask.Wait(500) || !isWindowTask.Result) // 0.5秒タイムアウト
                            {
                                Console.WriteLine($"⏰ WindowsManager: IsWindow タイムアウト/失敗 - ハンドル: {hWnd}");
                                return true; // 次のウィンドウへ
                            }

                            // Step 2: 自プロセスのウィンドウは除外（タイムアウト保護）
                            uint windowProcessId = 0;
                            var processIdTask = Task.Run(() =>
                            {
#pragma warning disable CA1806 // out引数でpidが取得できるため戻り値チェックは不要
                                NativeMethods.User32Methods.GetWindowThreadProcessId(hWnd, out uint pid);
#pragma warning restore CA1806
                                return pid;
                            });
                            if (!processIdTask.Wait(500)) // 0.5秒タイムアウト
                            {
                                Console.WriteLine($"⏰ WindowsManager: GetWindowThreadProcessId タイムアウト - ハンドル: {hWnd}");
                                return true; // タイムアウト時はスキップ
                            }

                            windowProcessId = processIdTask.Result; // 結果を取得
                            if (windowProcessId == currentProcessId)
                            {
                                Console.WriteLine($"⚠️ WindowsManager: 自プロセスウィンドウをスキップ - ハンドル: {hWnd}, PID: {windowProcessId}");
                                return true; // 次のウィンドウへ
                            }

                            // 🚀 Step 3: タイトル取得（タイムアウト保護）
                            string title = "";
                            var titleTask = Task.Run(() => GetWindowTitle(hWnd));
                            if (titleTask.Wait(1000)) // 1秒タイムアウト
                            {
                                title = titleTask.Result ?? "";
                            }
                            else
                            {
                                Console.WriteLine($"⏰ WindowsManager: GetWindowTitle タイムアウト - ハンドル: {hWnd}");
                                title = $"<Timeout-{hWnd}>"; // タイムアウト時は一意の識別子
                            }

                            // Step 4: 事前フィルタリング - 不要な内部ウィンドウを除外
                            if (IsInternalSystemWindow(title))
                            {
                                Console.WriteLine($"⚠️ WindowsManager: 内部ウィンドウをスキップ - タイトル: '{title}'");
                                return true; // 次のウィンドウへ
                            }

                            // タイトル長取得（タイムアウト保護）
                            int titleLength = 0;
                            var titleLengthTask = Task.Run(() => NativeMethods.User32Methods.GetWindowTextLength(hWnd));
                            if (titleLengthTask.Wait(500)) // 0.5秒タイムアウト
                            {
                                titleLength = titleLengthTask.Result;
                            }

                            Console.WriteLine($"🔍 WindowsManager: 候補ウィンドウ発見 - ハンドル: {hWnd}, PID: {windowProcessId}, タイトル長: {titleLength}, タイトル: '{title}'");

                            // Step 5: 有効な候補をリストに追加
                            lock (visibleWindows)
                            {
                                visibleWindows.Add(hWnd);
                            }
                            return true; // 列挙を続ける
                        }
                        catch (Exception ex)
                        {
                            // Win32 APIエラー時はログ出力してスキップ
                            Console.WriteLine($"❌ WindowsManager: EnumWindows例外 - ハンドル: {hWnd}, エラー: {ex.Message}");
                            return true;
                        }
                    }, IntPtr.Zero);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ WindowsManager: EnumWindowsタスク例外: {ex.Message}");
                    return false;
                }
            });

            // EnumWindows全体に15秒タイムアウト
            bool enumResult;
            if (enumTask.Wait(15000)) // 15秒タイムアウト
            {
                enumResult = enumTask.Result;
                Console.WriteLine("✅ WindowsManager: EnumWindowsタスク正常完了");
            }
            else
            {
                Console.WriteLine("⚠️ WindowsManager: EnumWindowsタスクタイムアウト（15秒） - フォールバック処理継続");
                enumResult = false;
            }

            // 🎯 Gemini Expert推奨: EnumWindows結果検証とエラーハンドリング
            if (!enumResult)
            {
                int lastError = Marshal.GetLastWin32Error();
                Console.WriteLine($"⚠️ WindowsManager: EnumWindows失敗 - Win32エラーコード: {lastError}");
                System.Diagnostics.Debug.WriteLine($"⚠️ WindowsManager: EnumWindows失敗 - Win32エラーコード: {lastError}");
            }

            Console.WriteLine($"✅ WindowsManager: EnumWindows完了 - 候補ウィンドウ数: {visibleWindows.Count}");

            // 🚀 UltraThink修正: Parallel.ForEachハング対策 - タイムアウトと並列度制限
            Console.WriteLine("🚀 WindowsManager: 並列処理でタイトル取得開始（ハング対策版）");

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount), // 最大4並列に制限
                CancellationToken = CancellationToken.None
            };

            var validWindows = new ConcurrentDictionary<IntPtr, string>();

            // 🛡️ UltraThink修正: タイムアウト保護付き並列処理
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)); // 10秒タイムアウト
            parallelOptions.CancellationToken = cts.Token;

            try
            {
                Parallel.ForEach(visibleWindows, parallelOptions, handle =>
                {
                    try
                    {
                        // キャンセレーション確認
                        cts.Token.ThrowIfCancellationRequested();

                        string title = GetWindowTitle(handle);

                        // 🚀 UltraThink緩和: 空タイトルには代替表示名を付与
                        string displayTitle = string.IsNullOrEmpty(title) ? $"<無題ウィンドウ {handle}>" : title;
                        Console.WriteLine($"🔍 WindowsManager: ハンドル {handle} のタイトル: '{title}' → 表示名: '{displayTitle}'");

                        // IsValidApplicationWindowの判定を実行（デバッグのため）
                        bool isValid = IsValidApplicationWindow(title, handle);

                        if (isValid)
                        {
                            validWindows[handle] = displayTitle;  // 表示名を使用
                            Console.WriteLine($"✅ WindowsManager: 有効ウィンドウ追加 - {displayTitle}");
                        }
                        else
                        {
                            Console.WriteLine($"❌ WindowsManager: ウィンドウ除外 - タイトル: '{title}', 表示名: '{displayTitle}', 有効性: {isValid}");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        Console.WriteLine($"⚠️ WindowsManager: 並列処理キャンセル - ハンドル: {handle}");
                        throw; // Parallel.ForEachに伝播
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ WindowsManager: タイトル取得エラー - ハンドル: {handle}, エラー: {ex.Message}");
                    }
                });

                Console.WriteLine("✅ WindowsManager: 並列処理完了");
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("⚠️ WindowsManager: 並列処理タイムアウト（10秒） - 部分結果を使用");
            }

            // ConcurrentDictionaryから通常のDictionaryに変換
            foreach (var kvp in validWindows)
            {
                windows[kvp.Key] = kvp.Value;
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

    /// <summary>
    /// 内部システムウィンドウ（処理不要）かどうかを判定
    /// ユーザーの翻訳対象として不適切な内部ウィンドウを早期フィルタリング
    /// </summary>
    /// <param name="title">ウィンドウタイトル</param>
    /// <returns>内部システムウィンドウの場合はtrue</returns>
    /// <summary>
    /// 内部システムウィンドウ（処理不要）かどうかを判定
    /// 最小限のフィルタリングのみ実行（WindowSelectionDialogViewModelの二次フィルタリングと重複しないよう）
    /// </summary>
    /// <param name="title">ウィンドウタイトル</param>
    /// <returns>内部システムウィンドウの場合はtrue</returns>
    private static bool IsInternalSystemWindow(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return true; // 空タイトルは内部ウィンドウとして扱う
        }

        // 最小限のIME関連ウィンドウのみフィルタリング（WindowSelectionDialogViewModelと重複回避）
        var criticalInternalPatterns = new[]
        {
                "MSCTFIME UI", "Default IME", "PopupHost"
            };

        foreach (var pattern in criticalInternalPatterns)
        {
            if (title.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // 極端に短いタイトル（1-2文字）のみ除外
        if (title.Trim().Length <= 2)
        {
            return true;
        }

        return false; // その他は有効なアプリケーションウィンドウとして扱う
    }
}
