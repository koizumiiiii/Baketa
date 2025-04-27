using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace Baketa.UI.Framework.Debugging
{
    /// <summary>
    /// ReactiveUI用デバッグ例外ハンドラ
    /// </summary>
    internal sealed class ReactiveUiDebuggingExceptionHandler(ILogger? logger = null) : IObserver<Exception>
    {
        private readonly ILogger? _logger = logger;

        // LoggerMessage デリゲートを定義
        private static readonly Action<ILogger, string, Exception> _logReactiveUiException =
            LoggerMessage.Define<string>(
                LogLevel.Error,
                new EventId(1, "ReactiveUiException"),
                "[ReactiveUI例外] {ExceptionMessage}");

        /// <inheritdoc/>
        public void OnCompleted()
        {
            // 何もしない
        }

        /// <inheritdoc/>
        public void OnError(Exception error)
        {
            ArgumentNullException.ThrowIfNull(error);
            HandleException(error);
        }

        /// <inheritdoc/>
        public void OnNext(Exception value)
        {
            ArgumentNullException.ThrowIfNull(value);
            HandleException(value);
        }

        private void HandleException(Exception ex)
        {
            var message = $"[ReactiveUI例外] {ex.GetType().Name}: {ex.Message}";
            Debug.WriteLine(message);
            Debug.WriteLine(ex.StackTrace);
            
            if (_logger != null)
            {
                _logReactiveUiException(_logger, $"{ex.GetType().Name}: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// コマンド実行メッセージ（デバッグ用）
    /// </summary>
    internal sealed class ExecuteCommandMessage(string commandName)
    {
        /// <summary>
        /// コマンド名
        /// </summary>
        public string CommandName { get; } = commandName;
    }
}