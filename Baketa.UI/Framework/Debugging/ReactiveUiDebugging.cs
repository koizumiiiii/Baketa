using System;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace Baketa.UI.Framework.Debugging;

    /// <summary>
    /// ReactiveUIデバッグツール統合
    /// </summary>
    internal static class ReactiveUiDebugging
    {
        /// <summary>
        /// デバッグモードを有効化するかどうか
        /// </summary>
        private static bool _debugModeEnabled;
        
        /// <summary>
        /// デバッグモードを有効化します
        /// </summary>
        /// <param name="logger">ロガー</param>
        // LoggerMessage デリゲートを定義
        private static readonly Action<ILogger, Exception?> _logEnableDebugMode =
            LoggerMessage.Define(
                LogLevel.Information,
                new EventId(1, "EnableDebugMode"),
                "ReactiveUIデバッグモードを有効化します");
                
        private static readonly Action<ILogger, Exception?> _logDebugModeEnabled =
            LoggerMessage.Define(
                LogLevel.Information,
                new EventId(2, "DebugModeEnabled"),
                "ReactiveUIデバッグモードが有効化されました");
                
        private static readonly Action<ILogger, string, Exception?> _logCommandExecuted =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(3, "CommandExecuted"),
                "[Command] {CommandName} executed");
                
        private static readonly Action<ILogger, Exception> _logDebugModeEnableFailed =
            LoggerMessage.Define(
                LogLevel.Warning,
                new EventId(4, "DebugModeEnableFailed"),
                "ReactiveUIデバッグモードの有効化に失敗しました");
        
        public static void EnableReactiveUiDebugMode(ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(logger);
            if (_debugModeEnabled)
                return;
                
            _debugModeEnabled = true;
            
            _logEnableDebugMode(logger, null);
            
            try
            {
                // プロパティ変更の詳細ロギング - ReactiveUI 20+ではRxApp.Defaultが削除されたため修正
#if DEBUG
                // デバッグビルドの場合のみ詳細ログを有効化
                // ReactiveUIオブジェクトのPropertyChangedイベントを個別に監視する方法に変更
                
                // コマンド実行ロギング
                MessageBus.Current.Listen<ExecuteCommandMessage>()
                    .Subscribe(msg => 
                    {
                        var message = $"[Command] {msg.CommandName} executed";
                        Debug.WriteLine(message);
                        _logCommandExecuted(logger, msg.CommandName, null);
                    });
                    
                _logDebugModeEnabled(logger, null);
#endif
            }
            catch (ObjectDisposedException ex)
            {
                _logDebugModeEnableFailed(logger, ex);
                _debugModeEnabled = false;
            }
            catch (InvalidOperationException ex)
            {
                _logDebugModeEnableFailed(logger, ex);
                _debugModeEnabled = false;
            }
            catch (Exception ex) when (ex.GetType().Name == "RoutingStateException")
            {
                _logDebugModeEnableFailed(logger, ex);
                _debugModeEnabled = false;
            }
        }
    }
