using System;
using System.Collections.Generic;
using System.Drawing;
using Baketa.Core.Abstractions.Platform;
using Baketa.Core.Abstractions.Platform.Windows;

namespace Baketa.Infrastructure.Platform.Adapters
{
    /// <summary>
    /// Windows固有のウィンドウ管理機能と抽象化レイヤーの間のアダプターインターフェース
    /// </summary>
    public interface IWindowManagerAdapter
    {
        /// <summary>
        /// Windows固有のウィンドウマネージャーを使用してアクティブなウィンドウハンドルを取得します
        /// </summary>
        /// <returns>アクティブウィンドウのハンドル</returns>
        IntPtr GetActiveWindowHandle();
        
        /// <summary>
        /// Windows固有のウィンドウマネージャーを使用して指定したタイトルを持つウィンドウハンドルを取得します
        /// </summary>
        /// <param name="title">ウィンドウタイトル (部分一致)</param>
        /// <returns>一致するウィンドウのハンドル。見つからなければIntPtr.Zero</returns>
        IntPtr FindWindowByTitle(string title);
        
        /// <summary>
        /// Windows固有のウィンドウマネージャーを使用して指定したクラス名を持つウィンドウハンドルを取得します
        /// </summary>
        /// <param name="className">ウィンドウクラス名</param>
        /// <returns>一致するウィンドウのハンドル。見つからなければIntPtr.Zero</returns>
        IntPtr FindWindowByClass(string className);
        
        /// <summary>
        /// Windows固有のウィンドウマネージャーを使用してウィンドウの位置とサイズを取得します
        /// </summary>
        /// <param name="handle">ウィンドウハンドル</param>
        /// <returns>ウィンドウの位置とサイズを表す Rectangle</returns>
        Rectangle? GetWindowBounds(IntPtr handle);
        
        /// <summary>
        /// Windows固有のウィンドウマネージャーを使用してウィンドウのクライアント領域を取得します
        /// </summary>
        /// <param name="handle">ウィンドウハンドル</param>
        /// <returns>クライアント領域の位置とサイズを表す Rectangle</returns>
        Rectangle? GetClientBounds(IntPtr handle);
        
        /// <summary>
        /// Windows固有のウィンドウマネージャーを使用してウィンドウのタイトルを取得します
        /// </summary>
        /// <param name="handle">ウィンドウハンドル</param>
        /// <returns>ウィンドウタイトル</returns>
        string GetWindowTitle(IntPtr handle);
        
        /// <summary>
        /// Windows固有のウィンドウマネージャーを使用して実行中のアプリケーションのウィンドウリストを取得します
        /// </summary>
        /// <returns>ウィンドウ情報のリスト</returns>
        IReadOnlyCollection<WindowInfo> GetRunningApplicationWindows();
        
        /// <summary>
        /// Windows固有のウィンドウマネージャーを使用してウィンドウが最小化されているか確認します
        /// </summary>
        /// <param name="handle">ウィンドウハンドル</param>
        /// <returns>最小化されている場合はtrue</returns>
        bool IsMinimized(IntPtr handle);
        
        /// <summary>
        /// Windows固有のウィンドウマネージャーを使用してウィンドウが最大化されているか確認します
        /// </summary>
        /// <param name="handle">ウィンドウハンドル</param>
        /// <returns>最大化されている場合はtrue</returns>
        bool IsMaximized(IntPtr handle);
        
        /// <summary>
        /// IWindowManager(Windows)からIWindowManager(Core)への適応を行います
        /// </summary>
        /// <param name="windowsManager">Windows固有のウィンドウマネージャー</param>
        /// <returns>プラットフォーム非依存のウィンドウマネージャー</returns>
        Baketa.Core.Abstractions.Platform.IWindowManager AdaptWindowManager(Baketa.Core.Abstractions.Platform.Windows.IWindowManager windowsManager);
        
        /// <summary>
        /// ゲームウィンドウを特定します
        /// </summary>
        /// <param name="gameTitle">ゲームタイトル（部分一致）</param>
        /// <returns>ゲームウィンドウのハンドル。見つからなければIntPtr.Zero</returns>
        IntPtr FindGameWindow(string gameTitle);
        
        /// <summary>
        /// ウィンドウの種類を判定します
        /// </summary>
        /// <param name="handle">ウィンドウハンドル</param>
        /// <returns>ウィンドウの種類</returns>
        WindowType GetWindowType(IntPtr handle);
    }
    
    /// <summary>
    /// プラットフォーム共通のウィンドウ情報
    /// </summary>
    public class WindowInfo
    {
        /// <summary>
        /// ウィンドウハンドル（プラットフォーム固有）
        /// </summary>
        public IntPtr Handle { get; set; }
        
        /// <summary>
        /// ウィンドウタイトル
        /// </summary>
        public string Title { get; set; } = string.Empty;
        
        /// <summary>
        /// ウィンドウの位置とサイズ
        /// </summary>
        public Rectangle Bounds { get; set; }
        
        /// <summary>
        /// クライアント領域の位置とサイズ
        /// </summary>
        public Rectangle ClientBounds { get; set; }
        
        /// <summary>
        /// 可視状態
        /// </summary>
        public bool IsVisible { get; set; }
        
        /// <summary>
        /// 最小化状態
        /// </summary>
        public bool IsMinimized { get; set; }
        
        /// <summary>
        /// 最大化状態
        /// </summary>
        public bool IsMaximized { get; set; }
        
        /// <summary>
        /// ウィンドウの種類
        /// </summary>
        public WindowType WindowType { get; set; }
        
        /// <summary>
        /// ウィンドウクラス名
        /// </summary>
        public string ClassName { get; set; } = string.Empty;
        
        /// <summary>
        /// プロセスID
        /// </summary>
        public int ProcessId { get; set; }
        
        /// <summary>
        /// プロセス名
        /// </summary>
        public string ProcessName { get; set; } = string.Empty;
    }
    
    /// <summary>
    /// ウィンドウの種類
    /// </summary>
    public enum WindowType
    {
        /// <summary>
        /// 標準的なアプリケーションウィンドウ
        /// </summary>
        Normal,
        
        /// <summary>
        /// ゲームウィンドウ
        /// </summary>
        Game,
        
        /// <summary>
        /// ダイアログ
        /// </summary>
        Dialog,
        
        /// <summary>
        /// ツール/ユーティリティウィンドウ
        /// </summary>
        Tool,
        
        /// <summary>
        /// システムウィンドウ
        /// </summary>
        System,
        
        /// <summary>
        /// 不明/その他
        /// </summary>
        Unknown
    }
}