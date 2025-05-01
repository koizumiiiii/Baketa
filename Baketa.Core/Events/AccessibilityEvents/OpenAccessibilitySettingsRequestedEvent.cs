using System;

namespace Baketa.Core.Events.AccessibilityEvents
{
    /// <summary>
    /// アクセシビリティ設定画面を開くリクエストイベント
    /// </summary>
    public class OpenAccessibilitySettingsRequestedEvent : EventBase
    {
        /// <summary>
        /// イベント名
        /// </summary>
        public override string Name => "OpenAccessibilitySettingsRequested";
        
        /// <summary>
        /// イベントカテゴリ
        /// </summary>
        public override string Category => "UI.Navigation";
    }
}