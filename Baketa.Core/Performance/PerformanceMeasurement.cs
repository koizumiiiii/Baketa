using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

namespace Baketa.Core.Performance;

/// <summary>
/// パフォーマンス測定の種類
/// </summary>
public enum MeasurementType
{
    /// <summary>翻訳エンジン初期化</summary>
    TranslationEngineInitialization,
    /// <summary>OCRエンジン初期化</summary>
    OcrEngineInitialization,
    /// <summary>バッチOCR処理</summary>
    BatchOcrProcessing,
    /// <summary>翻訳処理</summary>
    TranslationProcessing,
    /// <summary>画像キャプチャ</summary>
    ImageCapture,
    /// <summary>オーバーレイ表示</summary>
    OverlayDisplay,
    /// <summary>全体処理</summary>
    OverallProcessing,

    // 詳細測定用の新しい種類
    /// <summary>画像前処理（リサイズ、フィルタリング等）</summary>
    ImagePreprocessing,
    /// <summary>タイル分割処理</summary>
    ImageTileGeneration,
    /// <summary>OCRエンジン実行（PaddleOCR本体）</summary>
    OcrEngineExecution,
    /// <summary>OCR後処理（テキスト結合、フィルタリング等）</summary>
    OcrPostProcessing,
    /// <summary>翻訳エンジン実行（OPUS-MT本体）</summary>
    TranslationEngineExecution,
    /// <summary>SentencePiece トークナイザー処理</summary>
    SentencePieceTokenization,
    /// <summary>ONNX推論実行</summary>
    OnnxInference,
    /// <summary>オーバーレイ描画</summary>
    OverlayRendering,
    /// <summary>ウィンドウ操作（キャプチャ、位置取得等）</summary>
    WindowOperations,
    /// <summary>メモリ操作（画像の複製、変換等）</summary>
    MemoryOperations,
    /// <summary>ファイルI/O操作</summary>
    FileOperations
}

/// <summary>
/// パフォーマンス測定結果
/// </summary>
public sealed record PerformanceMeasurementResult
{
    public string OperationId { get; init; } = string.Empty;
    public MeasurementType Type { get; init; }
    public string Description { get; init; } = string.Empty;
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    public TimeSpan Duration { get; init; }
    public long MemoryBefore { get; init; }
    public long MemoryAfter { get; init; }
    public string? AdditionalInfo { get; init; }
}

/// <summary>
/// パフォーマンス測定用の統一ログシステム
/// 既存の複数のログファイルを統合し、詳細な時間測定を提供
/// </summary>
public sealed class PerformanceMeasurement : IDisposable
{
    private static readonly string LogFilePath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "performance_analysis.log");

    private static readonly ConcurrentQueue<PerformanceMeasurementResult> Results = new();
    private static readonly object FileLock = new();
    private static int _operationCounter;

    private readonly Stopwatch _stopwatch;
    private readonly string _operationId;
    private readonly MeasurementType _type;
    private readonly string _description;
    private readonly DateTime _startTime;
    private readonly long _memoryBefore;
    private string? _additionalInfo;
    private bool _disposed;

    static PerformanceMeasurement()
    {
        // 初期化時に既存ログをクリア
        try
        {
            WriteToFile($"=== Performance Analysis Started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        }
        catch
        {
            // ファイルアクセスエラーは無視
        }
    }

    /// <summary>
    /// パフォーマンス測定を開始
    /// </summary>
    /// <param name="type">測定種類</param>
    /// <param name="description">説明</param>
    public PerformanceMeasurement(MeasurementType type, string description)
    {
        _operationId = $"{type}_{Interlocked.Increment(ref _operationCounter):D4}_{DateTime.Now:HHmmss_fff}";
        _type = type;
        _description = description ?? throw new ArgumentNullException(nameof(description));
        _startTime = DateTime.Now;
        _memoryBefore = GC.GetTotalMemory(false);

        _stopwatch = Stopwatch.StartNew();

        // 開始ログを記録
        LogStart();
    }

    /// <summary>
    /// 追加情報を設定
    /// </summary>
    public PerformanceMeasurement WithAdditionalInfo(string info)
    {
        _additionalInfo = info;
        return this;
    }

    /// <summary>
    /// 中間ポイントをログに記録
    /// </summary>
    public void LogCheckpoint(string checkpointName)
    {
        if (_disposed) return;

        var elapsed = _stopwatch.Elapsed;
        var message = $"[{DateTime.Now:HH:mm:ss.fff}] 🔄 CHECKPOINT [{_operationId}] {checkpointName} - Elapsed: {elapsed.TotalMilliseconds:F1}ms";

        WriteToFile(message);
        WriteToConsole(message);
    }

    /// <summary>
    /// 測定を完了して結果を記録
    /// </summary>
    public PerformanceMeasurementResult Complete()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _stopwatch.Stop();
        var endTime = DateTime.Now;
        var memoryAfter = GC.GetTotalMemory(false);

        var result = new PerformanceMeasurementResult
        {
            OperationId = _operationId,
            Type = _type,
            Description = _description,
            StartTime = _startTime,
            EndTime = endTime,
            Duration = _stopwatch.Elapsed,
            MemoryBefore = _memoryBefore,
            MemoryAfter = memoryAfter,
            AdditionalInfo = _additionalInfo
        };

        Results.Enqueue(result);
        LogComplete(result);

        return result;
    }

    private void LogStart()
    {
        var message = $"[{_startTime:HH:mm:ss.fff}] 🚀 START [{_operationId}] {_type}: {_description}";

        WriteToFile(message);
        WriteToConsole(message);
    }

    private static void LogComplete(PerformanceMeasurementResult result)
    {
        var memoryDelta = result.MemoryAfter - result.MemoryBefore;
        var memoryDeltaStr = memoryDelta >= 0 ? $"+{memoryDelta / 1024:N0}KB" : $"{memoryDelta / 1024:N0}KB";

        var message = $"[{result.EndTime:HH:mm:ss.fff}] ✅ COMPLETE [{result.OperationId}] " +
                     $"Duration: {result.Duration.TotalMilliseconds:F1}ms, " +
                     $"Memory: {memoryDeltaStr}";

        if (!string.IsNullOrEmpty(result.AdditionalInfo))
        {
            message += $", Info: {result.AdditionalInfo}";
        }

        WriteToFile(message);
        WriteToConsole(message);

        // 重要な測定結果はより詳細にログ
        if (result.Type == MeasurementType.TranslationEngineInitialization ||
            result.Type == MeasurementType.OcrEngineInitialization ||
            result.Duration.TotalSeconds > 1.0)
        {
            var detailMessage = $"⚡ PERFORMANCE ALERT: {result.Type} took {result.Duration.TotalSeconds:F2} seconds";
            WriteToFile(detailMessage);
            WriteToConsole(detailMessage);
        }
    }

    /// <summary>
    /// 全ての測定結果のサマリーを出力
    /// </summary>
    public static void WriteSummary()
    {
        var summary = GenerateSummary();
        WriteToFile("\n" + summary);
        WriteToConsole(summary);
    }

    /// <summary>
    /// パフォーマンス測定結果のサマリーを生成
    /// </summary>
    public static string GenerateSummary()
    {
        var results = new List<PerformanceMeasurementResult>();
        while (Results.TryDequeue(out var result))
        {
            results.Add(result);
        }

        if (results.Count == 0)
            return "📊 No performance measurements recorded.";

        var summary = new System.Text.StringBuilder();
        summary.AppendLine("📊 PERFORMANCE SUMMARY");
        summary.AppendLine("=" + new string('=', 50));

        var groupedResults = results.GroupBy(r => r.Type);
        foreach (var group in groupedResults.OrderByDescending(g => g.Sum(r => r.Duration.TotalMilliseconds)))
        {
            var totalTime = group.Sum(r => r.Duration.TotalMilliseconds);
            var avgTime = group.Average(r => r.Duration.TotalMilliseconds);
            var count = group.Count();

            summary.AppendLine(CultureInfo.InvariantCulture, $"{group.Key}: {totalTime:F1}ms total, {avgTime:F1}ms avg, {count} calls");

            foreach (var result in group.OrderByDescending(r => r.Duration.TotalMilliseconds))
            {
                summary.AppendLine(CultureInfo.InvariantCulture, $"  - {result.Description}: {result.Duration.TotalMilliseconds:F1}ms");
            }
        }

        var overallTime = results.Sum(r => r.Duration.TotalMilliseconds);
        summary.AppendLine(CultureInfo.InvariantCulture, $"\n🎯 TOTAL MEASURED TIME: {overallTime:F1}ms ({overallTime / 1000:F1}s)");

        return summary.ToString();
    }

    private static void WriteToFile(string message)
    {
        try
        {
            lock (FileLock)
            {
                File.AppendAllText(LogFilePath, message + Environment.NewLine);
            }
        }
        catch
        {
            // ファイル書き込みエラーは無視
        }
    }

    private static void WriteToConsole(string message)
    {
        Console.WriteLine(message);
    }

    /// <summary>
    /// 簡単な測定用のヘルパーメソッド
    /// </summary>
    public static PerformanceMeasurementResult Measure(MeasurementType type, string description, Action action)
    {
        using var measurement = new PerformanceMeasurement(type, description);
        action();
        return measurement.Complete();
    }

    /// <summary>
    /// 非同期処理の測定用ヘルパーメソッド
    /// </summary>
    public static async Task<PerformanceMeasurementResult> MeasureAsync(MeasurementType type, string description, Func<Task> asyncAction)
    {
        using var measurement = new PerformanceMeasurement(type, description);
        await asyncAction().ConfigureAwait(false);
        return measurement.Complete();
    }

    public void Dispose()
    {
        if (_disposed) return;

        if (_stopwatch.IsRunning)
        {
            Complete();
        }

        _disposed = true;
    }
}
