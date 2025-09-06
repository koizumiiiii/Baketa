using System;
using System.Drawing;
using Baketa.Core.UI.Monitors;

namespace Baketa.Core.Abstractions.UI;

/// <summary>
/// 精密オーバーレイ位置調整設定
/// UltraThink Phase 10.2: 8段階精密位置調整戦略
/// </summary>
public sealed class OverlayPositioningOptions
{
    /// <summary>
    /// テキスト領域からの標準余白（物理ピクセル）
    /// </summary>
    public int StandardMargin { get; init; } = 5;
    
    /// <summary>
    /// 動的オフセット調整のステップサイズ（物理ピクセル）
    /// </summary>
    public int DynamicOffsetStep { get; init; } = 10;
    
    /// <summary>
    /// 最大動的オフセット調整回数
    /// </summary>
    public int MaxDynamicOffsetSteps { get; init; } = 20;
    
    /// <summary>
    /// 優先配置戦略（1-8の優先順位）
    /// デフォルト: [上, 下, 右, 左, 右上, 左上, 右下, 左下]
    /// </summary>
    public int[] PreferredPositionPriority { get; init; } = [1, 2, 3, 4, 5, 6, 7, 8];
    
    /// <summary>
    /// モニター境界からの最小距離（論理ピクセル）
    /// </summary>
    public int MonitorBoundaryMargin { get; init; } = 10;
    
    /// <summary>
    /// 衝突検知の最小重複面積閾値（平方ピクセル）
    /// </summary>
    public int CollisionThreshold { get; init; } = 1;
    
    /// <summary>
    /// 高パフォーマンスモード（計算の簡素化）
    /// </summary>
    public bool HighPerformanceMode { get; init; } = false;
}

/// <summary>
/// 位置調整結果情報
/// </summary>
public sealed class PositioningResult
{
    /// <summary>
    /// 最終決定位置
    /// </summary>
    public Point Position { get; init; }
    
    /// <summary>
    /// 使用された配置戦略
    /// </summary>
    public PositioningStrategy UsedStrategy { get; init; }
    
    /// <summary>
    /// 対象モニター情報
    /// </summary>
    public MonitorInfo TargetMonitor { get; init; }
    
    /// <summary>
    /// 衝突回避が適用されたか
    /// </summary>
    public bool CollisionAvoidanceApplied { get; init; }
    
    /// <summary>
    /// DPI補正が適用されたか
    /// </summary>
    public bool DpiCorrectionApplied { get; init; }
    
    /// <summary>
    /// 計算に要した時間（ミリ秒）
    /// </summary>
    public double ComputationTimeMs { get; init; }
}

/// <summary>
/// 配置戦略列挙
/// </summary>
public enum PositioningStrategy
{
    /// <summary>テキスト直上</summary>
    AboveText = 1,
    /// <summary>テキスト直下</summary>
    BelowText = 2,
    /// <summary>テキスト右側</summary>
    RightOfText = 3,
    /// <summary>テキスト左側</summary>
    LeftOfText = 4,
    /// <summary>テキスト右上角</summary>
    TopRightCorner = 5,
    /// <summary>テキスト左上角</summary>
    TopLeftCorner = 6,
    /// <summary>テキスト右下角</summary>
    BottomRightCorner = 7,
    /// <summary>テキスト左下角</summary>
    BottomLeftCorner = 8,
    /// <summary>動的オフセット調整</summary>
    DynamicOffset = 9,
    /// <summary>フォールバック（強制クランプ）</summary>
    ForcedClamp = 10
}