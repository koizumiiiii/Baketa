using System.ComponentModel.DataAnnotations;

namespace Baketa.Core.Settings;

/// <summary>
/// 近接度ベースのチャンクグループ化設定
/// UltraThink Phase 1: 自動適応アルゴリズム設定
/// </summary>
public sealed class ProximityGroupingSettings
{
    /// <summary>
    /// 近接度グループ化機能の有効/無効
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 垂直距離倍率（文字高さに対する倍率）
    /// 1.0 = 文字高さと同じ距離まで同一グループ
    /// 1.2 = 文字高さの1.2倍まで同一グループ（推奨）
    /// </summary>
    [Range(0.5, 3.0)]
    public double VerticalDistanceFactor { get; set; } = 1.2;

    /// <summary>
    /// 水平距離倍率（平均文字幅に対する倍率）
    /// 3.0 = 文字幅の3倍まで同一行として扱う（推奨）
    /// </summary>
    [Range(1.0, 10.0)]
    public double HorizontalDistanceFactor { get; set; } = 3.0;

    /// <summary>
    /// 詳細ログの有効化
    /// 開発時のデバッグやチューニングに使用
    /// </summary>
    public bool EnableDetailedLogging { get; set; } = false;

    /// <summary>
    /// 最小チャンク高さ（ピクセル）
    /// この値より小さいチャンクはノイズと見なす
    /// </summary>
    [Range(1, 50)]
    public int MinChunkHeight { get; set; } = 8;

    /// <summary>
    /// 最大チャンク高さ（ピクセル）
    /// この値より大きいチャンクは異常値として除外
    /// </summary>
    [Range(50, 500)]
    public int MaxChunkHeight { get; set; } = 200;

    /// <summary>
    /// グループ化統計ログの出力間隔（回数）
    /// この回数毎に統計情報を出力
    /// </summary>
    [Range(1, 100)]
    public int StatisticsLogInterval { get; set; } = 10;

    /// <summary>
    /// デフォルト設定
    /// </summary>
    public static ProximityGroupingSettings Default => new()
    {
        Enabled = true,
        VerticalDistanceFactor = 1.2,
        HorizontalDistanceFactor = 3.0,
        EnableDetailedLogging = false,
        MinChunkHeight = 8,
        MaxChunkHeight = 200,
        StatisticsLogInterval = 10
    };

    /// <summary>
    /// 開発環境用設定（詳細ログ有効）
    /// </summary>
    public static ProximityGroupingSettings Development => new()
    {
        Enabled = true,
        VerticalDistanceFactor = 1.2,
        HorizontalDistanceFactor = 3.0,
        EnableDetailedLogging = true,
        MinChunkHeight = 6,
        MaxChunkHeight = 300,
        StatisticsLogInterval = 5
    };

    /// <summary>
    /// 保守的設定（より厳しい判定）
    /// </summary>
    public static ProximityGroupingSettings Conservative => new()
    {
        Enabled = true,
        VerticalDistanceFactor = 1.0,
        HorizontalDistanceFactor = 2.0,
        EnableDetailedLogging = false,
        MinChunkHeight = 10,
        MaxChunkHeight = 150,
        StatisticsLogInterval = 20
    };

    /// <summary>
    /// 寛容設定（より積極的な結合）
    /// </summary>
    public static ProximityGroupingSettings Aggressive => new()
    {
        Enabled = true,
        VerticalDistanceFactor = 2.0,
        HorizontalDistanceFactor = 5.0,
        EnableDetailedLogging = false,
        MinChunkHeight = 5,
        MaxChunkHeight = 400,
        StatisticsLogInterval = 10
    };
}