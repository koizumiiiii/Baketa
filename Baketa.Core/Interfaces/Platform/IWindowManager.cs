using System;
using System.Drawing;

namespace Baketa.Core.Interfaces.Platform
{
    /// <summary>
    /// ウィンドウ管理インターフェース
    /// </summary>
    public interface IWindowManager
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
        /// ウィンドウが最小化されているか確認
        /// </summary>
        /// <param name="handle">ウィンドウハンドル</param>
        /// <returns>最小化されている場合はtrue</returns>
        bool IsMinimized(IntPtr handle);
        
        /// <summary>
        /// ウィンドウが最大化されているか確認
        /// </summary>
        /// <param name="handle">ウィンドウハンドル</param>
        /// <returns>最大化されている場合はtrue</returns>
        bool IsMaximized(IntPtr handle);
    }
}