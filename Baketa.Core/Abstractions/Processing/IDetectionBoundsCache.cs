using System.Drawing;

namespace Baketa.Core.Abstractions.Processing;

/// <summary>
/// [Issue #500] Detection-Onlyフィルタ用キャッシュエントリ
/// バウンディングボックスとpHashをペアで保持
/// </summary>
public sealed record DetectionCacheEntry(Rectangle[] Bounds, string[] RegionHashes);

/// <summary>
/// [Issue #500] Detection-Onlyフィルタ用バウンディングボックスキャッシュ
/// OcrExecutionStageStrategy（Transient）のサイクル間でDetection矩形を保持するためのSingletonキャッシュ
/// </summary>
public interface IDetectionBoundsCache
{
    /// <summary>
    /// 前回のDetection結果のキャッシュエントリ（矩形+ハッシュ）を取得
    /// </summary>
    DetectionCacheEntry? GetPreviousEntry(string contextId);

    /// <summary>
    /// Detection結果のキャッシュエントリを更新
    /// </summary>
    void UpdateEntry(string contextId, DetectionCacheEntry entry);

    /// <summary>
    /// 前回のDetection結果のバウンディングボックスを取得（後方互換）
    /// </summary>
    Rectangle[]? GetPreviousBounds(string contextId);

    /// <summary>
    /// Detection結果のバウンディングボックスを更新（後方互換）
    /// </summary>
    void UpdateBounds(string contextId, Rectangle[] bounds);

    /// <summary>
    /// 指定コンテキストのキャッシュをクリア
    /// </summary>
    void ClearContext(string contextId);

    /// <summary>
    /// 全コンテキストのキャッシュをクリア（Stop時）
    /// </summary>
    void ClearAll();
}
