# Adaptive Tile Strategy - 詳細設計仕様書
## テキスト境界認識型適応的領域分割システム

### システム構成図

```
┌─────────────────────────────────────────────────────────────┐
│                    BatchOcrProcessor                        │
│  ┌─────────────────┐    ┌──────────────────────────────┐   │
│  │ OcrRegionGen    │ -> │     ITileStrategy            │   │
│  │                 │    │  ┌─────────────────────────┐  │   │
│  └─────────────────┘    │  │   GridTileStrategy      │  │   │
│                         │  │   (既存互換)            │  │   │
│  ┌─────────────────┐    │  └─────────────────────────┘  │   │
│  │  List<Rect>     │ <- │  ┌─────────────────────────┐  │   │
│  │  ROI Regions    │    │  │  AdaptiveTileStrategy   │  │   │
│  └─────────────────┘    │  │  (新実装)               │  │   │
│                         │  └─────────────────────────┘  │   │
│  ┌─────────────────┐    └──────────────────────────────┘   │
│  │ Parallel OCR    │                                       │
│  │ Processing      │                                       │
│  └─────────────────┘                                       │
└─────────────────────────────────────────────────────────────┘
```

### インターフェース設計

#### ITileStrategy インターフェース
```csharp
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
```

#### TileRegion データモデル
```csharp
namespace Baketa.Infrastructure.OCR.Strategies;

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
```

### GridTileStrategy 実装設計

#### クラス構造
```csharp
namespace Baketa.Infrastructure.OCR.Strategies;

/// <summary>
/// 固定グリッド分割戦略（既存ロジック互換）
/// 従来のBatchOcrProcessorロジックを抽出・実装
/// </summary>
public sealed class GridTileStrategy : ITileStrategy
{
    public string StrategyName => "GridTile";
    public TileStrategyParameters Parameters { get; set; } = new();

    /// <summary>
    /// 固定グリッドによるタイル分割
    /// </summary>
    public async Task<List<TileRegion>> GenerateRegionsAsync(
        IAdvancedImage image, 
        TileGenerationOptions options, 
        CancellationToken cancellationToken = default)
    {
        var tileSize = Parameters.TileSize ?? options.DefaultTileSize;
        var regions = new List<TileRegion>();

        // 従来のSplitImageIntoOptimalTilesAsyncロジックを移植
        var tilesX = (int)Math.Ceiling((double)image.Width / tileSize);
        var tilesY = (int)Math.Ceiling((double)image.Height / tileSize);

        for (var y = 0; y < tilesY; y++)
        {
            for (var x = 0; x < tilesX; x++)
            {
                var bounds = new Rectangle(
                    x * tileSize,
                    y * tileSize,
                    Math.Min(tileSize, image.Width - x * tileSize),
                    Math.Min(tileSize, image.Height - y * tileSize)
                );

                regions.Add(new TileRegion
                {
                    Bounds = bounds,
                    RegionType = TileRegionType.Grid,
                    RegionId = $"grid-{x}-{y}",
                    ConfidenceScore = 1.0,
                    Metadata = { ["GridX"] = x, ["GridY"] = y }
                });
            }
        }

        return regions;
    }
}
```

### AdaptiveTileStrategy 実装設計

#### クラス構造
```csharp
namespace Baketa.Infrastructure.OCR.Strategies;

/// <summary>
/// テキスト検出ベース適応的分割戦略
/// PaddleOCR検出APIを活用したテキスト境界保護分割
/// </summary>
public sealed class AdaptiveTileStrategy : ITileStrategy
{
    private readonly IPaddleOcrDetector _textDetector;
    private readonly ILogger<AdaptiveTileStrategy> _logger;

    public string StrategyName => "AdaptiveTile";
    public TileStrategyParameters Parameters { get; set; } = new();

    public AdaptiveTileStrategy(
        IPaddleOcrDetector textDetector,
        ILogger<AdaptiveTileStrategy> logger)
    {
        _textDetector = textDetector ?? throw new ArgumentNullException(nameof(textDetector));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 適応的テキスト境界保護分割
    /// </summary>
    public async Task<List<TileRegion>> GenerateRegionsAsync(
        IAdvancedImage image, 
        TileGenerationOptions options, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Phase 1: 高速テキスト検出 (det=true, rec=false)
            var detectionResult = await _textDetector.DetectTextRegionsAsync(
                image, cancellationToken).ConfigureAwait(false);

            // Phase 2: バウンディングボックス統合
            var mergedRegions = await MergeBoundingBoxesAsync(
                detectionResult.BoundingBoxes, Parameters).ConfigureAwait(false);

            // Phase 3: ROI品質検証・調整
            var validatedRegions = await ValidateAndAdjustRegionsAsync(
                mergedRegions, image, options).ConfigureAwait(false);

            return validatedRegions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "適応的分割処理でエラー発生、GridTileStrategyにフォールバック");
            
            // フォールバック: 固定グリッド分割
            return await CreateFallbackRegionsAsync(image, options).ConfigureAwait(false);
        }
    }
}
```

#### バウンディングボックス統合アルゴリズム詳細設計

```csharp
/// <summary>
/// バウンディングボックス統合処理
/// テキスト検出結果を意味のある領域に統合
/// </summary>
private async Task<List<TileRegion>> MergeBoundingBoxesAsync(
    List<BoundingBox> boundingBoxes, 
    TileStrategyParameters parameters)
{
    // Step 1: ノイズ除去
    var filteredBoxes = FilterNoiseBoundingBoxes(boundingBoxes, parameters);

    // Step 2: 行グループ化
    var lineGroups = GroupBoundingBoxesByLines(filteredBoxes, parameters);

    // Step 3: 水平方向統合
    var mergedRegions = new List<TileRegion>();
    var regionIdCounter = 0;

    foreach (var lineGroup in lineGroups)
    {
        var horizontalMerged = MergeHorizontalBoundingBoxes(lineGroup, parameters);
        
        foreach (var mergedBounds in horizontalMerged)
        {
            mergedRegions.Add(new TileRegion
            {
                Bounds = mergedBounds,
                RegionType = TileRegionType.TextAdaptive,
                RegionId = $"adaptive-{regionIdCounter++}",
                ConfidenceScore = CalculateRegionConfidence(mergedBounds, lineGroup),
                Metadata = 
                {
                    ["SourceBoundingBoxCount"] = lineGroup.Count,
                    ["LineGroupId"] = lineGroup.GroupId
                }
            });
        }
    }

    return mergedRegions;
}
```

#### 統合パラメータ設定
```csharp
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
```

### OcrRegionGenerator 統合クラス設計

#### クラス構造
```csharp
namespace Baketa.Infrastructure.OCR.Strategies;

/// <summary>
/// OCR領域生成の統合管理クラス
/// ITileStrategy を活用した領域生成・画像切り出し
/// </summary>
public sealed class OcrRegionGenerator
{
    private readonly ITileStrategy _strategy;
    private readonly ILogger<OcrRegionGenerator> _logger;

    public OcrRegionGenerator(ITileStrategy strategy, ILogger<OcrRegionGenerator> logger)
    {
        _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 画像から OCR 処理用の領域画像リストを生成
    /// </summary>
    public async Task<List<RegionImagePair>> GenerateRegionImagesAsync(
        IAdvancedImage sourceImage, 
        TileGenerationOptions options,
        CancellationToken cancellationToken = default)
    {
        // Phase 1: 戦略による領域生成
        var regions = await _strategy.GenerateRegionsAsync(sourceImage, options, cancellationToken);

        // Phase 2: 各領域から画像を切り出し
        var regionImages = new List<RegionImagePair>();
        
        foreach (var region in regions)
        {
            try
            {
                var regionImage = await sourceImage.ExtractRegionAsync(region.Bounds, cancellationToken);
                regionImages.Add(new RegionImagePair(region, regionImage));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "領域画像切り出しエラー: {RegionId}", region.RegionId);
                // エラー領域はスキップして処理継続
            }
        }

        _logger.LogInformation("OCR領域生成完了: 戦略={Strategy}, 領域数={Count}", 
            _strategy.StrategyName, regionImages.Count);

        return regionImages;
    }
}

/// <summary>
/// 領域情報と対応する画像のペア
/// </summary>
public sealed record RegionImagePair(TileRegion Region, IAdvancedImage Image) : IDisposable
{
    public void Dispose() => Image.Dispose();
}
```

### BatchOcrProcessor 統合設計

#### 統合ポイント
```csharp
// 既存: SplitImageIntoOptimalTilesAsync メソッド
// ↓
// 新規: OcrRegionGenerator による領域生成

// 変更前
private static async Task<List<ImageTile>> SplitImageIntoOptimalTilesAsync(
    IAdvancedImage image, int optimalTileSize)

// 変更後  
private async Task<List<ImageTile>> GenerateOcrRegionsAsync(
    IAdvancedImage image, TileGenerationOptions options)
{
    var regionImages = await _regionGenerator.GenerateRegionImagesAsync(image, options);
    
    return regionImages.Select((pair, index) => new ImageTile
    {
        Image = pair.Image,
        Offset = pair.Region.Bounds.Location,
        Width = pair.Region.Bounds.Width,
        Height = pair.Region.Bounds.Height,
        TileIndex = index,
        RegionMetadata = pair.Region
    }).ToList();
}
```

### DI設定・設定管理設計

#### Dependency Injection 設定
```csharp
// InfrastructureModule.cs 追加
services.AddTransient<IPaddleOcrDetector, PaddleOcrDetector>();

// 戦略パターン設定
services.AddTransient<GridTileStrategy>();
services.AddTransient<AdaptiveTileStrategy>();

// Factory Pattern
services.AddTransient<ITileStrategyFactory, TileStrategyFactory>();

// 設定による戦略選択
services.AddTransient<ITileStrategy>(provider =>
{
    var config = provider.GetRequiredService<IConfiguration>();
    var strategyName = config.GetValue<string>("OCR:TileStrategy", "Grid");
    
    var factory = provider.GetRequiredService<ITileStrategyFactory>();
    return factory.CreateStrategy(strategyName);
});

services.AddTransient<OcrRegionGenerator>();
```

#### 設定ファイル拡張
```json
// appsettings.json
{
  "OCR": {
    "TileStrategy": "Adaptive", // "Grid" | "Adaptive"
    "TileStrategyParameters": {
      "TileSize": 1024,
      "MinBoundingBoxArea": 100,
      "MinConfidenceThreshold": 0.3,
      "LineGroupingYTolerance": 10,
      "HorizontalMergingMaxDistance": 50,
      "MaxRegionSizeRatio": 0.8
    }
  }
}
```

### テスト設計

#### 単体テスト構造
```csharp
// GridTileStrategyTests.cs
[TestClass]
public class GridTileStrategyTests
{
    [TestMethod]
    public async Task GenerateRegionsAsync_StandardImage_ReturnsExpectedGridRegions()
    {
        // 既存BatchOcrProcessorテストケースを移植
        // 完全な互換性確保テスト
    }
}

// AdaptiveTileStrategyTests.cs  
[TestClass]
public class AdaptiveTileStrategyTests
{
    [TestMethod]
    public async Task GenerateRegionsAsync_TextBoundaryCase_AvoidsTextSplitting()
    {
        // 「第一のスープ」問題の解決確認テスト
        // テキスト境界分割回避の検証
    }

    [TestMethod]
    public async Task MergeBoundingBoxesAsync_MultipleTextLines_CreatesProperRegions()
    {
        // バウンディングボックス統合ロジックのテスト
    }
}
```

#### 統合テスト設計
```csharp
// AdaptiveOcrIntegrationTests.cs
[TestClass]
public class AdaptiveOcrIntegrationTests
{
    [TestMethod]
    public async Task EndToEndOcrProcessing_AdaptiveVsGrid_QualityComparison()
    {
        // A/Bテスト形式での品質比較検証
        // GridTileStrategy vs AdaptiveTileStrategy
    }
}
```

### パフォーマンス・監視設計

#### メトリクス収集
```csharp
// AdaptiveTileStrategyMetrics.cs
public sealed class AdaptiveTileStrategyMetrics
{
    public TimeSpan TextDetectionTime { get; set; }
    public TimeSpan BoundingBoxMergingTime { get; set; }
    public int GeneratedRegionCount { get; set; }
    public int OriginalBoundingBoxCount { get; set; }
    public double RegionCompressionRatio => (double)GeneratedRegionCount / OriginalBoundingBoxCount;
    public List<TileRegion> GeneratedRegions { get; set; } = [];
}
```

#### デバッグキャプチャ拡張
```csharp
// 既存のSaveDebugCaptureWithTilesAsync を拡張
// AdaptiveTileStrategy の ROI 可視化対応
private static async Task SaveDebugCaptureWithRegionsAsync(
    IAdvancedImage image, 
    List<TileRegion> regions, 
    string strategyName)
{
    // 戦略別のデバッグ画像生成
    // ROI境界線、信頼度、領域タイプ別色分け表示
}
```

---

この詳細設計仕様書により、テキスト境界問題の根本解決を実現する適応的タイル戦略システムを構築します。既存アーキテクチャとの完全互換性を保ちながら、OCR精度と翻訳品質の大幅向上を達成します。