using Baketa.Core.Abstractions.OCR;

namespace Baketa.Core.Abstractions.GPU;

/// <summary>
/// GPU加速OCRエンジン インターフェース
/// ONNX Runtime + GPU実行プロバイダーによる高速テキスト認識
/// Issue #143 Week 3 Phase 2: 統合最適化システム
/// </summary>
public interface IGpuOcrEngine : IDisposable
{
    /// <summary>
    /// GPU加速OCR実行可能状態の確認
    /// </summary>
    /// <param name="cancellationToken">キャンセレーション トークン</param>
    /// <returns>GPU利用可能フラグ</returns>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// GPU加速によるテキスト認識処理
    /// </summary>
    /// <param name="imageData">入力画像データ</param>
    /// <param name="cancellationToken">キャンセレーション トークン</param>
    /// <returns>OCR結果</returns>
    Task<OcrResult> RecognizeTextAsync(byte[] imageData, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// GPU環境情報の取得
    /// </summary>
    /// <param name="cancellationToken">キャンセレーション トークン</param>
    /// <returns>GPU環境情報</returns>
    Task<GpuEnvironmentInfo> GetGpuEnvironmentAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// GPU実行プロバイダーの設定更新
    /// </summary>
    /// <param name="providerType">実行プロバイダータイプ</param>
    /// <param name="deviceId">デバイス識別子</param>
    /// <param name="cancellationToken">キャンセレーション トークン</param>
    /// <returns>設定更新結果</returns>
    Task<bool> UpdateExecutionProviderAsync(
        ExecutionProviderType providerType, 
        string? deviceId = null, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// GPUメモリ使用量の取得
    /// </summary>
    /// <param name="cancellationToken">キャンセレーション トークン</param>
    /// <returns>メモリ使用量（MB）</returns>
    Task<long> GetMemoryUsageAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// GPU統計情報の取得
    /// </summary>
    /// <param name="cancellationToken">キャンセレーション トークン</param>
    /// <returns>GPU処理統計</returns>
    Task<GpuOcrStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// GPU OCR統計情報
/// </summary>
public class GpuOcrStatistics
{
    /// <summary>
    /// 総実行回数
    /// </summary>
    public long TotalExecutions { get; init; }
    
    /// <summary>
    /// 成功実行回数
    /// </summary>
    public long SuccessfulExecutions { get; init; }
    
    /// <summary>
    /// 平均実行時間
    /// </summary>
    public TimeSpan AverageExecutionTime { get; init; }
    
    /// <summary>
    /// 最大メモリ使用量（MB）
    /// </summary>
    public long PeakMemoryUsageMB { get; init; }
    
    /// <summary>
    /// GPU使用率
    /// </summary>
    public double GpuUtilization { get; init; }
    
    /// <summary>
    /// エラー発生回数
    /// </summary>
    public long ErrorCount { get; init; }
    
    /// <summary>
    /// 最終更新時刻
    /// </summary>
    public DateTime LastUpdated { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// 実行プロバイダータイプ
/// </summary>
public enum ExecutionProviderType
{
    /// <summary>
    /// CPU実行プロバイダー
    /// </summary>
    Cpu,
    
    /// <summary>
    /// CUDA実行プロバイダー（NVIDIA GPU）
    /// </summary>
    Cuda,
    
    /// <summary>
    /// DirectML実行プロバイダー（DirectX GPU）
    /// </summary>
    DirectML,
    
    /// <summary>
    /// OpenVINO実行プロバイダー（Intel GPU/CPU）
    /// </summary>
    OpenVINO,
    
    /// <summary>
    /// TensorRT実行プロバイダー（NVIDIA最適化）
    /// </summary>
    TensorRT
}