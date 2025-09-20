using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Baketa.Core.Abstractions.Platform;
using Baketa.Core.Abstractions.Platform.Windows;

namespace Baketa.Infrastructure.Platform.Adapters;

    /// <summary>
    /// IWindowManagerAdapterインターフェースの基本スタブ実装
    /// 注：実際の機能実装は後の段階で行います
    /// </summary>
    public class WindowManagerAdapterStub(Baketa.Core.Abstractions.Platform.Windows.IWindowManager windowManager) : IWindowManagerAdapter
    {
        private readonly Baketa.Core.Abstractions.Platform.Windows.IWindowManager _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));

        /// <summary>
        /// Windows固有のウィンドウマネージャーを使用してアクティブなウィンドウハンドルを取得します
        /// </summary>
        /// <returns>アクティブウィンドウのハンドル</returns>
        public IntPtr GetActiveWindowHandle()
        {
            return _windowManager.GetActiveWindowHandle();
        }

        /// <summary>
        /// Windows固有のウィンドウマネージャーを使用して指定したタイトルを持つウィンドウハンドルを取得します
        /// </summary>
        /// <param name="title">ウィンドウタイトル (部分一致)</param>
        /// <returns>一致するウィンドウのハンドル。見つからなければIntPtr.Zero</returns>
        public IntPtr FindWindowByTitle(string title)
        {
            ArgumentNullException.ThrowIfNull(title, nameof(title));
            return _windowManager.FindWindowByTitle(title);
        }

        /// <summary>
        /// Windows固有のウィンドウマネージャーを使用して指定したクラス名を持つウィンドウハンドルを取得します
        /// </summary>
        /// <param name="className">ウィンドウクラス名</param>
        /// <returns>一致するウィンドウのハンドル。見つからなければIntPtr.Zero</returns>
        public IntPtr FindWindowByClass(string className)
        {
            ArgumentNullException.ThrowIfNull(className, nameof(className));
            return _windowManager.FindWindowByClass(className);
        }

        /// <summary>
        /// Windows固有のウィンドウマネージャーを使用してウィンドウの位置とサイズを取得します
        /// </summary>
        /// <param name="handle">ウィンドウハンドル</param>
        /// <returns>ウィンドウの位置とサイズを表す Rectangle</returns>
        public Rectangle? GetWindowBounds(IntPtr handle)
        {
            return _windowManager.GetWindowBounds(handle);
        }

        /// <summary>
        /// Windows固有のウィンドウマネージャーを使用してウィンドウのクライアント領域を取得します
        /// </summary>
        /// <param name="handle">ウィンドウハンドル</param>
        /// <returns>クライアント領域の位置とサイズを表す Rectangle</returns>
        public Rectangle? GetClientBounds(IntPtr handle)
        {
            return _windowManager.GetClientBounds(handle);
        }

        /// <summary>
        /// Windows固有のウィンドウマネージャーを使用してウィンドウのタイトルを取得します
        /// </summary>
        /// <param name="handle">ウィンドウハンドル</param>
        /// <returns>ウィンドウタイトル</returns>
        public string GetWindowTitle(IntPtr handle)
        {
            return _windowManager.GetWindowTitle(handle);
        }

        /// <summary>
        /// Windows固有のウィンドウマネージャーを使用して実行中のアプリケーションのウィンドウリストを取得します
        /// </summary>
        /// <returns>ウィンドウ情報のリスト</returns>
        public IReadOnlyCollection<WindowInfo> GetRunningApplicationWindows()
        {
            // 🚀 UltraThink修正: 実際のWindowsManagerを呼び出してWindowInfoに変換
            try
            {
                var windowsDict = _windowManager.GetRunningApplicationWindows();
                Console.WriteLine($"🔍 WindowManagerAdapterStub: {windowsDict.Count}個のウィンドウを取得");

                var windowInfoList = new List<WindowInfo>();

                foreach (var kvp in windowsDict)
                {
                    var handle = kvp.Key;
                    var title = kvp.Value;

                    try
                    {
                        // WindowsManagerの各メソッドを使用してWindowInfo作成
                        var bounds = _windowManager.GetWindowBounds(handle) ?? Rectangle.Empty;
                        var clientBounds = _windowManager.GetClientBounds(handle) ?? Rectangle.Empty;
                        var isMinimized = _windowManager.IsMinimized(handle);
                        var isMaximized = _windowManager.IsMaximized(handle);

                        var windowInfo = new WindowInfo
                        {
                            Handle = handle,
                            Title = title,
                            Bounds = bounds,
                            ClientBounds = clientBounds,
                            IsVisible = !bounds.IsEmpty, // 境界があれば可視とみなす
                            IsMinimized = isMinimized,
                            IsMaximized = isMaximized,
                            WindowType = WindowType.Normal, // スタブ実装では常にNormal
                            ClassName = "", // スタブでは空文字
                            ProcessId = 0, // スタブでは0
                            ProcessName = "" // スタブでは空文字
                        };

                        windowInfoList.Add(windowInfo);
                        Console.WriteLine($"✅ WindowManagerAdapterStub: WindowInfo作成完了 - '{title}' ({handle})");
                    }
                    catch (Exception infoEx)
                    {
                        Console.WriteLine($"⚠️ WindowManagerAdapterStub: WindowInfo作成エラー - Handle: {handle}, エラー: {infoEx.Message}");
                        // エラーがあっても基本情報だけでWindowInfoを作成
                        var fallbackInfo = new WindowInfo
                        {
                            Handle = handle,
                            Title = title,
                            Bounds = Rectangle.Empty,
                            ClientBounds = Rectangle.Empty,
                            IsVisible = true, // フォールバックでは可視とする
                            IsMinimized = false,
                            IsMaximized = false,
                            WindowType = WindowType.Normal,
                            ClassName = "",
                            ProcessId = 0,
                            ProcessName = ""
                        };
                        windowInfoList.Add(fallbackInfo);
                    }
                }

                Console.WriteLine($"✅ WindowManagerAdapterStub: {windowInfoList.Count}個のWindowInfo作成完了");
                return windowInfoList;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ WindowManagerAdapterStub: エラー発生 - {ex.Message}");
                // エラー時は空のリストを返す
                return [];
            }
        }

        /// <summary>
        /// Windows固有のウィンドウマネージャーを使用してウィンドウが最小化されているか確認します
        /// </summary>
        /// <param name="handle">ウィンドウハンドル</param>
        /// <returns>最小化されている場合はtrue</returns>
        public bool IsMinimized(IntPtr handle)
        {
            return _windowManager.IsMinimized(handle);
        }

        /// <summary>
        /// Windows固有のウィンドウマネージャーを使用してウィンドウが最大化されているか確認します
        /// </summary>
        /// <param name="handle">ウィンドウハンドル</param>
        /// <returns>最大化されている場合はtrue</returns>
        public bool IsMaximized(IntPtr handle)
        {
            return _windowManager.IsMaximized(handle);
        }

        /// <summary>
        /// IWindowManager(Windows)からIWindowManager(Core)への適応を行います
        /// </summary>
        /// <param name="windowsManager">Windows固有のウィンドウマネージャー</param>
        /// <returns>プラットフォーム非依存のウィンドウマネージャー</returns>
        public Baketa.Core.Abstractions.Platform.IWindowManager AdaptWindowManager(Baketa.Core.Abstractions.Platform.Windows.IWindowManager windowsManager)
        {
            // スタブ実装では適切なアダプターを返す
            return new WindowManagerAdapter(windowsManager);
        }

        /// <summary>
        /// ゲームウィンドウを特定します
        /// </summary>
        /// <param name="gameTitle">ゲームタイトル（部分一致）</param>
        /// <returns>ゲームウィンドウのハンドル。見つからなければIntPtr.Zero</returns>
        public IntPtr FindGameWindow(string gameTitle)
        {
            ArgumentNullException.ThrowIfNull(gameTitle, nameof(gameTitle));
            
            // スタブ実装では単純にタイトルで検索
            // 実際の実装ではゲーム特有の検出ロジックを使用
            return FindWindowByTitle(gameTitle);
        }

        /// <summary>
        /// ウィンドウの種類を判定します
        /// </summary>
        /// <param name="handle">ウィンドウハンドル</param>
        /// <returns>ウィンドウの種類</returns>
        public WindowType GetWindowType(IntPtr handle)
        {
            // スタブ実装では常にNormalを返す
            // 実際の実装ではウィンドウのスタイルやクラス名などを調査して種類を判定
            return WindowType.Normal;
        }
    }
