using System.Drawing;

namespace Baketa.Core.Abstractions.OCR;

/// <summary>
/// スティッキーROI（Region of Interest）管理インターフェース
/// 前回検出領域の記憶と優先再検出によるパフォーマンス最適化
/// Issue #143 Week 3 Phase 1: 処理効率向上システム
/// </summary>
public interface IStickyRoiManager
{
    /// <summary>
    /// 検出されたテキスト領域をROIとして記録
    /// </summary>
    /// <param name="regions">検出されたテキスト領域一覧</param>
    /// <param name="captureTimestamp">キャプチャ時刻</param>
    /// <param name="cancellationToken">キャンセレーション トークン</param>
    /// <returns>記録結果</returns>
    Task<RoiRecordResult> RecordDetectedRegionsAsync(
        IReadOnlyList<TextRegion> regions, 
        DateTime captureTimestamp, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 優先検出すべきROI領域を取得
    /// </summary>
    /// <param name="currentScreenBounds">現在の画面範囲</param>
    /// <param name="maxRegions">最大取得領域数</param>
    /// <param name="cancellationToken">キャンセレーション トークン</param>
    /// <returns>優先ROI領域</returns>
    Task<IReadOnlyList<StickyRoi>> GetPriorityRoisAsync(
        Rectangle currentScreenBounds, 
        int maxRegions = 10, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// ROI領域の信頼度を更新（検出成功/失敗に基づく）
    /// </summary>
    /// <param name="roiId">ROI識別子</param>
    /// <param name="detectionResult">検出結果</param>
    /// <param name="confidence">新しい信頼度</param>
    /// <param name="cancellationToken">キャンセレーション トークン</param>
    /// <returns>更新結果</returns>
    Task<bool> UpdateRoiConfidenceAsync(
        string roiId, 
        RoiDetectionResult detectionResult, 
        double confidence, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 期限切れROIのクリーンアップ
    /// </summary>
    /// <param name="expirationTime">期限切れ判定時間</param>
    /// <param name="cancellationToken">キャンセレーション トークン</param>
    /// <returns>削除されたROI数</returns>
    Task<int> CleanupExpiredRoisAsync(
        TimeSpan expirationTime, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// ROI統計情報を取得
    /// </summary>
    /// <param name="cancellationToken">キャンセレーション トークン</param>
    /// <returns>統計情報</returns>
    Task<RoiStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// ROI設定を更新
    /// </summary>
    /// <param name="settings">新しい設定（実装固有の型）</param>
    /// <param name="cancellationToken">キャンセレーション トークン</param>
    /// <returns>更新結果</returns>
    Task<bool> UpdateSettingsAsync(
        object settings, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// ROI領域の手動追加
    /// </summary>
    /// <param name="region">追加する領域</param>
    /// <param name="priority">優先度</param>
    /// <param name="cancellationToken">キャンセレーション トークン</param>
    /// <returns>追加されたROI</returns>
    Task<StickyRoi?> AddManualRoiAsync(
        Rectangle region, 
        RoiPriority priority = RoiPriority.Normal, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 特定ROIの削除
    /// </summary>
    /// <param name="roiId">削除するROI識別子</param>
    /// <param name="cancellationToken">キャンセレーション トークン</param>
    /// <returns>削除成功フラグ</returns>
    Task<bool> RemoveRoiAsync(string roiId, CancellationToken cancellationToken = default);
}

/// <summary>
/// テキスト領域情報
/// </summary>
public class TextRegion
{
    /// <summary>
    /// 領域の境界
    /// </summary>
    public Rectangle Bounds { get; init; }
    
    /// <summary>
    /// 検出されたテキスト
    /// </summary>
    public string Text { get; init; } = string.Empty;
    
    /// <summary>
    /// 検出信頼度
    /// </summary>
    public double Confidence { get; init; }
    
    /// <summary>
    /// テキスト言語
    /// </summary>
    public string Language { get; init; } = "unknown";
    
    /// <summary>
    /// 追加メタデータ
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = [];
}

/// <summary>
/// スティッキーROI情報
/// </summary>
public class StickyRoi
{
    /// <summary>
    /// ROI識別子
    /// </summary>
    public string RoiId { get; init; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// ROI領域
    /// </summary>
    public Rectangle Region { get; set; }
    
    /// <summary>
    /// 最後に検出されたテキスト
    /// </summary>
    public string LastDetectedText { get; set; } = string.Empty;
    
    /// <summary>
    /// 信頼度スコア
    /// </summary>
    public double ConfidenceScore { get; set; }
    
    /// <summary>
    /// 優先度
    /// </summary>
    public RoiPriority Priority { get; set; }
    
    /// <summary>
    /// 作成日時
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    
    /// <summary>
    /// 最終検出日時
    /// </summary>
    public DateTime LastDetectedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// 検出回数
    /// </summary>
    public int DetectionCount { get; set; }
    
    /// <summary>
    /// 連続失敗回数
    /// </summary>
    public int ConsecutiveFailures { get; set; }
    
    /// <summary>
    /// ROIタイプ
    /// </summary>
    public RoiType Type { get; init; } = RoiType.Automatic;
    
    /// <summary>
    /// 拡張プロパティ
    /// </summary>
    public Dictionary<string, object> Properties { get; init; } = [];
}

/// <summary>
/// ROI記録結果
/// </summary>
public class RoiRecordResult
{
    /// <summary>
    /// 記録が成功したかどうか
    /// </summary>
    public bool IsSuccessful { get; init; }
    
    /// <summary>
    /// 新しく追加されたROI数
    /// </summary>
    public int NewRoisAdded { get; init; }
    
    /// <summary>
    /// 更新されたROI数
    /// </summary>
    public int ExistingRoisUpdated { get; init; }
    
    /// <summary>
    /// 統合されたROI数
    /// </summary>
    public int RoisMerged { get; init; }
    
    /// <summary>
    /// 処理時間
    /// </summary>
    public TimeSpan ProcessingTime { get; init; }
    
    /// <summary>
    /// エラーメッセージ
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// ROI統計情報
/// </summary>
public class RoiStatistics
{
    /// <summary>
    /// 総ROI数
    /// </summary>
    public int TotalRois { get; init; }
    
    /// <summary>
    /// アクティブROI数
    /// </summary>
    public int ActiveRois { get; init; }
    
    /// <summary>
    /// 高優先度ROI数
    /// </summary>
    public int HighPriorityRois { get; init; }
    
    /// <summary>
    /// 平均信頼度
    /// </summary>
    public double AverageConfidence { get; init; }
    
    /// <summary>
    /// 総検出回数
    /// </summary>
    public long TotalDetections { get; init; }
    
    /// <summary>
    /// 成功率
    /// </summary>
    public double SuccessRate { get; init; }
    
    /// <summary>
    /// ROI効率性（処理時間短縮率）
    /// </summary>
    public double EfficiencyGain { get; init; }
    
    /// <summary>
    /// 最終更新時刻
    /// </summary>
    public DateTime LastUpdated { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// スティッキーROI設定
/// </summary>
public class StickyRoiSettings
{
    /// <summary>
    /// 最大ROI保持数
    /// </summary>
    public int MaxRoiCount { get; init; } = 50;
    
    /// <summary>
    /// ROI有効期限
    /// </summary>
    public TimeSpan RoiExpirationTime { get; init; } = TimeSpan.FromHours(1);
    
    /// <summary>
    /// 最小信頼度閾値
    /// </summary>
    public double MinConfidenceThreshold { get; init; } = 0.5;
    
    /// <summary>
    /// 領域マージ距離閾値
    /// </summary>
    public int MergeDistanceThreshold { get; init; } = 20;
    
    /// <summary>
    /// 信頼度減衰率
    /// </summary>
    public double ConfidenceDecayRate { get; init; } = 0.95;
    
    /// <summary>
    /// 最大連続失敗回数
    /// </summary>
    public int MaxConsecutiveFailures { get; init; } = 3;
    
    /// <summary>
    /// 自動クリーンアップ間隔
    /// </summary>
    public TimeSpan AutoCleanupInterval { get; init; } = TimeSpan.FromMinutes(10);
    
    /// <summary>
    /// 優先度調整有効化
    /// </summary>
    public bool EnablePriorityAdjustment { get; init; } = true;
    
    /// <summary>
    /// 領域拡張マージン
    /// </summary>
    public int RegionExpansionMargin { get; init; } = 5;
}

/// <summary>
/// ROI検出結果
/// </summary>
public enum RoiDetectionResult
{
    /// <summary>
    /// 検出成功
    /// </summary>
    Success,
    
    /// <summary>
    /// 検出失敗
    /// </summary>
    Failed,
    
    /// <summary>
    /// 部分的成功
    /// </summary>
    PartialSuccess,
    
    /// <summary>
    /// 領域変更
    /// </summary>
    RegionChanged,
    
    /// <summary>
    /// テキスト変更
    /// </summary>
    TextChanged
}

/// <summary>
/// ROI優先度
/// </summary>
public enum RoiPriority
{
    /// <summary>
    /// 低優先度
    /// </summary>
    Low = 1,
    
    /// <summary>
    /// 通常優先度
    /// </summary>
    Normal = 2,
    
    /// <summary>
    /// 高優先度
    /// </summary>
    High = 3,
    
    /// <summary>
    /// クリティカル優先度
    /// </summary>
    Critical = 4
}

/// <summary>
/// ROIタイプ
/// </summary>
public enum RoiType
{
    /// <summary>
    /// 自動検出ROI
    /// </summary>
    Automatic,
    
    /// <summary>
    /// 手動追加ROI
    /// </summary>
    Manual,
    
    /// <summary>
    /// 学習済みROI
    /// </summary>
    Learned,
    
    /// <summary>
    /// 一時的ROI
    /// </summary>
    Temporary
}