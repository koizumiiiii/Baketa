using System;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.ErrorHandling;

/// <summary>
/// シンプルなエラーハンドリングインターフェース
/// 複雑なエラー階層を排除し、実用的なエラー処理を提供
/// </summary>
public interface ISimpleErrorHandler
{
    /// <summary>
    /// エラーをログに記録し、必要に応じて復旧処理を実行
    /// </summary>
    /// <param name="errorInfo">エラー情報</param>
    /// <returns>復旧が成功した場合true</returns>
    Task<bool> HandleErrorAsync(SimpleError errorInfo);

    /// <summary>
    /// エラーの重要度を評価
    /// </summary>
    /// <param name="exception">例外オブジェクト</param>
    /// <returns>エラーレベル</returns>
    ErrorLevel EvaluateErrorLevel(Exception exception);

    /// <summary>
    /// システムの健全性チェック
    /// </summary>
    /// <returns>システムが正常な場合true</returns>
    Task<bool> CheckSystemHealthAsync();
}

/// <summary>
/// シンプルなエラー情報
/// </summary>
public sealed record SimpleError
{
    /// <summary>
    /// エラーが発生した操作
    /// </summary>
    public required string Operation { get; init; }

    /// <summary>
    /// エラーレベル
    /// </summary>
    public required ErrorLevel Level { get; init; }

    /// <summary>
    /// 例外オブジェクト
    /// </summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// エラーメッセージ
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// 発生日時
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 追加コンテキスト情報
    /// </summary>
    public string? Context { get; init; }

    /// <summary>
    /// 復旧を試行するかどうか
    /// </summary>
    public bool ShouldRetry { get; init; } = true;
}

/// <summary>
/// エラーレベル
/// </summary>
public enum ErrorLevel
{
    /// <summary>
    /// 情報（ログのみ）
    /// </summary>
    Information,

    /// <summary>
    /// 警告（動作は継続）
    /// </summary>
    Warning,

    /// <summary>
    /// エラー（部分的な機能停止）
    /// </summary>
    Error,

    /// <summary>
    /// 致命的エラー（全体停止）
    /// </summary>
    Critical
}