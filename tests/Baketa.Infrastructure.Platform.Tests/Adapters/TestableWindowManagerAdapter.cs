using System;
using System.Collections.Generic;
using System.Drawing;
using Baketa.Core.Abstractions.Platform;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Infrastructure.Platform.Adapters;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Platform.Tests.Adapters;

    /// <summary>
    /// テスト用のWindowManagerAdapter拡張クラス
    /// モックの代わりにこのテスト用サブクラスを使用します
    /// </summary>
    public class TestableWindowManagerAdapter : WindowManagerAdapter
    {
        private readonly Dictionary<IntPtr, WindowType> _windowTypes = [];
        
        /// <summary>
        /// テスト用のWindowManagerAdapterコンストラクタ
        /// </summary>
        /// <param name="windowsManager">Windows固有のウィンドウマネージャー</param>
        /// <param name="logger">ロガー</param>
        public TestableWindowManagerAdapter(
            Core.Abstractions.Platform.Windows.IWindowManager windowsManager,
            ILogger<WindowManagerAdapter>? logger = null)
            : base(windowsManager, logger)
        {
        }
        
        /// <summary>
        /// ウィンドウタイプをテスト用に設定
        /// </summary>
        /// <param name="handle">ウィンドウハンドル</param>
        /// <param name="type">設定するウィンドウタイプ</param>
        public void SetWindowType(IntPtr handle, WindowType type)
        {
            _windowTypes[handle] = type;
        }
        
        /// <summary>
        /// 内部的なウィンドウタイプの判定メソッドをオーバーライド
        /// </summary>
        /// <param name="handle">ウィンドウハンドル</param>
        /// <returns>ウィンドウタイプ</returns>
        protected internal override WindowType GetWindowTypeInternal(IntPtr handle)
        {
            // テスト用に設定された値があればそれを返す
            if (_windowTypes.TryGetValue(handle, out WindowType type))
            {
                return type;
            }
            
            // なければ基底クラスの実装を使用
            return base.GetWindowTypeInternal(handle);
        }
    }
