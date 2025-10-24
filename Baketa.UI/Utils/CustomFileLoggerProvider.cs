using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Utils;

/// <summary>
/// ファイルにログを出力するカスタムロガープロバイダー
/// Channel<T>ベースのバックグラウンドキュー処理でデッドロック回避
/// Gemini推奨アプローチ: 非ブロッキングTryWrite + 非同期バックグラウンド処理
/// </summary>
public sealed class CustomFileLoggerProvider : ILoggerProvider
{
    private readonly string _logFilePath;
    private readonly ConcurrentDictionary<string, CustomFileLogger> _loggers = new();
    private readonly Channel<string> _logQueue;
    private readonly Task _processTask;
    private readonly CancellationTokenSource _cts;

    public CustomFileLoggerProvider(string logFilePath)
    {
        _logFilePath = logFilePath ?? throw new ArgumentNullException(nameof(logFilePath));

        // 🔥 [CHANNEL_FIX] Unbounded Channelでログキュー作成（メモリ許容範囲内で無制限）
        _logQueue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,  // 単一リーダー最適化
            SingleWriter = false  // 複数ライターに対応
        });

        _cts = new CancellationTokenSource();

        // 🔥 [CHANNEL_FIX] バックグラウンドタスク起動（アプリ起動時に1回のみ）
        _processTask = Task.Run(() => ProcessLogQueueAsync(_cts.Token));
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new CustomFileLogger(name, _logFilePath, _logQueue));
    }

    /// <summary>
    /// バックグラウンドログ処理タスク
    /// Channel<string>からログエントリを読み取り、非同期ファイル書き込み
    /// </summary>
    private async Task ProcessLogQueueAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var entry in _logQueue.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    // 🔥 [CHANNEL_FIX] 非同期ファイル書き込み（デッドロックリスク無し）
                    await SafeFileLogger.AppendLogAsync(_logFilePath, entry).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // 個別エントリの失敗を吸収（バックグラウンドTask全体を停止させない）
                    System.Console.WriteLine($"⚠️ ログ書き込み失敗: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常なキャンセル（Dispose時）
        }
        catch (Exception ex)
        {
            // 予期しないエラー
            System.Console.WriteLine($"💥 ProcessLogQueueAsync critical error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        try
        {
            // 🔥 [CHANNEL_FIX] 新規書き込み停止（既存キューアイテムは処理続行）
            _logQueue.Writer.Complete();

            // 🔥 [CHANNEL_FIX] 残ログ処理完了を最大5秒待機
            if (!_processTask.Wait(TimeSpan.FromSeconds(5)))
            {
                // タイムアウト: キャンセル実行
                _cts.Cancel();
                _processTask.Wait(TimeSpan.FromSeconds(1)); // 追加1秒待機
            }
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"⚠️ CustomFileLoggerProvider.Dispose error: {ex.Message}");
        }
        finally
        {
            _cts.Dispose();
            _loggers.Clear();
        }
    }
}

/// <summary>
/// ファイルにログを出力するカスタムロガー実装
/// Channel<T>ベースの非ブロッキングログ書き込み
/// </summary>
internal sealed class CustomFileLogger : ILogger
{
    private readonly string _categoryName;
    private readonly string _logFilePath;
    private readonly Channel<string> _logQueue;

    public CustomFileLogger(string categoryName, string logFilePath, Channel<string> logQueue)
    {
        _categoryName = categoryName;
        _logFilePath = logFilePath;
        _logQueue = logQueue;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        // Debug以上のログレベルを有効化
        return logLevel >= LogLevel.Debug;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        try
        {
            // スレッドIDを取得
            var threadId = Environment.CurrentManagedThreadId;

            // ログレベルの短縮表記
            var logLevelString = logLevel switch
            {
                LogLevel.Trace => "TRACE",
                LogLevel.Debug => "DEBUG",
                LogLevel.Information => "INFO",
                LogLevel.Warning => "WARN",
                LogLevel.Error => "ERROR",
                LogLevel.Critical => "CRIT",
                _ => logLevel.ToString()
            };

            // フォーマット: [HH:mm:ss.fff][TXX] [LEVEL] CategoryName: Message
            var logEntry = $"[{DateTime.Now:HH:mm:ss.fff}][T{threadId:D2}] [{logLevelString}] {_categoryName}: {message}";

            if (exception != null)
            {
                logEntry += Environment.NewLine + exception.ToString();
            }

            // 🔥 [CHANNEL_FIX] 非ブロッキング書き込み（デッドロックリスク無し）
            // TryWrite()は即座にリターン、バックグラウンドTaskで非同期処理
            if (!_logQueue.Writer.TryWrite(logEntry))
            {
                // キュー満杯時（Unboundedなので通常発生しない）
                System.Console.WriteLine($"⚠️ ログキュー満杯: {logEntry.Substring(0, Math.Min(100, logEntry.Length))}...");
            }
        }
        catch (Exception ex)
        {
            // TryWrite失敗時のフォールバック
            System.Console.WriteLine($"⚠️ CustomFileLogger.TryWrite失敗: {ex.Message}");
        }
    }
}
