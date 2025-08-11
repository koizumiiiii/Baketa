using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Baketa.Core.Utilities;

namespace Baketa.Core.Logging;

/// <summary>
/// Baketa統一ログシステムの中央管理クラス
/// 構造化ログエントリをJSON形式で非同期に記録し、ファイルローテーションを提供
/// </summary>
public static class BaketaLogManager
{
    private static readonly string LogsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
    private static readonly object InitializationLock = new();
    private static volatile bool _initialized = false;
    
    // ログファイルのパス定数
    private static readonly string OcrResultsLogPath = Path.Combine(LogsDirectory, "ocr_results.log");
    private static readonly string TranslationResultsLogPath = Path.Combine(LogsDirectory, "translation_results.log");
    private static readonly string PerformanceAnalysisLogPath = Path.Combine(LogsDirectory, "performance_analysis.log");
    private static readonly string SystemDebugLogPath = Path.Combine(LogsDirectory, "system_debug.log");
    
    // 非同期書き込み用チャンネル
    private static readonly Channel<LogWriteOperation> _logChannel = Channel.CreateUnbounded<LogWriteOperation>();
    private static readonly ChannelWriter<LogWriteOperation> _logWriter = _logChannel.Writer;
    private static readonly ChannelReader<LogWriteOperation> _logReader = _logChannel.Reader;
    
    // バックグラウンドタスク
    private static Task? _backgroundTask;
    private static readonly CancellationTokenSource _cancellationTokenSource = new();
    
    // JSON シリアライズ設定
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };
    
    // ファイルサイズ制限（10MB）
    private const long MaxLogFileSize = 10 * 1024 * 1024;
    
    // ファイルロック
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileLocks = new();
    
    /// <summary>
    /// ログシステムを初期化
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;
        
        lock (InitializationLock)
        {
            if (_initialized) return;
            
            try
            {
                // ログディレクトリの作成
                Directory.CreateDirectory(LogsDirectory);
                
                // バックグラウンドタスクの開始
                _backgroundTask = Task.Run(ProcessLogEntriesAsync, _cancellationTokenSource.Token);
                
                _initialized = true;
                
                // 初期化完了ログ
                LogSystemDebug("📊 BaketaLogManager initialized successfully");
            }
            catch (Exception ex)
            {
                // 初期化失敗時は既存のDebugLogUtilityにフォールバック
                DebugLogUtility.WriteLog($"❌ BaketaLogManager initialization failed: {ex.Message}");
                throw;
            }
        }
    }
    
    /// <summary>
    /// OCR結果をログに記録
    /// </summary>
    /// <param name="entry">OCR結果ログエントリ</param>
    public static void LogOcrResult(OcrResultLogEntry entry)
    {
        if (!_initialized) Initialize();
        
        var operation = new LogWriteOperation
        {
            FilePath = OcrResultsLogPath,
            Content = JsonSerializer.Serialize(entry, JsonOptions),
            Timestamp = entry.Timestamp
        };
        
        if (!_logWriter.TryWrite(operation))
        {
            // チャンネル書き込み失敗時は同期でファイルに書き込み
            WriteToFileSynchronously(operation);
        }
    }
    
    /// <summary>
    /// 翻訳結果をログに記録
    /// </summary>
    /// <param name="entry">翻訳結果ログエントリ</param>
    public static void LogTranslationResult(TranslationResultLogEntry entry)
    {
        if (!_initialized) Initialize();
        
        var operation = new LogWriteOperation
        {
            FilePath = TranslationResultsLogPath,
            Content = JsonSerializer.Serialize(entry, JsonOptions),
            Timestamp = entry.Timestamp
        };
        
        if (!_logWriter.TryWrite(operation))
        {
            WriteToFileSynchronously(operation);
        }
    }
    
    /// <summary>
    /// パフォーマンス分析結果をログに記録
    /// </summary>
    /// <param name="entry">パフォーマンスログエントリ</param>
    public static void LogPerformance(PerformanceLogEntry entry)
    {
        if (!_initialized) Initialize();
        
        var operation = new LogWriteOperation
        {
            FilePath = PerformanceAnalysisLogPath,
            Content = JsonSerializer.Serialize(entry, JsonOptions),
            Timestamp = entry.Timestamp
        };
        
        if (!_logWriter.TryWrite(operation))
        {
            WriteToFileSynchronously(operation);
        }
    }
    
    /// <summary>
    /// システムデバッグメッセージをログに記録
    /// </summary>
    /// <param name="message">デバッグメッセージ</param>
    public static void LogSystemDebug(string message)
    {
        if (!_initialized) Initialize();
        
        var logEntry = new
        {
            Timestamp = DateTime.Now,
            Level = "Debug",
            Message = message,
            ThreadId = Environment.CurrentManagedThreadId,
            Environment.ProcessId
        };
        
        var operation = new LogWriteOperation
        {
            FilePath = SystemDebugLogPath,
            Content = JsonSerializer.Serialize(logEntry, JsonOptions),
            Timestamp = logEntry.Timestamp
        };
        
        if (!_logWriter.TryWrite(operation))
        {
            WriteToFileSynchronously(operation);
        }
        
        // 既存のDebugLogUtilityとの統合（段階的移行のため）
        DebugLogUtility.WriteLog(message);
    }
    
    /// <summary>
    /// エラー情報をログに記録
    /// </summary>
    /// <param name="ex">例外オブジェクト</param>
    /// <param name="context">エラーコンテキスト</param>
    public static void LogError(Exception ex, string context)
    {
        if (!_initialized) Initialize();
        
        var errorLogEntry = new
        {
            Timestamp = DateTime.Now,
            Level = "Error",
            Context = context,
            ExceptionType = ex.GetType().FullName,
            ex.Message,
            ex.StackTrace,
            InnerException = ex.InnerException?.Message,
            ThreadId = Environment.CurrentManagedThreadId,
            Environment.ProcessId
        };
        
        var operation = new LogWriteOperation
        {
            FilePath = SystemDebugLogPath,
            Content = JsonSerializer.Serialize(errorLogEntry, JsonOptions),
            Timestamp = errorLogEntry.Timestamp
        };
        
        if (!_logWriter.TryWrite(operation))
        {
            WriteToFileSynchronously(operation);
        }
        
        // 既存のDebugLogUtilityとの統合
        DebugLogUtility.WriteLog($"❌ ERROR in {context}: {ex.Message}");
    }
    
    /// <summary>
    /// ログシステムのシャットダウン
    /// </summary>
    public static async Task ShutdownAsync()
    {
        if (!_initialized) return;
        
        try
        {
            _logWriter.Complete();
            _cancellationTokenSource.Cancel();
            
            if (_backgroundTask != null)
            {
                await _backgroundTask.ConfigureAwait(false);
            }
            
            LogSystemDebug("📊 BaketaLogManager shutdown completed");
        }
        catch (Exception ex)
        {
            DebugLogUtility.WriteLog($"❌ BaketaLogManager shutdown error: {ex.Message}");
        }
        finally
        {
            _cancellationTokenSource.Dispose();
        }
    }
    
    /// <summary>
    /// バックグラウンドでログエントリを処理
    /// </summary>
    private static async Task ProcessLogEntriesAsync()
    {
        try
        {
            await foreach (var operation in _logReader.ReadAllAsync(_cancellationTokenSource.Token).ConfigureAwait(false))
            {
                await WriteToFileAsync(operation).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常なシャットダウン
        }
        catch (Exception ex)
        {
            DebugLogUtility.WriteLog($"❌ BaketaLogManager background task error: {ex.Message}");
        }
    }
    
    /// <summary>
    /// ファイルに非同期でログを書き込み
    /// </summary>
    private static async Task WriteToFileAsync(LogWriteOperation operation)
    {
        var semaphore = FileLocks.GetOrAdd(operation.FilePath, _ => new SemaphoreSlim(1, 1));
        
        try
        {
            await semaphore.WaitAsync(_cancellationTokenSource.Token).ConfigureAwait(false);
            
            // ファイルローテーションチェック
            await RotateLogFileIfNeededAsync(operation.FilePath).ConfigureAwait(false);
            
            // ログエントリを書き込み
            var logLine = operation.Content + Environment.NewLine;
            await File.AppendAllTextAsync(operation.FilePath, logLine, _cancellationTokenSource.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            DebugLogUtility.WriteLog($"❌ Failed to write log to {operation.FilePath}: {ex.Message}");
        }
        finally
        {
            semaphore.Release();
        }
    }
    
    /// <summary>
    /// 同期的にファイルにログを書き込み（フォールバック用）
    /// </summary>
    private static void WriteToFileSynchronously(LogWriteOperation operation)
    {
        try
        {
            var semaphore = FileLocks.GetOrAdd(operation.FilePath, _ => new SemaphoreSlim(1, 1));
            semaphore.Wait();
            
            try
            {
                // ファイルローテーションチェック
                RotateLogFileIfNeeded(operation.FilePath);
                
                var logLine = operation.Content + Environment.NewLine;
                File.AppendAllText(operation.FilePath, logLine);
            }
            finally
            {
                semaphore.Release();
            }
        }
        catch (Exception ex)
        {
            DebugLogUtility.WriteLog($"❌ Failed to write log synchronously to {operation.FilePath}: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 必要に応じてログファイルをローテーション（非同期版）
    /// </summary>
    private static async Task RotateLogFileIfNeededAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return;
            
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length < MaxLogFileSize) return;
            
            var rotatedPath = $"{filePath}.{DateTime.Now:yyyyMMdd_HHmmss}.old";
            File.Move(filePath, rotatedPath);
            
            await File.WriteAllTextAsync(filePath, 
                $"=== Log rotated at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}",
                _cancellationTokenSource.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            DebugLogUtility.WriteLog($"❌ Log rotation failed for {filePath}: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 必要に応じてログファイルをローテーション（同期版）
    /// </summary>
    private static void RotateLogFileIfNeeded(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return;
            
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length < MaxLogFileSize) return;
            
            var rotatedPath = $"{filePath}.{DateTime.Now:yyyyMMdd_HHmmss}.old";
            File.Move(filePath, rotatedPath);
            
            File.WriteAllText(filePath, $"=== Log rotated at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
        }
        catch (Exception ex)
        {
            DebugLogUtility.WriteLog($"❌ Log rotation failed for {filePath}: {ex.Message}");
        }
    }
}

/// <summary>
/// ログ書き込み操作を表すレコード
/// </summary>
internal record LogWriteOperation
{
    public required string FilePath { get; init; }
    public required string Content { get; init; }
    public DateTime Timestamp { get; init; }
}
