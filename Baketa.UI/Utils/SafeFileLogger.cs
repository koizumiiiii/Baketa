using System;
using System.IO;

namespace Baketa.UI.Utils;

/// <summary>
/// ファイル共有を考慮した安全なログ書き込み用ユーティリティ
/// 複数のプロセスが同時にアクセスしてもファイルロックエラーを回避
/// </summary>
public static class SafeFileLogger
{
    private static readonly object _lockObject = new();
    
    /// <summary>
    /// ファイルに安全にログを追記
    /// </summary>
    /// <param name="fileName">ファイル名</param>
    /// <param name="message">ログメッセージ</param>
    public static void AppendLog(string fileName, string message)
    {
        try
        {
            lock (_lockObject)
            {
                using var fileStream = new FileStream(fileName, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(fileStream);
                writer.WriteLine(message);
            }
        }
        catch (Exception ex)
        {
            // ログ書き込み失敗時はコンソールに出力（ファイルアクセス競合回避）
            Console.WriteLine($"⚠️ ログ書き込み失敗: {ex.Message}");
        }
    }
    
    /// <summary>
    /// タイムスタンプ付きでログを追記
    /// </summary>
    /// <param name="fileName">ファイル名</param>
    /// <param name="message">ログメッセージ</param>
    public static void AppendLogWithTimestamp(string fileName, string message)
    {
        var timestampedMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}";
        AppendLog(fileName, timestampedMessage);
    }
}