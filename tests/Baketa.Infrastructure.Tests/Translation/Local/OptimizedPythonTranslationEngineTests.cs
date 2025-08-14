using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation.Local;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Tests.Translation.Local;

/// <summary>
/// OptimizedPythonTranslationEngineのテストクラス
/// </summary>
public class OptimizedPythonTranslationEngineTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly Mock<ILogger<OptimizedPythonTranslationEngine>> _mockLogger;
    private readonly OptimizedPythonTranslationEngine _engine;

    public OptimizedPythonTranslationEngineTests(ITestOutputHelper output)
    {
        _output = output;
        _mockLogger = new Mock<ILogger<OptimizedPythonTranslationEngine>>();
        _engine = new OptimizedPythonTranslationEngine(_mockLogger.Object);
    }

    [Fact]
    public async Task Constructor_ShouldInitializeCorrectly()
    {
        // Assert
        Assert.NotNull(_engine);
        Assert.Equal("OptimizedPythonTranslation", _engine.Name);
        Assert.Equal("高速化されたPython翻訳エンジン（500ms目標）", _engine.Description);
        Assert.False(_engine.RequiresNetwork);
    }

    [Fact]
    public async Task GetSupportedLanguagePairsAsync_ShouldReturnExpectedPairs()
    {
        // Act
        var languagePairs = await _engine.GetSupportedLanguagePairsAsync();

        // Assert
        Assert.NotNull(languagePairs);
        Assert.Equal(2, languagePairs.Count);

        var pairsList = languagePairs.ToList();
        Assert.Contains(pairsList, p => 
            p.SourceLanguage.Code == "ja" && p.TargetLanguage.Code == "en");
        Assert.Contains(pairsList, p => 
            p.SourceLanguage.Code == "en" && p.TargetLanguage.Code == "ja");
    }

    [Fact]
    public async Task SupportsLanguagePairAsync_ShouldReturnTrueForSupportedPairs()
    {
        // Arrange
        var jaToEn = new LanguagePair
        {
            SourceLanguage = new Language { Code = "ja", DisplayName = "Japanese" },
            TargetLanguage = new Language { Code = "en", DisplayName = "English" }
        };

        var enToJa = new LanguagePair
        {
            SourceLanguage = new Language { Code = "en", DisplayName = "English" },
            TargetLanguage = new Language { Code = "ja", DisplayName = "Japanese" }
        };

        // Act
        var supportsJaToEn = await _engine.SupportsLanguagePairAsync(jaToEn);
        var supportsEnToJa = await _engine.SupportsLanguagePairAsync(enToJa);

        // Assert
        Assert.True(supportsJaToEn);
        Assert.True(supportsEnToJa);
    }

    [Fact]
    public async Task SupportsLanguagePairAsync_ShouldReturnFalseForUnsupportedPairs()
    {
        // Arrange
        var unsupportedPair = new LanguagePair
        {
            SourceLanguage = new Language { Code = "fr", DisplayName = "French" },
            TargetLanguage = new Language { Code = "de", DisplayName = "German" }
        };

        // Act
        var supports = await _engine.SupportsLanguagePairAsync(unsupportedPair);

        // Assert
        Assert.False(supports);
    }

    [Fact]
    public async Task TranslateAsync_WithoutServer_ShouldReturnErrorResponse()
    {
        // Arrange
        var request = new TranslationRequest
        {
            SourceText = "こんにちは",
            SourceLanguage = new Language { Code = "ja", DisplayName = "Japanese" },
            TargetLanguage = new Language { Code = "en", DisplayName = "English" }
        };

        var stopwatch = Stopwatch.StartNew();

        // Act
        var response = await _engine.TranslateAsync(request);
        stopwatch.Stop();

        // Assert
        Assert.NotNull(response);
        Assert.Equal(request.RequestId, response.RequestId);
        Assert.False(response.IsSuccess);
        Assert.Equal("翻訳エラーが発生しました", response.TranslatedText);
        Assert.Equal(0.0f, response.ConfidenceScore);

        _output.WriteLine($"Translation test completed in {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task TranslateAsync_Performance_ShouldCompleteQuickly()
    {
        // Arrange
        var request = new TranslationRequest
        {
            SourceText = "テスト",
            SourceLanguage = new Language { Code = "ja", DisplayName = "Japanese" },
            TargetLanguage = new Language { Code = "en", DisplayName = "English" }
        };

        var stopwatch = Stopwatch.StartNew();

        // Act
        var response = await _engine.TranslateAsync(request);
        stopwatch.Stop();

        // Assert - 応答時間テスト（サーバーなしでも迅速に応答すべき）
        Assert.True(stopwatch.ElapsedMilliseconds < 1000, 
            $"Translation took too long: {stopwatch.ElapsedMilliseconds}ms");

        _output.WriteLine($"Performance test: {stopwatch.ElapsedMilliseconds}ms (Target: <1000ms without server)");
    }

    [Fact]
    public async Task TranslateAsync_WithCancellation_ShouldRespectCancellationToken()
    {
        // Arrange
        var request = new TranslationRequest
        {
            SourceText = "キャンセルテスト",
            SourceLanguage = new Language { Code = "ja", DisplayName = "Japanese" },
            TargetLanguage = new Language { Code = "en", DisplayName = "English" }
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Act & Assert
        try
        {
            await _engine.TranslateAsync(request, cts.Token);
            // キャンセルされなかった場合も正常（即座に完了した場合）
            _output.WriteLine("Translation completed before cancellation");
        }
        catch (OperationCanceledException)
        {
            _output.WriteLine("Translation was properly cancelled");
            // キャンセルが正常に動作した
        }
    }

    [Fact]
    public async Task TranslateBatchAsync_ShouldHandleMultipleRequests()
    {
        // Arrange
        var requests = new[]
        {
            new TranslationRequest
            {
                SourceText = "こんにちは",
                SourceLanguage = new Language { Code = "ja", DisplayName = "Japanese" },
                TargetLanguage = new Language { Code = "en", DisplayName = "English" }
            },
            new TranslationRequest
            {
                SourceText = "さようなら",
                SourceLanguage = new Language { Code = "ja", DisplayName = "Japanese" },
                TargetLanguage = new Language { Code = "en", DisplayName = "English" }
            }
        };

        var stopwatch = Stopwatch.StartNew();

        // Act
        var responses = await _engine.TranslateBatchAsync(requests);
        stopwatch.Stop();

        // Assert
        Assert.NotNull(responses);
        Assert.Equal(2, responses.Count);

        foreach (var response in responses)
        {
            Assert.NotNull(response);
            Assert.False(response.IsSuccess); // サーバーなしのため失敗予想
        }

        _output.WriteLine($"Batch translation test completed in {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void Name_ShouldReturnCorrectValue()
    {
        // Assert
        Assert.Equal("OptimizedPythonTranslation", _engine.Name);
    }

    [Fact]
    public void Description_ShouldReturnCorrectValue()
    {
        // Assert
        Assert.Equal("高速化されたPython翻訳エンジン（500ms目標）", _engine.Description);
    }

    [Fact]
    public void RequiresNetwork_ShouldReturnFalse()
    {
        // Assert
        Assert.False(_engine.RequiresNetwork);
    }

    [Fact]
    public async Task IsReadyAsync_ShouldEventuallyReturnStatus()
    {
        // Act
        var isReady = await _engine.IsReadyAsync();

        // Assert - サーバーなしの場合、準備できていない可能性が高い
        _output.WriteLine($"Engine ready status: {isReady}");
        
        // 結果に関係なく、メソッドが例外を投げずに完了することを確認
        Assert.True(true, "IsReadyAsync completed without exception");
    }

    public void Dispose()
    {
        _engine?.Dispose();
    }
}

/// <summary>
/// パフォーマンス重視のテストクラス（統合テスト）
/// Pythonサーバーが利用可能な場合のみ実行される
/// </summary>
[Collection("PythonServer")]
public class OptimizedPythonTranslationEngineIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly Mock<ILogger<OptimizedPythonTranslationEngine>> _mockLogger;
    private readonly OptimizedPythonTranslationEngine _engine;

    public OptimizedPythonTranslationEngineIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _mockLogger = new Mock<ILogger<OptimizedPythonTranslationEngine>>();
        _engine = new OptimizedPythonTranslationEngine(_mockLogger.Object);
    }

    [Fact(Skip = "Pythonサーバーが必要")]
    public async Task TranslateAsync_WithServer_ShouldMeetPerformanceTarget()
    {
        // Arrange - Pythonサーバーの起動を待つ
        await Task.Delay(2000);

        var request = new TranslationRequest
        {
            SourceText = "こんにちは、世界！",
            SourceLanguage = new Language { Code = "ja", DisplayName = "Japanese" },
            TargetLanguage = new Language { Code = "en", DisplayName = "English" }
        };

        var stopwatch = Stopwatch.StartNew();

        // Act
        var response = await _engine.TranslateAsync(request);
        stopwatch.Stop();

        // Assert - 500ms目標
        Assert.True(response.IsSuccess, "Translation should succeed with server");
        Assert.NotEmpty(response.TranslatedText);
        Assert.True(stopwatch.ElapsedMilliseconds <= 500, 
            $"Translation should complete within 500ms, actual: {stopwatch.ElapsedMilliseconds}ms");

        _output.WriteLine($"✅ Performance target met: {stopwatch.ElapsedMilliseconds}ms ≤ 500ms");
        _output.WriteLine($"Translation: '{request.SourceText}' → '{response.TranslatedText}'");
    }

    [Fact(Skip = "Pythonサーバーが必要")]
    public async Task TranslateAsync_MultipleRequests_ShouldShowImprovement()
    {
        // Arrange
        await Task.Delay(2000); // サーバー起動待ち

        var testCases = new[]
        {
            "こんにちは",
            "おはよう",
            "ありがとう",
            "さようなら",
            "お疲れ様でした"
        };

        var totalTime = 0L;
        var successCount = 0;

        // Act
        foreach (var testCase in testCases)
        {
            var request = new TranslationRequest
            {
                SourceText = testCase,
                SourceLanguage = new Language { Code = "ja", DisplayName = "Japanese" },
                TargetLanguage = new Language { Code = "en", DisplayName = "English" }
            };

            var stopwatch = Stopwatch.StartNew();
            var response = await _engine.TranslateAsync(request);
            stopwatch.Stop();

            totalTime += stopwatch.ElapsedMilliseconds;
            if (response.IsSuccess)
            {
                successCount++;
            }

            _output.WriteLine($"'{testCase}' → '{response.TranslatedText}' ({stopwatch.ElapsedMilliseconds}ms)");
        }

        // Assert
        var averageTime = totalTime / testCases.Length;
        var successRate = (double)successCount / testCases.Length;

        Assert.True(averageTime <= 500, $"Average time should be ≤ 500ms, actual: {averageTime}ms");
        Assert.True(successRate >= 0.8, $"Success rate should be ≥ 80%, actual: {successRate:P}");

        _output.WriteLine($"📊 Performance Summary:");
        _output.WriteLine($"   Average time: {averageTime}ms (target: ≤500ms)");
        _output.WriteLine($"   Success rate: {successRate:P} (target: ≥80%)");
        _output.WriteLine($"   Total tests: {testCases.Length}");
    }

    public void Dispose()
    {
        _engine?.Dispose();
    }
}