using System;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.ErrorHandling;

/// <summary>
/// 統一されたエラーハンドリングシステムのインターフェース。
/// アプリケーション全体で一貫したエラー処理を提供します。
/// </summary>
public interface IBaketaExceptionHandler
{
    /// <summary>
    /// 同期例外を処理します。
    /// </summary>
    /// <param name="exception">処理する例外</param>
    /// <param name="context">エラー発生のコンテキスト情報</param>
    void HandleException(Exception exception, string context);

    /// <summary>
    /// 非同期例外を処理します。
    /// </summary>
    /// <param name="exception">処理する例外</param>
    /// <param name="context">エラー発生のコンテキスト情報</param>
    /// <returns>非同期処理タスク</returns>
    Task HandleExceptionAsync(Exception exception, string context);

    /// <summary>
    /// 重大なエラーを処理します（アプリケーション終了の可能性あり）。
    /// </summary>
    /// <param name="exception">重大な例外</param>
    /// <param name="context">エラー発生のコンテキスト情報</param>
    void HandleCriticalException(Exception exception, string context);

    /// <summary>
    /// エラーが回復可能かどうかを判定します。
    /// </summary>
    /// <param name="exception">判定する例外</param>
    /// <returns>回復可能な場合はtrue</returns>
    bool IsRecoverableException(Exception exception);

    /// <summary>
    /// エラー統計を取得します。
    /// </summary>
    /// <returns>エラー統計情報</returns>
    ErrorStatistics GetStatistics();
}
