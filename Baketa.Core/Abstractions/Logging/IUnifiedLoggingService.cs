using Microsoft.Extensions.Logging;

namespace Baketa.Core.Abstractions.Logging;

/// <summary>
/// 統一ログサービスのインターフェース
/// Console.WriteLine、File.AppendAllText、DebugLogUtilityを統一し、
/// ILoggerベースの標準ロギングに移行するためのサービス
/// </summary>
public interface IUnifiedLoggingService
{
    /// <summary>
    /// 情報レベルのログを出力します
    /// </summary>
    /// <param name="message">ログメッセージ</param>
    void LogInformation(string message);

    /// <summary>
    /// フォーマット付き情報レベルのログを出力します
    /// </summary>
    /// <param name="messageTemplate">メッセージテンプレート</param>
    /// <param name="args">フォーマット引数</param>
    void LogInformation(string messageTemplate, params object[] args);

    /// <summary>
    /// 警告レベルのログを出力します
    /// </summary>
    /// <param name="message">ログメッセージ</param>
    void LogWarning(string message);

    /// <summary>
    /// 警告レベルのログを出力します（例外付き）
    /// </summary>
    /// <param name="exception">例外情報</param>
    /// <param name="message">ログメッセージ</param>
    void LogWarning(Exception exception, string message);

    /// <summary>
    /// エラーレベルのログを出力します
    /// </summary>
    /// <param name="message">ログメッセージ</param>
    void LogError(string message);

    /// <summary>
    /// エラーレベルのログを出力します（例外付き）
    /// </summary>
    /// <param name="exception">例外情報</param>
    /// <param name="message">ログメッセージ</param>
    void LogError(Exception exception, string message);

    /// <summary>
    /// デバッグレベルのログを出力します
    /// </summary>
    /// <param name="message">ログメッセージ</param>
    void LogDebug(string message);

    /// <summary>
    /// デバッグレベルのログを出力します（フォーマット付き）
    /// </summary>
    /// <param name="messageTemplate">メッセージテンプレート</param>
    /// <param name="args">フォーマット引数</param>
    void LogDebug(string messageTemplate, params object[] args);

    /// <summary>
    /// デバッグファイル専用のログを出力します（旧DebugLogUtility互換）
    /// </summary>
    /// <param name="message">ログメッセージ</param>
    void WriteDebugLog(string message);

    /// <summary>
    /// コンソール専用出力（開発・デバッグ用）
    /// </summary>
    /// <param name="message">コンソール出力メッセージ</param>
    void WriteConsole(string message);
}