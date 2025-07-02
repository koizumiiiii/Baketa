using System;

namespace Baketa.Core.Abstractions.Common;

    /// <summary>
    /// ロガーアダプターインターフェース
    /// </summary>
    public interface ILoggerAdapter
    {
        /// <summary>
        /// デバッグレベルのログを出力します
        /// </summary>
        /// <param name="message">メッセージ</param>
        /// <param name="args">フォーマット引数</param>
        void Debug(string message, params object[] args);
        
        /// <summary>
        /// 情報レベルのログを出力します
        /// </summary>
        /// <param name="message">メッセージ</param>
        /// <param name="args">フォーマット引数</param>
        void Info(string message, params object[] args);
        
        /// <summary>
        /// 警告レベルのログを出力します
        /// </summary>
        /// <param name="message">メッセージ</param>
        /// <param name="args">フォーマット引数</param>
        void Warn(string message, params object[] args);
        
        /// <summary>
        /// エラーレベルのログを出力します
        /// </summary>
        /// <param name="message">メッセージ</param>
        /// <param name="args">フォーマット引数</param>
        void LogError(string message, params object[] args);
        
        /// <summary>
        /// エラーレベルのログを例外情報付きで出力します
        /// </summary>
        /// <param name="exception">例外</param>
        /// <param name="message">メッセージ</param>
        /// <param name="args">フォーマット引数</param>
        void LogError(Exception exception, string message, params object[] args);
        
        /// <summary>
        /// 致命的なエラーレベルのログを出力します
        /// </summary>
        /// <param name="message">メッセージ</param>
        /// <param name="args">フォーマット引数</param>
        void Fatal(string message, params object[] args);
        
        /// <summary>
        /// 致命的なエラーレベルのログを例外情報付きで出力します
        /// </summary>
        /// <param name="exception">例外</param>
        /// <param name="message">メッセージ</param>
        /// <param name="args">フォーマット引数</param>
        void Fatal(Exception exception, string message, params object[] args);
        
        /// <summary>
        /// 指定されたレベルのログが有効かどうかを取得します
        /// </summary>
        /// <param name="level">ログレベル</param>
        /// <returns>有効な場合はtrue</returns>
        bool IsEnabled(LoggingLevel level);
    }
    
    /// <summary>
    /// ログレベル
    /// </summary>
    public enum LoggingLevel
    {
        /// <summary>
        /// デバッグ
        /// </summary>
        Debug,
        
        /// <summary>
        /// 情報
        /// </summary>
        Info,
        
        /// <summary>
        /// 警告
        /// </summary>
        Warn,
        
        /// <summary>
        /// エラー
        /// </summary>
        Error,
        
        /// <summary>
        /// 致命的なエラー
        /// </summary>
        Fatal
    }
