using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Models.Roi;

namespace Baketa.Core.Abstractions.Roi;

/// <summary>
/// [Issue #324] ROI領域変化監視サービスインターフェース
/// 学習済みROI領域のみを監視し、テキスト送りを検知して即時キャプチャを実行
/// </summary>
/// <remarks>
/// 高信頼度ROI領域のハッシュ比較を1秒間隔で実行し、
/// 変化検知時にRoiChangeDetectedイベントを発行。
/// </remarks>
public interface IRoiChangeMonitorService : IDisposable
{
    /// <summary>
    /// ROI監視が有効かどうか
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// 監視中かどうか
    /// </summary>
    bool IsMonitoring { get; }

    /// <summary>
    /// 学習が完了しているか（高信頼度領域が存在するか）
    /// </summary>
    bool IsLearningComplete { get; }

    /// <summary>
    /// 現在監視中の高信頼度ROI領域数
    /// </summary>
    int MonitoredRegionCount { get; }

    /// <summary>
    /// 監視を開始
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task StartMonitoringAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 監視を停止
    /// </summary>
    Task StopMonitoringAsync();

    /// <summary>
    /// 手動でROI領域の変化をチェック
    /// </summary>
    /// <param name="currentImage">現在の画像</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>変化が検出されたROI領域のリスト</returns>
    Task<IReadOnlyList<RoiRegion>> CheckForChangesAsync(
        IImage currentImage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// ROI領域のベースラインハッシュを更新
    /// キャプチャ/翻訳完了後に呼び出し
    /// </summary>
    /// <param name="currentImage">現在の画像</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task UpdateBaselineAsync(
        IImage currentImage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// ROI領域変化検知イベント
    /// </summary>
    event EventHandler<RoiChangeDetectedEventArgs>? RoiChangeDetected;

    /// <summary>
    /// 監視状態変更イベント
    /// </summary>
    event EventHandler<RoiMonitoringStateChangedEventArgs>? MonitoringStateChanged;
}

/// <summary>
/// [Issue #324] ROI領域変化検知イベント引数
/// </summary>
public sealed class RoiChangeDetectedEventArgs : EventArgs
{
    /// <summary>変化が検出されたROI領域</summary>
    public required IReadOnlyList<RoiRegion> ChangedRegions { get; init; }

    /// <summary>検出時刻</summary>
    public DateTime DetectedAt { get; init; } = DateTime.UtcNow;

    /// <summary>変化の割合（0.0-1.0）</summary>
    public float ChangeRatio { get; init; }

    /// <summary>推定されるテキスト送りかどうか</summary>
    public bool IsLikelyTextAdvance { get; init; }
}

/// <summary>
/// [Issue #324] ROI監視状態変更イベント引数
/// </summary>
public sealed class RoiMonitoringStateChangedEventArgs : EventArgs
{
    /// <summary>監視中かどうか</summary>
    public required bool IsMonitoring { get; init; }

    /// <summary>監視中のROI領域数</summary>
    public int MonitoredRegionCount { get; init; }

    /// <summary>状態変更時刻</summary>
    public DateTime ChangedAt { get; init; } = DateTime.UtcNow;

    /// <summary>状態変更理由</summary>
    public required RoiMonitoringStateChangeReason Reason { get; init; }
}

/// <summary>
/// [Issue #324] ROI監視状態変更理由
/// </summary>
public enum RoiMonitoringStateChangeReason
{
    /// <summary>手動で開始</summary>
    ManualStart,

    /// <summary>手動で停止</summary>
    ManualStop,

    /// <summary>学習完了による自動開始</summary>
    LearningComplete,

    /// <summary>リソース不足による一時停止</summary>
    ResourceConstrained,

    /// <summary>Live翻訳開始による一時停止</summary>
    LiveTranslationStarted,

    /// <summary>アプリ終了</summary>
    Disposing
}
