using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Application.Services.Translation;
using Baketa.Core.Abstractions.License;
using Baketa.Core.Abstractions.OCR.Results;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.Validation;
using Baketa.Core.Models.Validation;
using Baketa.Core.Translation.Abstractions;
using Baketa.Core.Translation.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

// 名前空間競合を解決するためのエイリアス
using TranslationAbstractions = Baketa.Core.Abstractions.Translation;

namespace Baketa.Application.Tests.Services.Translation;

/// <summary>
/// ParallelTranslationOrchestrator の単体テスト
/// Issue #78 Phase 4: 並列翻訳オーケストレーション
/// </summary>
public class ParallelTranslationOrchestratorTests : IDisposable
{
    private readonly Mock<TranslationAbstractions.ITranslationService> _translationServiceMock;
    private readonly Mock<IFallbackOrchestrator> _fallbackOrchestratorMock;
    private readonly Mock<ICrossValidator> _crossValidatorMock;
    private readonly Mock<ILicenseManager> _licenseManagerMock;
    private readonly Mock<ILogger<ParallelTranslationOrchestrator>> _loggerMock;

    private readonly ParallelTranslationOrchestrator _orchestrator;
    private bool _disposed;

    public ParallelTranslationOrchestratorTests()
    {
        _translationServiceMock = new Mock<TranslationAbstractions.ITranslationService>();
        _fallbackOrchestratorMock = new Mock<IFallbackOrchestrator>();
        _crossValidatorMock = new Mock<ICrossValidator>();
        _licenseManagerMock = new Mock<ILicenseManager>();
        _loggerMock = new Mock<ILogger<ParallelTranslationOrchestrator>>();

        SetupDefaultMocks();

        _orchestrator = new ParallelTranslationOrchestrator(
            _translationServiceMock.Object,
            _fallbackOrchestratorMock.Object,
            _crossValidatorMock.Object,
            _licenseManagerMock.Object,
            _loggerMock.Object);
    }

    private void SetupDefaultMocks()
    {
        // デフォルトのFallbackStatus
        _fallbackOrchestratorMock
            .Setup(x => x.GetCurrentStatus())
            .Returns(new FallbackStatus
            {
                PrimaryAvailable = true,
                SecondaryAvailable = true,
                LocalAvailable = true
            });

        // デフォルトのバッチ翻訳結果
        _translationServiceMock
            .Setup(x => x.TranslateBatchAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<Language>(),
                It.IsAny<Language>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<string> texts, Language source, Language target, string? ___, CancellationToken ____) =>
            {
                return texts.Select(t => new TranslationResponse
                {
                    RequestId = Guid.NewGuid(),
                    SourceText = t,
                    SourceLanguage = source,
                    TargetLanguage = target,
                    EngineName = "MockEngine",
                    IsSuccess = true,
                    TranslatedText = $"[Translated] {t}"
                }).ToList();
            });

        // Issue #258: デフォルトのトークン消費記録結果
        _licenseManagerMock
            .Setup(x => x.ConsumeCloudAiTokensAsync(
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenConsumptionResult.CreateSuccess(1000, 9_999_000));
    }

    #region IsCloudTranslationAvailable テスト

    [Fact]
    public void IsCloudTranslationAvailable_WhenPrimaryAvailable_ReturnsTrue()
    {
        // Arrange
        _fallbackOrchestratorMock
            .Setup(x => x.GetCurrentStatus())
            .Returns(new FallbackStatus { PrimaryAvailable = true, SecondaryAvailable = false });

        // Act
        var result = _orchestrator.IsCloudTranslationAvailable;

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsCloudTranslationAvailable_WhenSecondaryAvailable_ReturnsTrue()
    {
        // Arrange
        _fallbackOrchestratorMock
            .Setup(x => x.GetCurrentStatus())
            .Returns(new FallbackStatus { PrimaryAvailable = false, SecondaryAvailable = true });

        // Act
        var result = _orchestrator.IsCloudTranslationAvailable;

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsCloudTranslationAvailable_WhenNoneAvailable_ReturnsFalse()
    {
        // Arrange
        _fallbackOrchestratorMock
            .Setup(x => x.GetCurrentStatus())
            .Returns(new FallbackStatus { PrimaryAvailable = false, SecondaryAvailable = false });

        // Act
        var result = _orchestrator.IsCloudTranslationAvailable;

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region GetStatus テスト

    [Fact]
    public void GetStatus_ReturnsCorrectStatus()
    {
        // Arrange
        _fallbackOrchestratorMock
            .Setup(x => x.GetCurrentStatus())
            .Returns(new FallbackStatus
            {
                PrimaryAvailable = true,
                SecondaryAvailable = false,
                LocalAvailable = true
            });

        // Act
        var status = _orchestrator.GetStatus();

        // Assert
        status.LocalEngineAvailable.Should().BeTrue();
        status.CloudEngineAvailable.Should().BeTrue();
        status.CrossValidationEnabled.Should().BeTrue();
        status.FallbackStatus.Should().NotBeNull();
    }

    #endregion

    #region TranslateAsync - ローカルのみテスト

    [Fact]
    public async Task TranslateAsync_WithoutCloudTranslation_ReturnsLocalOnlyResult()
    {
        // Arrange
        var chunks = CreateTestChunks(2);
        var request = new ParallelTranslationRequest
        {
            OcrChunks = chunks,
            ImageBase64 = "dGVzdA==",
            TargetLanguage = "ja",
            UseCloudTranslation = false
        };

        // Act
        var result = await _orchestrator.TranslateAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.EngineUsed.Should().Be(TranslationEngineUsed.LocalOnly);
        result.ValidatedChunks.Should().HaveCount(2);
        result.ValidatedChunks.All(c => c.Status == ValidationStatus.LocalOnly).Should().BeTrue();
    }

    [Fact]
    public async Task TranslateAsync_WithoutSessionToken_ReturnsLocalOnlyResult()
    {
        // Arrange
        var chunks = CreateTestChunks(1);
        var request = new ParallelTranslationRequest
        {
            OcrChunks = chunks,
            ImageBase64 = "dGVzdA==",
            TargetLanguage = "ja",
            UseCloudTranslation = true,
            SessionToken = null // セッショントークンなし
        };

        // Act
        var result = await _orchestrator.TranslateAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.EngineUsed.Should().Be(TranslationEngineUsed.LocalOnly);
    }

    [Fact]
    public async Task TranslateAsync_WithEmptyChunks_ReturnsEmptyResult()
    {
        // Arrange
        var request = new ParallelTranslationRequest
        {
            OcrChunks = [],
            ImageBase64 = "dGVzdA==",
            TargetLanguage = "ja",
            UseCloudTranslation = false
        };

        // Act
        var result = await _orchestrator.TranslateAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.ValidatedChunks.Should().BeEmpty();
    }

    #endregion

    #region TranslateAsync - 並列翻訳テスト

    [Fact]
    public async Task TranslateAsync_WithCloudTranslation_ExecutesBothInParallel()
    {
        // Arrange
        var chunks = CreateTestChunks(2);
        var request = new ParallelTranslationRequest
        {
            OcrChunks = chunks,
            ImageBase64 = "dGVzdA==",
            TargetLanguage = "ja",
            UseCloudTranslation = true,
            SessionToken = "test-token",
            EnableCrossValidation = true
        };

        var cloudResponse = ImageTranslationResponse.Success(
            "req-1",
            "Hello World",
            "こんにちは世界",
            "gemini",
            TokenUsageDetail.Empty,
            TimeSpan.FromMilliseconds(100));

        _fallbackOrchestratorMock
            .Setup(x => x.TranslateWithFallbackAsync(It.IsAny<ImageTranslationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FallbackTranslationResult.Success(cloudResponse, FallbackLevel.Primary, []));

        var validatedChunks = chunks.Select(c =>
            ValidatedTextChunk.CrossValidated(c, "翻訳結果", "Hello", 0.95f)).ToList();

        _crossValidatorMock
            .Setup(x => x.ValidateAsync(It.IsAny<IReadOnlyList<TextChunk>>(), It.IsAny<ImageTranslationResponse>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CrossValidationResult.Create(
                validatedChunks,
                new CrossValidationStatistics { TotalLocalChunks = 2, CrossValidatedCount = 2 },
                TimeSpan.FromMilliseconds(10)));

        // Act
        var result = await _orchestrator.TranslateAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.EngineUsed.Should().Be(TranslationEngineUsed.BothWithValidation);
        result.ValidatedChunks.Should().HaveCount(2);
        result.ValidationStatistics.Should().NotBeNull();
        result.CloudTranslationResponse.Should().NotBeNull();
    }

    [Fact]
    public async Task TranslateAsync_WhenCrossValidationFails_ReturnsBothWithValidationFailed()
    {
        // Arrange
        var chunks = CreateTestChunks(2);
        var request = new ParallelTranslationRequest
        {
            OcrChunks = chunks,
            ImageBase64 = "dGVzdA==",
            TargetLanguage = "ja",
            UseCloudTranslation = true,
            SessionToken = "test-token",
            EnableCrossValidation = true
        };

        var cloudResponse = ImageTranslationResponse.Success(
            "req-1",
            "Hello World",
            "こんにちは世界",
            "gemini",
            TokenUsageDetail.Empty,
            TimeSpan.FromMilliseconds(100));

        _fallbackOrchestratorMock
            .Setup(x => x.TranslateWithFallbackAsync(It.IsAny<ImageTranslationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FallbackTranslationResult.Success(cloudResponse, FallbackLevel.Primary, []));

        // 相互検証が例外をスロー
        _crossValidatorMock
            .Setup(x => x.ValidateAsync(It.IsAny<IReadOnlyList<TextChunk>>(), It.IsAny<ImageTranslationResponse>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Cross validation failed"));

        // Act
        var result = await _orchestrator.TranslateAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.EngineUsed.Should().Be(TranslationEngineUsed.BothWithValidationFailed);
        result.ValidatedChunks.Should().HaveCount(2);
        result.CloudTranslationResponse.Should().NotBeNull();
        result.Error.Should().NotBeNull();
        result.Error!.Code.Should().Be("CROSS_VALIDATION_FAILED");
        result.Error.Message.Should().Contain("Cross validation failed");
    }

    [Fact]
    public async Task TranslateAsync_WhenCloudFails_FallsBackToLocalOnly()
    {
        // Arrange
        var chunks = CreateTestChunks(1);
        var request = new ParallelTranslationRequest
        {
            OcrChunks = chunks,
            ImageBase64 = "dGVzdA==",
            TargetLanguage = "ja",
            UseCloudTranslation = true,
            SessionToken = "test-token"
        };

        _fallbackOrchestratorMock
            .Setup(x => x.TranslateWithFallbackAsync(It.IsAny<ImageTranslationRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Cloud AI Error"));

        // Act
        var result = await _orchestrator.TranslateAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.EngineUsed.Should().Be(TranslationEngineUsed.LocalOnly);
        result.CloudTranslationResponse.Should().BeNull();
    }

    [Fact]
    public async Task TranslateAsync_WhenLocalFails_FallsBackToCloudOnly()
    {
        // Arrange
        var chunks = CreateTestChunks(1);
        var request = new ParallelTranslationRequest
        {
            OcrChunks = chunks,
            ImageBase64 = "dGVzdA==",
            TargetLanguage = "ja",
            UseCloudTranslation = true,
            SessionToken = "test-token"
        };

        _translationServiceMock
            .Setup(x => x.TranslateBatchAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<Language>(),
                It.IsAny<Language>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Local Translation Error"));

        var cloudResponse = ImageTranslationResponse.Success(
            "req-1",
            "Hello",
            "こんにちは",
            "gemini",
            TokenUsageDetail.Empty,
            TimeSpan.FromMilliseconds(100));

        _fallbackOrchestratorMock
            .Setup(x => x.TranslateWithFallbackAsync(It.IsAny<ImageTranslationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FallbackTranslationResult.Success(cloudResponse, FallbackLevel.Primary, []));

        // Act
        var result = await _orchestrator.TranslateAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.EngineUsed.Should().Be(TranslationEngineUsed.CloudOnly);
        result.CloudTranslationResponse.Should().NotBeNull();
    }

    [Fact]
    public async Task TranslateAsync_WhenBothFail_ReturnsFailure()
    {
        // Arrange
        var chunks = CreateTestChunks(1);
        var request = new ParallelTranslationRequest
        {
            OcrChunks = chunks,
            ImageBase64 = "dGVzdA==",
            TargetLanguage = "ja",
            UseCloudTranslation = true,
            SessionToken = "test-token"
        };

        _translationServiceMock
            .Setup(x => x.TranslateBatchAsync(
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<Language>(),
                It.IsAny<Language>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Local Error"));

        _fallbackOrchestratorMock
            .Setup(x => x.TranslateWithFallbackAsync(It.IsAny<ImageTranslationRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Cloud Error"));

        // Act
        var result = await _orchestrator.TranslateAsync(request);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.EngineUsed.Should().Be(TranslationEngineUsed.None);
        result.Error.Should().NotBeNull();
    }

    #endregion

    #region Issue #258: トークン消費記録テスト

    [Fact]
    public async Task TranslateAsync_WhenCloudSucceeds_RecordsTokenConsumption()
    {
        // Arrange: Cloud AI翻訳成功時にトークン消費が記録されることを検証
        var chunks = CreateTestChunks(1);
        var request = new ParallelTranslationRequest
        {
            OcrChunks = chunks,
            ImageBase64 = "dGVzdA==",
            TargetLanguage = "ja",
            UseCloudTranslation = true,
            SessionToken = "test-token",
            EnableCrossValidation = false
        };

        var tokenUsage = new TokenUsageDetail
        {
            InputTokens = 100,
            OutputTokens = 50
        };

        var cloudResponse = ImageTranslationResponse.Success(
            "req-123",
            "Hello World",
            "こんにちは世界",
            "gemini",
            tokenUsage,
            TimeSpan.FromMilliseconds(100));

        _fallbackOrchestratorMock
            .Setup(x => x.TranslateWithFallbackAsync(It.IsAny<ImageTranslationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FallbackTranslationResult.Success(cloudResponse, FallbackLevel.Primary, []));

        // Act
        var result = await _orchestrator.TranslateAsync(request);

        // Assert: トークン消費が正しい引数で1回呼び出されること
        result.IsSuccess.Should().BeTrue();
        _licenseManagerMock.Verify(
            x => x.ConsumeCloudAiTokensAsync(
                150, // InputTokens + OutputTokens = TotalTokens
                It.Is<string>(key => key.StartsWith("translation-")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TranslateAsync_WhenTokenUsageIsZero_SkipsTokenConsumption()
    {
        // Arrange: TotalTokensが0の場合にトークン消費記録をスキップすることを検証
        var chunks = CreateTestChunks(1);
        var request = new ParallelTranslationRequest
        {
            OcrChunks = chunks,
            ImageBase64 = "dGVzdA==",
            TargetLanguage = "ja",
            UseCloudTranslation = true,
            SessionToken = "test-token",
            EnableCrossValidation = false
        };

        var zeroTokenUsage = new TokenUsageDetail
        {
            InputTokens = 0,
            OutputTokens = 0
        };

        var cloudResponse = ImageTranslationResponse.Success(
            "req-123",
            "Hello",
            "こんにちは",
            "gemini",
            zeroTokenUsage,
            TimeSpan.FromMilliseconds(100));

        _fallbackOrchestratorMock
            .Setup(x => x.TranslateWithFallbackAsync(It.IsAny<ImageTranslationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FallbackTranslationResult.Success(cloudResponse, FallbackLevel.Primary, []));

        // Act
        var result = await _orchestrator.TranslateAsync(request);

        // Assert: ConsumeCloudAiTokensAsyncが呼び出されないこと
        result.IsSuccess.Should().BeTrue();
        _licenseManagerMock.Verify(
            x => x.ConsumeCloudAiTokensAsync(
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TranslateAsync_WhenTokenConsumptionFails_StillReturnsSuccess()
    {
        // Arrange: トークン消費記録が失敗しても翻訳は成功として扱われることを検証
        var chunks = CreateTestChunks(1);
        var request = new ParallelTranslationRequest
        {
            OcrChunks = chunks,
            ImageBase64 = "dGVzdA==",
            TargetLanguage = "ja",
            UseCloudTranslation = true,
            SessionToken = "test-token",
            EnableCrossValidation = false
        };

        var tokenUsage = new TokenUsageDetail { InputTokens = 100, OutputTokens = 50 };

        var cloudResponse = ImageTranslationResponse.Success(
            "req-123",
            "Hello",
            "こんにちは",
            "gemini",
            tokenUsage,
            TimeSpan.FromMilliseconds(100));

        _fallbackOrchestratorMock
            .Setup(x => x.TranslateWithFallbackAsync(It.IsAny<ImageTranslationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FallbackTranslationResult.Success(cloudResponse, FallbackLevel.Primary, []));

        // トークン消費記録が失敗を返す
        _licenseManagerMock
            .Setup(x => x.ConsumeCloudAiTokensAsync(
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(TokenConsumptionResult.CreateFailure(
                TokenConsumptionFailureReason.QuotaExceeded,
                "Monthly token quota exceeded"));

        // Act
        var result = await _orchestrator.TranslateAsync(request);

        // Assert: 翻訳自体は成功
        result.IsSuccess.Should().BeTrue();
        result.EngineUsed.Should().NotBe(TranslationEngineUsed.None);
    }

    [Fact]
    public async Task TranslateAsync_WhenTokenConsumptionThrows_StillReturnsSuccess()
    {
        // Arrange: トークン消費記録で例外が発生しても翻訳は成功として扱われることを検証
        var chunks = CreateTestChunks(1);
        var request = new ParallelTranslationRequest
        {
            OcrChunks = chunks,
            ImageBase64 = "dGVzdA==",
            TargetLanguage = "ja",
            UseCloudTranslation = true,
            SessionToken = "test-token",
            EnableCrossValidation = false
        };

        var tokenUsage = new TokenUsageDetail { InputTokens = 100, OutputTokens = 50 };

        var cloudResponse = ImageTranslationResponse.Success(
            "req-123",
            "Hello",
            "こんにちは",
            "gemini",
            tokenUsage,
            TimeSpan.FromMilliseconds(100));

        _fallbackOrchestratorMock
            .Setup(x => x.TranslateWithFallbackAsync(It.IsAny<ImageTranslationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FallbackTranslationResult.Success(cloudResponse, FallbackLevel.Primary, []));

        // トークン消費記録で例外をスロー
        _licenseManagerMock
            .Setup(x => x.ConsumeCloudAiTokensAsync(
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Network error"));

        // Act
        var result = await _orchestrator.TranslateAsync(request);

        // Assert: 例外がキャッチされ、翻訳自体は成功
        result.IsSuccess.Should().BeTrue();
        result.EngineUsed.Should().NotBe(TranslationEngineUsed.None);
    }

    #endregion

    #region TranslateAsync - キャンセルテスト

    [Fact]
    public async Task TranslateAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        var chunks = CreateTestChunks(1);
        var request = new ParallelTranslationRequest
        {
            OcrChunks = chunks,
            ImageBase64 = "dGVzdA==",
            TargetLanguage = "ja",
            UseCloudTranslation = false
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        // TaskCanceledException は OperationCanceledException のサブクラス
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _orchestrator.TranslateAsync(request, cts.Token));

        exception.Should().NotBeNull();
    }

    #endregion

    #region Timing テスト

    [Fact]
    public async Task TranslateAsync_RecordsTimingCorrectly()
    {
        // Arrange
        var chunks = CreateTestChunks(1);
        var request = new ParallelTranslationRequest
        {
            OcrChunks = chunks,
            ImageBase64 = "dGVzdA==",
            TargetLanguage = "ja",
            UseCloudTranslation = false
        };

        // Act
        var result = await _orchestrator.TranslateAsync(request);

        // Assert
        result.Timing.Should().NotBeNull();
        result.Timing.TotalDuration.Should().BeGreaterThan(TimeSpan.Zero);
        result.Timing.LocalTranslationDuration.Should().BeGreaterThan(TimeSpan.Zero);
    }

    #endregion

    #region ヘルパーメソッド

    private static List<TextChunk> CreateTestChunks(int count)
    {
        var chunks = new List<TextChunk>();

        for (int i = 0; i < count; i++)
        {
            chunks.Add(new TextChunk
            {
                ChunkId = i + 1,
                CombinedText = $"Test Text {i + 1}",
                CombinedBounds = new System.Drawing.Rectangle(i * 100, 0, 100, 30),
                TextResults = [],
                SourceWindowHandle = IntPtr.Zero
            });
        }

        return chunks;
    }

    #endregion

    #region Dispose

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _orchestrator.Dispose();
            }

            _disposed = true;
        }
    }

    #endregion
}
