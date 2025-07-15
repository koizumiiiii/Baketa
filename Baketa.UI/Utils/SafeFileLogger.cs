using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Baketa.UI.Utils;

/// <summary>
/// ファイル共有を考慮した安全なログ書き込み用ユーティリティ
/// 複数のプロセスが同時にアクセスしてもファイルロックエラーを回避
/// </summary>
public static class SafeFileLogger
{
    private static readonly SemaphoreSlim _semaphore = new(1, 1);
    private const int MaxRetryAttempts = 3;
    private const int RetryDelayMs = 10;
    
    /// <summary>
    /// ファイルに安全にログを追記（同期版）
    /// </summary>
    /// <param name="fileName">ファイル名</param>
    /// <param name="message">ログメッセージ</param>
    public static void AppendLog(string fileName, string message)
    {
        AppendLogAsync(fileName, message).GetAwaiter().GetResult();
    }
    
    /// <summary>
    /// ファイルに安全にログを追記（非同期版）
    /// </summary>
    /// <param name="fileName">ファイル名</param>
    /// <param name="message">ログメッセージ</param>
    public static async Task AppendLogAsync(string fileName, string message)
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            for (int attempt = 0; attempt < MaxRetryAttempts; attempt++)
            {
                try
                {
                    using var fileStream = new FileStream(fileName, FileMode.Append, FileAccess.Write, FileShare.Read, bufferSize: 4096, useAsync: true);
                    using var writer = new StreamWriter(fileStream);
                    await writer.WriteLineAsync(message).ConfigureAwait(false);
                    await writer.FlushAsync().ConfigureAwait(false);
                    return; // 成功
                }
                catch (IOException ex) when (ex.HResult == unchecked((int)0x80070020) && attempt < MaxRetryAttempts - 1)
                {
                    // ファイルが他のプロセスによって使用中の場合のリトライ
                    await Task.Delay(RetryDelayMs * (attempt + 1)).ConfigureAwait(false);
                }
                catch (UnauthorizedAccessException) when (attempt < MaxRetryAttempts - 1)
                {
                    await Task.Delay(RetryDelayMs * (attempt + 1)).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            // 最終的にログ書き込み失敗時はコンソールに出力
            Console.WriteLine($"⚠️ ログ書き込み失敗 ({fileName}): {ex.Message}");
        }
        finally
        {
            _semaphore.Release();
        }
    }
    
    /// <summary>
    /// タイムスタンプ付きでログを追記（同期版）
    /// </summary>
    /// <param name="fileName">ファイル名</param>
    /// <param name="message">ログメッセージ</param>
    public static void AppendLogWithTimestamp(string fileName, string message)
    {
        var timestampedMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}";
        AppendLog(fileName, timestampedMessage);
    }
    
    /// <summary>
    /// タイムスタンプ付きでログを追記（非同期版）
    /// </summary>
    /// <param name="fileName">ファイル名</param>
    /// <param name="message">ログメッセージ</param>
    public static async Task AppendLogWithTimestampAsync(string fileName, string message)
    {
        var timestampedMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}";
        await AppendLogAsync(fileName, timestampedMessage).ConfigureAwait(false);
    }
}