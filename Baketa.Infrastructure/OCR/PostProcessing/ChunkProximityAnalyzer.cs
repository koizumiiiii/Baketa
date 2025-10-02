using System.Drawing;
using Baketa.Core.Abstractions.Translation;
using Microsoft.Extensions.Logging;
using Baketa.Core.Settings;

namespace Baketa.Infrastructure.OCR.PostProcessing;

/// <summary>
/// TextChunk の近接度分析器
/// 文字サイズを自動検出し、適切なグループ化閾値を計算
/// </summary>
public sealed class ChunkProximityAnalyzer
{
    private readonly ILogger<ChunkProximityAnalyzer> _logger;
    private readonly ProximityGroupingSettings _settings;

    /// <summary>
    /// 垂直距離倍率（文字高さに対する倍率）
    /// </summary>
    public double VerticalDistanceFactor { get; set; } = 1.2;

    /// <summary>
    /// 水平距離倍率（平均文字幅に対する倍率）
    /// </summary>
    public double HorizontalDistanceFactor { get; set; } = 3.0;

    public ChunkProximityAnalyzer(ILogger<ChunkProximityAnalyzer> logger, ProximityGroupingSettings settings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        // 設定値をプロパティに反映
        VerticalDistanceFactor = settings.VerticalDistanceFactor;
        HorizontalDistanceFactor = settings.HorizontalDistanceFactor;
    }

    /// <summary>
    /// TextChunkリストから近接度コンテキストを分析
    /// </summary>
    public ProximityContext AnalyzeChunks(IReadOnlyList<TextChunk> chunks)
    {
        if (chunks.Count == 0)
        {
            _logger.LogTrace("チャンクが0個 - デフォルトコンテキストを使用");
            return ProximityContext.Default;
        }

        // 有効な高さのみを取得（ノイズ除去）
        var validHeights = chunks
            .Select(c => c.CombinedBounds.Height)
            .Where(h => h >= _settings.MinChunkHeight && h <= _settings.MaxChunkHeight) // 設定値で範囲制限
            .ToList();

        if (validHeights.Count == 0)
        {
            _logger.LogWarning("有効な文字高さが取得できません - デフォルトコンテキストを使用");
            return ProximityContext.Default;
        }

        // 統計計算
        var avgHeight = validHeights.Average();
        var medianHeight = CalculateMedian(validHeights);
        var minHeight = Math.Max(validHeights.Min(), _settings.MinChunkHeight);  // 設定値から最小値
        var maxHeight = Math.Min(validHeights.Max(), _settings.MaxChunkHeight); // 設定値から最大値

        // より信頼性の高い値を選択（中央値の方が外れ値に強い）
        var charHeight = medianHeight;
        var charWidth = charHeight * 0.6; // 一般的な文字の縦横比

        // 動的閾値計算
        var verticalThreshold = charHeight * VerticalDistanceFactor;
        var horizontalThreshold = charWidth * HorizontalDistanceFactor;

        var context = new ProximityContext
        {
            AverageCharHeight = charHeight,
            AverageCharWidth = charWidth,
            VerticalThreshold = verticalThreshold,
            HorizontalThreshold = horizontalThreshold,
            MinCharHeight = minHeight,
            MaxCharHeight = maxHeight
        };

        _logger.LogInformation(
            "🔍 近接度コンテキスト分析完了 - " +
            "文字高さ: {CharHeight:F1}px, " +
            "垂直閾値: {VThreshold:F1}px, " +
            "水平閾値: {HThreshold:F1}px, " +
            "チャンク数: {Count}",
            charHeight, verticalThreshold, horizontalThreshold, chunks.Count);

        return context;
    }

    /// <summary>
    /// 2つのチャンクが近接しているかを判定
    /// Gemini推奨: 同一行と異なる行で異なる水平距離閾値を適用
    /// </summary>
    public bool IsProximityClose(TextChunk a, TextChunk b, ProximityContext context)
    {
        var rectA = a.CombinedBounds;
        var rectB = b.CombinedBounds;

        // 1. 垂直方向の距離チェック
        var vGap = context.GetVerticalGap(rectA, rectB);
        if (vGap > context.VerticalThreshold)
        {
            _logger.LogTrace(
                "垂直距離超過 - ChunkA:{AId} vs ChunkB:{BId}, " +
                "距離:{VGap:F1}px > 閾値:{VThreshold:F1}px",
                a.ChunkId, b.ChunkId, vGap, context.VerticalThreshold);
            return false;
        }

        // 2. 水平距離の計算（共通化）
        var hGap = context.GetHorizontalGap(rectA, rectB);
        var isSameLine = context.IsSameLine(rectA, rectB);

        // 3. 同一行 vs 異なる行で閾値を切り替え
        var horizontalThreshold = isSameLine
            ? context.HorizontalThreshold
            : Math.Min(
                context.HorizontalThreshold * _settings.CrossRowHorizontalDistanceFactor,
                _settings.MaxCrossRowHorizontalGapPixels  // 絶対値上限
              );

        var isClose = hGap <= horizontalThreshold;

        // 4. デバッグログ（トラブルシューティング用）
        if (_settings.EnableDetailedLogging)
        {
            _logger.LogTrace(
                "近接判定 - ChunkA:{AId}「{AText}」 vs ChunkB:{BId}「{BText}」, " +
                "水平距離:{HGap:F1}px, 閾値:{HThreshold:F1}px, " +
                "同一行:{SameLine}, 結果:{Result}",
                a.ChunkId, a.CombinedText, b.ChunkId, b.CombinedText,
                hGap, horizontalThreshold, isSameLine, isClose);
        }

        return isClose;
    }

    /// <summary>
    /// 中央値を計算
    /// </summary>
    private static double CalculateMedian(List<int> values)
    {
        if (values.Count == 0) return 0;

        var sorted = values.OrderBy(x => x).ToList();
        var mid = sorted.Count / 2;

        if (sorted.Count % 2 == 0)
        {
            return (sorted[mid - 1] + sorted[mid]) / 2.0;
        }
        else
        {
            return sorted[mid];
        }
    }
}