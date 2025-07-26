using Baketa.Core.Abstractions.Factories;
using Baketa.Core.Abstractions.OCR;
using Baketa.Infrastructure.OCR.Measurement;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Tests.OCR.Measurement;

/// <summary>
/// OCR精度改善効果のベンチマークテスト
/// </summary>
public sealed class OcrAccuracyBenchmarkTests
{
    private readonly ITestOutputHelper _output;
    private readonly Mock<IImageFactory> _mockImageFactory;
    private readonly Mock<ILogger<OcrAccuracyMeasurement>> _mockMeasurementLogger;
    private readonly Mock<ILogger<AccuracyBenchmarkService>> _mockBenchmarkLogger;
    private readonly Mock<ILogger<TestImageGenerator>> _mockGeneratorLogger;

    public OcrAccuracyBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
        _mockImageFactory = new Mock<IImageFactory>();
        _mockMeasurementLogger = new Mock<ILogger<OcrAccuracyMeasurement>>();
        _mockBenchmarkLogger = new Mock<ILogger<AccuracyBenchmarkService>>();
        _mockGeneratorLogger = new Mock<ILogger<TestImageGenerator>>();
    }

    [Fact]
    public async Task GenerateTestImages_CreatesAllRequiredTestCases()
    {
        // Arrange
        var generator = new TestImageGenerator(_mockGeneratorLogger.Object);
        var testDataDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BaketaOcrTest", Guid.NewGuid().ToString());

        try
        {
            // Act
            var testCases = await generator.GenerateTestCasesAsync(testDataDir);

            // Assert
            Assert.NotEmpty(testCases);
            Assert.True(testCases.Count >= 10, $"期待: 10件以上のテストケース, 実際: {testCases.Count}件");

            // すべてのテストケースが適切に生成されていることを確認
            foreach (var (imagePath, expectedText) in testCases)
            {
                // ダミーファイル(.txt)の存在確認
                var dummyFile = imagePath + ".txt";
                Assert.True(System.IO.File.Exists(dummyFile), $"ダミーファイルが存在しません: {dummyFile}");
                Assert.False(string.IsNullOrEmpty(expectedText), "期待テキストが空です");
                
                // ダミーファイルの内容確認
                var content = await System.IO.File.ReadAllTextAsync(dummyFile);
                Assert.Contains(expectedText, content);
                
                _output.WriteLine($"✅ テストケース: {System.IO.Path.GetFileName(imagePath)} -> '{expectedText}'");
            }
        }
        finally
        {
            // クリーンアップ
            if (System.IO.Directory.Exists(testDataDir))
            {
                System.IO.Directory.Delete(testDataDir, true);
            }
        }
    }

    [Fact]
    public async Task AccuracyMeasurement_CalculatesCorrectMetrics()
    {
        // Arrange
        var measurement = new OcrAccuracyMeasurement(_mockImageFactory.Object, _mockMeasurementLogger.Object);

        // 完全一致のケース
        var exactMatch = await TestAccuracyCalculation("Hello World", "Hello World");
        Assert.Equal(1.0, exactMatch.OverallAccuracy, 2);
        Assert.Equal(1.0, exactMatch.CharacterAccuracy, 2);
        Assert.Equal(1.0, exactMatch.WordAccuracy, 2);

        // 部分一致のケース
        var partialMatch = await TestAccuracyCalculation("Hello World", "Hello Wold"); // 'r'が欠落
        Assert.True(partialMatch.OverallAccuracy > 0.8, $"部分一致精度が低すぎます: {partialMatch.OverallAccuracy}");
        Assert.True(partialMatch.CharacterAccuracy > 0.7, $"文字精度が低すぎます: {partialMatch.CharacterAccuracy}");
        
        // 完全不一致のケース
        var noMatch = await TestAccuracyCalculation("Hello World", "Goodbye");
        Assert.True(noMatch.OverallAccuracy < 0.5, $"不一致の精度が高すぎます: {noMatch.OverallAccuracy}");

        _output.WriteLine($"完全一致: {exactMatch.OverallAccuracy:P2}");
        _output.WriteLine($"部分一致: {partialMatch.OverallAccuracy:P2}");
        _output.WriteLine($"完全不一致: {noMatch.OverallAccuracy:P2}");
    }

    [Fact]
    public void AccuracyComparison_DetectsSignificantImprovement()
    {
        // Arrange
        var baselineResult = new AccuracyMeasurementResult
        {
            OverallAccuracy = 0.7,
            ProcessingTime = TimeSpan.FromMilliseconds(1000),
            SettingsHash = "baseline"
        };

        var improvedResult = new AccuracyMeasurementResult
        {
            OverallAccuracy = 0.85, // 15%改善
            ProcessingTime = TimeSpan.FromMilliseconds(1100), // 10%遅化
            SettingsHash = "improved"
        };

        // Act
        var comparison = new AccuracyComparisonResult
        {
            BaselineResult = baselineResult,
            ImprovedResult = improvedResult
        };

        // Assert
        Assert.Equal(0.15, comparison.AccuracyImprovement, 2);
        Assert.Equal(0.1, comparison.ProcessingTimeChange, 2);
        Assert.True(comparison.IsSignificantImprovement, "5%以上の改善が検出されるべきです");

        _output.WriteLine($"精度改善: {comparison.AccuracyImprovement:+0.00%;-0.00%;+0.00%}");
        _output.WriteLine($"処理時間変化: {comparison.ProcessingTimeChange:+0.00%;-0.00%;+0.00%}");
        _output.WriteLine($"有意な改善: {comparison.IsSignificantImprovement}");
    }

    [Fact]
    public async Task BenchmarkService_ProvidesMeaningfulTestCases()
    {
        // Arrange
        var accuracyMeasurement = new OcrAccuracyMeasurement(_mockImageFactory.Object, _mockMeasurementLogger.Object);
        var benchmarkService = new AccuracyBenchmarkService(accuracyMeasurement, _mockBenchmarkLogger.Object);

        // Act
        var gameTestCases = benchmarkService.GetGameTextTestCases();

        // Assert
        Assert.NotEmpty(gameTestCases);
        Assert.True(gameTestCases.Count >= 8, $"期待: 8件以上のゲームテストケース, 実際: {gameTestCases.Count}件");

        // 日本語・英語・数字・混合テキストが含まれていることを確認
        var texts = gameTestCases.Select(tc => tc.ExpectedText).ToList();
        
        Assert.Contains(texts, text => ContainsJapanese(text));
        Assert.Contains(texts, text => ContainsEnglish(text));
        Assert.Contains(texts, text => ContainsNumbers(text));

        foreach (var (imagePath, expectedText) in gameTestCases)
        {
            _output.WriteLine($"🎮 ゲームテストケース: {System.IO.Path.GetFileName(imagePath)} -> '{expectedText}'");
        }
    }

    private async Task<AccuracyMeasurementResult> TestAccuracyCalculation(string expected, string detected)
    {
        // プライベートメソッドのテスト用 - リフレクションを使用
        var measurement = new OcrAccuracyMeasurement(_mockImageFactory.Object, _mockMeasurementLogger.Object);
        var type = typeof(OcrAccuracyMeasurement);
        
        var calculateAccuracyMethod = type.GetMethod("CalculateAccuracy", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var calculateCharAccuracyMethod = type.GetMethod("CalculateCharacterAccuracy", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var calculateWordAccuracyMethod = type.GetMethod("CalculateWordAccuracy", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var overallAccuracy = (double)calculateAccuracyMethod!.Invoke(null, new object[] { expected, detected })!;
        var charAccuracy = (double)calculateCharAccuracyMethod!.Invoke(null, new object[] { expected, detected })!;
        var wordAccuracy = (double)calculateWordAccuracyMethod!.Invoke(null, new object[] { expected, detected })!;

        return new AccuracyMeasurementResult
        {
            OverallAccuracy = overallAccuracy,
            CharacterAccuracy = charAccuracy,
            WordAccuracy = wordAccuracy,
            DetectedCharacterCount = detected.Length,
            CorrectCharacterCount = Math.Max(0, Math.Min(expected.Length, detected.Length) - LevenshteinDistance(expected, detected)),
            ExpectedCharacterCount = expected.Length,
            ProcessingTime = TimeSpan.FromMilliseconds(100),
            AverageConfidence = 0.9,
            SettingsHash = "test"
        };
    }

    private static bool ContainsJapanese(string text)
    {
        return text.Any(c => (c >= 0x3040 && c <= 0x309F) || // ひらがな
                            (c >= 0x30A0 && c <= 0x30FF) || // カタカナ
                            (c >= 0x4E00 && c <= 0x9FAF));  // 漢字
    }

    private static bool ContainsEnglish(string text)
    {
        return text.Any(c => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'));
    }

    private static bool ContainsNumbers(string text)
    {
        return text.Any(char.IsDigit);
    }

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
}

/// <summary>
/// 実際のOCRエンジンを使用した統合ベンチマークテスト
/// </summary>
[Collection("OCR Integration Tests")]
public sealed class OcrAccuracyIntegrationBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public OcrAccuracyIntegrationBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(Skip = "実際のOCRエンジンが必要なため通常はスキップ")]
    public async Task MeasureActualOcrImprovements_WithRealEngine()
    {
        // このテストは実際のPaddleOCRエンジンが利用可能な場合にのみ実行
        // 継続的インテグレーションでは通常スキップされる
        
        _output.WriteLine("⚠️ 実際のOCRエンジンを使用した統合テストは手動実行が必要です");
        _output.WriteLine("手動実行方法:");
        _output.WriteLine("1. PaddleOCRモデルが適切にセットアップされていることを確認");
        _output.WriteLine("2. [Fact(Skip = \"...\")] の Skip 属性を削除");
        _output.WriteLine("3. テストを個別実行");
        
        await Task.CompletedTask;
    }
}