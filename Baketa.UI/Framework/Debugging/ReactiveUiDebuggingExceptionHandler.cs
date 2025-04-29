using System;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Framework.Debugging
{
    /// <summary>
    /// ReactiveUIのデバッグ用例外ハンドラー
    /// </summary>
    internal sealed class ReactiveUiDebuggingExceptionHandler(ILogger<ReactiveUiDebuggingExceptionHandler>? logger = null) : IObserver<Exception>
    {
        private readonly ILogger<ReactiveUiDebuggingExceptionHandler>? _logger = logger;
        
        /// <summary>
        /// シーケンスの完了通知
        /// </summary>
        public void OnCompleted()
        {
            _logger?.LogInformation("ReactiveUI例外シーケンスが完了しました");
        }

        /// <summary>
        /// エラー通知
        /// </summary>
        /// <param name="error">エラー</param>
        public void OnError(Exception error)
        {
            _logger?.LogError(error, "ReactiveUI例外ハンドラーでエラーが発生しました");
        }

        /// <summary>
        /// 例外通知
        /// </summary>
        /// <param name="value">例外</param>
        public void OnNext(Exception value)
        {
            // このハンドラーはReactiveUIフレームワーク内で発生した例外をログに記録する
            _logger?.LogError(value, "ReactiveUIで未処理の例外が発生しました: {ExceptionType}", value.GetType().Name);
            
            #if DEBUG
            // デバッグビルドではスタックトレースをより詳細にログ出力
            if (value.StackTrace != null)
            {
                _logger?.LogDebug("スタックトレース: {StackTrace}", value.StackTrace);
            }
            #endif
        }
    }
}