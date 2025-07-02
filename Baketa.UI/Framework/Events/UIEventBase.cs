using System;

namespace Baketa.UI.Framework.Events;

    /// <summary>
    /// UI関連イベントの基底クラス
    /// </summary>
    internal abstract class UIEventBase : Baketa.Core.Events.EventBase, IEvent
    {
        /// <summary>
        /// イベントの一意な識別子 (Coreインターフェース互換用)
        /// </summary>
        public Guid EventId => Id;
    }
