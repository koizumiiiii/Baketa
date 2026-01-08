using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.Validation;
using Baketa.Core.Models;
using Baketa.Core.Translation.Abstractions;
using Baketa.Infrastructure.Validation;
using Microsoft.Extensions.Logging;
using Moq;
using System.Drawing;
using Xunit;

namespace Baketa.Infrastructure.Tests.Validation;

/// <summary>
/// ContainmentMatcherのユニットテスト
/// Issue #78 Phase 3.5: 双方向マッチング（統合/分割）検証
/// </summary>
public sealed class ContainmentMatcherTests
{
    private readonly Mock<ILogger<ContainmentMatcher>> _loggerMock;
    private readonly ContainmentMatcher _sut;

    public ContainmentMatcherTests()
    {
        _loggerMock = new Mock<ILogger<ContainmentMatcher>>();
        _sut = new ContainmentMatcher(_loggerMock.Object);
    }

    #region IsContainedWithBoundary Tests

    [Fact]
    public void IsContainedWithBoundary_ExactMatch_ReturnsTrue()
    {
        // Arrange
        var text = "Hello";
        var container = "Hello";

        // Act
        var result = _sut.IsContainedWithBoundary(text, container);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsContainedWithBoundary_TextContainedWithSpaceBoundary_ReturnsTrue()
    {
        // Arrange
        var text = "World";
        var container = "Hello World Test";

        // Act
        var result = _sut.IsContainedWithBoundary(text, container);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsContainedWithBoundary_TextContainedWithPunctuationBoundary_ReturnsTrue()
    {
        // Arrange
        var text = "Hello";
        var container = "Hello,World";

        // Act
        var result = _sut.IsContainedWithBoundary(text, container);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsContainedWithBoundary_TextContainedWithNoBoundary_ReturnsFalse()
    {
        // Arrange
        var text = "ell";
        var container = "Hello";

        // Act
        var result = _sut.IsContainedWithBoundary(text, container);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsContainedWithBoundary_TooShortText_ReturnsFalse()
    {
        // Arrange - 3文字未満は除外
        var text = "ab";
        var container = "ab cd";

        // Act
        var result = _sut.IsContainedWithBoundary(text, container);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData(null, "container")]
    [InlineData("text", null)]
    [InlineData("", "container")]
    [InlineData("text", "")]
    public void IsContainedWithBoundary_NullOrEmpty_ReturnsFalse(string? text, string? container)
    {
        // Act
        var result = _sut.IsContainedWithBoundary(text!, container!);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsContainedWithBoundary_TextLongerThanContainer_ReturnsFalse()
    {
        // Arrange
        var text = "Hello World";
        var container = "Hello";

        // Act
        var result = _sut.IsContainedWithBoundary(text, container);

        // Assert
        Assert.False(result);
    }

    #region Japanese Character Type Boundary Tests

    [Fact]
    public void IsContainedWithBoundary_HiraganaToKatakana_ReturnsTrue()
    {
        // Arrange - ひらがな→カタカナの境界
        var text = "ゲーム";
        var container = "これはゲームです";

        // Act
        var result = _sut.IsContainedWithBoundary(text, container);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsContainedWithBoundary_KanjiWithSpaceBoundary_ReturnsTrue()
    {
        // Arrange - 漢字が空白で区切られている
        var text = "日本語";
        var container = "Hello 日本語 World";

        // Act
        var result = _sut.IsContainedWithBoundary(text, container);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsContainedWithBoundary_AlphabetToKanji_ReturnsTrue()
    {
        // Arrange - 英字→漢字の境界
        var text = "ABC";
        var container = "開始ABC終了";

        // Act
        var result = _sut.IsContainedWithBoundary(text, container);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsContainedWithBoundary_SameCharacterType_ReturnsFalse()
    {
        // Arrange - 同じ文字種の途中は境界ではない
        var text = "ello";
        var container = "HelloWorld";

        // Act
        var result = _sut.IsContainedWithBoundary(text, container);

        // Assert
        Assert.False(result);
    }

    #endregion

    #endregion

    #region FindMergeGroups Tests

    [Fact]
    public void FindMergeGroups_EmptyInputs_ReturnsEmpty()
    {
        // Arrange
        var chunks = Array.Empty<TextChunk>();
        var cloudTexts = new[] { "Hello World" };

        // Act
        var result = _sut.FindMergeGroups(chunks, cloudTexts);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void FindMergeGroups_NoCloudTexts_ReturnsEmpty()
    {
        // Arrange
        var chunks = new[] { CreateTextChunk(1, "Hello", new Rectangle(0, 0, 50, 20)) };
        var cloudTexts = Array.Empty<string>();

        // Act
        var result = _sut.FindMergeGroups(chunks, cloudTexts);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void FindMergeGroups_TwoChunksContainedInOneCloudText_ReturnsOneMergeGroup()
    {
        // Arrange
        var chunks = new[]
        {
            CreateTextChunk(1, "Hello", new Rectangle(0, 0, 50, 20)),
            CreateTextChunk(2, "World", new Rectangle(55, 0, 50, 20))
        };
        var cloudTexts = new[] { "Hello World" };

        // Act
        var result = _sut.FindMergeGroups(chunks, cloudTexts);

        // Assert
        Assert.Single(result);
        Assert.Equal(2, result[0].LocalChunks.Count);
        Assert.Equal(0, result[0].CloudTextIndex);
        Assert.Equal("Hello World", result[0].CloudText);
    }

    [Fact]
    public void FindMergeGroups_SingleChunk_ReturnsEmpty()
    {
        // Arrange - 1チャンクのみでは統合不要
        var chunks = new[]
        {
            CreateTextChunk(1, "Hello", new Rectangle(0, 0, 50, 20))
        };
        var cloudTexts = new[] { "Hello World" };

        // Act
        var result = _sut.FindMergeGroups(chunks, cloudTexts);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void FindMergeGroups_ChunksTooFarApart_ReturnsEmpty()
    {
        // Arrange - 距離が遠すぎるチャンク（100px以上離れている）
        var chunks = new[]
        {
            CreateTextChunk(1, "Hello", new Rectangle(0, 0, 50, 20)),
            CreateTextChunk(2, "World", new Rectangle(500, 0, 50, 20)) // 距離 > MaxProximityDistance (100px)
        };
        var cloudTexts = new[] { "Hello World" };

        // Act
        var result = _sut.FindMergeGroups(chunks, cloudTexts);

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region FindSplitInfo Tests

    [Fact]
    public void FindSplitInfo_SingleCloudText_ReturnsNull()
    {
        // Arrange - 1つのCloud AIテキストでは分割不要
        var chunk = CreateTextChunk(1, "Hello World", new Rectangle(0, 0, 100, 20));
        var cloudTextItems = new[] { CreateTranslatedTextItem("Hello", null) };

        // Act
        var result = _sut.FindSplitInfo(chunk, cloudTextItems);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void FindSplitInfo_TwoCloudTextsContainedInChunk_ReturnsSplitInfo()
    {
        // Arrange
        var chunk = CreateTextChunk(1, "Hello World", new Rectangle(0, 0, 100, 20));
        var cloudTextItems = new[]
        {
            CreateTranslatedTextItem("Hello", null),
            CreateTranslatedTextItem("World", null)
        };

        // Act
        var result = _sut.FindSplitInfo(chunk, cloudTextItems);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Segments.Count);
        Assert.Equal(chunk, result.LocalChunk);
    }

    [Fact]
    public void FindSplitInfo_CloudTextsNotContained_ReturnsNull()
    {
        // Arrange
        var chunk = CreateTextChunk(1, "Hello World", new Rectangle(0, 0, 100, 20));
        var cloudTextItems = new[]
        {
            CreateTranslatedTextItem("Goodbye", null),
            CreateTranslatedTextItem("Universe", null)
        };

        // Act
        var result = _sut.FindSplitInfo(chunk, cloudTextItems);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void FindSplitInfo_SegmentsAreSortedByStartIndex()
    {
        // Arrange - Cloud AIテキストは順番に検索されるため、出現順に並べる
        // FindSplitInfoはsearchStartIndexを使って重複を防ぐ仕様
        var chunk = CreateTextChunk(1, "Hello World Test", new Rectangle(0, 0, 150, 20));
        var cloudTextItems = new[]
        {
            CreateTranslatedTextItem("Hello", null),
            CreateTranslatedTextItem("World", null),
            CreateTranslatedTextItem("Test", null)
        };

        // Act
        var result = _sut.FindSplitInfo(chunk, cloudTextItems);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Segments.Count);
        // ソートされていることを確認（開始位置順）
        Assert.True(result.Segments[0].StartIndex < result.Segments[1].StartIndex);
        Assert.True(result.Segments[1].StartIndex < result.Segments[2].StartIndex);
    }

    [Fact]
    public void FindSplitInfo_TooShortCloudText_IsSkipped()
    {
        // Arrange - 3文字未満のCloud AIテキストは除外
        var chunk = CreateTextChunk(1, "A B C D", new Rectangle(0, 0, 100, 20));
        var cloudTextItems = new[]
        {
            CreateTranslatedTextItem("A", null),
            CreateTranslatedTextItem("B", null),
            CreateTranslatedTextItem("C", null),
            CreateTranslatedTextItem("D", null)
        };

        // Act
        var result = _sut.FindSplitInfo(chunk, cloudTextItems);

        // Assert
        Assert.Null(result); // 全て3文字未満なので分割対象なし
    }

    [Fact]
    public void FindSplitInfo_LongestMatchPriority_SelectsLongerText()
    {
        // Arrange - 最長一致優先テスト（Geminiレビュー反映）
        // "Hello" と "Hello World" の両方が候補にある場合、長い方を優先
        var chunk = CreateTextChunk(1, "Say Hello World Today", new Rectangle(0, 0, 200, 20));
        var cloudTextItems = new[]
        {
            CreateTranslatedTextItem("Hello", null),
            CreateTranslatedTextItem("Hello World", null),
            CreateTranslatedTextItem("Today", null)
        };

        // Act
        var result = _sut.FindSplitInfo(chunk, cloudTextItems);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Segments.Count);
        // "Hello World"（長い方）が選択され、"Hello"は除外される
        Assert.Contains(result.Segments, s => s.CloudText == "Hello World");
        Assert.Contains(result.Segments, s => s.CloudText == "Today");
        Assert.DoesNotContain(result.Segments, s => s.CloudText == "Hello");
    }

    [Fact]
    public void FindSplitInfo_OutOfOrderCloudTexts_FindsAll()
    {
        // Arrange - Cloud AIテキストが出現順でなくても全て検出
        var chunk = CreateTextChunk(1, "Hello World Test", new Rectangle(0, 0, 150, 20));
        var cloudTextItems = new[]
        {
            CreateTranslatedTextItem("Test", null),
            CreateTranslatedTextItem("Hello", null),
            CreateTranslatedTextItem("World", null)
        };

        // Act
        var result = _sut.FindSplitInfo(chunk, cloudTextItems);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Segments.Count);
        // 全て検出されている
        Assert.Contains(result.Segments, s => s.CloudText == "Hello");
        Assert.Contains(result.Segments, s => s.CloudText == "World");
        Assert.Contains(result.Segments, s => s.CloudText == "Test");
    }

    [Fact]
    public void FindSplitInfo_WithBoundingBox_PreservesCloudAiCoordinates()
    {
        // Arrange - Issue #275: BoundingBoxがある場合はCloud AI座標を保持
        var chunk = CreateTextChunk(1, "Hello World", new Rectangle(0, 0, 100, 20));
        var cloudTextItems = new[]
        {
            CreateTranslatedTextItem("Hello", new Int32Rect(10, 50, 40, 15)),
            CreateTranslatedTextItem("World", new Int32Rect(10, 70, 40, 15))  // 縦配置
        };

        // Act
        var result = _sut.FindSplitInfo(chunk, cloudTextItems);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Segments.Count);

        // BoundingBoxが保持されていることを確認
        var helloSegment = result.Segments.First(s => s.CloudText == "Hello");
        var worldSegment = result.Segments.First(s => s.CloudText == "World");

        Assert.True(helloSegment.CloudBoundingBox.HasValue);
        Assert.True(worldSegment.CloudBoundingBox.HasValue);
        Assert.Equal(50, helloSegment.CloudBoundingBox!.Value.Y);  // Y=50
        Assert.Equal(70, worldSegment.CloudBoundingBox!.Value.Y);  // Y=70（縦配置）
    }

    [Fact]
    public void FindSplitInfo_WithoutBoundingBox_ReturnsNullBoundingBox()
    {
        // Arrange - BoundingBoxがない場合はnull
        var chunk = CreateTextChunk(1, "Hello World", new Rectangle(0, 0, 100, 20));
        var cloudTextItems = new[]
        {
            CreateTranslatedTextItem("Hello", null),
            CreateTranslatedTextItem("World", null)
        };

        // Act
        var result = _sut.FindSplitInfo(chunk, cloudTextItems);

        // Assert
        Assert.NotNull(result);
        Assert.All(result.Segments, s => Assert.False(s.CloudBoundingBox.HasValue));
    }

    #endregion

    #region Union-Find Clustering Tests (Gemini Review)

    [Fact]
    public void FindMergeGroups_TransitiveConnection_MergesAll()
    {
        // Arrange - A-B近い、B-C近い、A-C遠いケース（Geminiレビュー反映）
        // Union-Findにより、B経由で全て同一クラスタになる（推移的接続）
        var chunks = new[]
        {
            CreateTextChunk(1, "Hello", new Rectangle(0, 0, 50, 20)),     // A
            CreateTextChunk(2, "World", new Rectangle(60, 0, 50, 20)),   // B (Aから10px)
            CreateTextChunk(3, "Test", new Rectangle(200, 0, 50, 20))    // C (Bから90px, Aからは150px)
        };
        // A-B=10px ✓, B-C=90px ✓, A-C=150px ✗
        // Union-Find: A-B接続、B-C接続 → A-B-C全て同一クラスタ
        var cloudTexts = new[] { "Hello World Test" };

        // Act
        var result = _sut.FindMergeGroups(chunks, cloudTexts);

        // Assert - Union-Findにより全て統合（B経由の推移的接続）
        Assert.Single(result);
        Assert.Equal(3, result[0].LocalChunks.Count);
    }

    [Fact]
    public void FindMergeGroups_IndirectConnection_MergesViaBridge()
    {
        // Arrange - A-B近い、B-C近い、A-C遠いケース
        // Union-Findにより、B経由で全て同一クラスタになる
        var chunks = new[]
        {
            CreateTextChunk(1, "Hello", new Rectangle(0, 0, 50, 20)),     // A
            CreateTextChunk(2, "World", new Rectangle(80, 0, 50, 20)),   // B (A近く、C近く)
            CreateTextChunk(3, "Test", new Rectangle(160, 0, 50, 20))    // C (Bに近い)
        };
        // A-B=80-50=30px, B-C=160-130=30px, A-C=160-50=110px > 100px
        // 旧実装ではA-Bは統合、Cは隣接比較で30pxなので統合される
        // Union-FindでもA-B-Cは同一クラスタ
        var cloudTexts = new[] { "Hello World Test" };

        // Act
        var result = _sut.FindMergeGroups(chunks, cloudTexts);

        // Assert - 全て統合される（B経由の間接接続）
        Assert.Single(result);
        Assert.Equal(3, result[0].LocalChunks.Count);
    }

    [Fact]
    public void FindMergeGroups_TwoSeparateClusters_ReturnsOnlyLargestCluster()
    {
        // Arrange - 複数の分離したクラスタがある場合、最大のものだけを返す（Geminiレビュー反映）
        // Cluster 1 (3 chunks) - これが最大クラスタ
        // Cluster 2 (2 chunks) - こちらは除外される
        var chunks = new[]
        {
            // Cluster 1: A-B-C (各10px間隔)
            CreateTextChunk(1, "AAA", new Rectangle(0, 0, 10, 10)),
            CreateTextChunk(2, "BBB", new Rectangle(20, 0, 10, 10)),
            CreateTextChunk(3, "CCC", new Rectangle(40, 0, 10, 10)),
            // Cluster 2: D-E (500px離れた位置)
            CreateTextChunk(4, "DDD", new Rectangle(500, 500, 10, 10)),
            CreateTextChunk(5, "EEE", new Rectangle(520, 500, 10, 10))
        };
        var cloudTexts = new[] { "AAA BBB CCC DDD EEE" };

        // Act
        var result = _sut.FindMergeGroups(chunks, cloudTexts);

        // Assert
        Assert.Single(result);
        // 最大クラスタである3つのチャンクのみが返される
        Assert.Equal(3, result[0].LocalChunks.Count);
        // Cluster 1のチャンクのみが含まれる
        Assert.True(result[0].LocalChunks.All(c => c.ChunkId <= 3));
        Assert.DoesNotContain(result[0].LocalChunks, c => c.ChunkId > 3);
    }

    #endregion

    #region Helper Methods

    private static TextChunk CreateTextChunk(int chunkId, string text, Rectangle bounds)
    {
        return new TextChunk
        {
            ChunkId = chunkId,
            CombinedText = text,
            CombinedBounds = bounds,
            TextResults = [],
            SourceWindowHandle = IntPtr.Zero
        };
    }

    /// <summary>
    /// テスト用TranslatedTextItem作成ヘルパー（Issue #275）
    /// </summary>
    private static TranslatedTextItem CreateTranslatedTextItem(string original, Int32Rect? boundingBox)
    {
        return new TranslatedTextItem
        {
            Original = original,
            Translation = $"[翻訳]{original}",
            BoundingBox = boundingBox
        };
    }

    #endregion
}
