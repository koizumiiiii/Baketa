using Baketa.Core.Abstractions.Events;
using System;

namespace Baketa.Core.Events.EventTypes
{
    /// <summary>
    /// アプリケーション起動完了時に発行されるイベント
    /// </summary>
    public class ApplicationStartupEvent : IEvent
    {
        /// <summary>
        /// イベントID
        /// </summary>
        public Guid Id { get; } = Guid.NewGuid();
        
        /// <summary>
        /// イベント発生時刻
        /// </summary>
        public DateTime Timestamp { get; } = DateTime.Now;
        
        /// <summary>
        /// イベント名
        /// </summary>
        public string Name => "ApplicationStartup";
        
        /// <summary>
        /// イベントカテゴリ
        /// </summary>
        public string Category => "Application";
    }
}