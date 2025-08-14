using System.Drawing;
using Baketa.Core.Abstractions.Imaging;

namespace Baketa.Infrastructure.OCR.Strategies;

/// <summary>
/// タイル分割戦略の抽象化インターフェース
/// 固定グリッド分割と適応的分割を統一的に扱う
/// </summary>
public interface ITileStrategy
{
    /// <summary>
    /// 画像からOCR処理用の領域リストを生成
    /// </summary>
    /// <param name="image">入力画像</param>
    /// <param name="options">分割オプション</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>OCR処理対象領域のリスト</returns>
    Task<List<TileRegion>> GenerateRegionsAsync(
        IAdvancedImage image, 
        TileGenerationOptions options, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 戦略の名前（ログ・デバッグ用）
    /// </summary>
    string StrategyName { get; }

    /// <summary>
    /// 戦略の設定可能パラメータ
    /// </summary>
    TileStrategyParameters Parameters { get; set; }
}

/// <summary>
/// OCR処理対象領域の詳細情報
/// </summary>
public sealed record TileRegion
{
    /// <summary>領域の矩形座標</summary>
    public required Rectangle Bounds { get; init; }

    /// <summary>領域のタイプ</summary>
    public required TileRegionType RegionType { get; init; }

    /// <summary>信頼度スコア (0.0-1.0)</summary>
    public double ConfidenceScore { get; init; } = 1.0;

    /// <summary>領域識別子</summary>
    public required string RegionId { get; init; }

    /// <summary>追加メタデータ</summary>
    public Dictionary<string, object> Metadata { get; init; } = [];
}

/// <summary>
/// 領域タイプの分類
/// </summary>
public enum TileRegionType
{
    /// <summary>固定グリッド分割による領域</summary>
    Grid,
    /// <summary>テキスト検出による適応的領域</summary>
    TextAdaptive,
    /// <summary>複合領域（テキスト+背景）</summary>
    Composite,
    /// <summary>フォールバック領域</summary>
    Fallback
}

/// <summary>
/// タイル生成オプション
/// </summary>
public sealed record TileGenerationOptions
{
    /// <summary>デフォルトタイルサイズ</summary>
    public int DefaultTileSize { get; init; } = 1024;

    /// <summary>デバッグモード有効化</summary>
    public bool EnableDebugCapture { get; init; } = false;

    /// <summary>デバッグキャプチャ保存パス</summary>
    public string? DebugCapturePath { get; init; }

    /// <summary>最大領域数制限</summary>
    public int MaxRegionCount { get; init; } = 20;
}

/// <summary>
/// 適応的分割戦略のパラメータ
/// </summary>
public sealed class TileStrategyParameters
{
    /// <summary>タイルサイズ (GridTileStrategy用)</summary>
    public int? TileSize { get; set; }

    /// <summary>ノイズ除去: 最小面積閾値</summary>
    public int MinBoundingBoxArea { get; set; } = 100;

    /// <summary>ノイズ除去: 最小信頼度閾値</summary>
    public double MinConfidenceThreshold { get; set; } = 0.3;

    /// <summary>行グループ化: Y座標許容範囲</summary>
    public int LineGroupingYTolerance { get; set; } = 10;

    /// <summary>水平統合: X方向最大距離</summary>
    public int HorizontalMergingMaxDistance { get; set; } = 50;

    /// <summary>ROI最大サイズ制限 (画像比率)</summary>
    public double MaxRegionSizeRatio { get; set; } = 0.8;

    /// <summary>ROI最小サイズ制限</summary>
    public Size MinRegionSize { get; set; } = new(50, 20);
}