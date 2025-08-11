using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Logging;

/// <summary>
/// Baketa統一ログインターフェース
/// Console、ファイル、ILoggerの重複出力を統一し、構造化ログをサポート
/// </summary>
public interface IBaketaLogger
{
    /// <summary>
    /// 翻訳関連イベントのログを出力
    /// </summary>
    /// <param name="eventType">イベント種別（OCR完了、翻訳開始等）</param>
    /// <param name="message">メッセージ</param>
    /// <param name="data">追加データ（構造化ログ用）</param>
    /// <param name="level">ログレベル</param>
    void LogTranslationEvent(string eventType, string message, object? data = null, BaketaLogLevel level = BaketaLogLevel.Information);

    /// <summary>
    /// パフォーマンス測定結果のログを出力
    /// </summary>
    /// <param name="operation">操作名</param>
    /// <param name="duration">実行時間</param>
    /// <param name="success">成功フラグ</param>
    /// <param name="additionalMetrics">追加メトリクス</param>
    void LogPerformanceMetrics(string operation, TimeSpan duration, bool success, Dictionary<string, object>? additionalMetrics = null);

    /// <summary>
    /// ユーザーアクションのログを出力
    /// </summary>
    /// <param name="action">アクション名</param>
    /// <param name="context">コンテキスト情報</param>
    /// <param name="level">ログレベル</param>
    void LogUserAction(string action, Dictionary<string, object>? context = null, BaketaLogLevel level = BaketaLogLevel.Information);

    /// <summary>
    /// デバッグ情報のログを出力
    /// </summary>
    /// <param name="component">コンポーネント名</param>
    /// <param name="message">デバッグメッセージ</param>
    /// <param name="data">追加データ</param>
    void LogDebug(string component, string message, object? data = null);

    /// <summary>
    /// エラー情報のログを出力
    /// </summary>
    /// <param name="component">コンポーネント名</param>
    /// <param name="message">エラーメッセージ</param>
    /// <param name="exception">例外オブジェクト</param>
    /// <param name="data">追加データ</param>
    void LogError(string component, string message, Exception? exception = null, object? data = null);

    /// <summary>
    /// 警告情報のログを出力
    /// </summary>
    /// <param name="component">コンポーネント名</param>
    /// <param name="message">警告メッセージ</param>
    /// <param name="data">追加データ</param>
    void LogWarning(string component, string message, object? data = null);

    /// <summary>
    /// 情報ログを出力
    /// </summary>
    /// <param name="component">コンポーネント名</param>
    /// <param name="message">情報メッセージ</param>
    /// <param name="data">追加データ</param>
    void LogInformation(string component, string message, object? data = null);

    /// <summary>
    /// 非同期でログをフラッシュ
    /// </summary>
    Task FlushAsync();

    /// <summary>
    /// デバッグモードの有効/無効を設定
    /// </summary>
    /// <param name="enabled">デバッグモード有効フラグ</param>
    void SetDebugMode(bool enabled);

    /// <summary>
    /// ログレベルを設定
    /// </summary>
    /// <param name="level">ログレベル</param>
    void SetLogLevel(BaketaLogLevel level);
}

/// <summary>
/// Baketaログレベル
/// </summary>
public enum BaketaLogLevel
{
    /// <summary>
    /// トレース（最も詳細）
    /// </summary>
    Trace = 0,

    /// <summary>
    /// デバッグ
    /// </summary>
    Debug = 1,

    /// <summary>
    /// 情報
    /// </summary>
    Information = 2,

    /// <summary>
    /// 警告
    /// </summary>
    Warning = 3,

    /// <summary>
    /// エラー
    /// </summary>
    Error = 4,

    /// <summary>
    /// 致命的エラー
    /// </summary>
    Critical = 5
}

/// <summary>
/// ログエントリの構造
/// </summary>
public sealed class BaketaLogEntry(
    BaketaLogLevel level,
    string component,
    string message,
    Dictionary<string, object>? data = null,
    Exception? exception = null)
{
    /// <summary>
    /// ログの一意ID
    /// </summary>
    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// タイムスタンプ
    /// </summary>
    public DateTime Timestamp { get; } = DateTime.Now;

    /// <summary>
    /// ログレベル
    /// </summary>
    public BaketaLogLevel Level { get; } = level;

    /// <summary>
    /// コンポーネント名
    /// </summary>
    public string Component { get; } = component;

    /// <summary>
    /// ログメッセージ
    /// </summary>
    public string Message { get; } = message;

    /// <summary>
    /// 追加データ
    /// </summary>
    public Dictionary<string, object> Data { get; } = data ?? new Dictionary<string, object>();

    /// <summary>
    /// 例外情報
    /// </summary>
    public Exception? Exception { get; } = exception;

    /// <summary>
    /// ログエントリを文字列形式でフォーマット
    /// </summary>
    /// <param name="includeData">データを含むかどうか</param>
    /// <returns>フォーマット済みログ文字列</returns>
    public string Format(bool includeData = true)
    {
        var levelIcon = Level switch
        {
            BaketaLogLevel.Trace => "🔍",
            BaketaLogLevel.Debug => "🐛",
            BaketaLogLevel.Information => "ℹ️",
            BaketaLogLevel.Warning => "⚠️",
            BaketaLogLevel.Error => "❌",
            BaketaLogLevel.Critical => "🆘",
            _ => "📝"
        };

        var baseMessage = $"{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {levelIcon} [{Component}] {Message}";

        if (!includeData || Data.Count == 0)
            return baseMessage;

        var dataString = string.Join(", ", Data.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        return $"{baseMessage} | Data: {dataString}";
    }
}
