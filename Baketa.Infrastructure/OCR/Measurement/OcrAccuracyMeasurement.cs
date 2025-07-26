using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using Microsoft.Extensions.Logging;
using IImageFactory = Baketa.Core.Abstractions.Factories.IImageFactory;
using IOcrEngine = Baketa.Core.Abstractions.OCR.IOcrEngine;

namespace Baketa.Infrastructure.OCR.Measurement;

/// <summary>
/// OCR精度測定実装
/// </summary>
public sealed class OcrAccuracyMeasurement(
    IImageFactory imageFactory,
    ILogger<OcrAccuracyMeasurement> logger) : IOcrAccuracyMeasurement
{
    private static readonly char[] WordSeparators = [' ', '\t', '\n', '\r'];
    
    private readonly IImageFactory _imageFactory = imageFactory ?? throw new ArgumentNullException(nameof(imageFactory));
    private readonly ILogger<OcrAccuracyMeasurement> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<AccuracyMeasurementResult> MeasureAccuracyAsync(
        Core.Abstractions.OCR.IOcrEngine ocrEngine,
        string testImagePath,
        string expectedText,
        CancellationToken cancellationToken = default)
    {
        if (!System.IO.File.Exists(testImagePath))
        {
            throw new System.IO.FileNotFoundException($"テスト画像が見つかりません: {testImagePath}");
        }

        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // 画像を読み込み
            using var image = await _imageFactory.CreateFromFileAsync(testImagePath).ConfigureAwait(false);
            using var advancedImage = _imageFactory.ConvertToAdvancedImage(image);
            
            // OCR実行
            var ocrResult = await ocrEngine.RecognizeAsync(advancedImage, cancellationToken: cancellationToken).ConfigureAwait(false);
            
            stopwatch.Stop();
            
            // 精度計算
            var detectedText = ocrResult.Text;
            var accuracy = CalculateAccuracy(expectedText, detectedText);
            var characterAccuracy = CalculateCharacterAccuracy(expectedText, detectedText);
            var wordAccuracy = CalculateWordAccuracy(expectedText, detectedText);
            
            var result = new AccuracyMeasurementResult
            {
                OverallAccuracy = accuracy,
                CharacterAccuracy = characterAccuracy,
                WordAccuracy = wordAccuracy,
                DetectedCharacterCount = detectedText.Length,
                CorrectCharacterCount = CalculateCorrectCharacterCount(expectedText, detectedText),
                ExpectedCharacterCount = expectedText.Length,
                ProcessingTime = stopwatch.Elapsed,
                AverageConfidence = ocrResult.TextRegions.Count > 0 
                    ? ocrResult.TextRegions.Average(r => r.Confidence) 
                    : 0.0,
                SettingsHash = GenerateSettingsHash(ocrEngine.GetSettings())
            };
            
            _logger.LogInformation("📊 精度測定完了: 全体精度={OverallAccuracy:P2}, 文字精度={CharacterAccuracy:P2}, 単語精度={WordAccuracy:P2}, 処理時間={ProcessingTime}ms",
                result.OverallAccuracy, result.CharacterAccuracy, result.WordAccuracy, result.ProcessingTime.TotalMilliseconds);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 精度測定中にエラー: {ImagePath}", testImagePath);
            throw;
        }
    }

    public async Task<IReadOnlyList<AccuracyMeasurementResult>> MeasureBatchAccuracyAsync(
        Core.Abstractions.OCR.IOcrEngine ocrEngine,
        IReadOnlyList<(string ImagePath, string ExpectedText)> testCases,
        CancellationToken cancellationToken = default)
    {
        var results = new List<AccuracyMeasurementResult>();
        
        _logger.LogInformation("🧪 バッチ精度測定開始: {TestCaseCount}件のテストケース", testCases.Count);
        
        foreach (var (imagePath, expectedText) in testCases)
        {
            try
            {
                var result = await MeasureAccuracyAsync(ocrEngine, imagePath, expectedText, cancellationToken).ConfigureAwait(false);
                results.Add(result);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ テストケース実行失敗: {ImagePath}", imagePath);
                // エラーのテストケースはスキップして続行
            }
            
            if (cancellationToken.IsCancellationRequested)
                break;
        }
        
        if (results.Count > 0)
        {
            var averageAccuracy = results.Average(r => r.OverallAccuracy);
            var averageTime = results.Average(r => r.ProcessingTime.TotalMilliseconds);
            
            _logger.LogInformation("📈 バッチ測定結果: 平均精度={AverageAccuracy:P2}, 平均処理時間={AverageTime}ms, 完了率={CompletionRate:P2}",
                averageAccuracy, averageTime, (double)results.Count / testCases.Count);
        }
        
        return results;
    }

    public async Task<AccuracyComparisonResult> CompareSettingsAccuracyAsync(
        Core.Abstractions.OCR.IOcrEngine ocrEngine,
        OcrEngineSettings baselineSettings,
        OcrEngineSettings improvedSettings,
        IReadOnlyList<(string ImagePath, string ExpectedText)> testCases,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🔬 設定比較精度測定開始: {TestCaseCount}件で比較", testCases.Count);
        
        // 基準設定で測定
        await ocrEngine.ApplySettingsAsync(baselineSettings, cancellationToken).ConfigureAwait(false);
        var baselineResults = await MeasureBatchAccuracyAsync(ocrEngine, testCases, cancellationToken).ConfigureAwait(false);
        
        // 改善設定で測定
        await ocrEngine.ApplySettingsAsync(improvedSettings, cancellationToken).ConfigureAwait(false);
        var improvedResults = await MeasureBatchAccuracyAsync(ocrEngine, testCases, cancellationToken).ConfigureAwait(false);
        
        // 結果を統合
        var baselineAverage = CalculateAverageResult(baselineResults, GenerateSettingsHash(baselineSettings));
        var improvedAverage = CalculateAverageResult(improvedResults, GenerateSettingsHash(improvedSettings));
        
        var comparison = new AccuracyComparisonResult
        {
            BaselineResult = baselineAverage,
            ImprovedResult = improvedAverage
        };
        
        _logger.LogInformation("📊 比較測定完了: 精度改善={AccuracyImprovement:+0.00%;-0.00%;+0.00%}, 処理時間変化={ProcessingTimeChange:+0.00%;-0.00%;+0.00%}, 有意な改善={IsSignificant}",
            comparison.AccuracyImprovement, comparison.ProcessingTimeChange, comparison.IsSignificantImprovement);
        
        return comparison;
    }

    private static double CalculateAccuracy(string expected, string detected)
    {
        if (string.IsNullOrEmpty(expected))
            return string.IsNullOrEmpty(detected) ? 1.0 : 0.0;
        
        if (string.IsNullOrEmpty(detected))
            return 0.0;
        
        // 基本的な文字列類似度（Levenshtein距離ベース）
        var distance = CalculateLevenshteinDistance(expected, detected);
        var maxLength = Math.Max(expected.Length, detected.Length);
        
        return maxLength > 0 ? 1.0 - (double)distance / maxLength : 1.0;
    }

    private static double CalculateCharacterAccuracy(string expected, string detected)
    {
        if (string.IsNullOrEmpty(expected))
            return string.IsNullOrEmpty(detected) ? 1.0 : 0.0;
        
        var correctChars = 0;
        var minLength = Math.Min(expected.Length, detected.Length);
        
        for (int i = 0; i < minLength; i++)
        {
            if (expected[i] == detected[i])
                correctChars++;
        }
        
        return expected.Length > 0 ? (double)correctChars / expected.Length : 0.0;
    }

    private static double CalculateWordAccuracy(string expected, string detected)
    {
        var expectedWords = expected.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
        var detectedWords = detected.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
        
        if (expectedWords.Length == 0)
            return detectedWords.Length == 0 ? 1.0 : 0.0;
        
        var correctWords = expectedWords.Intersect(detectedWords).Count();
        return (double)correctWords / expectedWords.Length;
    }

    private static int CalculateCorrectCharacterCount(string expected, string detected)
    {
        var correctChars = 0;
        var minLength = Math.Min(expected.Length, detected.Length);
        
        for (int i = 0; i < minLength; i++)
        {
            if (expected[i] == detected[i])
                correctChars++;
        }
        
        return correctChars;
    }

    private static int CalculateLevenshteinDistance(string source, string target)
    {
        if (string.IsNullOrEmpty(source))
            return target?.Length ?? 0;
        
        if (string.IsNullOrEmpty(target))
            return source.Length;
        
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

    private static string GenerateSettingsHash(OcrEngineSettings settings)
    {
        var settingsJson = System.Text.Json.JsonSerializer.Serialize(settings);
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(settingsJson));
        return Convert.ToHexString(hashBytes)[..8]; // 最初の8文字のみ使用
    }

    private static AccuracyMeasurementResult CalculateAverageResult(
        IReadOnlyList<AccuracyMeasurementResult> results,
        string settingsHash)
    {
        if (results.Count == 0)
        {
            return new AccuracyMeasurementResult
            {
                SettingsHash = settingsHash
            };
        }
        
        return new AccuracyMeasurementResult
        {
            OverallAccuracy = results.Average(r => r.OverallAccuracy),
            CharacterAccuracy = results.Average(r => r.CharacterAccuracy),
            WordAccuracy = results.Average(r => r.WordAccuracy),
            DetectedCharacterCount = (int)results.Average(r => r.DetectedCharacterCount),
            CorrectCharacterCount = (int)results.Average(r => r.CorrectCharacterCount),
            ExpectedCharacterCount = (int)results.Average(r => r.ExpectedCharacterCount),
            ProcessingTime = TimeSpan.FromMilliseconds(results.Average(r => r.ProcessingTime.TotalMilliseconds)),
            AverageConfidence = results.Average(r => r.AverageConfidence),
            SettingsHash = settingsHash
        };
    }
}
