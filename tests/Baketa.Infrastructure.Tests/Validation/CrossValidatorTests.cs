using System.Drawing;
using Baketa.Core.Abstractions.OCR.Results;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.Validation;
using Baketa.Core.Models;
using Baketa.Core.Models.Validation;
using Baketa.Core.Translation.Abstractions;
using Baketa.Infrastructure.Validation;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.Validation;

/// <summary>
/// CrossValidatorのユニットテスト
/// Issue #78 Phase 3: 相互検証ロジック検証
/// </summary>
public sealed class CrossValidatorTests
{
    private readonly Mock<ILogger<CrossValidator>> _loggerMock;
    private readonly Mock<IFuzzyTextMatcher> _fuzzyMatcherMock;
    private readonly Mock<IConfidenceRescuer> _rescuerMock;
    private readonly CrossValidator _sut;

    public CrossValidatorTests()
    {
        _loggerMock = new Mock<ILogger<CrossValidator>>();
        _fuzzyMatcherMock = new Mock<IFuzzyTextMatcher>();
        _rescuerMock = new Mock<IConfidenceRescuer>();
        _sut = new CrossValidator(_fuzzyMatcherMock.Object, _rescuerMock.Object, _loggerMock.Object);
    }

    #region Basic Flow Tests

    [Fact]
    public async Task ValidateAsync_EmptyChunks_ReturnsEmptyResult()
    {
        // Arrange
        var chunks = Array.Empty<TextChunk>();
        var cloudResponse = CreateCloudResponse("detected", "translated");

        // Act
        var result = await _sut.ValidateAsync(chunks, cloudResponse);

        // Assert
        Assert.Empty(result.ValidatedChunks);
        Assert.Equal(0, result.Statistics.TotalLocalChunks);
    }

    [Fact]
    public async Task ValidateAsync_FailedCloudResponse_ReturnsEmptyResult()
    {
        // Arrange
        var chunks = new[] { CreateChunk("hello", 0.80f) };
        var cloudResponse = CreateFailedCloudResponse();

        // Act
        var result = await _sut.ValidateAsync(chunks, cloudResponse);

        // Assert
        Assert.Empty(result.ValidatedChunks);
    }

    #endregion

    #region Confidence Filtering Tests

    [Fact]
    public async Task ValidateAsync_LowConfidence_FiltersOut()
    {
        // Arrange: 信頼度 < 0.30
        var chunks = new[] { CreateChunk("hello", 0.25f) };
        var cloudResponse = CreateCloudResponse("hello", "こんにちは");

        // Act
        var result = await _sut.ValidateAsync(chunks, cloudResponse);

        // Assert: Geminiレビュー対応 - 除外チャンクはnullを返すため結果リストに含まれない
        Assert.Empty(result.ValidatedChunks);
        Assert.Equal(1, result.Statistics.FilteredByConfidenceCount);
    }

    #endregion

    #region Cross-Validation Tests

    [Fact]
    public async Task ValidateAsync_BothDetected_CrossValidated()
    {
        // Arrange
        var chunks = new[] { CreateChunk("hello", 0.80f) };
        var cloudResponse = CreateCloudResponse("hello", "こんにちは");

        SetupFuzzyMatch("hello", "hello", isMatch: true, similarity: 1.0f);

        // Act
        var result = await _sut.ValidateAsync(chunks, cloudResponse);

        // Assert
        Assert.Single(result.ValidatedChunks);
        Assert.Equal(ValidationStatus.CrossValidated, result.ValidatedChunks[0].Status);
        Assert.Equal(1, result.Statistics.CrossValidatedCount);
    }

    [Fact]
    public async Task ValidateAsync_LocalOnlyHighConfidence_MarkedAsLocalOnly()
    {
        // Arrange: 高信頼度だがCloud AIで検出されず
        var chunks = new[] { CreateChunk("hello", 0.80f) };
        var cloudResponse = CreateCloudResponse("world", "世界");

        SetupFuzzyMatch("hello", "world", isMatch: false, similarity: 0.20f);

        // Act
        var result = await _sut.ValidateAsync(chunks, cloudResponse);

        // Assert: Geminiレビュー対応 - 除外チャンクはnullを返すため結果リストに含まれない
        Assert.Empty(result.ValidatedChunks);
        Assert.Equal(1, result.Statistics.LocalOnlyCount);
    }

    #endregion

    #region Rescue Tests

    [Fact]
    public async Task ValidateAsync_LowConfidenceWithCloudMatch_Rescued()
    {
        // Arrange: 低信頼度だがCloud AIと一致
        var chunk = CreateChunk("hello", 0.50f);
        var chunks = new[] { chunk };
        var cloudResponse = CreateCloudResponse("hello", "こんにちは");

        SetupFuzzyMatch("hello", "hello", isMatch: true, similarity: 0.85f);

        // Act
        var result = await _sut.ValidateAsync(chunks, cloudResponse);

        // Assert
        Assert.Single(result.ValidatedChunks);
        Assert.Equal(ValidationStatus.Rescued, result.ValidatedChunks[0].Status);
        Assert.Equal(1, result.Statistics.RescuedCount);
    }

    [Fact]
    public async Task ValidateAsync_LowConfidenceNoMatch_AttemptsRescue()
    {
        // Arrange: 低信頼度、Cloud AIとマッチなし
        var chunk = CreateChunk("hello", 0.50f);
        var chunks = new[] { chunk };
        var cloudResponse = CreateCloudResponse("world", "世界");

        SetupFuzzyMatch("hello", "world", isMatch: false, similarity: 0.20f);
        _rescuerMock
            .Setup(r => r.TryRescue(It.IsAny<TextChunk>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns(RescueResult.NotRescued("No match found"));

        // Act
        var result = await _sut.ValidateAsync(chunks, cloudResponse);

        // Assert: Geminiレビュー対応 - 除外チャンクはnullを返すため結果リストに含まれない
        Assert.Empty(result.ValidatedChunks);
        Assert.Equal(1, result.Statistics.FilteredByMismatchCount);
    }

    #endregion

    #region Multiple Chunks Tests

    [Fact]
    public async Task ValidateAsync_MultipleChunks_ProcessesAll()
    {
        // Arrange
        var chunks = new[]
        {
            CreateChunk("hello", 0.80f),
            CreateChunk("world", 0.80f),
            CreateChunk("test", 0.25f) // 低信頼度 → 除外
        };
        var cloudResponse = CreateCloudResponse("hello\nworld", "こんにちは\n世界");

        SetupFuzzyMatch("hello", "hello", isMatch: true, similarity: 1.0f);
        SetupFuzzyMatch("hello", "world", isMatch: false, similarity: 0.20f);
        SetupFuzzyMatch("world", "hello", isMatch: false, similarity: 0.20f);
        SetupFuzzyMatch("world", "world", isMatch: true, similarity: 1.0f);

        // Act
        var result = await _sut.ValidateAsync(chunks, cloudResponse);

        // Assert: Geminiレビュー対応 - 除外チャンクはnullを返すため、採用チャンクのみ結果に含まれる
        Assert.Equal(2, result.ValidatedChunks.Count); // CrossValidatedの2つのみ
        Assert.Equal(3, result.Statistics.TotalLocalChunks);
        Assert.Equal(2, result.Statistics.CrossValidatedCount);
        Assert.Equal(1, result.Statistics.FilteredByConfidenceCount);
    }

    #endregion

    #region Statistics Tests

    [Fact]
    public async Task ValidateAsync_ReturnsCorrectStatistics()
    {
        // Arrange
        var chunks = new[]
        {
            CreateChunk("matched", 0.85f),   // CrossValidated
            CreateChunk("local", 0.80f),     // LocalOnly
            CreateChunk("low", 0.20f)        // Filtered
        };
        var cloudResponse = CreateCloudResponse("matched\ncloud", "マッチ\nクラウド");

        SetupFuzzyMatch("matched", "matched", isMatch: true, similarity: 1.0f);
        SetupFuzzyMatch("matched", "cloud", isMatch: false, similarity: 0.10f);
        SetupFuzzyMatch("local", "matched", isMatch: false, similarity: 0.20f);
        SetupFuzzyMatch("local", "cloud", isMatch: false, similarity: 0.20f);

        // Act
        var result = await _sut.ValidateAsync(chunks, cloudResponse);

        // Assert: Geminiレビュー対応 - 除外チャンクはnullを返すため、採用チャンクのみ結果に含まれる
        Assert.Single(result.ValidatedChunks); // CrossValidatedの1つのみ
        Assert.Equal(3, result.Statistics.TotalLocalChunks);
        Assert.Equal(2, result.Statistics.TotalCloudDetections);
        Assert.Equal(1, result.Statistics.CrossValidatedCount);
        Assert.Equal(1, result.Statistics.LocalOnlyCount);
        Assert.Equal(1, result.Statistics.FilteredByConfidenceCount);
        Assert.True(result.ProcessingTime.TotalMilliseconds > 0);
    }

    [Fact]
    public async Task ValidateAsync_CalculatesAcceptanceRate()
    {
        // Arrange
        var chunks = new[]
        {
            CreateChunk("a", 0.85f),  // CrossValidated
            CreateChunk("b", 0.50f),  // Rescued
            CreateChunk("c", 0.25f)   // Filtered
        };
        var cloudResponse = CreateCloudResponse("a\nb", "A\nB");

        SetupFuzzyMatch("a", "a", isMatch: true, similarity: 1.0f);
        SetupFuzzyMatch("a", "b", isMatch: false, similarity: 0.0f);
        SetupFuzzyMatch("b", "a", isMatch: false, similarity: 0.0f);
        SetupFuzzyMatch("b", "b", isMatch: true, similarity: 0.85f);

        // Act
        var result = await _sut.ValidateAsync(chunks, cloudResponse);

        // Assert
        // 採用率 = (CrossValidated + Rescued) / Total = 2/3 ≈ 0.67
        Assert.True(result.Statistics.AcceptanceRate > 0.6f);
    }

    #endregion

    #region Issue #242: Multiple Texts Property Tests

    /// <summary>
    /// Issue #242: Textsプロパティがある場合はそちらを優先使用
    /// </summary>
    [Fact]
    public async Task ValidateAsync_WithTextsProperty_UsesMultipleTexts()
    {
        // Arrange: ローカルOCRで複数テキスト検出
        var chunks = new[]
        {
            CreateChunk("Document", 0.90f),
            CreateChunk("Skip", 0.95f),
            CreateChunk("Menu", 0.92f)
        };

        // Geminiから複数テキストを含むレスポンス（Textsプロパティ使用）
        var cloudResponse = CreateCloudResponseWithTexts(new[]
        {
            ("Document", "ドキュメント"),
            ("Skip", "スキップ"),
            ("Menu", "メニュー"),
            ("Other", "その他") // ローカルにないテキスト
        });

        // 全てのテキストでマッチ設定
        SetupFuzzyMatch("Document", "Document", isMatch: true, similarity: 1.0f);
        SetupFuzzyMatch("Document", "Skip", isMatch: false, similarity: 0.10f);
        SetupFuzzyMatch("Document", "Menu", isMatch: false, similarity: 0.15f);
        SetupFuzzyMatch("Document", "Other", isMatch: false, similarity: 0.10f);
        SetupFuzzyMatch("Skip", "Document", isMatch: false, similarity: 0.10f);
        SetupFuzzyMatch("Skip", "Skip", isMatch: true, similarity: 1.0f);
        SetupFuzzyMatch("Skip", "Menu", isMatch: false, similarity: 0.20f);
        SetupFuzzyMatch("Skip", "Other", isMatch: false, similarity: 0.10f);
        SetupFuzzyMatch("Menu", "Document", isMatch: false, similarity: 0.15f);
        SetupFuzzyMatch("Menu", "Skip", isMatch: false, similarity: 0.20f);
        SetupFuzzyMatch("Menu", "Menu", isMatch: true, similarity: 1.0f);
        SetupFuzzyMatch("Menu", "Other", isMatch: false, similarity: 0.10f);

        // Act
        var result = await _sut.ValidateAsync(chunks, cloudResponse);

        // Assert: 全3チャンクがCrossValidatedになるはず
        Assert.Equal(3, result.ValidatedChunks.Count);
        Assert.All(result.ValidatedChunks, c =>
            Assert.Equal(ValidationStatus.CrossValidated, c.Status));
        Assert.Equal(3, result.Statistics.CrossValidatedCount);
        Assert.Equal(0, result.Statistics.LocalOnlyCount);
    }

    /// <summary>
    /// Issue #242: 後方互換性 - Textsがnullの場合はDetectedTextを使用
    /// </summary>
    [Fact]
    public async Task ValidateAsync_WithoutTextsProperty_FallsBackToDetectedText()
    {
        // Arrange
        var chunks = new[] { CreateChunk("hello", 0.80f) };
        var cloudResponse = CreateCloudResponse("hello\nworld", "こんにちは\n世界");

        SetupFuzzyMatch("hello", "hello", isMatch: true, similarity: 1.0f);
        SetupFuzzyMatch("hello", "world", isMatch: false, similarity: 0.20f);

        // Act
        var result = await _sut.ValidateAsync(chunks, cloudResponse);

        // Assert
        Assert.Single(result.ValidatedChunks);
        Assert.Equal(ValidationStatus.CrossValidated, result.ValidatedChunks[0].Status);
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task ValidateAsync_Cancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var chunks = Enumerable.Range(0, 100)
            .Select(i => CreateChunk($"text{i}", 0.80f))
            .ToArray();
        var cloudResponse = CreateCloudResponse("text0", "テキスト0");

        // Setup generic matcher for any string comparison
        _fuzzyMatcherMock
            .Setup(m => m.IsMatch(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new FuzzyMatchResult
            {
                IsMatch = false,
                Similarity = 0.10f,
                AppliedThreshold = 0.80f,
                Text1 = "any",
                Text2 = "any",
                EditDistance = 3
            });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _sut.ValidateAsync(chunks, cloudResponse, cts.Token));
    }

    #endregion

    #region Helper Methods

    private static TextChunk CreateChunk(string text, float confidence)
    {
        var textResult = new PositionedTextResult
        {
            Text = text,
            BoundingBox = new Rectangle(0, 0, 100, 20),
            Confidence = confidence,
            ChunkId = 1
        };

        return new TextChunk
        {
            ChunkId = 1,
            TextResults = new[] { textResult },
            CombinedBounds = new Rectangle(0, 0, 100, 20),
            CombinedText = text,
            SourceWindowHandle = IntPtr.Zero
        };
    }

    private static ImageTranslationResponse CreateCloudResponse(
        string detectedText, string translatedText)
    {
        return ImageTranslationResponse.Success(
            requestId: "test-request",
            detectedText: detectedText,
            translatedText: translatedText,
            providerId: "gemini",
            tokenUsage: TokenUsageDetail.Empty,
            processingTime: TimeSpan.FromMilliseconds(100));
    }

    private static ImageTranslationResponse CreateFailedCloudResponse()
    {
        return ImageTranslationResponse.Failure(
            requestId: "test-request",
            error: new TranslationErrorDetail
            {
                Code = TranslationErrorDetail.Codes.ApiError,
                Message = "Test error",
                IsRetryable = false
            },
            processingTime: TimeSpan.FromMilliseconds(100));
    }

    /// <summary>
    /// Issue #242: 複数テキストを含むレスポンス作成
    /// </summary>
    private static ImageTranslationResponse CreateCloudResponseWithTexts(
        (string original, string translation)[] texts)
    {
        var textItems = texts.Select(t => new TranslatedTextItem
        {
            Original = t.original,
            Translation = t.translation
        }).ToList();

        return ImageTranslationResponse.SuccessWithMultipleTexts(
            requestId: "test-request",
            texts: textItems,
            providerId: "gemini",
            tokenUsage: TokenUsageDetail.Empty,
            processingTime: TimeSpan.FromMilliseconds(100));
    }

    private void SetupFuzzyMatch(string text1, string text2, bool isMatch, float similarity)
    {
        var result = new FuzzyMatchResult
        {
            IsMatch = isMatch,
            Similarity = similarity,
            AppliedThreshold = 0.80f,
            Text1 = text1,
            Text2 = text2,
            EditDistance = (int)((1 - similarity) * Math.Max(text1.Length, text2.Length))
        };

        _fuzzyMatcherMock
            .Setup(m => m.IsMatch(text1, text2))
            .Returns(result);
    }

    /// <summary>
    /// Issue #275: BoundingBox付きTranslatedTextItemを含むレスポンス作成
    /// </summary>
    private static ImageTranslationResponse CreateCloudResponseWithBoundingBoxes(
        (string original, string translation, Int32Rect? boundingBox)[] texts)
    {
        var textItems = texts.Select(t => new TranslatedTextItem
        {
            Original = t.original,
            Translation = t.translation,
            BoundingBox = t.boundingBox
        }).ToList();

        return ImageTranslationResponse.SuccessWithMultipleTexts(
            requestId: "test-request",
            texts: textItems,
            providerId: "gemini",
            tokenUsage: TokenUsageDetail.Empty,
            processingTime: TimeSpan.FromMilliseconds(100));
    }

    #endregion

    #region Issue #275 CloudBoundingBox Tests

    /// <summary>
    /// Issue #275: CloudBoundingBoxがある場合、Cloud AI座標を優先使用することを検証
    /// </summary>
    [Fact]
    public async Task ValidateAsync_WithCloudBoundingBox_UsesCloudAiCoordinates()
    {
        // Arrange
        // ローカルチャンク: "Hello World" (0,0,100,20)
        var chunk = CreateChunkWithBounds("Hello World", 0.80f, new Rectangle(0, 0, 100, 20));

        // Cloud AIレスポンス: "Hello"と"World"が縦に配置されている（Y座標が異なる）
        var cloudResponse = CreateCloudResponseWithBoundingBoxes([
            ("Hello", "こんにちは", new Int32Rect(10, 50, 40, 15)),   // Y=50
            ("World", "世界", new Int32Rect(10, 70, 40, 15))          // Y=70 (縦配置)
        ]);

        // IContainmentMatcherのモック設定
        var containmentMatcherMock = new Mock<IContainmentMatcher>();

        // FindMergeGroupsは空を返す
        containmentMatcherMock
            .Setup(m => m.FindMergeGroups(It.IsAny<IReadOnlyList<TextChunk>>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns([]);

        // FindSplitInfoはCloudBoundingBox付きのSplitInfoを返す
        containmentMatcherMock
            .Setup(m => m.FindSplitInfo(It.IsAny<TextChunk>(), It.IsAny<IReadOnlyList<TranslatedTextItem>>()))
            .Returns(new SplitInfo
            {
                LocalChunk = chunk,
                Segments =
                [
                    new SplitSegment
                    {
                        CloudTextIndex = 0,
                        CloudText = "Hello",
                        StartIndex = 0,
                        EndIndex = 5,
                        CloudBoundingBox = new Int32Rect(10, 50, 40, 15)  // Cloud AI座標
                    },
                    new SplitSegment
                    {
                        CloudTextIndex = 1,
                        CloudText = "World",
                        StartIndex = 6,
                        EndIndex = 11,
                        CloudBoundingBox = new Int32Rect(10, 70, 40, 15)  // Cloud AI座標（縦配置）
                    }
                ]
            });

        // FuzzyMatcherはマッチしないように設定（Split処理に進むため）
        _fuzzyMatcherMock
            .Setup(m => m.IsMatch(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new FuzzyMatchResult
            {
                IsMatch = false,
                Similarity = 0.10f,
                AppliedThreshold = 0.80f,
                Text1 = "any",
                Text2 = "any",
                EditDistance = 10
            });

        // ContainmentMatcher付きでCrossValidatorを作成
        var sutWithContainment = new CrossValidator(
            _fuzzyMatcherMock.Object,
            _rescuerMock.Object,
            containmentMatcherMock.Object,
            _loggerMock.Object);

        // Act
        var result = await sutWithContainment.ValidateAsync([chunk], cloudResponse);

        // Assert
        Assert.Equal(2, result.ValidatedChunks.Count);

        // Cloud AI座標が使用されていることを検証（比率計算ではなくCloudBoundingBoxの値）
        var helloChunk = result.ValidatedChunks.FirstOrDefault(c => c.CloudDetectedText == "Hello");
        var worldChunk = result.ValidatedChunks.FirstOrDefault(c => c.CloudDetectedText == "World");

        Assert.NotNull(helloChunk);
        Assert.NotNull(worldChunk);

        // Y座標がCloud AIの値（50, 70）であることを確認
        // 比率計算だと両方Y=0になるはずなので、これでCloud AI座標が使われていることを検証
        Assert.Equal(50, helloChunk.OriginalChunk.CombinedBounds.Y);
        Assert.Equal(70, worldChunk.OriginalChunk.CombinedBounds.Y);

        // X座標も検証
        Assert.Equal(10, helloChunk.OriginalChunk.CombinedBounds.X);
        Assert.Equal(10, worldChunk.OriginalChunk.CombinedBounds.X);
    }

    /// <summary>
    /// Issue #275: CloudBoundingBoxがない場合、従来の比率計算にフォールバックすることを検証
    /// </summary>
    [Fact]
    public async Task ValidateAsync_WithoutCloudBoundingBox_UsesRatioCalculation()
    {
        // Arrange
        var chunk = CreateChunkWithBounds("Hello World", 0.80f, new Rectangle(0, 0, 110, 20));

        // Cloud AIレスポンス: BoundingBoxなし
        var cloudResponse = CreateCloudResponseWithBoundingBoxes([
            ("Hello", "こんにちは", null),
            ("World", "世界", null)
        ]);

        var containmentMatcherMock = new Mock<IContainmentMatcher>();

        containmentMatcherMock
            .Setup(m => m.FindMergeGroups(It.IsAny<IReadOnlyList<TextChunk>>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns([]);

        // CloudBoundingBoxがnullのSplitInfo
        containmentMatcherMock
            .Setup(m => m.FindSplitInfo(It.IsAny<TextChunk>(), It.IsAny<IReadOnlyList<TranslatedTextItem>>()))
            .Returns(new SplitInfo
            {
                LocalChunk = chunk,
                Segments =
                [
                    new SplitSegment
                    {
                        CloudTextIndex = 0,
                        CloudText = "Hello",
                        StartIndex = 0,
                        EndIndex = 5,
                        CloudBoundingBox = null  // BoundingBoxなし
                    },
                    new SplitSegment
                    {
                        CloudTextIndex = 1,
                        CloudText = "World",
                        StartIndex = 6,
                        EndIndex = 11,
                        CloudBoundingBox = null  // BoundingBoxなし
                    }
                ]
            });

        _fuzzyMatcherMock
            .Setup(m => m.IsMatch(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new FuzzyMatchResult
            {
                IsMatch = false,
                Similarity = 0.10f,
                AppliedThreshold = 0.80f,
                Text1 = "any",
                Text2 = "any",
                EditDistance = 10
            });

        var sutWithContainment = new CrossValidator(
            _fuzzyMatcherMock.Object,
            _rescuerMock.Object,
            containmentMatcherMock.Object,
            _loggerMock.Object);

        // Act
        var result = await sutWithContainment.ValidateAsync([chunk], cloudResponse);

        // Assert
        Assert.Equal(2, result.ValidatedChunks.Count);

        // フォールバック（比率計算）の座標が使用されていることを検証
        // ローカルチャンクのY座標（0）がそのまま使用される
        Assert.All(result.ValidatedChunks, c => Assert.Equal(0, c.OriginalChunk.CombinedBounds.Y));

        // X座標は比率計算される（0から開始）
        var helloChunk = result.ValidatedChunks.FirstOrDefault(c => c.CloudDetectedText == "Hello");
        Assert.NotNull(helloChunk);
        Assert.Equal(0, helloChunk.OriginalChunk.CombinedBounds.X);  // StartIndex=0 なので X=0
    }

    /// <summary>
    /// Issue #275 + Geminiレビュー: CloudBoundingBoxが無効値（Width=0）の場合は比率計算にフォールバック
    /// </summary>
    [Fact]
    public async Task ValidateAsync_WithInvalidCloudBoundingBox_FallsBackToRatioCalculation()
    {
        // Arrange
        var chunk = CreateChunkWithBounds("Hello World", 0.95f, new Rectangle(0, 0, 200, 30));
        var cloudResponse = CreateCloudResponseWithBoundingBoxes([
            ("Hello", "こんにちは", new Core.Models.Int32Rect(100, 100, 0, 0))  // 無効: Width=0, Height=0
        ]);

        var containmentMatcherMock = new Mock<IContainmentMatcher>();
        containmentMatcherMock
            .Setup(m => m.FindMergeGroups(It.IsAny<IReadOnlyList<TextChunk>>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns([]);
        containmentMatcherMock
            .Setup(m => m.FindSplitInfo(It.IsAny<TextChunk>(), It.IsAny<IReadOnlyList<TranslatedTextItem>>()))
            .Returns(new SplitInfo
            {
                LocalChunk = chunk,
                Segments =
                [
                    new SplitSegment
                    {
                        CloudTextIndex = 0,
                        CloudText = "Hello",
                        StartIndex = 0,
                        EndIndex = 5,
                        CloudBoundingBox = new Core.Models.Int32Rect(100, 100, 0, 0)  // 無効値
                    }
                ]
            });

        _fuzzyMatcherMock
            .Setup(m => m.IsMatch(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new FuzzyMatchResult
            {
                IsMatch = false,
                Similarity = 0.10f,
                AppliedThreshold = 0.80f,
                Text1 = "any",
                Text2 = "any",
                EditDistance = 10
            });

        var sutWithContainment = new CrossValidator(
            _fuzzyMatcherMock.Object,
            _rescuerMock.Object,
            containmentMatcherMock.Object,
            _loggerMock.Object);

        // Act
        var result = await sutWithContainment.ValidateAsync([chunk], cloudResponse);

        // Assert
        Assert.Single(result.ValidatedChunks);

        var validatedChunk = result.ValidatedChunks[0];
        // 無効なCloudBoundingBoxは無視され、比率計算が使用される
        // ローカルチャンクのY座標（0）が使用される（Cloud AI座標の100ではない）
        Assert.Equal(0, validatedChunk.OriginalChunk.CombinedBounds.Y);
        // X座標も比率計算される（StartIndex=0 なので X=0）
        Assert.Equal(0, validatedChunk.OriginalChunk.CombinedBounds.X);
    }

    /// <summary>
    /// テスト用TextChunk作成ヘルパー（座標指定可能）
    /// </summary>
    private static TextChunk CreateChunkWithBounds(string text, float confidence, Rectangle bounds)
    {
        var textResult = new PositionedTextResult
        {
            Text = text,
            BoundingBox = bounds,
            Confidence = confidence,
            ChunkId = 1
        };

        return new TextChunk
        {
            ChunkId = 1,
            TextResults = [textResult],
            CombinedBounds = bounds,
            CombinedText = text,
            SourceWindowHandle = IntPtr.Zero
        };
    }

    #endregion
}
