using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Settings;

namespace Baketa.Core.Abstractions.Services;

/// <summary>
/// キャプチャサービスのステータス
/// </summary>
public enum CaptureServiceStatus
{
    /// <summary>
    /// 停止状態
    /// </summary>
    Stopped,

    /// <summary>
    /// キャプチャ実行中
    /// </summary>
    Running,

    /// <summary>
    /// 一時停止中
    /// </summary>
    Paused,

    /// <summary>
    /// エラー状態
    /// </summary>
    Error,

    /// <summary>
    /// 初期化中
    /// </summary>
    Initializing,

    /// <summary>
    /// 停止処理中
    /// </summary>
    Stopping
}

/// <summary>
/// 拡張キャプチャサービスのインターフェース
/// 連続キャプチャ、ステータス管理、最適化機能を提供
/// </summary>
public interface IAdvancedCaptureService : ICaptureService
{
    /// <summary>
    /// 現在のキャプチャサービスのステータス
    /// </summary>
    CaptureServiceStatus Status { get; }

    /// <summary>
    /// 最後にキャプチャした画像
    /// </summary>
    IImage? LastCapturedImage { get; }

    /// <summary>
    /// 最後にキャプチャした時刻
    /// </summary>
    DateTime? LastCaptureTime { get; }

    /// <summary>
    /// 現在のキャプチャパフォーマンス情報
    /// </summary>
    CapturePerformanceInfo PerformanceInfo { get; }

    /// <summary>
    /// 連続キャプチャを開始します
    /// </summary>
    /// <param name="settings">キャプチャ設定</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task StartContinuousCaptureAsync(CaptureSettings? settings = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 連続キャプチャを停止します
    /// </summary>
    Task StopCaptureAsync();

    /// <summary>
    /// 連続キャプチャを一時停止します
    /// </summary>
    Task PauseCaptureAsync();

    /// <summary>
    /// 連続キャプチャを再開します
    /// </summary>
    Task ResumeCaptureAsync();

    /// <summary>
    /// 現在のキャプチャ設定を取得します
    /// </summary>
    /// <returns>キャプチャ設定</returns>
    CaptureSettings GetCurrentSettings();

    /// <summary>
    /// キャプチャ設定を更新します
    /// </summary>
    /// <param name="settings">新しい設定</param>
    void UpdateSettings(CaptureSettings settings);

    /// <summary>
    /// ゲームプロファイルを適用します
    /// </summary>
    /// <param name="profile">ゲームプロファイル</param>
    void ApplyGameProfile(Baketa.Core.Settings.GameCaptureProfile profile);

    /// <summary>
    /// パフォーマンス統計をリセットします
    /// </summary>
    void ResetPerformanceStatistics();

    /// <summary>
    /// キャプチャ最適化を実行します
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task OptimizeCaptureAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定されたウィンドウの継続監視を開始します
    /// </summary>
    /// <param name="windowHandle">監視対象ウィンドウ</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task StartWindowMonitoringAsync(IntPtr windowHandle, CancellationToken cancellationToken = default);

    /// <summary>
    /// ウィンドウ監視を停止します
    /// </summary>
    Task StopWindowMonitoringAsync();
}

/// <summary>
/// キャプチャパフォーマンス情報
/// </summary>
public class CapturePerformanceInfo
{
    /// <summary>
    /// 平均キャプチャ時間（ミリ秒）
    /// </summary>
    public double AverageCaptureTimeMs { get; set; }

    /// <summary>
    /// 最大キャプチャ時間（ミリ秒）
    /// </summary>
    public double MaxCaptureTimeMs { get; set; }

    /// <summary>
    /// 最小キャプチャ時間（ミリ秒）
    /// </summary>
    public double MinCaptureTimeMs { get; set; } = double.MaxValue;

    /// <summary>
    /// 総キャプチャ回数
    /// </summary>
    public long TotalCaptureCount { get; set; }

    /// <summary>
    /// 成功したキャプチャ回数
    /// </summary>
    public long SuccessfulCaptureCount { get; set; }

    /// <summary>
    /// 失敗したキャプチャ回数
    /// </summary>
    public long FailedCaptureCount { get; set; }

    /// <summary>
    /// 差分検出によりスキップされた回数
    /// </summary>
    public long SkippedCaptureCount { get; set; }

    /// <summary>
    /// 現在のキャプチャレート（回/秒）
    /// </summary>
    public double CurrentCaptureRate { get; set; }

    /// <summary>
    /// 平均CPU使用率（%）
    /// </summary>
    public double AverageCpuUsage { get; set; }

    /// <summary>
    /// 平均メモリ使用量（MB）
    /// </summary>
    public double AverageMemoryUsageMB { get; set; }

    /// <summary>
    /// パフォーマンス統計の開始時刻
    /// </summary>
    public DateTime StartTime { get; set; } = DateTime.Now;

    /// <summary>
    /// 最後に統計が更新された時刻
    /// </summary>
    public DateTime LastUpdateTime { get; set; } = DateTime.Now;

    /// <summary>
    /// 成功率（%）
    /// </summary>
    public double SuccessRate => TotalCaptureCount > 0 ? (SuccessfulCaptureCount / (double)TotalCaptureCount) * 100.0 : 0.0;

    /// <summary>
    /// 統計をリセットします
    /// </summary>
    public void Reset()
    {
        TotalCaptureCount = 0;
        SuccessfulCaptureCount = 0;
        FailedCaptureCount = 0;
        SkippedCaptureCount = 0;
        AverageCaptureTimeMs = 0;
        MinCaptureTimeMs = double.MaxValue;
        MaxCaptureTimeMs = 0;
        CurrentCaptureRate = 0;
        AverageCpuUsage = 0;
        AverageMemoryUsageMB = 0;
        StartTime = DateTime.Now;
        LastUpdateTime = DateTime.Now;
    }

    /// <summary>
    /// パフォーマンス情報の概要を文字列で返します
    /// </summary>
    /// <returns>パフォーマンス概要</returns>
    public override string ToString()
    {
        var uptime = LastUpdateTime - StartTime;
        return $"総回数: {TotalCaptureCount}, 成功率: {SuccessRate:F1}%, " +
               $"平均時間: {AverageCaptureTimeMs:F1}ms, レート: {CurrentCaptureRate:F1}/秒, " +
               $"稼働時間: {uptime:hh\\:mm\\:ss}";
    }
}
