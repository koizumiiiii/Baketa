using System.Collections.Concurrent;
using Baketa.Core.Abstractions.Services;

namespace Baketa.Core.Services;

/// <summary>
/// グローバルROI画像情報コレクション
/// TileStrategyからBatchOcrProcessorへの統合用静的コレクション
/// </summary>
public static class GlobalRoiImageCollection
{
    private static readonly ConcurrentBag<TileRoiImageInfo> _roiImages = new();
    private static readonly object _lockObject = new();

    /// <summary>
    /// ROI画像情報を追加
    /// </summary>
    /// <param name="roiInfo">ROI画像情報</param>
    public static void AddRoiImage(TileRoiImageInfo roiInfo)
    {
        if (roiInfo == null)
            return;

        _roiImages.Add(roiInfo);

        // デバッグログ出力
        System.Diagnostics.Debug.WriteLine($"🎯 GlobalRoiImageCollection: ROI追加 - {roiInfo.Strategy}/{roiInfo.RegionId} ({_roiImages.Count}個)");
    }

    /// <summary>
    /// 現在のセッションのすべてのROI画像情報を取得
    /// </summary>
    /// <returns>ROI画像情報のリスト</returns>
    public static IReadOnlyList<TileRoiImageInfo> GetAllRoiImages()
    {
        return [.. _roiImages];
    }

    /// <summary>
    /// 指定された戦略のROI画像情報を取得
    /// </summary>
    /// <param name="strategyName">戦略名</param>
    /// <returns>指定戦略のROI画像情報</returns>
    public static IReadOnlyList<TileRoiImageInfo> GetRoiImagesByStrategy(string strategyName)
    {
        if (string.IsNullOrEmpty(strategyName))
            return Array.Empty<TileRoiImageInfo>();

        return [.. _roiImages.Where(roi => string.Equals(roi.Strategy, strategyName, StringComparison.OrdinalIgnoreCase))];
    }

    /// <summary>
    /// ROI画像情報をクリア
    /// </summary>
    public static void ClearAll()
    {
        lock (_lockObject)
        {
            var count = _roiImages.Count;
            _roiImages.Clear();

            // デバッグログ出力
            System.Diagnostics.Debug.WriteLine($"🧹 GlobalRoiImageCollection: 全ROI情報クリア ({count}個)");
        }
    }

    /// <summary>
    /// 現在のROI画像数を取得
    /// </summary>
    /// <returns>ROI画像数</returns>
    public static int Count => _roiImages.Count;

    /// <summary>
    /// BatchOcrProcessor統合用のRoiImageInfo変換
    /// </summary>
    /// <returns>DiagnosticReportGenerator用のRoiImageInfo配列</returns>
    public static IReadOnlyList<RoiImageInfo> ConvertToDiagnosticFormat()
    {
        return [.. _roiImages.Select(tileRoi => new RoiImageInfo
        {
            ImageId = tileRoi.RegionId,
            FilePath = tileRoi.FilePath,
            DetectedText = null, // TileStrategy段階では検出テキスト不明
            Confidence = tileRoi.ConfidenceScore,
            Width = tileRoi.Bounds.Width,
            Height = tileRoi.Bounds.Height,
            Format = "png",
            TileId = tileRoi.RegionId,
            CreatedAt = tileRoi.SavedAt,
            RelatedEventId = null
        })];
    }
}
