using System;
using System.Collections.Generic;
using System.Drawing;
using Baketa.Core.Abstractions.Platform;
using Baketa.Core.Abstractions.Platform.Windows;

namespace Baketa.Infrastructure.Platform.Adapters
{
    /// <summary>
    /// Windows固有のウィンドウマネージャーをプラットフォーム非依存のウィンドウマネージャーに変換するアダプター
    /// </summary>
    public class WindowManagerAdapter : Baketa.Core.Abstractions.Platform.IWindowManager
    {
        private readonly Baketa.Core.Abstractions.Platform.Windows.IWindowManager _windowsManager;

        /// <summary>
        /// WindowManagerAdapterのコンストラクタ
        /// </summary>
        /// <param name="windowsManager">Windows固有のウィンドウマネージャー</param>
        public WindowManagerAdapter(Baketa.Core.Abstractions.Platform.Windows.IWindowManager windowsManager)
        {
            _windowsManager = windowsManager ?? throw new ArgumentNullException(nameof(windowsManager));
        }

        /// <inheritdoc />
        public IntPtr GetActiveWindowHandle()
        {
            return _windowsManager.GetActiveWindowHandle();
        }

        /// <inheritdoc />
        public IntPtr FindWindowByTitle(string title)
        {
            return _windowsManager.FindWindowByTitle(title);
        }

        /// <inheritdoc />
        public IntPtr FindWindowByClass(string className)
        {
            return _windowsManager.FindWindowByClass(className);
        }

        /// <inheritdoc />
        public Rectangle? GetWindowBounds(IntPtr handle)
        {
            return _windowsManager.GetWindowBounds(handle);
        }

        /// <inheritdoc />
        public Rectangle? GetClientBounds(IntPtr handle)
        {
            return _windowsManager.GetClientBounds(handle);
        }

        /// <inheritdoc />
        public string GetWindowTitle(IntPtr handle)
        {
            return _windowsManager.GetWindowTitle(handle);
        }

        /// <inheritdoc />
        public bool IsMinimized(IntPtr handle)
        {
            return _windowsManager.IsMinimized(handle);
        }

        /// <inheritdoc />
        public bool IsMaximized(IntPtr handle)
        {
            return _windowsManager.IsMaximized(handle);
        }

        /// <inheritdoc />
        public bool SetWindowBounds(IntPtr handle, Rectangle bounds)
        {
            return _windowsManager.SetWindowBounds(handle, bounds);
        }

        /// <inheritdoc />
        public bool BringWindowToFront(IntPtr handle)
        {
            return _windowsManager.BringWindowToFront(handle);
        }

        /// <inheritdoc />
        public Dictionary<IntPtr, string> GetRunningApplicationWindows()
        {
            return _windowsManager.GetRunningApplicationWindows();
        }
    }
}