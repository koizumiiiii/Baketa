using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Models.OCR;

namespace Baketa.Core.Abstractions.OCR;

/// <summary>
/// 投機的OCRサービスインターフェース
/// GPU余裕時にOCRを先行実行し、Shot翻訳の応答時間を短縮
/// </summary>
/// <remarks>
/// Issue #293: 投機的実行とリソース適応
/// - GPU使用率が低い場合にバックグラウンドでOCRを実行
/// - 結果をキャッシュし、Shot翻訳時に再利用
/// - 画面変化検知によるキャッシュ無効化
/// </remarks>
public interface ISpeculativeOcrService : IDisposable
{
    /// <summary>
    /// 投機的OCRが有効かどうか
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// 投機的OCRが現在実行中かどうか
    /// </summary>
    /// <remarks>
    /// 競合状態対策: Shot翻訳ボタン押下時に実行中かどうかを確認
    /// </remarks>
    bool IsExecuting { get; }

    /// <summary>
    /// キャッシュされた投機的OCR結果
    /// </summary>
    SpeculativeOcrResult? CachedResult { get; }

    /// <summary>
    /// キャッシュが有効かどうか（TTL内かつ画面変化なし）
    /// </summary>
    bool IsCacheValid { get; }

    /// <summary>
    /// 投機的OCR実行（GPU余裕時）
    /// </summary>
    /// <param name="image">キャプチャ画像</param>
    /// <param name="imageHash">画像ハッシュ（画面変化検知用）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>実行成功した場合はtrue</returns>
    /// <remarks>
    /// - リソース閾値を満たさない場合は実行せずfalseを返す
    /// - 既に実行中の場合は実行せずfalseを返す
    /// </remarks>
    Task<bool> TryExecuteSpeculativeOcrAsync(
        IImage image,
        string? imageHash = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// キャッシュ結果の取得と無効化（Consume）
    /// </summary>
    /// <param name="currentImageHash">現在の画像ハッシュ（検証用）</param>
    /// <returns>有効なキャッシュがあれば結果を返し、キャッシュを無効化。なければnull</returns>
    /// <remarks>
    /// - ハッシュが一致しない場合はnullを返す（画面が変化している）
    /// - 取得後はキャッシュを無効化（一度だけ使用可能）
    /// </remarks>
    SpeculativeOcrResult? ConsumeCache(string? currentImageHash = null);

    /// <summary>
    /// キャッシュ無効化（画面変化時、Live翻訳開始時など）
    /// </summary>
    void InvalidateCache();

    /// <summary>
    /// 現在実行中の投機的OCRをキャンセル
    /// </summary>
    /// <remarks>
    /// 競合状態対策: Shot翻訳開始時やLive翻訳開始時に呼び出し
    /// </remarks>
    void CancelCurrentExecution();

    /// <summary>
    /// 投機的OCR実行完了イベント
    /// </summary>
    event EventHandler<SpeculativeOcrCompletedEventArgs>? SpeculativeOcrCompleted;

    /// <summary>
    /// キャッシュ無効化イベント
    /// </summary>
    event EventHandler<SpeculativeOcrCacheInvalidatedEventArgs>? CacheInvalidated;
}

/// <summary>
/// 投機的OCR完了イベント引数
/// </summary>
public sealed class SpeculativeOcrCompletedEventArgs : EventArgs
{
    /// <summary>OCR結果</summary>
    public required SpeculativeOcrResult Result { get; init; }

    /// <summary>実行時間</summary>
    public required TimeSpan ExecutionTime { get; init; }

    /// <summary>実行時のGPU使用率</summary>
    public double? GpuUsagePercent { get; init; }

    /// <summary>実行時のVRAM使用率</summary>
    public double? VramUsagePercent { get; init; }

    /// <summary>完了時刻</summary>
    public DateTime CompletedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// キャッシュ無効化イベント引数
/// </summary>
public sealed class SpeculativeOcrCacheInvalidatedEventArgs : EventArgs
{
    /// <summary>無効化理由</summary>
    public required CacheInvalidationReason Reason { get; init; }

    /// <summary>キャッシュの年齢（無効化時点）</summary>
    public TimeSpan? CacheAge { get; init; }

    /// <summary>無効化時刻</summary>
    public DateTime InvalidatedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// キャッシュ無効化理由
/// </summary>
public enum CacheInvalidationReason
{
    /// <summary>TTL期限切れ</summary>
    Expired,

    /// <summary>画面変化検知</summary>
    ScreenChanged,

    /// <summary>手動無効化（Live翻訳開始など）</summary>
    ManualInvalidation,

    /// <summary>キャッシュ消費（ConsumeCache呼び出し）</summary>
    Consumed,

    /// <summary>新しい投機的OCR開始</summary>
    NewExecutionStarted,

    /// <summary>投機的OCRキャンセル</summary>
    ExecutionCancelled
}
