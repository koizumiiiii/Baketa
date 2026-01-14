using System.Diagnostics;
using System.Drawing;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.Services.OCR;

/// <summary>
/// [Issue #290] 並列OCR実行サービス実装
/// 画像をタイルに分割し、並列にOCRを実行することで処理時間を短縮
/// </summary>
/// <remarks>
/// 技術詳細:
/// - 2x2タイル分割（デフォルト）: 4並列で約50%の処理時間短縮を期待
/// - タイルオーバーラップ: 境界付近のテキスト検出漏れを防ぐ
/// - 座標変換: タイルローカル座標 → 元画像座標への自動変換
/// - 重複排除: オーバーラップ領域での重複テキスト検出を排除
/// </remarks>
public sealed class ParallelOcrExecutor : IParallelOcrExecutor, IDisposable
{
    private readonly IOcrEngine _ocrEngine;
    private readonly IImageProcessingService _imageProcessingService;
    private readonly ILogger<ParallelOcrExecutor> _logger;
    private readonly SemaphoreSlim _parallelSemaphore;
    private ParallelOcrSettings _settings;

    public ParallelOcrExecutor(
        IOcrEngine ocrEngine,
        IImageProcessingService imageProcessingService,
        ILogger<ParallelOcrExecutor> logger)
    {
        _ocrEngine = ocrEngine ?? throw new ArgumentNullException(nameof(ocrEngine));
        _imageProcessingService = imageProcessingService ?? throw new ArgumentNullException(nameof(imageProcessingService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = new ParallelOcrSettings();
        _parallelSemaphore = new SemaphoreSlim(_settings.MaxParallelism, _settings.MaxParallelism);
    }

    /// <inheritdoc/>
    public async Task<OcrResults> ExecuteParallelOcrAsync(
        IImage image,
        IProgress<OcrProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);

        var stopwatch = Stopwatch.StartNew();
        var imageSize = image.Width * image.Height;

        // 並列OCRが無効または画像が小さすぎる場合は通常のOCRを実行
        if (!_settings.EnableParallelOcr || imageSize < _settings.MinImageSizeForParallel)
        {
            _logger.LogDebug("[ParallelOcr] 通常OCR実行: Size={Width}x{Height}, Parallel={Enabled}, MinSize={MinSize}",
                image.Width, image.Height, _settings.EnableParallelOcr, _settings.MinImageSizeForParallel);
            return await _ocrEngine.RecognizeAsync(image, progressCallback, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("[ParallelOcr] 並列OCR開始: Size={Width}x{Height}, Tiles={Cols}x{Rows}",
            image.Width, image.Height, _settings.TileColumnsCount, _settings.TileRowsCount);

        progressCallback?.Report(new OcrProgress(0.0, "並列OCR開始") { Phase = OcrPhase.Initializing });

        try
        {
            // タイル領域を計算
            var tileRegions = CalculateTileRegions(image.Width, image.Height);
            _logger.LogDebug("[ParallelOcr] タイル数: {Count}", tileRegions.Count);

            // 各タイルをクロップして並列OCR実行
            var tileResults = new List<(Rectangle Region, OcrResults Results)>();
            var completedCount = 0;
            var totalTiles = tileRegions.Count;

            var tasks = tileRegions.Select(async region =>
            {
                await _parallelSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // タイル画像をクロップ
                    using var tileImage = await _imageProcessingService.CropImageAsync(image, region).ConfigureAwait(false);

                    _logger.LogDebug("[ParallelOcr] タイルOCR開始: Region={Region}", region);

                    // OCR実行
                    var result = await _ocrEngine.RecognizeAsync(tileImage, null, cancellationToken).ConfigureAwait(false);

                    // 進捗更新
                    var completed = Interlocked.Increment(ref completedCount);
                    var progress = (double)completed / totalTiles;
                    progressCallback?.Report(new OcrProgress(progress, $"タイル {completed}/{totalTiles} 完了")
                    {
                        Phase = OcrPhase.TextRecognition
                    });

                    return (Region: region, Results: result);
                }
                finally
                {
                    _parallelSemaphore.Release();
                }
            }).ToList();

            var results = await Task.WhenAll(tasks).ConfigureAwait(false);
            tileResults.AddRange(results);

            // 結果をマージ
            progressCallback?.Report(new OcrProgress(0.95, "結果マージ中") { Phase = OcrPhase.PostProcessing });

            var mergedResults = MergeTileResults(tileResults, image, stopwatch.Elapsed);

            stopwatch.Stop();
            _logger.LogInformation("[ParallelOcr] 並列OCR完了: TotalTime={Time}ms, Regions={Count}",
                stopwatch.ElapsedMilliseconds, mergedResults.TextRegions.Count);

            progressCallback?.Report(new OcrProgress(1.0, "完了") { Phase = OcrPhase.Completed });

            return mergedResults;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[ParallelOcr] 並列OCRがキャンセルされました");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ParallelOcr] 並列OCRでエラーが発生");
            throw;
        }
    }

    /// <inheritdoc/>
    public ParallelOcrSettings GetSettings() => _settings;

    /// <inheritdoc/>
    public void UpdateSettings(ParallelOcrSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        _logger.LogInformation("[ParallelOcr] 設定更新: MaxParallelism={Max}, Tiles={Cols}x{Rows}",
            settings.MaxParallelism, settings.TileColumnsCount, settings.TileRowsCount);
    }

    /// <summary>
    /// タイル領域を計算
    /// </summary>
    private List<Rectangle> CalculateTileRegions(int imageWidth, int imageHeight)
    {
        var regions = new List<Rectangle>();
        var cols = _settings.TileColumnsCount;
        var rows = _settings.TileRowsCount;
        var overlap = _settings.TileOverlapPixels;

        // 基本タイルサイズ
        var baseTileWidth = imageWidth / cols;
        var baseTileHeight = imageHeight / rows;

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                // タイルの開始位置（オーバーラップを考慮）
                var x = Math.Max(0, col * baseTileWidth - overlap);
                var y = Math.Max(0, row * baseTileHeight - overlap);

                // タイルの終了位置（オーバーラップを考慮）
                var endX = Math.Min(imageWidth, (col + 1) * baseTileWidth + overlap);
                var endY = Math.Min(imageHeight, (row + 1) * baseTileHeight + overlap);

                var width = endX - x;
                var height = endY - y;

                regions.Add(new Rectangle(x, y, width, height));
            }
        }

        return regions;
    }

    /// <summary>
    /// タイル結果をマージ
    /// </summary>
    private OcrResults MergeTileResults(
        List<(Rectangle Region, OcrResults Results)> tileResults,
        IImage sourceImage,
        TimeSpan totalProcessingTime)
    {
        var allRegions = new List<OcrTextRegion>();

        foreach (var (tileRegion, result) in tileResults)
        {
            foreach (var textRegion in result.TextRegions)
            {
                // タイルローカル座標 → 元画像座標に変換
                var adjustedBounds = new Rectangle(
                    textRegion.Bounds.X + tileRegion.X,
                    textRegion.Bounds.Y + tileRegion.Y,
                    textRegion.Bounds.Width,
                    textRegion.Bounds.Height);

                // 輪郭点も座標変換
                Point[]? adjustedContour = null;
                if (textRegion.Contour != null)
                {
                    adjustedContour = textRegion.Contour
                        .Select(p => new Point(p.X + tileRegion.X, p.Y + tileRegion.Y))
                        .ToArray();
                }

                var adjustedRegion = new OcrTextRegion(
                    textRegion.Text,
                    adjustedBounds,
                    textRegion.Confidence,
                    adjustedContour,
                    textRegion.Direction);

                allRegions.Add(adjustedRegion);
            }
        }

        // 重複排除: オーバーラップ領域での重複テキストを排除
        var deduplicatedRegions = DeduplicateRegions(allRegions);

        // 言語コードは最初の結果から取得
        var languageCode = tileResults.FirstOrDefault().Results?.LanguageCode ?? "unknown";

        return new OcrResults(
            deduplicatedRegions,
            sourceImage,
            totalProcessingTime,
            languageCode);
    }

    /// <summary>
    /// 重複テキスト領域を排除
    /// </summary>
    private List<OcrTextRegion> DeduplicateRegions(List<OcrTextRegion> regions)
    {
        if (regions.Count <= 1)
            return regions;

        var result = new List<OcrTextRegion>();
        var processed = new HashSet<int>();

        for (int i = 0; i < regions.Count; i++)
        {
            if (processed.Contains(i))
                continue;

            var region = regions[i];
            var duplicates = new List<(int Index, OcrTextRegion Region)> { (i, region) };

            // 類似領域を検索
            for (int j = i + 1; j < regions.Count; j++)
            {
                if (processed.Contains(j))
                    continue;

                var other = regions[j];

                // テキストが同じで、領域が重なっている場合は重複とみなす
                if (IsSimilarText(region.Text, other.Text) && IsOverlapping(region.Bounds, other.Bounds))
                {
                    duplicates.Add((j, other));
                    processed.Add(j);
                }
            }

            // 重複の中で最も信頼度が高いものを選択
            var best = duplicates.OrderByDescending(d => d.Region.Confidence).First();
            result.Add(best.Region);
            processed.Add(best.Index);
        }

        _logger.LogDebug("[ParallelOcr] 重複排除: {Original} → {Deduplicated}",
            regions.Count, result.Count);

        return result;
    }

    /// <summary>
    /// テキストが類似しているか判定
    /// </summary>
    private static bool IsSimilarText(string text1, string text2)
    {
        if (string.IsNullOrEmpty(text1) || string.IsNullOrEmpty(text2))
            return false;

        // 完全一致
        if (text1 == text2)
            return true;

        // 一方が他方を含む
        if (text1.Contains(text2) || text2.Contains(text1))
            return true;

        // 編集距離が短い（簡易判定）
        var maxLen = Math.Max(text1.Length, text2.Length);
        var threshold = maxLen * 0.2; // 20%以下の差異なら類似とみなす

        return LevenshteinDistance(text1, text2) <= threshold;
    }

    /// <summary>
    /// 領域が重なっているか判定
    /// </summary>
    private static bool IsOverlapping(Rectangle r1, Rectangle r2)
    {
        // IoU (Intersection over Union) で判定
        var intersection = Rectangle.Intersect(r1, r2);
        if (intersection.IsEmpty)
            return false;

        var intersectionArea = intersection.Width * intersection.Height;
        var unionArea = (r1.Width * r1.Height) + (r2.Width * r2.Height) - intersectionArea;

        var iou = (double)intersectionArea / unionArea;
        return iou > 0.5; // 50%以上重なっていれば重複とみなす
    }

    /// <summary>
    /// レーベンシュタイン距離を計算
    /// </summary>
    private static int LevenshteinDistance(string s1, string s2)
    {
        var n = s1.Length;
        var m = s2.Length;
        var d = new int[n + 1, m + 1];

        for (int i = 0; i <= n; i++)
            d[i, 0] = i;
        for (int j = 0; j <= m; j++)
            d[0, j] = j;

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[n, m];
    }

    /// <summary>
    /// リソースを解放
    /// </summary>
    public void Dispose()
    {
        _parallelSemaphore.Dispose();
    }
}
