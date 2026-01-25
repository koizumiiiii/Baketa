using Baketa.Core.Abstractions.OCR;

namespace Baketa.Core.Models.OCR;

/// <summary>
/// 投機的OCR結果キャッシュモデル
/// </summary>
/// <remarks>
/// Issue #293: 投機的実行とリソース適応
/// - OCR結果のみをキャッシュ（画像はキャッシュしない - メモリ効率）
/// - 画面変化検知用のハッシュを保持
/// </remarks>
public sealed record SpeculativeOcrResult
{
    /// <summary>OCR結果</summary>
    public required OcrResults OcrResults { get; init; }

    /// <summary>キャプチャ画像のハッシュ（画面変化検知用）</summary>
    public required string ImageHash { get; init; }

    /// <summary>キャプチャ時刻</summary>
    public required DateTime CapturedAt { get; init; }

    /// <summary>OCR実行完了時刻</summary>
    public required DateTime CompletedAt { get; init; }

    /// <summary>OCR実行時間</summary>
    public required TimeSpan ExecutionTime { get; init; }

    /// <summary>キャプチャ画像のサイズ（参照情報）</summary>
    public required ImageSize ImageSize { get; init; }

    /// <summary>検出されたテキスト領域数</summary>
    public int DetectedRegionCount => OcrResults.TextRegions?.Count ?? 0;

    /// <summary>キャッシュの有効期限</summary>
    public DateTime ExpiresAt { get; init; }

    /// <summary>キャッシュが有効かどうか（TTL内）</summary>
    public bool IsExpired => DateTime.UtcNow > ExpiresAt;

    /// <summary>キャッシュの年齢</summary>
    public TimeSpan Age => DateTime.UtcNow - CompletedAt;

    /// <summary>
    /// 指定されたハッシュと一致するかどうか
    /// </summary>
    /// <param name="hash">比較対象のハッシュ</param>
    /// <returns>一致する場合はtrue</returns>
    public bool MatchesHash(string? hash)
    {
        if (string.IsNullOrEmpty(hash))
            return false;

        return string.Equals(ImageHash, hash, StringComparison.Ordinal);
    }
}

/// <summary>
/// 画像サイズ情報
/// </summary>
public readonly record struct ImageSize(int Width, int Height)
{
    /// <summary>ピクセル数</summary>
    public int PixelCount => Width * Height;

    /// <summary>アスペクト比</summary>
    public double AspectRatio => Height > 0 ? (double)Width / Height : 0;

    public override string ToString() => $"{Width}x{Height}";
}

/// <summary>
/// 投機的OCR統計情報（メトリクス収集用）
/// </summary>
public sealed record SpeculativeOcrMetrics
{
    /// <summary>投機的OCR実行回数</summary>
    public int ExecutionCount { get; init; }

    /// <summary>キャッシュヒット回数</summary>
    public int CacheHitCount { get; init; }

    /// <summary>キャッシュミス回数</summary>
    public int CacheMissCount { get; init; }

    /// <summary>無駄になった投機的OCR回数（消費されずに無効化）</summary>
    public int WastedExecutionCount { get; init; }

    /// <summary>リソース不足でスキップされた回数</summary>
    public int SkippedDueToResourceCount { get; init; }

    /// <summary>キャッシュヒット率</summary>
    public double CacheHitRate => ExecutionCount > 0
        ? (double)CacheHitCount / ExecutionCount * 100
        : 0;

    /// <summary>効率性（無駄にならなかった割合）</summary>
    public double EfficiencyRate => ExecutionCount > 0
        ? (double)(ExecutionCount - WastedExecutionCount) / ExecutionCount * 100
        : 0;

    /// <summary>平均OCR実行時間</summary>
    public TimeSpan AverageExecutionTime { get; init; }

    /// <summary>統計収集開始時刻</summary>
    public DateTime CollectionStartedAt { get; init; }

    /// <summary>最終更新時刻</summary>
    public DateTime LastUpdatedAt { get; init; }
}
