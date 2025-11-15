using System;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Monitoring;

/// <summary>
/// パフォーマンスメトリクス統合収集インターフェース
/// 既存のTranslationMetricsCollector、IBaketaLogger、IPerformanceOrchestratorとの統合レイヤー
/// </summary>
public interface IPerformanceMetricsCollector : IDisposable
{
    /// <summary>
    /// 翻訳メトリクスを記録（高速・ノンブロッキング）
    /// </summary>
    /// <param name="metrics">翻訳メトリクス</param>
    void RecordTranslationMetrics(TranslationPerformanceMetrics metrics);

    /// <summary>
    /// OCRメトリクスを記録（高速・ノンブロッキング）
    /// </summary>
    /// <param name="metrics">OCRメトリクス</param>
    void RecordOcrMetrics(OcrPerformanceMetrics metrics);

    /// <summary>
    /// リソース調整メトリクスを記録
    /// </summary>
    /// <param name="metrics">リソース調整メトリクス</param>
    void RecordResourceAdjustment(ResourceAdjustmentMetrics metrics);

    /// <summary>
    /// 統合パフォーマンスレポートを生成（非同期）
    /// </summary>
    /// <returns>統合パフォーマンスレポート</returns>
    Task<IntegratedPerformanceReport> GenerateReportAsync();

    /// <summary>
    /// メトリクスファイルを手動フラッシュ（オプション）
    /// </summary>
    Task FlushAsync();
}

/// <summary>
/// 翻訳パフォーマンスメトリクス（既存TranslationMetricsとの互換性維持）
/// </summary>
public class TranslationPerformanceMetrics
{
    public TimeSpan TotalDuration { get; init; }           // 翻訳全体時間
    public TimeSpan OcrDuration { get; init; }             // OCR処理時間  
    public TimeSpan TranslationDuration { get; init; }     // NLLB-200処理時間
    public long MemoryUsageMB { get; init; }               // メモリ使用量
    public double GpuUtilization { get; init; }            // GPU使用率
    public bool IsSuccess { get; init; }                   // 成功/失敗
    public string? ErrorMessage { get; init; }             // エラーメッセージ（失敗時）
    public int InputTextLength { get; init; }              // 入力テキスト長
    public int OutputTextLength { get; init; }             // 出力テキスト長
    public string Engine { get; init; } = string.Empty;    // 使用エンジン（NLLB200/Gemini）
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// OCRパフォーマンスメトリクス（既存OCRメトリクス系との統合）
/// </summary>
public class OcrPerformanceMetrics
{
    public TimeSpan ProcessingDuration { get; init; }      // OCR処理時間
    public int ImageWidth { get; init; }                   // 画像幅
    public int ImageHeight { get; init; }                  // 画像高さ
    public int DetectedRegions { get; init; }              // 検出リージョン数
    public double ConfidenceScore { get; init; }           // 信頼度スコア
    public bool IsSuccess { get; init; }                   // 成功/失敗
    public string OcrEngine { get; init; } = string.Empty; // 使用OCRエンジン（PaddleOCR等）
    public long MemoryUsageMB { get; init; }               // メモリ使用量
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// リソース調整メトリクス（HybridResourceManager連携用）
/// </summary>
public class ResourceAdjustmentMetrics
{
    public string ComponentName { get; init; } = string.Empty;  // 調整対象（OCR/Translation）
    public string AdjustmentType { get; init; } = string.Empty; // 調整種別（IncreaseParallelism/DecreaseParallelism）
    public int OldValue { get; init; }                          // 調整前値
    public int NewValue { get; init; }                          // 調整後値
    public string Reason { get; init; } = string.Empty;         // 調整理由
    public double CpuUsage { get; init; }                       // 調整時CPU使用率
    public double MemoryUsage { get; init; }                    // 調整時メモリ使用率
    public double GpuUtilization { get; init; }                 // 調整時GPU使用率
    public double VramUsage { get; init; }                      // 調整時VRAM使用率
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// 統合パフォーマンスレポート
/// </summary>
public class IntegratedPerformanceReport
{
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
    public TimeSpan ReportPeriod { get; init; }

    // 翻訳統計
    public long TotalTranslations { get; init; }
    public long SuccessfulTranslations { get; init; }
    public double TranslationSuccessRate { get; init; }
    public TimeSpan AverageTranslationTime { get; init; }

    // OCR統計
    public long TotalOcrOperations { get; init; }
    public long SuccessfulOcrOperations { get; init; }
    public double OcrSuccessRate { get; init; }
    public TimeSpan AverageOcrTime { get; init; }
    public double AverageConfidenceScore { get; init; }

    // リソース調整統計
    public int ResourceAdjustmentCount { get; init; }
    public int ParallelismIncreases { get; init; }
    public int ParallelismDecreases { get; init; }

    // システムリソース統計
    public double AverageCpuUsage { get; init; }
    public double AverageMemoryUsage { get; init; }
    public double AverageGpuUtilization { get; init; }
    public long PeakMemoryUsageMB { get; init; }

    // ファイル情報
    public string LogFilePath { get; init; } = string.Empty;
    public long LogFileSizeBytes { get; init; }
}
