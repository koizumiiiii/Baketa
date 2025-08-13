namespace Baketa.Infrastructure.OCR.StickyRoi;

/// <summary>
/// スティッキーROI設定
/// Issue #143 Week 3 Phase 1: ROI管理システム設定
/// </summary>
public sealed class StickyRoiSettings
{
    /// <summary>
    /// 最大ROI数
    /// </summary>
    public int MaxRoiCount { get; set; } = 100;
    
    /// <summary>
    /// ROI有効期限
    /// </summary>
    public TimeSpan RoiExpirationTime { get; set; } = TimeSpan.FromMinutes(30);
    
    /// <summary>
    /// 最大連続失敗回数
    /// </summary>
    public int MaxConsecutiveFailures { get; set; } = 3;
    
    /// <summary>
    /// 最小信頼度閾値
    /// </summary>
    public double MinConfidenceThreshold { get; set; } = 0.1;
    
    /// <summary>
    /// 自動クリーンアップ間隔
    /// </summary>
    public TimeSpan AutoCleanupInterval { get; set; } = TimeSpan.FromMinutes(5);
    
    /// <summary>
    /// ROI統合距離閾値（ピクセル）
    /// </summary>
    public int MergeDistanceThreshold { get; set; } = 20;
    
    /// <summary>
    /// ROI重複判定閾値（比率）
    /// </summary>
    public double OverlapThreshold { get; set; } = 0.7;
    
    /// <summary>
    /// 信頼度減衰率
    /// </summary>
    public double ConfidenceDecayRate { get; set; } = 0.05;
    
    /// <summary>
    /// 信頼度更新レート
    /// </summary>
    public double ConfidenceUpdateRate { get; set; } = 0.2;
    
    /// <summary>
    /// 最小テキスト幅（ピクセル）
    /// </summary>
    public int MinTextWidth { get; set; } = 20;
    
    /// <summary>
    /// 最小テキスト高さ（ピクセル）
    /// </summary>
    public int MinTextHeight { get; set; } = 10;
    
    /// <summary>
    /// 最小テキスト面積（平方ピクセル）
    /// </summary>
    public int MinTextArea { get; set; } = 200;
    
    /// <summary>
    /// 優先度調整の有効/無効
    /// </summary>
    public bool EnablePriorityAdjustment { get; set; } = true;
    
    /// <summary>
    /// 領域拡張マージン（ピクセル）
    /// </summary>
    public int RegionExpansionMargin { get; set; } = 5;
}