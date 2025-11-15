using Baketa.Core.Abstractions.Monitoring;

namespace Baketa.Infrastructure.ResourceManagement;

/// <summary>
/// ハイブリッドリソース管理システム - メインインターフェース
/// OCRと翻訳処理のリソース競合を防ぐ統合制御システム
/// </summary>
public interface IResourceManager : IDisposable
{
    /// <summary>
    /// リソース管理システムの初期化状態
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// リソース状況に基づく動的並列度調整（ヒステリシス付き）
    /// </summary>
    Task AdjustParallelismAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// OCR処理実行（リソース制御付き）
    /// 実際の処理を関数として受け取り、リソース管理下で実行する
    /// </summary>
    Task<TResult> ProcessOcrAsync<TResult>(
        Func<ProcessingRequest, CancellationToken, Task<TResult>> ocrTaskFactory,
        ProcessingRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 翻訳処理実行（動的クールダウン付き）
    /// 実際の処理を関数として受け取り、リソース管理下で実行する
    /// </summary>
    Task<TResult> ProcessTranslationAsync<TResult>(
        Func<TranslationRequest, CancellationToken, Task<TResult>> translationTaskFactory,
        TranslationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// リソース管理システムの初期化
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 現在のリソース状況取得
    /// </summary>
    Task<ResourceStatus> GetCurrentResourceStatusAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// OCR処理リクエスト
/// </summary>
public sealed record ProcessingRequest(
    string ImagePath,
    string OperationId,
    DateTime Timestamp
);

/// <summary>
/// 翻訳処理リクエスト
/// </summary>
public sealed record TranslationRequest(
    string Text,
    string SourceLanguage,
    string TargetLanguage,
    string OperationId,
    DateTime Timestamp
);

/// <summary>
/// リソース状態
/// </summary>
public sealed class ResourceStatus
{
    public double CpuUsage { get; set; }       // CPU使用率 (%)
    public double MemoryUsage { get; set; }    // メモリ使用率 (%)
    public double GpuUtilization { get; set; } // GPU使用率 (%)
    public double VramUsage { get; set; }      // VRAM使用率 (%)
    public DateTime Timestamp { get; set; }
    public bool IsOptimalForOcr { get; set; }
    public bool IsOptimalForTranslation { get; set; }
}

/// <summary>
/// リソース閾値設定
/// </summary>
public sealed class ResourceThresholds
{
    public double CpuLowThreshold { get; set; } = 50;
    public double CpuHighThreshold { get; set; } = 80;
    public double MemoryLowThreshold { get; set; } = 60;
    public double MemoryHighThreshold { get; set; } = 85;
    public double GpuLowThreshold { get; set; } = 40;
    public double GpuHighThreshold { get; set; } = 75;
    public double VramLowThreshold { get; set; } = 50;
    public double VramHighThreshold { get; set; } = 80;
}
