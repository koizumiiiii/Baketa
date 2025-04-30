using System;

namespace Baketa.UI.Framework.Events
{
    /// <summary>
    /// イベント集約機構で使用される全てのイベントの基底インターフェース
    /// </summary>
    internal interface IEvent
    {
        /// <summary>
        /// イベントの一意な識別子を取得します
        /// </summary>
        Guid EventId { get; }
        
        /// <summary>
        /// イベントが発生した時刻を取得します
        /// </summary>
        DateTime Timestamp { get; }
    }
}