using System;
using System.Collections.Generic;
using System.Drawing;

namespace Baketa.Core.Abstractions.Platform.Windows.Adapters;

    /// <summary>
    /// WindowManagerをコアウィンドウ管理サービスに変換するアダプターインターフェース
    /// </summary>
    public interface IWindowManagerAdapter : IWindowsAdapter
    {
        /// <summary>
        /// アクティブなウィンドウハンドルを取得
        /// </summary>
        /// <returns>アクティブウィンドウのハンドル</returns>
        IntPtr GetActiveWindowHandle();
        
        /// <summary>
        /// 指定したタイトルを持つウィンドウハンドルを取得
        /// </summary>
        /// <param name="title">ウィンドウタイトル (部分一致)</param>
        /// <returns>一致するウィンドウのハンドル。見つからなければIntPtr.Zero</returns>
        IntPtr FindWindowByTitle(string title);
        
        /// <summary>
        /// 指定したクラス名を持つウィンドウハンドルを取得
        /// </summary>
        /// <param name="className">ウィンドウクラス名</param>
        /// <returns>一致するウィンドウのハンドル。見つからなければIntPtr.Zero</returns>
        IntPtr FindWindowByClass(string className);
        
        /// <summary>
        /// ウィンドウの位置とサイズを取得
        /// </summary>
        /// <param name="handle">ウィンドウハンドル</param>
        /// <returns>ウィンドウの位置とサイズを表す Rectangle</returns>
        Rectangle? GetWindowBounds(IntPtr handle);
        
        /// <summary>
        /// ウィンドウのクライアント領域を取得
        /// </summary>
        /// <param name="handle">ウィンドウハンドル</param>
        /// <returns>クライアント領域の位置とサイズを表す Rectangle</returns>
        Rectangle? GetClientBounds(IntPtr handle);
        
        /// <summary>
        /// ウィンドウのタイトルを取得
        /// </summary>
        /// <param name="handle">ウィンドウハンドル</param>
        /// <returns>ウィンドウタイトル</returns>
        string GetWindowTitle(IntPtr handle);
        
        /// <summary>
        /// 実行中のアプリケーションのウィンドウリストを取得（プラットフォーム共通のオブジェクトで表現）
        /// </summary>
        /// <returns>ウィンドウ情報のリスト</returns>
        IReadOnlyCollection<WindowInfo> GetRunningApplicationWindows();
        
        /// <summary>
        /// ウィンドウのサムネイル画像を取得
        /// </summary>
        /// <param name="handle">ウィンドウハンドル</param>
        /// <param name="maxWidth">最大幅 (デフォルト: 160)</param>
        /// <param name="maxHeight">最大高さ (デフォルト: 120)</param>
        /// <returns>Base64エンコードされたサムネイル画像 (PNG形式)</returns>
        string? GetWindowThumbnail(IntPtr handle, int maxWidth = 160, int maxHeight = 120);
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
        public required string Title { get; set; }
        
        /// <summary>
        /// ウィンドウの位置とサイズ
        /// </summary>
        public Rectangle Bounds { get; set; }
        
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
        /// ウィンドウのサムネイル画像（Base64エンコード）
        /// </summary>
        public string? ThumbnailBase64 { get; set; }
    }
