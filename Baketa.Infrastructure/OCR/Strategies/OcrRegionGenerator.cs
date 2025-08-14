using System.Drawing;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Imaging;

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
        try
        {
            _logger?.LogInformation("🎯 OCR領域生成開始 - 戦略: {Strategy}, 画像: {Width}x{Height}",
                _strategy.StrategyName, sourceImage.Width, sourceImage.Height);

            // Phase 1: 戦略による領域生成
            var regions = await _strategy.GenerateRegionsAsync(sourceImage, options, cancellationToken)
                .ConfigureAwait(false);

            _logger?.LogDebug("🔍 戦略による領域生成完了 - 領域数: {Count}", regions.Count);

            // Phase 2: 各領域から画像を切り出し
            var regionImages = new List<RegionImagePair>();
            var successCount = 0;
            var errorCount = 0;

            foreach (var region in regions)
            {
                try
                {
                    var regionImage = await sourceImage.ExtractRegionAsync(region.Bounds)
                        .ConfigureAwait(false);
                    
                    regionImages.Add(new RegionImagePair(region, regionImage));
                    successCount++;

                    _logger?.LogTrace("✅ 領域画像切り出し成功: {RegionId}, サイズ: {Width}x{Height}", 
                        region.RegionId, region.Bounds.Width, region.Bounds.Height);
                }
                catch (Exception ex)
                {
                    errorCount++;
                    _logger?.LogWarning(ex, "❌ 領域画像切り出しエラー: {RegionId}, 座標: ({X},{Y}), サイズ: {Width}x{Height}", 
                        region.RegionId, region.Bounds.X, region.Bounds.Y, 
                        region.Bounds.Width, region.Bounds.Height);
                    // エラー領域はスキップして処理継続
                }
            }

            _logger?.LogInformation("✅ OCR領域生成完了 - 戦略: {Strategy}, 成功: {Success}/{Total}, エラー: {Error}",
                _strategy.StrategyName, successCount, regions.Count, errorCount);

            return regionImages;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ OCR領域生成で重大エラー発生");
            throw;
        }
    }

    /// <summary>
    /// 戦略名を取得
    /// </summary>
    public string StrategyName => _strategy.StrategyName;

    /// <summary>
    /// 戦略パラメータを取得・設定
    /// </summary>
    public TileStrategyParameters StrategyParameters
    {
        get => _strategy.Parameters;
        set => _strategy.Parameters = value;
    }
}

/// <summary>
/// 領域情報と対応する画像のペア
/// </summary>
public sealed record RegionImagePair(TileRegion Region, IAdvancedImage Image) : IDisposable
{
    /// <summary>
    /// 画像リソース解放
    /// </summary>
    public void Dispose() => Image.Dispose();

    /// <summary>
    /// 領域境界座標
    /// </summary>
    public Rectangle Bounds => Region.Bounds;

    /// <summary>
    /// 領域識別子
    /// </summary>
    public string RegionId => Region.RegionId;

    /// <summary>
    /// 領域タイプ
    /// </summary>
    public TileRegionType RegionType => Region.RegionType;

    /// <summary>
    /// 信頼度スコア
    /// </summary>
    public double ConfidenceScore => Region.ConfidenceScore;

    /// <summary>
    /// 追加メタデータ
    /// </summary>
    public IReadOnlyDictionary<string, object> Metadata => Region.Metadata;
}