namespace Baketa.Core.Abstractions.GPU;

/// <summary>
/// GPU環境検出インターフェース
/// Issue #143 GPU推論対応のコア機能
/// </summary>
public interface IGpuEnvironmentDetector
{
    /// <summary>
    /// GPU環境情報を検出・取得します
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>GPU環境情報</returns>
    Task<GpuEnvironmentInfo> DetectEnvironmentAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// キャッシュ済みGPU環境情報を取得します（高速アクセス用）
    /// </summary>
    /// <returns>キャッシュ済み環境情報、未検出の場合はnull</returns>
    GpuEnvironmentInfo? GetCachedEnvironment();
    
    /// <summary>
    /// GPU環境情報の再検出を強制実行します
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>更新された環境情報</returns>
    Task<GpuEnvironmentInfo> RefreshEnvironmentAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 非同期ウォームアップサービスインターフェース（Issue #143: コールドスタート遅延根絶）
/// </summary>
public interface IWarmupService
{
    /// <summary>
    /// ウォームアップが完了しているかどうか
    /// </summary>
    bool IsWarmupCompleted { get; }
    
    /// <summary>
    /// OCRエンジンのウォームアップが完了しているかどうか
    /// </summary>
    bool IsOcrWarmupCompleted { get; }
    
    /// <summary>
    /// 翻訳エンジンのウォームアップが完了しているかどうか
    /// </summary>
    bool IsTranslationWarmupCompleted { get; }
    
    /// <summary>
    /// ウォームアップ進捗（0.0～1.0）
    /// </summary>
    double WarmupProgress { get; }
    
    /// <summary>
    /// バックグラウンドでウォームアップを開始
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>ウォームアップタスク</returns>
    Task StartWarmupAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// ウォームアップ完了まで待機
    /// </summary>
    /// <param name="timeout">タイムアウト</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>成功した場合はtrue</returns>
    Task<bool> WaitForWarmupAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// ウォームアップ進捗通知イベント
    /// </summary>
    event EventHandler<WarmupProgressEventArgs>? WarmupProgressChanged;
}

/// <summary>
/// ウォームアップ進捗イベント引数
/// </summary>
public class WarmupProgressEventArgs : EventArgs
{
    public double Progress { get; }
    public string Status { get; }
    public WarmupPhase Phase { get; }
    
    public WarmupProgressEventArgs(double progress, string status, WarmupPhase phase)
    {
        Progress = Math.Clamp(progress, 0.0, 1.0);
        Status = status;
        Phase = phase;
    }
}

/// <summary>
/// ウォームアップフェーズ
/// </summary>
public enum WarmupPhase
{
    /// <summary>
    /// 開始
    /// </summary>
    Starting = 0,
    
    /// <summary>
    /// GPU環境検出
    /// </summary>
    GpuDetection = 1,
    
    /// <summary>
    /// OCRエンジン初期化
    /// </summary>
    OcrInitialization = 2,
    
    /// <summary>
    /// 翻訳エンジン初期化
    /// </summary>
    TranslationInitialization = 3,
    
    /// <summary>
    /// OCRウォームアップ
    /// </summary>
    OcrWarmup = 4,
    
    /// <summary>
    /// 翻訳ウォームアップ
    /// </summary>
    TranslationWarmup = 5,
    
    /// <summary>
    /// 完了
    /// </summary>
    Completed = 6
}
