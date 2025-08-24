using System.Drawing;
using Baketa.Core.Abstractions.Events;

namespace Baketa.Core.Events;

/// <summary>
/// ROI画像保存完了イベント
/// TileStrategyからBatchOcrProcessorへROI画像情報を通知
/// </summary>
public sealed class RoiImageSavedEvent : IEvent
{
    /// <summary>
    /// イベントID
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();
    
    /// <summary>
    /// イベント発生時刻
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    
    /// <summary>
    /// イベント名
    /// </summary>
    public string Name { get; init; } = nameof(RoiImageSavedEvent);
    
    /// <summary>
    /// イベントカテゴリ
    /// </summary>
    public string Category { get; init; } = "ROI";
    
    /// <summary>
    /// 領域ID
    /// </summary>
    public required string RegionId { get; init; }
    
    /// <summary>
    /// タイル戦略名
    /// </summary>
    public required string StrategyName { get; init; }
    
    /// <summary>
    /// 保存されたROI画像のファイルパス
    /// </summary>
    public required string ImageFilePath { get; init; }
    
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