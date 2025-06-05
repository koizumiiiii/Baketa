using Baketa.Core.Events.EventTypes;
using System;
using System.Threading.Tasks;

namespace Baketa.Core.Events.Handlers;

    /// <summary>
    /// 通知イベントハンドラー
    /// </summary>
    /// <remarks>
    /// 実際のUIフレームワークに依存する実装は、UI層で行われます。
    /// このハンドラーはサンプル/デモ用のロギング処理を実装しています。
    /// </remarks>
    public class NotificationHandler : IEventProcessor<NotificationEvent>
    {
        /// <inheritdoc />
        public Task HandleAsync(NotificationEvent eventData)
        {
            // NULLチェック
            ArgumentNullException.ThrowIfNull(eventData);

            // 実際の実装では、このハンドラーはUI層に配置され、
            // 通知メッセージをトースト通知やダイアログとして表示します。
            
            // このサンプル実装ではコンソール出力のみを行います
            Console.WriteLine($"[{eventData.Type}] {eventData.Title}: {eventData.Message}");
            
            if (eventData.RelatedError != null)
            {
                Console.WriteLine($"Error details: {eventData.RelatedError.Message}");
                Console.WriteLine($"Stack trace: {eventData.RelatedError.StackTrace}");
            }
            
            return Task.CompletedTask;
        }
    }
