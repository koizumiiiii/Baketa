using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece.Native.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Tests.Translation.Local.Onnx.SentencePiece;

/// <summary>
/// Python SentencePieceとの一致性検証テスト
/// 手動作成された検証データを使用してC#実装の正確性を確認
/// </summary>
public class SentencePieceVerificationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly Mock<ILogger<SentencePieceNormalizer>> _mockLogger;
    private readonly SentencePieceNormalizer _normalizer;
    private readonly string _verificationDataPath;
    private bool _disposed;

    public SentencePieceVerificationTests(ITestOutputHelper output)
    {
        _output = output;
        _mockLogger = new Mock<ILogger<SentencePieceNormalizer>>();
        _normalizer = new SentencePieceNormalizer(_mockLogger.Object);
        
        // 検証データファイルのパスを設定
        var projectRoot = GetProjectRootDirectory();
        _verificationDataPath = Path.Combine(projectRoot, "tests", "test_data", "normalization_verification.json");
    }

    [Fact]
    public void VerificationDataFile_ShouldExist()
    {
        // Act & Assert
        File.Exists(_verificationDataPath).Should().BeTrue(
            $"Verification data file should exist at: {_verificationDataPath}");
        
        _output.WriteLine($"✓ Verification data file found: {_verificationDataPath}");
    }

    [Fact]
    public void VerificationData_ShouldBeValidJson()
    {
        // Arrange
        if (!File.Exists(_verificationDataPath))
        {
            _output.WriteLine($"Skipping test: Verification data file not found at {_verificationDataPath}");
            return;
        }

        // Act
        var jsonContent = File.ReadAllText(_verificationDataPath);
        _output.WriteLine($"JSON content length: {jsonContent.Length}");
        _output.WriteLine($"JSON content preview: {jsonContent[..Math.Min(200, jsonContent.Length)]}...");
        
        var action = () => JsonSerializer.Deserialize<VerificationData>(jsonContent);

        // Assert
        action.Should().NotThrow("Verification data should be valid JSON");
        
        var data = JsonSerializer.Deserialize<VerificationData>(jsonContent);
        _output.WriteLine($"Deserialized data: {data}");
        _output.WriteLine($"TestCases property: {data?.TestCases}");
        
        data.Should().NotBeNull();
        data!.TestCases.Should().NotBeNull().And.NotBeEmpty();
        
        _output.WriteLine($"✓ Verification data loaded: {data.TestCases.Count} test cases");
    }

    [Theory]
    [MemberData(nameof(GetVerificationTestCases))]
    public void Normalizer_ShouldMatchExpectedResults_ForVerificationCases(VerificationTestCase testCase)
    {
        // Arrange
        _output.WriteLine($"Testing: {testCase.TestCaseId} ({testCase.Category})");
        _output.WriteLine($"  Input: '{testCase.Input}'");
        _output.WriteLine($"  Expected: '{testCase.ExpectedNormalized}'");

        // Act
        var actualResult = _normalizer.Normalize(testCase.Input);

        // Assert
        actualResult.Should().Be(testCase.ExpectedNormalized,
            $"Normalization result should match expected for test case: {testCase.TestCaseId}");

        _output.WriteLine($"  Actual: '{actualResult}'");
        _output.WriteLine($"  ✓ Match: {actualResult == testCase.ExpectedNormalized}");
    }

    [Fact]
    public void Normalizer_VerificationSummary_ShouldShowResults()
    {
        // Arrange
        if (!File.Exists(_verificationDataPath))
        {
            _output.WriteLine("Skipping test: Verification data file not found");
            return;
        }

        var verificationData = LoadVerificationData();
        if (verificationData?.TestCases == null)
        {
            _output.WriteLine("Skipping test: No test cases found");
            return;
        }

        // Act
        var results = new List<(string TestCaseId, bool IsMatch, string Input, string Expected, string Actual)>();
        
        foreach (var testCase in verificationData.TestCases)
        {
            var actualResult = _normalizer.Normalize(testCase.Input);
            var isMatch = actualResult == testCase.ExpectedNormalized;
            
            results.Add((testCase.TestCaseId, isMatch, testCase.Input, testCase.ExpectedNormalized, actualResult));
        }

        // Assert & Report
        var totalCases = results.Count;
        var matchingCases = results.Count(r => r.IsMatch);
        var mismatchCases = results.Where(r => !r.IsMatch).ToList();

        _output.WriteLine($"📊 Verification Summary:");
        _output.WriteLine($"  Total test cases: {totalCases}");
        _output.WriteLine($"  Matching cases: {matchingCases}");
        _output.WriteLine($"  Mismatched cases: {mismatchCases.Count}");
        _output.WriteLine($"  Accuracy: {(double)matchingCases / totalCases:P2}");

        if (mismatchCases.Count > 0)
        {
            _output.WriteLine($"\n❌ Mismatched cases:");
            foreach (var (testCaseId, _, input, expected, actual) in mismatchCases)
            {
                _output.WriteLine($"  {testCaseId}:");
                _output.WriteLine($"    Input: '{input}'");
                _output.WriteLine($"    Expected: '{expected}'");
                _output.WriteLine($"    Actual: '{actual}'");
            }
        }

        // テストの成功条件: 少なくとも80%の一致率
        var accuracy = (double)matchingCases / totalCases;
        accuracy.Should().BeGreaterThanOrEqualTo(0.8, 
            "At least 80% of test cases should match expected results");
    }

    [Fact]
    public void Normalizer_CategoryWiseAnalysis_ShouldShowBreakdown()
    {
        // Arrange
        if (!File.Exists(_verificationDataPath))
        {
            _output.WriteLine("Skipping test: Verification data file not found");
            return;
        }

        var verificationData = LoadVerificationData();
        if (verificationData?.TestCases == null) return;

        // Act
        var categoryResults = verificationData.TestCases
            .GroupBy(tc => tc.Category)
            .Select(g => new
            {
                Category = g.Key,
                Total = g.Count(),
                Matches = g.Count(tc => _normalizer.Normalize(tc.Input) == tc.ExpectedNormalized)
            })
            .ToList();

        // Assert & Report
        _output.WriteLine($"📈 Category-wise Analysis:");
        
        foreach (var category in categoryResults)
        {
            var accuracy = (double)category.Matches / category.Total;
            _output.WriteLine($"  {category.Category}: {category.Matches}/{category.Total} ({accuracy:P1})");
        }

        // すべてのカテゴリで50%以上の一致率を期待
        foreach (var category in categoryResults)
        {
            var accuracy = (double)category.Matches / category.Total;
            accuracy.Should().BeGreaterThanOrEqualTo(0.5,
                $"Category '{category.Category}' should have at least 50% accuracy");
        }
    }

    public static TheoryData<VerificationTestCase> GetVerificationTestCases()
    {
        var data = new TheoryData<VerificationTestCase>();
        var verificationData = LoadVerificationDataSafe();
        
        if (verificationData?.TestCases == null) 
            return data;

        foreach (var testCase in verificationData.TestCases)
        {
            data.Add(testCase);
        }
        
        return data;
    }

    private static VerificationData? LoadVerificationDataSafe()
    {
        try
        {
            return LoadVerificationData();
        }
        catch
        {
            return null;
        }
    }

    private static VerificationData? LoadVerificationData()
    {
        var projectRoot = GetProjectRootDirectory();
        var verificationDataPath = Path.Combine(projectRoot, "tests", "test_data", "normalization_verification.json");

        if (!File.Exists(verificationDataPath))
            return null;

        var jsonContent = File.ReadAllText(verificationDataPath);
        return JsonSerializer.Deserialize<VerificationData>(jsonContent);
    }

    private static string GetProjectRootDirectory()
    {
        var currentDir = Directory.GetCurrentDirectory();
        while (currentDir != null && !File.Exists(Path.Combine(currentDir, "Baketa.sln")))
        {
            currentDir = Directory.GetParent(currentDir)?.FullName;
        }
        return currentDir ?? throw new DirectoryNotFoundException("Project root not found");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _normalizer?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}

#region Verification Data Models

/// <summary>
/// 検証データのルートオブジェクト
/// </summary>
public record VerificationData(
    [property: JsonPropertyName("generated_by")] string GeneratedBy,
    [property: JsonPropertyName("test_cases")] List<VerificationTestCase> TestCases,
    [property: JsonPropertyName("statistics")] VerificationStatistics Statistics
);

/// <summary>
/// 個別の検証テストケース
/// </summary>
public record VerificationTestCase(
    [property: JsonPropertyName("test_case_id")] string TestCaseId,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("input")] string Input,
    [property: JsonPropertyName("expected_normalized")] string ExpectedNormalized,
    [property: JsonPropertyName("steps")] VerificationSteps Steps
);

/// <summary>
/// 正規化処理のステップ詳細
/// </summary>
public record VerificationSteps(
    [property: JsonPropertyName("nfkc")] string Nfkc,
    [property: JsonPropertyName("control_filtered")] string ControlFiltered,
    [property: JsonPropertyName("whitespace_normalized")] string WhitespaceNormalized,
    [property: JsonPropertyName("final")] string Final
);

/// <summary>
/// 検証統計情報
/// </summary>
public record VerificationStatistics(
    [property: JsonPropertyName("total_cases")] int TotalCases,
    [property: JsonPropertyName("categories")] List<string> Categories
);

#endregion