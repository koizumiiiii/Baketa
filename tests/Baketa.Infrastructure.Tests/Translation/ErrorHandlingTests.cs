using System;
using System.IO;
using System.Threading.Tasks;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece.Native.ErrorHandling;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Tests.Translation;

/// <summary>
/// エラーハンドリング強化機能テスト
/// 堅牢性、回復能力、監視機能の検証
/// </summary>
public class ErrorHandlingTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _projectRoot;
    private bool _disposed;

    public ErrorHandlingTests(ITestOutputHelper output)
    {
        _output = output;
        _projectRoot = GetProjectRootDirectory();
    }

    [Fact]
    public async Task RobustErrorHandler_ShouldRetryOnTransientErrors()
    {
        // Arrange
        var logger = NullLogger<RobustErrorHandler>.Instance;
        var policy = ErrorHandlingPolicy.CreateDefault();
        using var errorHandler = new RobustErrorHandler(logger, policy);

        var attemptCount = 0;
        var expectedAttempts = 3;

        _output.WriteLine($"🔄 リトライ機能テスト開始");

        // Act & Assert
        var result = await errorHandler.ExecuteWithRetryAsync(async () =>
        {
            attemptCount++;
            _output.WriteLine($"  試行 {attemptCount}: ");

            if (attemptCount < expectedAttempts)
            {
                _output.WriteLine($"    一時的エラーをシミュレート");
                throw new InvalidOperationException($"Simulated transient error (attempt {attemptCount})");
            }

            _output.WriteLine($"    成功");
            return await Task.FromResult($"Success on attempt {attemptCount}");
        }, "RetryTest");

        // Assert
        result.Should().Be($"Success on attempt {expectedAttempts}");
        attemptCount.Should().Be(expectedAttempts);
        
        var stats = errorHandler.GetCurrentStatistics();
        stats.TotalErrors.Should().Be(expectedAttempts - 1); // 成功した試行以外
        stats.Recoveries.Should().Be(1); // 最終的に成功

        _output.WriteLine($"📊 統計: {stats}");
        _output.WriteLine($"✅ リトライ機能テスト成功");
    }

    [Fact]
    public async Task RobustErrorHandler_ShouldExecuteFallbackOnPrimaryFailure()
    {
        // Arrange
        var logger = NullLogger<RobustErrorHandler>.Instance;
        using var errorHandler = new RobustErrorHandler(logger);

        var primaryCalled = false;
        var fallbackCalled = false;

        _output.WriteLine($"🔀 フォールバック機能テスト開始");

        // Act
        var result = await errorHandler.ExecuteWithFallbackAsync(
            primaryAction: async () =>
            {
                primaryCalled = true;
                _output.WriteLine($"  プライマリ操作実行 → 失敗をシミュレート");
                throw new InvalidOperationException("Primary operation failed");
            },
            fallbackAction: async () =>
            {
                fallbackCalled = true;
                _output.WriteLine($"  フォールバック操作実行 → 成功");
                return await Task.FromResult("Fallback success");
            },
            "FallbackTest"
        );

        // Assert
        primaryCalled.Should().BeTrue("プライマリ操作が呼ばれるべき");
        fallbackCalled.Should().BeTrue("フォールバック操作が呼ばれるべき");
        result.Should().Be("Fallback success");

        var stats = errorHandler.GetCurrentStatistics();
        stats.Fallbacks.Should().Be(1);

        _output.WriteLine($"📊 統計: {stats}");
        _output.WriteLine($"✅ フォールバック機能テスト成功");
    }

    [Fact]
    public void RobustErrorHandler_ShouldNotRetryNonRetryableExceptions()
    {
        // Arrange
        var logger = NullLogger<RobustErrorHandler>.Instance;
        var policy = ErrorHandlingPolicy.CreateDefault();
        using var errorHandler = new RobustErrorHandler(logger, policy);

        var attemptCount = 0;

        _output.WriteLine($"🚫 非リトライ例外テスト開始");

        // Act & Assert
        var action = () => errorHandler.ExecuteWithRetry<string>(() =>
        {
            attemptCount++;
            _output.WriteLine($"  試行 {attemptCount}: ArgumentException スロー");
            throw new ArgumentException("Non-retryable exception");
        }, "NonRetryableTest");

        action.Should().Throw<TokenizerOperationException>()
            .WithInnerException<ArgumentException>();

        attemptCount.Should().Be(1, "非リトライ例外は1回だけ実行されるべき");

        _output.WriteLine($"✅ 非リトライ例外テスト成功");
    }

    [Fact]
    public async Task ResilientOpusMtTokenizer_ShouldHandleBasicOperations()
    {
        // Arrange
        var modelPath = Path.Combine(_projectRoot, "Models", "SentencePiece", "opus-mt-ja-en.model");
        
        if (!File.Exists(modelPath))
        {
            _output.WriteLine($"⚠️  Skipping test: Model file not found at {modelPath}");
            return;
        }

        _output.WriteLine($"🛡️ 堅牢なトークナイザー基本操作テスト");
        _output.WriteLine($"📂 Model: {Path.GetFileName(modelPath)}");

        // Act
        using var resilientTokenizer = await ResilientOpusMtTokenizer.CreateResilientAsync(
            modelPath, 
            ErrorHandlingPolicy.CreateDefault(), 
            enableFallback: true);

        // Assert
        resilientTokenizer.Should().NotBeNull();
        resilientTokenizer.IsInitialized.Should().BeTrue();
        resilientTokenizer.VocabularySize.Should().BeGreaterThan(0);

        _output.WriteLine($"✅ トークナイザー作成成功");
        _output.WriteLine($"  TokenizerId: {resilientTokenizer.TokenizerId}");
        _output.WriteLine($"  VocabularySize: {resilientTokenizer.VocabularySize:N0}");

        // 基本的なトークン化テスト
        var testTexts = new[]
        {
            "こんにちは、世界！",
            "テストメッセージ",
            "",
            "英語とにほんご混合text"
        };

        foreach (var text in testTexts)
        {
            var tokens = resilientTokenizer.Tokenize(text);
            var decoded = resilientTokenizer.Decode(tokens);

            tokens.Should().NotBeNull();
            decoded.Should().NotBeNull();

            _output.WriteLine($"  📝 '{text}' → [{string.Join(", ", tokens)}]");
            _output.WriteLine($"      デコード: '{decoded}'");
        }

        _output.WriteLine($"✅ 基本操作テスト成功");
    }

    [Fact]
    public async Task ResilientOpusMtTokenizer_HealthCheck_ShouldProvideStatusInformation()
    {
        // Arrange
        var modelPath = Path.Combine(_projectRoot, "Models", "SentencePiece", "opus-mt-ja-en.model");
        
        if (!File.Exists(modelPath))
        {
            _output.WriteLine($"⚠️  Skipping test: Model file not found at {modelPath}");
            return;
        }

        _output.WriteLine($"🏥 ヘルスチェック機能テスト");

        using var resilientTokenizer = await ResilientOpusMtTokenizer.CreateResilientAsync(
            modelPath, ErrorHandlingPolicy.CreateDefault());

        // Act
        var healthResult = await resilientTokenizer.PerformHealthCheckAsync();

        // Assert
        healthResult.Should().NotBeNull();
        healthResult.IsHealthy.Should().BeTrue();
        healthResult.Message.Should().NotBeNullOrEmpty();
        healthResult.TokenCount.Should().BeGreaterThan(0);

        _output.WriteLine($"📊 ヘルスチェック結果:");
        _output.WriteLine($"  健康状態: {(healthResult.IsHealthy ? "正常" : "異常")}");
        _output.WriteLine($"  メッセージ: {healthResult.Message}");
        _output.WriteLine($"  テストテキスト: '{healthResult.TestText}'");
        _output.WriteLine($"  トークン数: {healthResult.TokenCount}");
        _output.WriteLine($"  デコード結果: '{healthResult.DecodedText}'");
        _output.WriteLine($"  応答時間: {healthResult.ResponseTime.TotalMilliseconds:F1}ms");

        _output.WriteLine($"✅ ヘルスチェック機能テスト成功");
    }

    [Fact]
    public async Task ResilientOpusMtTokenizer_Report_ShouldProvideDetailedStatistics()
    {
        // Arrange
        var modelPath = Path.Combine(_projectRoot, "Models", "SentencePiece", "opus-mt-ja-en.model");
        
        if (!File.Exists(modelPath))
        {
            _output.WriteLine($"⚠️  Skipping test: Model file not found at {modelPath}");
            return;
        }

        _output.WriteLine($"📋 レポート機能テスト");

        using var resilientTokenizer = await ResilientOpusMtTokenizer.CreateResilientAsync(
            modelPath, ErrorHandlingPolicy.CreateDefault());

        // いくつかの操作を実行してから統計を確認
        resilientTokenizer.Tokenize("テスト1");
        resilientTokenizer.Tokenize("テスト2");
        resilientTokenizer.Decode([1, 2, 3]);

        // Act
        var report = resilientTokenizer.GetReport();

        // Assert
        report.Should().NotBeNull();
        report.TokenizerId.Should().NotBeNullOrEmpty();
        report.IsInitialized.Should().BeTrue();
        report.VocabularySize.Should().BeGreaterThan(0);
        report.ModelPath.Should().Be(modelPath);

        _output.WriteLine($"📊 トークナイザーレポート:");
        _output.WriteLine($"  ID: {report.TokenizerId}");
        _output.WriteLine($"  初期化済み: {report.IsInitialized}");
        _output.WriteLine($"  語彙サイズ: {report.VocabularySize:N0}");
        _output.WriteLine($"  フォールバック有効: {report.HasFallback}");
        _output.WriteLine($"  モデルパス: {report.ModelPath}");
        _output.WriteLine($"  エラー統計: {report.ErrorStatistics}");

        _output.WriteLine($"✅ レポート機能テスト成功");
    }

    [Fact]
    public async Task ErrorHandlingPolicy_CustomPolicy_ShouldRespectConfiguration()
    {
        // Arrange
        var customPolicy = new ErrorHandlingPolicy
        {
            MaxRetryAttempts = 5,
            BaseRetryDelay = TimeSpan.FromMilliseconds(50),
            MaxRetryDelay = TimeSpan.FromSeconds(2),
            HighFrequencyErrorThreshold = 3,
            NonRetryableExceptions = [typeof(ArgumentException)]
        };

        var logger = NullLogger<RobustErrorHandler>.Instance;
        using var errorHandler = new RobustErrorHandler(logger, customPolicy);

        var attemptCount = 0;

        _output.WriteLine($"⚙️ カスタムポリシーテスト開始");
        _output.WriteLine($"  最大リトライ回数: {customPolicy.MaxRetryAttempts}");

        // Act & Assert
        var result = await errorHandler.ExecuteWithRetryAsync(async () =>
        {
            attemptCount++;
            _output.WriteLine($"  試行 {attemptCount}");

            if (attemptCount < 4) // 4回目で成功
            {
                throw new InvalidOperationException($"Retry attempt {attemptCount}");
            }

            return await Task.FromResult("Custom policy success");
        }, "CustomPolicyTest");

        // Assert
        result.Should().Be("Custom policy success");
        attemptCount.Should().Be(4);

        _output.WriteLine($"✅ カスタムポリシーテスト成功");
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
            
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}