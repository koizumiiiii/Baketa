
using System;
using System.Drawing;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Baketa.Core.Events.EventTypes;
using Baketa.Core.Events.Handlers;
using Baketa.Core.Models.Translation;
using Baketa.Core.Abstractions.Events;
using System.Collections.Generic;

class Program 
{
    static async Task Main() 
    {
        Console.WriteLine("[TEST] Starting Priority Translation System Test...");
        
        try 
        {
            // 最小限のDIコンテナ設定
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
            
            // イベントアグリゲーターのモック
            services.AddSingleton<IEventAggregator, MockEventAggregator>();
            
            // PriorityAwareOcrCompletedHandler登録
            services.AddSingleton<PriorityAwareOcrCompletedHandler>();
            
            var provider = services.BuildServiceProvider();
            
            Console.WriteLine("[TEST] DI Container built successfully");
            
            // PriorityAwareOcrCompletedHandler取得
            var handler = provider.GetRequiredService<PriorityAwareOcrCompletedHandler>();
            
            Console.WriteLine($"[RESULT] Handler Type: {handler.GetType().Name}");
            Console.WriteLine($"[RESULT] Handler Priority: {handler.Priority}");
            
            // テスト用OCR完了イベント作成（画面中央のテキスト）
            var centerEvent = new OcrCompletedEvent 
            {
                CapturedImage = null, // テスト用なのでnull
                DetectedTextBlocks = new List<TextBlock> 
                {
                    new TextBlock 
                    {
                        Text = "Center Priority Text",
                        BoundingBox = new Rectangle(400, 300, 200, 50), // 画面中央付近
                        Confidence = 0.95f
                    }
                },
                ProcessingMetrics = new Dictionary<string, object>(),
                ScreenDimensions = new Size(800, 600)
            };
            
            Console.WriteLine("[TEST] Testing center priority text processing...");
            await handler.ProcessAsync(centerEvent);
            
            Console.WriteLine("[SUCCESS] Priority translation system test completed successfully");
            
        } 
        catch (Exception ex) 
        {
            Console.WriteLine($"[ERROR] Priority Translation Test Exception: {ex.GetType().Name}");
            Console.WriteLine($"[ERROR] Message: {ex.Message}");
            Console.WriteLine($"[ERROR] Inner: {ex.InnerException?.Message}");
            Console.WriteLine($"[ERROR] Stack: {ex.StackTrace}");
        }
        
        Console.WriteLine("[TEST] Priority translation system test completed");
    }
}

public class MockEventAggregator : IEventAggregator
{
    public void Publish<TEvent>(TEvent eventInstance) where TEvent : class
    {
        Console.WriteLine($"[MOCK] Event published: {typeof(TEvent).Name}");
    }
    
    public void Subscribe<TEvent>(Func<TEvent, Task> handler) where TEvent : class
    {
        Console.WriteLine($"[MOCK] Subscribed to: {typeof(TEvent).Name}");
    }
    
    public void Unsubscribe<TEvent>(Func<TEvent, Task> handler) where TEvent : class
    {
        Console.WriteLine($"[MOCK] Unsubscribed from: {typeof(TEvent).Name}");
    }
}
