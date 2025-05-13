using System;
using System.Collections.Generic;
using System.Drawing;
using Baketa.Core.Abstractions.Events;

namespace Baketa.Core.Events.Capture
{
    /// <summary>
    /// テキスト消失イベント
    /// </summary>
    public class TextDisappearanceEvent : IEvent
    {
        // IEvent のプロパティを実装
        /// <summary>
        /// イベントID
        /// </summary>
        public Guid Id { get; } = Guid.NewGuid();
        
        /// <summary>
        /// イベント名
        /// </summary>
        public string Name => "TextDisappearance";
        
        /// <summary>
        /// イベントカテゴリ
        /// </summary>
        public string Category => "Capture";
        
        /// <summary>
        /// イベント発生時刻
        /// </summary>
        public DateTime Timestamp { get; }
        
        // クラス固有のプロパティ
        /// <summary>
        /// 消失したテキスト領域
        /// </summary>
        public IReadOnlyList<Rectangle> DisappearedRegions { get; }
        
        /// <summary>
        /// ソースウィンドウハンドル
        /// </summary>
        public IntPtr SourceWindowHandle { get; }
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="regions">消失したテキスト領域</param>
        /// <param name="sourceWindow">ソースウィンドウハンドル</param>
        public TextDisappearanceEvent(IReadOnlyList<Rectangle> regions, IntPtr sourceWindow = default)
        {
            ArgumentNullException.ThrowIfNull(regions, nameof(regions));
                
            DisappearedRegions = regions;
            SourceWindowHandle = sourceWindow;
            Timestamp = DateTime.Now;
        }
    }
}