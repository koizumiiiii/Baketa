using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Baketa.Core.Settings;
using Baketa.Core.Utilities;

namespace Baketa.Core.Performance;

/// <summary>
/// パフォーマンス分析用の統一ログシステム
/// 既存の分散したログファイルを統合し、整理された出力を提供
/// </summary>
public static class PerformanceLogger
{
    /// <summary>
    /// メインのパフォーマンス分析ログファイル
    /// </summary>
    public static readonly string MainLogPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "performance_analysis.log");

    /// <summary>
    /// デバッグ情報用のログファイル
    /// </summary>
    public static readonly string DebugLogPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "debug_detailed.log");

    /// <summary>
    /// 旧ログファイルパス（クリーンアップ用）
    /// </summary>
    private static readonly string[] ObsoleteLogPaths = GetObsoleteLogPaths();

    private static string[] GetObsoleteLogPaths()
    {
        var loggingSettings = LoggingSettings.CreateDevelopmentSettings();
        var debugLogPath = loggingSettings.GetFullDebugLogPath();

        return [
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "baketa_debug.log"),
            Path.Combine(Environment.CurrentDirectory, "debug_batch_ocr.txt"),
            debugLogPath,
            Path.Combine(Environment.CurrentDirectory, "bottleneck_analysis.txt")
        ];
    }

    private static readonly object LogLock = new();
    private static bool _initialized;

    /// <summary>
    /// ログシステムを初期化し、既存の分散ログをクリーンアップ
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;

        lock (LogLock)
        {
            if (_initialized) return;

            // 新しいログファイルを初期化
            InitializeLogFile(MainLogPath, "PERFORMANCE ANALYSIS");
            InitializeLogFile(DebugLogPath, "DEBUG DETAILED LOG");

            // 既存の分散ログファイルをクリーンアップ（オプション）
            CleanupObsoleteLogs();

            _initialized = true;

            LogPerformance("📊 Performance Logging System Initialized");
        }
    }

    /// <summary>
    /// パフォーマンス情報をメインログに記録
    /// </summary>
    public static void LogPerformance(string message)
    {
        var timestampedMessage = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";

        WriteToFile(MainLogPath, timestampedMessage);
        Console.WriteLine(timestampedMessage);

        // 従来のDebugLogUtility互換性も提供
        Console.WriteLine(message);
    }

    /// <summary>
    /// 詳細デバッグ情報をデバッグログに記録
    /// </summary>
    public static void LogDebug(string message)
    {
        var timestampedMessage = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        WriteToFile(DebugLogPath, timestampedMessage);
    }

    /// <summary>
    /// パフォーマンス測定結果のサマリーを出力
    /// </summary>
    public static void LogPerformanceSummary()
    {
        var summary = PerformanceMeasurement.GenerateSummary();
        var separator = new string('=', 80);

        var fullSummary = $"\n{separator}\n{summary}\n{separator}\n";

        WriteToFile(MainLogPath, fullSummary);
        Console.WriteLine(fullSummary);
    }

    /// <summary>
    /// 重要なボトルネック情報をハイライト
    /// </summary>
    public static void LogBottleneck(string operation, TimeSpan duration, string details = "")
    {
        var message = $"🚨 BOTTLENECK DETECTED: {operation} - {duration.TotalMilliseconds:F1}ms";
        if (!string.IsNullOrEmpty(details))
            message += $" | {details}";

        LogPerformance(message);

        // 1秒以上の処理は特別にハイライト
        if (duration.TotalSeconds >= 1.0)
        {
            var alertMessage = $"⚠️  SLOW OPERATION: {operation} took {duration.TotalSeconds:F2} seconds!";
            LogPerformance(alertMessage);
        }
    }

    /// <summary>
    /// エンジン初期化の詳細ログ
    /// </summary>
    public static void LogEngineInitialization(string engineName, TimeSpan duration, long memoryUsage)
    {
        var message = $"🔧 ENGINE INIT: {engineName} - {duration.TotalMilliseconds:F1}ms, Memory: {memoryUsage / 1024:N0}KB";
        LogPerformance(message);

        if (duration.TotalSeconds > 5.0)
        {
            LogBottleneck($"{engineName} Initialization", duration, $"Memory: {memoryUsage / 1024:N0}KB");
        }
    }

    /// <summary>
    /// プロセス開始時の環境情報をログ
    /// </summary>
    public static void LogSystemInfo()
    {
        var messages = new[]
        {
            $"🖥️  System: {Environment.OSVersion}",
            $"💾 Memory: {GC.GetTotalMemory(false) / 1024 / 1024:N0}MB",
            $"🏗️  Runtime: {Environment.Version}",
            $"📂 WorkDir: {Environment.CurrentDirectory}",
            $"📂 BaseDir: {AppDomain.CurrentDomain.BaseDirectory}"
        };

        foreach (var message in messages)
        {
            LogPerformance(message);
        }
    }

    private static void InitializeLogFile(string logPath, string header)
    {
        try
        {
            var initMessage = $"=== {header} - Started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n";
            File.WriteAllText(logPath, initMessage);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Failed to initialize log file {logPath}: {ex.Message}");
        }
    }

    private static void WriteToFile(string filePath, string message)
    {
        try
        {
            lock (LogLock)
            {
                File.AppendAllText(filePath, message + Environment.NewLine);
            }
        }
        catch
        {
            // ファイル書き込みエラーは無視（コンソール出力は継続）
        }
    }

    /// <summary>
    /// 古い分散ログファイルをクリーンアップ（オプション）
    /// </summary>
    private static void CleanupObsoleteLogs()
    {
        foreach (var obsoletePath in ObsoleteLogPaths)
        {
            try
            {
                if (File.Exists(obsoletePath))
                {
                    // バックアップとして .old 拡張子で保存
                    var backupPath = obsoletePath + ".old";
                    if (File.Exists(backupPath))
                        File.Delete(backupPath);

                    File.Move(obsoletePath, backupPath);
                    LogDebug($"📁 Moved obsolete log: {Path.GetFileName(obsoletePath)} → {Path.GetFileName(backupPath)}");
                }
            }
            catch (Exception ex)
            {
                LogDebug($"⚠️ Failed to cleanup {obsoletePath}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// アプリケーション終了時の最終サマリー出力
    /// </summary>
    public static void FinalizeSession()
    {
        LogPerformanceSummary();
        LogPerformance($"📊 Performance Analysis Session Ended at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
    }
}
