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
    /// 0.4 = 文字高さの0.4倍まで同一グループ（非常に厳格）
    /// 1.0 = 文字高さと同じ距離まで同一グループ
    /// </summary>
    [Range(0.5, 3.0)]
    public double VerticalDistanceFactor { get; set; } = 0.4;

    /// <summary>
    /// 水平距離倍率（平均文字幅に対する倍率）
    /// 3.0 = 文字幅の3倍まで同一行として扱う（推奨）
    /// </summary>
    [Range(1.0, 10.0)]
    public double HorizontalDistanceFactor { get; set; } = 3.0;

    /// <summary>
    /// 異なる行のチャンクをグルーピングする際の水平距離係数
    /// 改行・折り返しテキストを考慮して、同一行より寛容な値を設定
    /// 2.0 = HorizontalThresholdの2倍まで異なる行でもグルーピング（推奨）
    /// </summary>
    [Range(1.0, 5.0)]
    public double CrossRowHorizontalDistanceFactor { get; set; } = 2.0;

    /// <summary>
    /// 異なる行のチャンクグルーピングの絶対値上限（ピクセル）
    /// 超高解像度画面でも過剰なグルーピングを防ぐための上限値
    /// 100px = 離れたUI要素（左メニューと中央ステータスなど）を確実に分離
    /// </summary>
    [Range(50, 500)]
    public int MaxCrossRowHorizontalGapPixels { get; set; } = 100;

    /// <summary>
    /// 詳細ログの有効化
    /// 開発時のデバッグやチューニングに使用
    /// </summary>
    public bool EnableDetailedLogging { get; set; } = true;

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
        VerticalDistanceFactor = 0.4,
        HorizontalDistanceFactor = 3.0,
        CrossRowHorizontalDistanceFactor = 2.0,
        MaxCrossRowHorizontalGapPixels = 100,
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
        VerticalDistanceFactor = 0.4,
        HorizontalDistanceFactor = 3.0,
        CrossRowHorizontalDistanceFactor = 2.5,
        MaxCrossRowHorizontalGapPixels = 120,
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
        VerticalDistanceFactor = 0.3,
        HorizontalDistanceFactor = 2.0,
        CrossRowHorizontalDistanceFactor = 1.5,
        MaxCrossRowHorizontalGapPixels = 80,
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
        VerticalDistanceFactor = 1.0,
        HorizontalDistanceFactor = 5.0,
        CrossRowHorizontalDistanceFactor = 3.0,
        MaxCrossRowHorizontalGapPixels = 150,
        EnableDetailedLogging = false,
        MinChunkHeight = 5,
        MaxChunkHeight = 400,
        StatisticsLogInterval = 10
    };
}