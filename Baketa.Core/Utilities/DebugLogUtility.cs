using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Baketa.Core.Utilities;

/// <summary>
/// デバッグログをファイルに書き込むユーティリティクラス
/// Console.WriteLineの代替としてファイルベースのログ機能を提供
///
/// 🔥 UltraThink修正: 複数ログファイル同時出力対応
/// - 従来のbaketa_debug.log（AppDomain.BaseDirectory）
/// - 明示的パス（bin\Debug\net8.0-windows10.0.19041.0\baketa_debug.log）
/// - appsettings.json設定パス対応
/// </summary>
public static class DebugLogUtility
{
    private static readonly string PrimaryLogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "baketa_debug.log");
    private static readonly string SecondaryLogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
    private static readonly object _lock = new();

    // 🔥 UltraThink修正: 複数ログファイル対応
    private static readonly List<string> LogFilePaths = new()
    {
        PrimaryLogFilePath,     // baketa_debug.log
        SecondaryLogFilePath    // debug_app_logs.txt (appsettings.json既定値)
    };

    static DebugLogUtility()
    {
        // ログファイルの初期化（アプリケーション起動時に新しいログファイルを作成）
        InitializeLogFiles();
    }

    /// <summary>
    /// 複数ログファイルを初期化
    /// 🔥 UltraThink修正: 複数ログファイル同時初期化対応
    /// </summary>
    private static void InitializeLogFiles()
    {
        try
        {
            lock (_lock)
            {
                var logHeader = $"=== Baketa Debug Log - {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n" +
                              $"Primary Log File: {PrimaryLogFilePath}\n" +
                              $"Secondary Log File: {SecondaryLogFilePath}\n" +
                              $"Process ID: {Environment.ProcessId}\n" +
                              $"==========================================\n";

                foreach (var logFilePath in LogFilePaths)
                {
                    try
                    {
                        // ディレクトリが存在しない場合は作成
                        var directory = Path.GetDirectoryName(logFilePath);
                        if (!string.IsNullOrEmpty(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        File.WriteAllText(logFilePath, logHeader);
                    }
                    catch (Exception ex)
                    {
                        // 個別ファイルの初期化失敗は続行（他のファイルは正常に作成される）
                        Console.WriteLine($"DebugLogUtility: ログファイル初期化失敗 ({logFilePath}): {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // 全体的な初期化失敗の場合は標準出力に出力
            Console.WriteLine($"DebugLogUtility: ログファイル初期化全体失敗: {ex.Message}");
        }
    }

    /// <summary>
    /// デバッグメッセージを複数ファイルに書き込み
    /// 🔥 UltraThink修正: 複数ログファイル同時書き込み対応
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
                foreach (var logFilePath in LogFilePaths)
                {
                    try
                    {
                        // 個別ファイルへの書き込み試行（一つ失敗しても他は続行）
                        File.AppendAllText(logFilePath, logEntry);
                    }
                    catch (Exception fileEx)
                    {
                        // 個別ファイル書き込み失敗は標準出力に報告（続行）
                        Console.WriteLine($"DebugLogUtility: 個別ファイル書き込み失敗 ({logFilePath}): {fileEx.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // 全体的な書き込み失敗の場合は標準出力に出力
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
    /// プライマリログファイルパスを取得
    /// 🔥 UltraThink修正: 複数ログファイル対応のためプライマリパス返却
    /// </summary>
    /// <returns>プライマリログファイルの絶対パス</returns>
    public static string GetLogFilePath()
    {
        return PrimaryLogFilePath;
    }

    /// <summary>
    /// すべてのログファイルパスを取得
    /// 🔥 UltraThink追加: 複数ログファイルパス一覧取得
    /// </summary>
    /// <returns>すべてのログファイルパスのリスト</returns>
    public static IReadOnlyList<string> GetAllLogFilePaths()
    {
        return LogFilePaths.AsReadOnly();
    }

    /// <summary>
    /// 複数ログファイルをクリア
    /// 🔥 UltraThink修正: 複数ログファイル同時クリア対応
    /// </summary>
    public static void ClearLog()
    {
        try
        {
            lock (_lock)
            {
                var clearHeader = $"=== Baketa Debug Log - {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n";

                foreach (var logFilePath in LogFilePaths)
                {
                    try
                    {
                        File.WriteAllText(logFilePath, clearHeader);
                    }
                    catch (Exception fileEx)
                    {
                        Console.WriteLine($"DebugLogUtility: 個別ファイルクリア失敗 ({logFilePath}): {fileEx.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DebugLogUtility: ログクリア失敗: {ex.Message}");
        }
    }
}
