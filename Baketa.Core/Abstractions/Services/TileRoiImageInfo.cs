using System.Drawing;

namespace Baketa.Core.Abstractions.Services;

/// <summary>
/// タイル戦略由来のROI画像情報
/// TileStrategyからBatchOcrProcessorへの統合用
/// </summary>
public sealed class TileRoiImageInfo
{
    /// <summary>
    /// 領域ID
    /// </summary>
    public required string RegionId { get; init; }
    
    /// <summary>
    /// タイル戦略名
    /// </summary>
    public required string Strategy { get; init; }
    
    /// <summary>
    /// 保存されたROI画像のファイルパス
    /// </summary>
    public required string FilePath { get; init; }
    
    /// <summary>
    /// ROI領域の境界
    /// </summary>
    public required Rectangle Bounds { get; init; }
    
    /// <summary>
    /// 画像サイズ（バイト）
    /// </summary>
    public long ImageSizeBytes { get; init; }
    
    /// <summary>
    /// 保存日時
    /// </summary>
    public DateTime SavedAt { get; init; } = DateTime.UtcNow;
    
    /// <summary>
    /// 信頼度スコア
    /// </summary>
    public double ConfidenceScore { get; init; } = 1.0;
    
    /// <summary>
    /// 追加メタデータ
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();
}