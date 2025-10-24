using OpenCvSharp;
using Baketa.Core.Utilities;
using Baketa.Infrastructure.OCR.Preprocessing;
using Baketa.Infrastructure.OCR.PaddleOCR.Engine;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Memory;
using System.Text.Json;
using System.IO;

namespace Baketa.Infrastructure.OCR.Optimization;

/// <summary>
/// OCR前処理パラメータの自動最適化システム
/// 正解データセットを使用してグリッドサーチで最適なパラメータを発見
/// </summary>
public class PreprocessingParameterOptimizer(IOcrEngine ocrEngine, string groundTruthDataPath)
{
    private readonly IOcrEngine _ocrEngine = ocrEngine ?? throw new ArgumentNullException(nameof(ocrEngine));
    private readonly string _groundTruthDataPath = groundTruthDataPath ?? throw new ArgumentNullException(nameof(groundTruthDataPath));

    /// <summary>
    /// グリッドサーチによる最適パラメータ探索
    /// </summary>
    public async Task<OptimalParameters> FindOptimalParametersAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine("🔍 パラメータ最適化開始");

        // 1. 正解データセットを読み込み
        var dataset = await LoadGroundTruthDatasetAsync().ConfigureAwait(false);
        Console.WriteLine($"📚 正解データセット読み込み完了: {dataset.Count}件");

        // 2. グリッドサーチ用パラメータ範囲を定義
        var parameterGrid = GenerateParameterGrid();
        Console.WriteLine($"🎯 テスト対象パラメータ組み合わせ: {parameterGrid.Count}通り");

        var results = new List<OptimizationResult>();

        // 3. 各パラメータ組み合わせをテスト
        int tested = 0;
        foreach (var config in parameterGrid)
        {
            tested++;
            Console.WriteLine($"⚙️ パラメータテスト {tested}/{parameterGrid.Count}: {config}");

            try
            {
                var accuracy = await EvaluateParameterConfigurationAsync(dataset, config, cancellationToken).ConfigureAwait(false);
                results.Add(new OptimizationResult(config, accuracy));

                Console.WriteLine($"✅ 精度測定完了: {accuracy:F3}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ パラメータテストエラー: {ex.Message}");
            }
        }

        // 4. 最高精度の設定を選択
        var bestResult = results.OrderByDescending(r => r.Accuracy).First();
        Console.WriteLine($"🏆 最適パラメータ発見: 精度 {bestResult.Accuracy:F3}");
        Console.WriteLine($"🏆 最適設定: {bestResult.Configuration}");

        // 5. 結果レポートを生成
        await GenerateOptimizationReportAsync(results, bestResult).ConfigureAwait(false);

        return new OptimalParameters(bestResult.Configuration, bestResult.Accuracy, results.Count);
    }

    /// <summary>
    /// 正解データセットの読み込み
    /// </summary>
    private async Task<List<GroundTruthEntry>> LoadGroundTruthDatasetAsync()
    {
        var jsonContent = await File.ReadAllTextAsync(_groundTruthDataPath).ConfigureAwait(false);
        var jsonDocument = JsonDocument.Parse(jsonContent);

        var entries = new List<GroundTruthEntry>();
        var datasetArray = jsonDocument.RootElement.GetProperty("dataset");

        foreach (var item in datasetArray.EnumerateArray())
        {
            var entry = new GroundTruthEntry
            {
                ImagePath = item.GetProperty("imagePath").GetString()!,
                GroundTruthText = item.GetProperty("groundTruthText").GetString()!,
                SceneType = item.GetProperty("sceneType").GetString()!,
                Brightness = item.GetProperty("brightness").GetString()!
            };
            entries.Add(entry);
        }

        return entries;
    }

    /// <summary>
    /// パラメータ組み合わせの生成
    /// </summary>
    private List<PreprocessingConfiguration> GenerateParameterGrid()
    {
        var configurations = new List<PreprocessingConfiguration>();

        // CLAHE clipLimit: 1.5 - 4.0 (0.5刻み)
        var clipLimits = new[] { 1.5, 2.0, 2.5, 3.0, 3.5, 4.0 };

        // 適応的二値化 blockSize: 9 - 17 (2刻み、奇数のみ)
        var blockSizes = new[] { 9, 11, 13, 15, 17 };

        // バイラテラルフィルタ sigmaColor: 40 - 80 (10刻み)
        var sigmaColors = new[] { 40, 50, 60, 70, 80 };

        // ガウシアンブラー sigmaX: 0.3 - 1.0 (0.1刻み)
        var gaussianSigmas = new[] { 0.3, 0.5, 0.7, 1.0 };

        foreach (var clipLimit in clipLimits)
        foreach (var blockSize in blockSizes)
        foreach (var sigmaColor in sigmaColors)
        foreach (var gaussianSigma in gaussianSigmas)
        {
            configurations.Add(new PreprocessingConfiguration
            {
                CLAHEClipLimit = clipLimit,
                AdaptiveThresholdBlockSize = blockSize,
                BilateralSigmaColor = sigmaColor,
                GaussianBlurSigma = gaussianSigma
            });
        }

        return configurations;
    }

    /// <summary>
    /// 特定のパラメータ設定での精度評価
    /// </summary>
    private async Task<double> EvaluateParameterConfigurationAsync(
        List<GroundTruthEntry> dataset,
        PreprocessingConfiguration config,
        CancellationToken cancellationToken)
    {
        var totalAccuracy = 0.0;
        var validTests = 0;

        foreach (var entry in dataset)
        {
            try
            {
                // 画像を読み込み
                var imagePath = Path.Combine(Path.GetDirectoryName(_groundTruthDataPath)!, "ground-truth-images", entry.ImagePath);
                using var image = Cv2.ImRead(imagePath);

                if (image.Empty())
                {
                    Console.WriteLine($"⚠️ 画像読み込み失敗: {imagePath}");
                    continue;
                }

                // カスタムパラメータで前処理実行
                using var processed = ApplyCustomPreprocessing(image, config);

                // 暫定的に直接PaddleOcrEngineを使用（将来的にはIImageアダプター経由）
                var ocrResults = await ExecuteOcrDirectAsync(processed, cancellationToken).ConfigureAwait(false);
                var recognizedText = string.Join("", ocrResults.Select(r => r.Text));

                // 精度計算
                var accuracy = CalculateTextAccuracy(recognizedText, entry.GroundTruthText);
                totalAccuracy += accuracy;
                validTests++;

                Console.WriteLine($"   📊 {entry.ImagePath}: 精度 {accuracy:F3} (認識: '{recognizedText}' / 正解: '{entry.GroundTruthText}')");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ テスト実行エラー {entry.ImagePath}: {ex.Message}");
            }
        }

        return validTests > 0 ? totalAccuracy / validTests : 0.0;
    }

    /// <summary>
    /// OCRを直接実行（暫定実装）
    /// </summary>
    private async Task<IReadOnlyList<OcrTextRegion>> ExecuteOcrDirectAsync(Mat image, CancellationToken cancellationToken)
    {
        // MatをIImageに変換するアダプターが必要だが、暫定的に画像ファイル経由で対応
        var tempImagePath = Path.GetTempFileName() + ".png";
        try
        {
            // Matを一時的に画像ファイルとして保存
            Cv2.ImWrite(tempImagePath, image);
            
            // 暫定的な画像ラッパー実装
            var imageWrapper = new TempImageWrapper(tempImagePath, image.Width, image.Height);
            
            var ocrResults = await _ocrEngine.RecognizeAsync(imageWrapper, cancellationToken: cancellationToken).ConfigureAwait(false);
            return ocrResults.TextRegions;
        }
        finally
        {
            // 一時ファイルを削除
            if (File.Exists(tempImagePath))
            {
                File.Delete(tempImagePath);
            }
        }
    }

    /// <summary>
    /// カスタムパラメータによる前処理適用
    /// </summary>
    private Mat ApplyCustomPreprocessing(Mat input, PreprocessingConfiguration config)
    {
        var output = new Mat();

        try
        {
            // 1. CLAHE適応的コントラスト強化
            using var clahe = Cv2.CreateCLAHE(clipLimit: config.CLAHEClipLimit, tileGridSize: new OpenCvSharp.Size(8, 8));
            using var contrastEnhanced = new Mat();

            if (input.Channels() == 3)
            {
                using var lab = new Mat();
                Cv2.CvtColor(input, lab, ColorConversionCodes.BGR2Lab);
                var channels = Cv2.Split(lab);

                using var enhancedL = new Mat();
                clahe.Apply(channels[0], enhancedL);

                var enhancedChannels = new Mat[] { enhancedL, channels[1], channels[2] };
                using var enhancedLab = new Mat();
                Cv2.Merge(enhancedChannels, enhancedLab);
                Cv2.CvtColor(enhancedLab, contrastEnhanced, ColorConversionCodes.Lab2BGR);

                foreach (var ch in channels) ch.Dispose();
                foreach (var ch in enhancedChannels.Skip(1)) ch.Dispose();
            }
            else
            {
                clahe.Apply(input, contrastEnhanced);
            }

            // 2. バイラテラルフィルタでノイズ除去
            using var denoised = new Mat();
            Cv2.BilateralFilter(contrastEnhanced, denoised, d: 9, sigmaColor: config.BilateralSigmaColor, sigmaSpace: config.BilateralSigmaColor);

            // 3. ガウシアンブラーで細かいノイズ平滑化
            using var blurred = new Mat();
            Cv2.GaussianBlur(denoised, blurred, new OpenCvSharp.Size(3, 3), config.GaussianBlurSigma);

            // 4. 適応的二値化
            using var gray = new Mat();
            if (blurred.Channels() == 3)
            {
                Cv2.CvtColor(blurred, gray, ColorConversionCodes.BGR2GRAY);
            }
            else
            {
                blurred.CopyTo(gray);
            }

            using var binary = new Mat();
            Cv2.AdaptiveThreshold(gray, binary,
                maxValue: 255,
                adaptiveMethod: AdaptiveThresholdTypes.GaussianC,
                thresholdType: ThresholdTypes.Binary,
                blockSize: config.AdaptiveThresholdBlockSize,
                c: 2);

            // 5. モルフォロジー演算
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(2, 2));
            using var cleaned = new Mat();
            Cv2.MorphologyEx(binary, cleaned, MorphTypes.Open, kernel);
            Cv2.MorphologyEx(cleaned, output, MorphTypes.Close, kernel);

            return output;
        }
        catch
        {
            output?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// テキスト認識精度の計算（編集距離ベース）
    /// </summary>
    private double CalculateTextAccuracy(string recognized, string groundTruth)
    {
        if (string.IsNullOrEmpty(groundTruth))
            return string.IsNullOrEmpty(recognized) ? 1.0 : 0.0;

        // 正規化: 空白文字・改行を除去
        var normalizedRecognized = recognized.Replace(" ", "").Replace("\n", "").Replace("\r", "");
        var normalizedGroundTruth = groundTruth.Replace(" ", "").Replace("\n", "").Replace("\r", "");

        var editDistance = ComputeLevenshteinDistance(normalizedRecognized, normalizedGroundTruth);
        var maxLength = Math.Max(normalizedRecognized.Length, normalizedGroundTruth.Length);

        return maxLength == 0 ? 1.0 : 1.0 - (double)editDistance / maxLength;
    }

    /// <summary>
    /// レーベンシュタイン距離の計算
    /// </summary>
    private int ComputeLevenshteinDistance(string source, string target)
    {
        if (string.IsNullOrEmpty(source)) return target?.Length ?? 0;
        if (string.IsNullOrEmpty(target)) return source.Length;

        var matrix = new int[source.Length + 1, target.Length + 1];

        for (int i = 0; i <= source.Length; i++)
            matrix[i, 0] = i;
        for (int j = 0; j <= target.Length; j++)
            matrix[0, j] = j;

        for (int i = 1; i <= source.Length; i++)
        {
            for (int j = 1; j <= target.Length; j++)
            {
                var cost = source[i - 1] == target[j - 1] ? 0 : 1;
                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        }

        return matrix[source.Length, target.Length];
    }

    /// <summary>
    /// 最適化結果レポートの生成
    /// </summary>
    private async Task GenerateOptimizationReportAsync(List<OptimizationResult> results, OptimizationResult bestResult)
    {
        var reportPath = Path.Combine(Path.GetDirectoryName(_groundTruthDataPath)!, "optimization_report.md");

        var report = $@"# OCR前処理パラメータ最適化レポート

## 最適化結果サマリー

**最高精度**: {bestResult.Accuracy:F4} ({bestResult.Accuracy * 100:F1}%)

**最適パラメータ**:
- CLAHE clipLimit: {bestResult.Configuration.CLAHEClipLimit}
- 適応的二値化 blockSize: {bestResult.Configuration.AdaptiveThresholdBlockSize}
- バイラテラルフィルタ sigmaColor: {bestResult.Configuration.BilateralSigmaColor}
- ガウシアンブラー sigma: {bestResult.Configuration.GaussianBlurSigma}

## 性能統計

- **テスト済み設定数**: {results.Count}
- **平均精度**: {results.Average(r => r.Accuracy):F4}
- **最低精度**: {results.Min(r => r.Accuracy):F4}
- **精度標準偏差**: {CalculateStandardDeviation(results.Select(r => r.Accuracy)):F4}

## トップ10設定

| 順位 | 精度 | clipLimit | blockSize | sigmaColor | gaussianSigma |
|------|------|-----------|-----------|------------|---------------|
";

        var top10 = results.OrderByDescending(r => r.Accuracy).Take(10);
        int rank = 1;
        foreach (var result in top10)
        {
            report += $"| {rank} | {result.Accuracy:F4} | {result.Configuration.CLAHEClipLimit} | {result.Configuration.AdaptiveThresholdBlockSize} | {result.Configuration.BilateralSigmaColor} | {result.Configuration.GaussianBlurSigma} |\n";
            rank++;
        }

        report += $@"

## 推奨事項

1. **CLAHE clipLimit**: {bestResult.Configuration.CLAHEClipLimit} が最適 
2. **適応的二値化**: blockSize {bestResult.Configuration.AdaptiveThresholdBlockSize} が最適
3. **ノイズ除去**: sigmaColor {bestResult.Configuration.BilateralSigmaColor} が最適

## 生成日時
{DateTime.Now:yyyy-MM-dd HH:mm:ss}
";

        await File.WriteAllTextAsync(reportPath, report).ConfigureAwait(false);
        Console.WriteLine($"📊 最適化レポート生成完了: {reportPath}");
    }

    private double CalculateStandardDeviation(IEnumerable<double> values)
    {
        var avg = values.Average();
        var sumSquaredDiffs = values.Sum(v => Math.Pow(v - avg, 2));
        return Math.Sqrt(sumSquaredDiffs / values.Count());
    }
}

/// <summary>
/// 前処理設定パラメータ
/// </summary>
public class PreprocessingConfiguration
{
    public double CLAHEClipLimit { get; set; }
    public int AdaptiveThresholdBlockSize { get; set; }
    public double BilateralSigmaColor { get; set; }
    public double GaussianBlurSigma { get; set; }

    public override string ToString()
    {
        return $"CLAHE:{CLAHEClipLimit}, Block:{AdaptiveThresholdBlockSize}, Sigma:{BilateralSigmaColor}, Blur:{GaussianBlurSigma}";
    }
}

/// <summary>
/// 正解データエントリ
/// </summary>
public class GroundTruthEntry
{
    public string ImagePath { get; set; } = string.Empty;
    public string GroundTruthText { get; set; } = string.Empty;
    public string SceneType { get; set; } = string.Empty;
    public string Brightness { get; set; } = string.Empty;
}

/// <summary>
/// 最適化結果
/// </summary>
public record OptimizationResult(PreprocessingConfiguration Configuration, double Accuracy);

/// <summary>
/// 最適パラメータ結果
/// </summary>
public record OptimalParameters(PreprocessingConfiguration Configuration, double Accuracy, int TestedConfigurations);

/// <summary>
/// 一時的な画像ラッパー（暫定実装）
/// </summary>
internal sealed class TempImageWrapper(string filePath, int width, int height) : IImage
{
    public string FilePath { get; } = filePath;

    public int Width { get; } = width;
    public int Height { get; } = height;
    public bool IsDisposed => false;
    public ImageFormat Format => ImageFormat.Png;

    /// <summary>
    /// PixelFormat property for IImage extension
    /// </summary>
    public ImagePixelFormat PixelFormat => ImagePixelFormat.Rgba32;

    /// <summary>
    /// GetImageMemory method for IImage extension
    /// </summary>
    public ReadOnlyMemory<byte> GetImageMemory()
    {
        var bytes = File.ReadAllBytes(FilePath);
        return new ReadOnlyMemory<byte>(bytes);
    }

    /// <summary>
    /// 🔥 [PHASE5.2G-A] LockPixelData (TempImageWrapper is test-only, not supported)
    /// </summary>
    public PixelDataLock LockPixelData() => throw new NotSupportedException("TempImageWrapper does not support LockPixelData");

    public IImage Clone()
    {
        return new TempImageWrapper(FilePath, Width, Height);
    }

    public async Task<IImage> ResizeAsync(int width, int height)
    {
        // 暫定実装：リサイズは未サポート
        await Task.CompletedTask.ConfigureAwait(false);
        return this;
    }

    public async Task<byte[]> ToByteArrayAsync()
    {
        // ファイルを読み込んでバイト配列として返す
        return await File.ReadAllBytesAsync(FilePath).ConfigureAwait(false);
    }

    public void Dispose()
    {
        // 特に何もしない（ファイル削除は呼び出し側で管理）
    }
}
