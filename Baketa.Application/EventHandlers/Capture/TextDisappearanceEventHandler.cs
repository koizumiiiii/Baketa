using System;
using System.IO;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events.Capture;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.EventHandlers.Capture;

    /// <summary>
    /// テキスト消失イベントのハンドラー
    /// </summary>
    public class TextDisappearanceEventHandler : IEventProcessor<IEvent>
    {
        private readonly ILogger<TextDisappearanceEventHandler>? _logger;
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="logger">ロガー</param>
        public TextDisappearanceEventHandler(ILogger<TextDisappearanceEventHandler>? logger = null)
        {
            _logger = logger;
        }
        
        /// <summary>
        /// イベントを処理します
        /// </summary>
        /// <param name="event">テキスト消失イベント</param>
        public async Task HandleAsync(IEvent @event)
        {
            ArgumentNullException.ThrowIfNull(@event, nameof(@event));
                
            // TextDisappearanceEvent型にキャスト
            if (@event is TextDisappearanceEvent textDisappearanceEvent)
            {
                try
                {
                    _logger?.LogDebug("テキスト消失イベントを処理: {Count}個の領域, ウィンドウ: {WindowHandle}",
                        textDisappearanceEvent.DisappearedRegions.Count, textDisappearanceEvent.SourceWindowHandle);
                    
                    // テキスト消失時の処理
                    // 例：翻訳ウィンドウの非表示処理
                    await HideTranslationWindowsAsync(textDisappearanceEvent).ConfigureAwait(false);
                    
                    _logger?.LogInformation("テキスト消失イベント処理完了");
                }
                catch (ArgumentException ex)
                {
                    _logger?.LogError(ex, "テキスト消失イベント処理中に引数エラーが発生しました: {Message}", ex.Message);
                }
                catch (InvalidOperationException ex)
                {
                    _logger?.LogError(ex, "テキスト消失イベント処理中に操作エラーが発生しました: {Message}", ex.Message);
                }
                catch (IOException ex)
                {
                    _logger?.LogError(ex, "テキスト消失イベント処理中にIO例外が発生しました: {Message}", ex.Message);
                }
                catch (Exception ex) when (ex is not ApplicationException)
                {
                    _logger?.LogError(ex, "テキスト消失イベント処理中にエラーが発生しました");
                }
            }
            else
            {
                _logger?.LogWarning("サポートされていないイベントタイプ: {EventType}", @event.GetType().Name);
            }
        }
        
        /// <summary>
        /// 消失したテキスト領域に対応する翻訳ウィンドウを非表示にします
        /// </summary>
        private async Task HideTranslationWindowsAsync(TextDisappearanceEvent @event)
        {
            // ここでは実際の実装は省略（UI層と連携する必要あり）
            // 実際の実装では、以下の処理を行う
            // 1. テキスト領域に対応する翻訳ウィンドウを特定
            // 2. ウィンドウの非表示処理を実行
            
            // 例：イベント発火で他のコンポーネントに通知
            // await _eventAggregator.PublishAsync(new TranslationHideEvent(...))
            
            await Task.Delay(1).ConfigureAwait(false); // 非同期メソッドの要件
        }

        /// <summary>
        /// このハンドラーの優先度
        /// </summary>
        public int Priority => 0;

        /// <summary>
        /// 処理を同期的に実行するかどうか
        /// </summary>
        public bool SynchronousExecution => false;
    }
