using System;
using System.IO;
using System.Threading;

namespace Baketa.Core.Utilities;

/// <summary>
/// デバッグログをファイルに書き込むユーティリティクラス
/// Console.WriteLineの代替としてファイルベースのログ機能を提供
/// </summary>
public static class DebugLogUtility
{
    private static readonly string LogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "baketa_debug.log");
    private static readonly object _lock = new();

    static DebugLogUtility()
    {
        // ログファイルの初期化（アプリケーション起動時に新しいログファイルを作成）
        InitializeLogFile();
    }

    /// <summary>
    /// ログファイルを初期化
    /// </summary>
    private static void InitializeLogFile()
    {
        try
        {
            lock (_lock)
            {
                var logHeader = $"=== Baketa Debug Log - {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n" +
                              $"Log File: {LogFilePath}\n" +
                              $"Process ID: {Environment.ProcessId}\n" +
                              $"==========================================\n";
                File.WriteAllText(LogFilePath, logHeader);
            }
        }
        catch (Exception ex)
        {
            // ログファイルの作成に失敗した場合は標準出力に出力
            Console.WriteLine($"DebugLogUtility: ログファイル初期化失敗: {ex.Message}");
        }
    }

    /// <summary>
    /// デバッグメッセージをファイルに書き込み
    /// </summary>
    /// <param name="message">ログメッセージ</param>
    public static void WriteLog(string message)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
            var threadId = Environment.CurrentManagedThreadId;
            var logEntry = $"[{timestamp}][T{threadId:D2}] {message}\n";

            lock (_lock)
            {
                File.AppendAllText(LogFilePath, logEntry);
            }
        }
        catch (Exception ex)
        {
            // ログ書き込みに失敗した場合は標準出力に出力
            Console.WriteLine($"DebugLogUtility: ログ書き込み失敗: {ex.Message}");
            Console.WriteLine($"DebugLogUtility: 元のメッセージ: {message}");
        }
    }

    /// <summary>
    /// フォーマット付きデバッグメッセージをファイルに書き込み
    /// </summary>
    /// <param name="format">フォーマット文字列</param>
    /// <param name="args">フォーマット引数</param>
    public static void WriteLog(string format, params object[] args)
    {
        try
        {
            var message = string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args);
            WriteLog(message);
        }
        catch (Exception ex)
        {
            // フォーマット失敗時は元のフォーマット文字列をログに出力
            Console.WriteLine($"DebugLogUtility: フォーマット失敗: {ex.Message}");
            WriteLog($"FORMAT_ERROR: {format}");
        }
    }

    /// <summary>
    /// 現在のログファイルパスを取得
    /// </summary>
    /// <returns>ログファイルの絶対パス</returns>
    public static string GetLogFilePath()
    {
        return LogFilePath;
    }

    /// <summary>
    /// ログファイルをクリア
    /// </summary>
    public static void ClearLog()
    {
        try
        {
            lock (_lock)
            {
                File.WriteAllText(LogFilePath, $"=== Baketa Debug Log - {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DebugLogUtility: ログクリア失敗: {ex.Message}");
        }
    }
}
