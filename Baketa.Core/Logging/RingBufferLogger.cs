using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace Baketa.Core.Logging;

/// <summary>
/// [Issue #252] メモリ上に最新N行のログを保持するリングバッファロガー
/// クラッシュ時にログをダンプしてレポートに含める
/// </summary>
public sealed class RingBufferLogger : IDisposable
{
    private readonly ConcurrentQueue<LogEntry> _buffer = new();
    private readonly int _maxEntries;
    private readonly int _maxTotalSizeBytes;
    private readonly object _lock = new();
    private int _currentSizeBytes;
    private bool _disposed;

    /// <summary>
    /// シングルトンインスタンス
    /// </summary>
    public static RingBufferLogger Instance { get; } = new(500, 1024 * 1024); // 500行、1MB制限

    /// <summary>
    /// リングバッファロガーを初期化
    /// </summary>
    /// <param name="maxEntries">保持する最大エントリ数</param>
    /// <param name="maxTotalSizeBytes">最大合計サイズ（バイト）</param>
    public RingBufferLogger(int maxEntries = 500, int maxTotalSizeBytes = 1024 * 1024)
    {
        _maxEntries = maxEntries;
        _maxTotalSizeBytes = maxTotalSizeBytes;
    }

    /// <summary>
    /// ログエントリを追加
    /// </summary>
    public void Log(string level, string message, string? source = null, Exception? exception = null)
    {
        if (_disposed) return;

        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Message = message,
            Source = source,
            ExceptionType = exception?.GetType().Name,
            ExceptionMessage = exception?.Message
        };

        var entrySize = EstimateSize(entry);

        lock (_lock)
        {
            // サイズ制限チェック - 古いエントリを削除
            while (_currentSizeBytes + entrySize > _maxTotalSizeBytes && _buffer.TryDequeue(out var old))
            {
                _currentSizeBytes -= EstimateSize(old);
            }

            // エントリ数制限チェック
            while (_buffer.Count >= _maxEntries && _buffer.TryDequeue(out var old))
            {
                _currentSizeBytes -= EstimateSize(old);
            }

            _buffer.Enqueue(entry);
            _currentSizeBytes += entrySize;
        }
    }

    /// <summary>
    /// 情報レベルのログを追加
    /// </summary>
    public void LogInfo(string message, string? source = null)
        => Log("INFO", message, source);

    /// <summary>
    /// 警告レベルのログを追加
    /// </summary>
    public void LogWarning(string message, string? source = null, Exception? exception = null)
        => Log("WARN", message, source, exception);

    /// <summary>
    /// エラーレベルのログを追加
    /// </summary>
    public void LogError(string message, string? source = null, Exception? exception = null)
        => Log("ERROR", message, source, exception);

    /// <summary>
    /// デバッグレベルのログを追加
    /// </summary>
    public void LogDebug(string message, string? source = null)
        => Log("DEBUG", message, source);

    /// <summary>
    /// バッファ内のすべてのログを取得
    /// </summary>
    public IReadOnlyList<LogEntry> GetAllEntries()
    {
        return [.. _buffer];
    }

    /// <summary>
    /// バッファ内のログを文字列としてダンプ
    /// </summary>
    public string Dump()
    {
        var entries = GetAllEntries();
        var sb = new StringBuilder();

        foreach (var entry in entries)
        {
            sb.AppendLine(entry.ToString());
        }

        return sb.ToString();
    }

    /// <summary>
    /// バッファ内のログをファイルに保存
    /// </summary>
    public async Task DumpToFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var content = Dump();
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        await File.WriteAllTextAsync(filePath, content, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// バッファをクリア
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            while (_buffer.TryDequeue(out _)) { }
            _currentSizeBytes = 0;
        }
    }

    /// <summary>
    /// 現在のエントリ数
    /// </summary>
    public int Count => _buffer.Count;

    /// <summary>
    /// 現在の合計サイズ（バイト）
    /// </summary>
    public int CurrentSizeBytes => _currentSizeBytes;

    private static int EstimateSize(LogEntry entry)
    {
        // 概算サイズ（UTF-16文字列として計算）
        var size = 50; // 基本オーバーヘッド
        size += (entry.Message?.Length ?? 0) * 2;
        size += (entry.Source?.Length ?? 0) * 2;
        size += (entry.ExceptionType?.Length ?? 0) * 2;
        size += (entry.ExceptionMessage?.Length ?? 0) * 2;
        return size;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Clear();
    }
}

/// <summary>
/// ログエントリ
/// </summary>
public sealed class LogEntry
{
    public DateTime Timestamp { get; init; }
    public string Level { get; init; } = "INFO";
    public string? Message { get; init; }
    public string? Source { get; init; }
    public string? ExceptionType { get; init; }
    public string? ExceptionMessage { get; init; }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append($"[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}][{Level}]");

        if (!string.IsNullOrEmpty(Source))
        {
            sb.Append($"[{Source}]");
        }

        sb.Append($" {Message}");

        if (!string.IsNullOrEmpty(ExceptionType))
        {
            sb.Append($" | Exception: {ExceptionType}: {ExceptionMessage}");
        }

        return sb.ToString();
    }
}
