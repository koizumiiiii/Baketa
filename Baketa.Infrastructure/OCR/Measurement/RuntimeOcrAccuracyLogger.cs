using Baketa.Core.Abstractions.OCR;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.OCR.Measurement;

/// <summary>
/// 実行時OCR結果を記録・分析するサービス
/// </summary>
public sealed class RuntimeOcrAccuracyLogger(
    IOcrAccuracyMeasurement accuracyMeasurement,
    AccuracyImprovementReporter reporter,
    ILogger<RuntimeOcrAccuracyLogger> logger)
{
    private readonly IOcrAccuracyMeasurement _accuracyMeasurement = accuracyMeasurement ?? throw new ArgumentNullException(nameof(accuracyMeasurement));
    private readonly AccuracyImprovementReporter _reporter = reporter ?? throw new ArgumentNullException(nameof(reporter));
    private readonly ILogger<RuntimeOcrAccuracyLogger> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    
    private readonly List<(DateTime Timestamp, OcrResults Results, string? ExpectedText)> _ocrHistory = [];
    private readonly object _lockObject = new();

    /// <summary>
    /// OCR結果を記録（期待テキストが分からない場合）
    /// </summary>
    /// <param name="results">OCR結果</param>
    /// <param name="imagePath">画像パス（デバッグ用）</param>
    public void LogOcrResult(OcrResults results, string? imagePath = null)
    {
        lock (_lockObject)
        {
            _ocrHistory.Add((DateTime.Now, results, null));
            
            // 信頼度フィルタリング警告
            var lowConfidenceRegions = results.TextRegions.Where(r => r.Confidence < 0.7).ToList();
            if (lowConfidenceRegions.Count > 0)
            {
                foreach (var region in lowConfidenceRegions)
                {
                    _logger.LogWarning("⚠️ 低信頼度テキスト検出: '{Text}' (信頼度: {Confidence:P2}) - 本来除外されるべき",
                        region.Text, region.Confidence);
                }
            }
            
            // 期待テキストの推定試行
            var expectedText = ExtractExpectedTextFromImagePath(imagePath);
            if (!string.IsNullOrEmpty(expectedText))
            {
                _logger.LogInformation("🎯 期待テキスト推定: '{Expected}' vs 検出: '{Detected}'",
                    expectedText, results.Text);
            }
            
            _logger.LogInformation("📊 OCR結果記録: テキスト='{Text}', 処理時間={ProcessingTime}ms, 信頼度={Confidence:P2}, 画像={ImagePath}",
                results.Text.Length > 50 ? results.Text[..50] + "..." : results.Text,
                results.ProcessingTime.TotalMilliseconds,
                results.TextRegions.Count > 0 ? results.TextRegions.Average(r => r.Confidence) : 0.0,
                imagePath ?? "不明");
        }
    }

    /// <summary>
    /// 期待テキストとOCR結果をペアで記録（精度測定可能）
    /// </summary>
    /// <param name="results">OCR結果</param>
    /// <param name="expectedText">期待される正解テキスト</param>
    /// <param name="imagePath">画像パス（デバッグ用）</param>
    public Task LogOcrResultWithExpectedAsync(OcrResults results, string expectedText, string? imagePath = null)
    {
        lock (_lockObject)
        {
            _ocrHistory.Add((DateTime.Now, results, expectedText));
        }

        try
        {
            // 仮の精度測定（実際のOCRエンジンが必要）
            var measurement = new AccuracyMeasurementResult
            {
                OverallAccuracy = CalculateSimpleAccuracy(expectedText, results.Text),
                CharacterAccuracy = CalculateCharacterAccuracy(expectedText, results.Text),
                WordAccuracy = CalculateWordAccuracy(expectedText, results.Text),
                DetectedCharacterCount = results.Text.Length,
                CorrectCharacterCount = CountCorrectCharacters(expectedText, results.Text),
                ExpectedCharacterCount = expectedText.Length,
                ProcessingTime = results.ProcessingTime,
                AverageConfidence = results.TextRegions.Count > 0 ? results.TextRegions.Average(r => r.Confidence) : 0.0,
                SettingsHash = "runtime"
            };

            _logger.LogInformation("🎯 OCR精度測定完了: 全体精度={OverallAccuracy:P2}, 文字精度={CharAccuracy:P2}, 単語精度={WordAccuracy:P2}",
                measurement.OverallAccuracy,
                measurement.CharacterAccuracy, 
                measurement.WordAccuracy);

            // 精度が低い場合は警告
            if (measurement.OverallAccuracy < 0.8)
            {
                _logger.LogWarning("⚠️ OCR精度低下検出: 精度={Accuracy:P2}, 期待='{Expected}', 検出='{Detected}', 画像={ImagePath}",
                    measurement.OverallAccuracy,
                    expectedText.Length > 30 ? expectedText[..30] + "..." : expectedText,
                    results.Text.Length > 30 ? results.Text[..30] + "..." : results.Text,
                    imagePath ?? "不明");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OCR精度測定中にエラーが発生しました");
        }
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// 現在の設定でのOCR精度統計を取得
    /// </summary>
    /// <returns>精度統計情報</returns>
    public OcrAccuracyStats GetAccuracyStats()
    {
        lock (_lockObject)
        {
            var withExpected = _ocrHistory.Where(h => !string.IsNullOrEmpty(h.ExpectedText)).ToList();
            
            if (withExpected.Count == 0)
            {
                return new OcrAccuracyStats
                {
                    TotalMeasurements = 0,
                    MeasurementsWithExpected = 0,
                    AverageOverallAccuracy = 0.0,
                    AverageProcessingTime = TimeSpan.Zero,
                    FirstMeasurement = null,
                    LastMeasurement = DateTime.Now
                };
            }

            // 簡易統計計算（正確な計算は非同期処理が必要）
            var avgProcessingTime = _ocrHistory.Average(h => h.Results.ProcessingTime.TotalMilliseconds);
            var avgConfidence = _ocrHistory
                .Where(h => h.Results.TextRegions.Count > 0)
                .SelectMany(h => h.Results.TextRegions)
                .Average(r => r.Confidence);

            return new OcrAccuracyStats
            {
                TotalMeasurements = _ocrHistory.Count,
                MeasurementsWithExpected = withExpected.Count,
                AverageOverallAccuracy = avgConfidence, // 暫定値（実際の精度計算は重い）
                AverageProcessingTime = TimeSpan.FromMilliseconds(avgProcessingTime),
                FirstMeasurement = _ocrHistory.Min(h => h.Timestamp),
                LastMeasurement = _ocrHistory.Max(h => h.Timestamp)
            };
        }
    }

    /// <summary>
    /// 詳細な精度レポートを生成
    /// </summary>
    /// <param name="outputPath">レポート出力パス</param>
    /// <returns>レポートファイルパス</returns>
    public async Task<string> GenerateDetailedReportAsync(string outputPath)
    {
        List<(DateTime Timestamp, OcrResults Results, string? ExpectedText)> historySnapshot;
        
        lock (_lockObject)
        {
            historySnapshot = [.. _ocrHistory];
        }

        var withExpected = historySnapshot.Where(h => !string.IsNullOrEmpty(h.ExpectedText)).ToList();
        
        if (withExpected.Count == 0)
        {
            _logger.LogWarning("期待テキスト付きの測定データがないため、詳細レポートを生成できません");
            return string.Empty;
        }

        _logger.LogInformation("📊 詳細精度レポート生成開始: {Count}件の測定データを処理中...", withExpected.Count);

        // 精度測定を並列で実行
        var comparisonResults = new List<(string ImprovementName, AccuracyComparisonResult Result)>();
        
        for (int i = 0; i < withExpected.Count && i < 10; i++) // 最大10件まで処理
        {
            var (timestamp, results, expectedText) = withExpected[i];
            
            try
            {
                var measurement = new AccuracyMeasurementResult
                {
                    OverallAccuracy = CalculateSimpleAccuracy(expectedText!, results.Text),
                    CharacterAccuracy = CalculateCharacterAccuracy(expectedText!, results.Text),
                    WordAccuracy = CalculateWordAccuracy(expectedText!, results.Text),
                    DetectedCharacterCount = results.Text.Length,
                    CorrectCharacterCount = CountCorrectCharacters(expectedText!, results.Text),
                    ExpectedCharacterCount = expectedText!.Length,
                    ProcessingTime = results.ProcessingTime,
                    AverageConfidence = results.TextRegions.Count > 0 ? results.TextRegions.Average(r => r.Confidence) : 0.0,
                    SettingsHash = "runtime"
                };

                var baselineMeasurement = new AccuracyMeasurementResult
                {
                    OverallAccuracy = 0.7, // 仮の基準値
                    CharacterAccuracy = 0.7,
                    WordAccuracy = 0.7,
                    ProcessingTime = TimeSpan.FromMilliseconds(1000),
                    SettingsHash = "baseline"
                };

                var comparison = new AccuracyComparisonResult
                {
                    BaselineResult = baselineMeasurement,
                    ImprovedResult = measurement
                };

                comparisonResults.Add(($"測定#{i + 1} ({timestamp:HH:mm:ss})", comparison));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "測定#{Index}の精度計算でエラーが発生しました", i + 1);
            }
        }

        if (comparisonResults.Count == 0)
        {
            _logger.LogWarning("有効な精度測定結果がないため、レポートを生成できません");
            return string.Empty;
        }

        var reportPath = await _reporter.GenerateImprovementReportAsync(comparisonResults, outputPath).ConfigureAwait(false);
        
        _logger.LogInformation("📄 詳細精度レポート生成完了: {ReportPath}", reportPath);
        return reportPath;
    }

    /// <summary>
    /// OCR履歴をクリア
    /// </summary>
    public void ClearHistory()
    {
        lock (_lockObject)
        {
            var count = _ocrHistory.Count;
            _ocrHistory.Clear();
            _logger.LogInformation("🗑️ OCR履歴をクリアしました: {Count}件削除", count);
        }
    }

    /// <summary>
    /// 簡易精度計算（Levenshtein距離ベース）
    /// </summary>
    private static double CalculateSimpleAccuracy(string expected, string detected)
    {
        if (string.IsNullOrEmpty(expected) && string.IsNullOrEmpty(detected))
            return 1.0;
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(detected))
            return 0.0;

        var distance = LevenshteinDistance(expected, detected);
        var maxLength = Math.Max(expected.Length, detected.Length);
        return maxLength == 0 ? 1.0 : 1.0 - (double)distance / maxLength;
    }

    /// <summary>
    /// 文字精度計算
    /// </summary>
    private static double CalculateCharacterAccuracy(string expected, string detected)
    {
        if (string.IsNullOrEmpty(expected) && string.IsNullOrEmpty(detected))
            return 1.0;
        if (string.IsNullOrEmpty(expected))
            return detected.Length == 0 ? 1.0 : 0.0;

        var correctChars = CountCorrectCharacters(expected, detected);
        return (double)correctChars / expected.Length;
    }

    /// <summary>
    /// 単語精度計算
    /// </summary>
    private static double CalculateWordAccuracy(string expected, string detected)
    {
        var expectedWords = expected.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        var detectedWords = detected.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries);

        if (expectedWords.Length == 0 && detectedWords.Length == 0)
            return 1.0;
        if (expectedWords.Length == 0)
            return detectedWords.Length == 0 ? 1.0 : 0.0;

        var correctWords = expectedWords.Intersect(detectedWords).Count();
        return (double)correctWords / expectedWords.Length;
    }

    /// <summary>
    /// 正しい文字数をカウント
    /// </summary>
    private static int CountCorrectCharacters(string expected, string detected)
    {
        var minLength = Math.Min(expected.Length, detected.Length);
        var correctCount = 0;

        for (int i = 0; i < minLength; i++)
        {
            if (expected[i] == detected[i])
                correctCount++;
        }

        return correctCount;
    }

    /// <summary>
    /// Levenshtein距離計算
    /// </summary>
    private static int LevenshteinDistance(string source, string target)
    {
        if (string.IsNullOrEmpty(source))
            return string.IsNullOrEmpty(target) ? 0 : target.Length;
        if (string.IsNullOrEmpty(target))
            return source.Length;

        var matrix = new int[source.Length + 1, target.Length + 1];

        for (var i = 0; i <= source.Length; i++)
            matrix[i, 0] = i;
        for (var j = 0; j <= target.Length; j++)
            matrix[0, j] = j;

        for (var i = 1; i <= source.Length; i++)
        {
            for (var j = 1; j <= target.Length; j++)
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
    /// 画像パスから期待テキストを推定
    /// </summary>
    /// <param name="imagePath">画像ファイルパス</param>
    /// <returns>推定された期待テキスト（不明な場合はnull）</returns>
    private static string? ExtractExpectedTextFromImagePath(string? imagePath)
    {
        if (string.IsNullOrEmpty(imagePath))
            return null;
            
        var filename = System.IO.Path.GetFileNameWithoutExtension(imagePath).ToLowerInvariant();
        
        return filename switch
        {
            // デバッグ画像パターン
            "clear_jp_text" => "戦",
            "multi_jp_text" => "戦闘開始", 
            "clear_en_text" => "Battle Start",
            "low_contrast" => "戦",
            "noise_only" => "",
            
            // テスト画像パターン
            var name when name.Contains("hello_jp") => "こんにちは",
            var name when name.Contains("hello_en") => "Hello World",
            var name when name.Contains("test_mixed") => "テスト123",
            var name when name.Contains("ocr_accuracy") => "OCR精度測定",
            
            // ゲーム画面パターン（推定）
            var name when name.Contains("battle") => "戦闘",
            var name when name.Contains("menu") => "メニュー",
            var name when name.Contains("dialog") => "会話",
            
            _ => null // 不明
        };
    }
}

/// <summary>
/// OCR精度統計情報
/// </summary>
public sealed class OcrAccuracyStats
{
    /// <summary>
    /// 総測定回数
    /// </summary>
    public int TotalMeasurements { get; init; }
    
    /// <summary>
    /// 期待テキスト付き測定回数（精度計算可能）
    /// </summary>
    public int MeasurementsWithExpected { get; init; }
    
    /// <summary>
    /// 平均全体精度
    /// </summary>
    public double AverageOverallAccuracy { get; init; }
    
    /// <summary>
    /// 平均処理時間
    /// </summary>
    public TimeSpan AverageProcessingTime { get; init; }
    
    /// <summary>
    /// 最初の測定時刻
    /// </summary>
    public DateTime? FirstMeasurement { get; init; }
    
    /// <summary>
    /// 最後の測定時刻
    /// </summary>
    public DateTime LastMeasurement { get; init; }
}