using System;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Settings;

namespace Baketa.Core.Abstractions.Services;

/// <summary>
/// フルスクリーン最適化状態
/// </summary>
public enum FullscreenOptimizationStatus
{
    /// <summary>
    /// 無効状態
    /// </summary>
    Disabled,
    
    /// <summary>
    /// 待機中
    /// </summary>
    Standby,
    
    /// <summary>
    /// フルスクリーン検出中
    /// </summary>
    Detecting,
    
    /// <summary>
    /// 最適化適用中
    /// </summary>
    Optimizing,
    
    /// <summary>
    /// 最適化中
    /// </summary>
    Active,
    
    /// <summary>
    /// 復元中
    /// </summary>
    Restoring,
    
    /// <summary>
    /// エラー状態
    /// </summary>
    Error
}

/// <summary>
/// フルスクリーン最適化統計
/// </summary>
public class FullscreenOptimizationStats
{
    /// <summary>
    /// 最適化が適用された回数
    /// </summary>
    public int OptimizationAppliedCount { get; set; }
    
    /// <summary>
    /// 最適化が解除された回数
    /// </summary>
    public int OptimizationRemovedCount { get; set; }
    
    /// <summary>
    /// エラー発生回数
    /// </summary>
    public int ErrorCount { get; set; }
    
    /// <summary>
    /// 最後の最適化適用時刻
    /// </summary>
    public DateTime? LastOptimizationTime { get; set; }
    
    /// <summary>
    /// 最後のエラー発生時刻
    /// </summary>
    public DateTime? LastErrorTime { get; set; }
    
    /// <summary>
    /// 現在最適化が適用されているウィンドウ
    /// </summary>
    public string? CurrentOptimizedWindow { get; set; }
    
    /// <summary>
    /// 統計をリセットします
    /// </summary>
    public void Reset()
    {
        OptimizationAppliedCount = 0;
        OptimizationRemovedCount = 0;
        ErrorCount = 0;
        LastOptimizationTime = null;
        LastErrorTime = null;
        CurrentOptimizedWindow = null;
    }
    
    /// <summary>
    /// 統計情報の概要を文字列で返します
    /// </summary>
    /// <returns>統計概要</returns>
    public override string ToString()
    {
        return $"適用回数: {OptimizationAppliedCount}, 解除回数: {OptimizationRemovedCount}, " +
               $"エラー: {ErrorCount}, 現在の対象: {CurrentOptimizedWindow ?? "なし"}";
    }
}

/// <summary>
/// フルスクリーン最適化サービス
/// フルスクリーン検出時に自動的にキャプチャ設定を最適化し、
/// ウィンドウモード復帰時に元の設定に復元する
/// </summary>
public interface IFullscreenOptimizationService
{
    /// <summary>
    /// 現在の最適化状態
    /// </summary>
    FullscreenOptimizationStatus Status { get; }
    
    /// <summary>
    /// 最適化統計情報
    /// </summary>
    FullscreenOptimizationStats Statistics { get; }
    
    /// <summary>
    /// 最適化サービスが有効かどうか
    /// </summary>
    bool IsEnabled { get; set; }
    
    /// <summary>
    /// 現在フルスクリーン最適化が適用されているかどうか
    /// </summary>
    bool IsOptimizationActive { get; }
    
    /// <summary>
    /// 現在のフルスクリーン情報
    /// </summary>
    FullscreenInfo? CurrentFullscreenInfo { get; }
    
    /// <summary>
    /// フルスクリーン最適化を開始します
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task StartOptimizationAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// フルスクリーン最適化を停止します
    /// </summary>
    Task StopOptimizationAsync();
    
    /// <summary>
    /// 指定されたフルスクリーン情報に基づいて最適化を手動適用します
    /// </summary>
    /// <param name="fullscreenInfo">フルスクリーン情報</param>
    Task ApplyOptimizationAsync(FullscreenInfo fullscreenInfo);
    
    /// <summary>
    /// 現在の最適化を手動で解除し、元の設定に復元します
    /// </summary>
    Task RemoveOptimizationAsync();
    
    /// <summary>
    /// 最適化設定を強制的にリセットします
    /// </summary>
    Task ForceResetAsync();
    
    /// <summary>
    /// 統計情報をリセットします
    /// </summary>
    void ResetStatistics();
    
    /// <summary>
    /// フルスクリーン最適化が適用されたときのイベント
    /// </summary>
    event EventHandler<FullscreenOptimizationAppliedEventArgs>? OptimizationApplied;
    
    /// <summary>
    /// フルスクリーン最適化が解除されたときのイベント
    /// </summary>
    event EventHandler<FullscreenOptimizationRemovedEventArgs>? OptimizationRemoved;
    
    /// <summary>
    /// 最適化処理でエラーが発生したときのイベント
    /// </summary>
    event EventHandler<FullscreenOptimizationErrorEventArgs>? OptimizationError;
}

/// <summary>
/// フルスクリーン最適化適用イベント引数
/// </summary>
public class FullscreenOptimizationAppliedEventArgs(
    FullscreenInfo fullscreenInfo,
    CaptureSettings optimizedSettings,
    CaptureSettings? originalSettings = null) : EventArgs
{
    /// <summary>
    /// フルスクリーン情報
    /// </summary>
    public FullscreenInfo FullscreenInfo { get; } = fullscreenInfo ?? throw new ArgumentNullException(nameof(fullscreenInfo));

    /// <summary>
    /// 最適化後のキャプチャ設定
    /// </summary>
    public CaptureSettings OptimizedSettings { get; } = optimizedSettings ?? throw new ArgumentNullException(nameof(optimizedSettings));

    /// <summary>
    /// 最適化前の元の設定
    /// </summary>
    public CaptureSettings? OriginalSettings { get; } = originalSettings;

    /// <summary>
    /// 最適化が適用された時刻
    /// </summary>
    public DateTime AppliedTime { get; } = DateTime.Now;
}

/// <summary>
/// フルスクリーン最適化解除イベント引数
/// </summary>
public class FullscreenOptimizationRemovedEventArgs(CaptureSettings? restoredSettings = null, string reason = "") : EventArgs
{
    /// <summary>
    /// 復元されたキャプチャ設定
    /// </summary>
    public CaptureSettings? RestoredSettings { get; } = restoredSettings;

    /// <summary>
    /// 最適化が解除された時刻
    /// </summary>
    public DateTime RemovedTime { get; } = DateTime.Now;

    /// <summary>
    /// 解除理由
    /// </summary>
    public string Reason { get; } = reason;
}

/// <summary>
/// フルスクリーン最適化エラーイベント引数
/// </summary>
public class FullscreenOptimizationErrorEventArgs(Exception exception, string? message = null) : EventArgs
{
    /// <summary>
    /// 発生した例外
    /// </summary>
    public Exception Exception { get; } = exception ?? throw new ArgumentNullException(nameof(exception));

    /// <summary>
    /// エラーが発生した時刻
    /// </summary>
    public DateTime ErrorTime { get; } = DateTime.Now;

    /// <summary>
    /// エラーメッセージ
    /// </summary>
    public string Message { get; } = message ?? exception.Message;
}
