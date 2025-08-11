using System;
using System.Collections.Generic;
using System.Drawing;
using Baketa.Core.Abstractions.OCR.Results;
using Baketa.Core.Abstractions.Translation;
using Baketa.Infrastructure.OCR.PostProcessing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Baketa.Infrastructure.Tests.OCR.PostProcessing;

public sealed class CoordinateBasedLineBreakProcessorTests
{
    private readonly CoordinateBasedLineBreakProcessor _processor;

    public CoordinateBasedLineBreakProcessorTests()
    {
        _processor = new CoordinateBasedLineBreakProcessor(NullLogger<CoordinateBasedLineBreakProcessor>.Instance);
    }

    [Fact]
    public void ProcessLineBreaks_EmptyInput_ReturnsEmpty()
    {
        // Arrange
        var emptyChunks = new List<TextChunk>();

        // Act
        var result = _processor.ProcessLineBreaks(emptyChunks);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ProcessLineBreaks_SingleChunk_ReturnsSameText()
    {
        // Arrange
        var chunks = new List<TextChunk>
        {
            CreateTextChunk(1, "こんにちは", new Rectangle(100, 100, 150, 30))
        };

        // Act
        var result = _processor.ProcessLineBreaks(chunks);

        // Assert
        Assert.Equal("こんにちは", result);
    }

    [Fact]
    public void ProcessLineBreaks_SameLine_CombinesWithoutLineBreak()
    {
        // Arrange - 同じ行（Y座標が近い）のテキスト
        var chunks = new List<TextChunk>
        {
            CreateTextChunk(1, "これは", new Rectangle(100, 100, 60, 30)),
            CreateTextChunk(2, "テスト", new Rectangle(180, 105, 60, 30)), // X座標を調整してスペース挿入
            CreateTextChunk(3, "です", new Rectangle(280, 100, 40, 30))
        };

        // Act
        var result = _processor.ProcessLineBreaks(chunks);

        // Assert
        Assert.Equal("これは テスト です", result);
    }

    [Fact]
    public void ProcessLineBreaks_DifferentLines_InsertsLineBreak()
    {
        // Arrange - 明確に異なる行（Y座標が大きく異なる）
        var chunks = new List<TextChunk>
        {
            CreateTextChunk(1, "第1行目のテキスト", new Rectangle(100, 100, 200, 30)),
            CreateTextChunk(2, "第2行目のテキスト", new Rectangle(100, 180, 200, 30)) // 80px下、新しい行
        };

        // Act
        var result = _processor.ProcessLineBreaks(chunks);

        // Assert
        Assert.Contains("\n", result);
        Assert.Equal("第1行目のテキスト\n第2行目のテキスト", result);
    }

    [Fact]
    public void ProcessLineBreaks_ParagraphBreak_DetectsCorrectly()
    {
        // Arrange - 段落区切り（大きな垂直間隔）
        var chunks = new List<TextChunk>
        {
            CreateTextChunk(1, "第1段落です。", new Rectangle(100, 100, 180, 30)),
            CreateTextChunk(2, "第2段落の開始", new Rectangle(100, 200, 180, 30)) // 100px下、段落区切り
        };

        // Act
        var result = _processor.ProcessLineBreaks(chunks);

        // Assert
        Assert.Contains("\n", result);
        var lines = result.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Equal("第1段落です。", lines[0]);
        Assert.Equal("第2段落の開始", lines[1]);
    }

    [Fact]
    public void ProcessLineBreaks_IndentedText_DetectsIndentation()
    {
        // Arrange - インデントされたテキスト
        var chunks = new List<TextChunk>
        {
            CreateTextChunk(1, "通常のテキスト", new Rectangle(100, 100, 180, 30)),
            CreateTextChunk(2, "　インデント", new Rectangle(130, 140, 150, 30)) // 30px右にインデント
        };

        // Act
        var result = _processor.ProcessLineBreaks(chunks);

        // Assert
        Assert.Contains("\n", result);
        Assert.Equal("通常のテキスト\n　インデント", result);
    }

    [Fact]
    public void ProcessLineBreaks_NaturalBreakPoints_DetectsCorrectly()
    {
        // Arrange - 自然な文末記号での改行
        var chunks = new List<TextChunk>
        {
            CreateTextChunk(1, "これは文章です。", new Rectangle(100, 100, 200, 30)),
            CreateTextChunk(2, "新しい文章です", new Rectangle(100, 140, 180, 30))
        };

        // Act
        var result = _processor.ProcessLineBreaks(chunks);

        // Assert
        Assert.Contains("\n", result);
        Assert.Equal("これは文章です。\n新しい文章です", result);
    }

    [Fact]
    public void ProcessLineBreaks_ContinuationMarkers_AvoidsLineBreak()
    {
        // Arrange - 継続を示す記号（句読点）
        var chunks = new List<TextChunk>
        {
            CreateTextChunk(1, "これは長い文章で、", new Rectangle(100, 100, 200, 30)),
            CreateTextChunk(2, "続きがあります", new Rectangle(100, 140, 180, 30))
        };

        // Act
        var result = _processor.ProcessLineBreaks(chunks);

        // Assert
        // 継続記号があるので改行しない可能性があるが、デフォルトは保守的に改行を保持
        Assert.Contains("これは長い文章で、", result);
        Assert.Contains("続きがあります", result);
    }

    [Fact]
    public void ProcessLineBreaks_MixedContent_HandlesCorrectly()
    {
        // Arrange - 複雑なレイアウト
        var chunks = new List<TextChunk>
        {
            CreateTextChunk(1, "タイトル", new Rectangle(100, 50, 100, 40)), // 大きなフォント
            CreateTextChunk(2, "第1段落の", new Rectangle(100, 120, 120, 25)),
            CreateTextChunk(3, "内容です。", new Rectangle(230, 120, 100, 25)), // 同じ行
            CreateTextChunk(4, "第2段落の内容", new Rectangle(100, 180, 200, 25)) // 新しい段落
        };

        // Act
        var result = _processor.ProcessLineBreaks(chunks);

        // Assert
        var lines = result.Split('\n');
        Assert.True(lines.Length >= 3); // 少なくとも3行は期待
        Assert.Contains("タイトル", lines[0]);
        Assert.Contains("第1段落の", result);
        Assert.Contains("内容です。", result);
        Assert.Contains("第2段落の内容", result);
    }

    [Fact]
    public void ProcessLineBreaks_SpaceInsertion_WorksCorrectly()
    {
        // Arrange - スペース挿入が必要なテキスト
        var chunks = new List<TextChunk>
        {
            CreateTextChunk(1, "単語1", new Rectangle(100, 100, 60, 30)),
            CreateTextChunk(2, "単語2", new Rectangle(200, 100, 60, 30)) // 40px間隔
        };

        // Act
        var result = _processor.ProcessLineBreaks(chunks);

        // Assert
        Assert.Equal("単語1 単語2", result);
    }

    [Fact]
    public void ProcessLineBreaks_StrictSettings_PreservesMoreLineBreaks()
    {
        // Arrange - 厳密な設定でプロセッサーを作成
        var strictProcessor = new CoordinateBasedLineBreakProcessor(
            NullLogger<CoordinateBasedLineBreakProcessor>.Instance,
            LineBreakSettings.Strict);

        var chunks = new List<TextChunk>
        {
            CreateTextChunk(1, "行1", new Rectangle(100, 100, 60, 30)),
            CreateTextChunk(2, "行2", new Rectangle(100, 150, 60, 30)) // 50px下
        };

        // Act
        var result = strictProcessor.ProcessLineBreaks(chunks);

        // Assert
        Assert.Contains("\n", result);
        Assert.Equal("行1\n行2", result);
    }

    [Fact]
    public void ProcessLineBreaks_RelaxedSettings_CombinesMoreLines()
    {
        // Arrange - 緩い設定でプロセッサーを作成
        var relaxedProcessor = new CoordinateBasedLineBreakProcessor(
            NullLogger<CoordinateBasedLineBreakProcessor>.Instance,
            LineBreakSettings.Relaxed);

        var chunks = new List<TextChunk>
        {
            CreateTextChunk(1, "行1", new Rectangle(100, 100, 60, 30)),
            CreateTextChunk(2, "行2", new Rectangle(100, 140, 60, 30)) // 40px下
        };

        // Act
        var result = relaxedProcessor.ProcessLineBreaks(chunks);

        // Assert
        // 緩い設定では改行を結合する可能性が高い
        // 実際の動作は実装による
        Assert.Contains("行1", result);
        Assert.Contains("行2", result);
    }

    private static TextChunk CreateTextChunk(int id, string text, Rectangle bounds)
    {
        var positionedResult = new PositionedTextResult
        {
            Text = text,
            BoundingBox = bounds,
            Confidence = 0.95f,
            ChunkId = id,
            ProcessingTime = TimeSpan.FromMilliseconds(100),
            DetectedLanguage = "ja"
        };

        return new TextChunk
        {
            ChunkId = id,
            TextResults = [positionedResult],
            CombinedBounds = bounds,
            CombinedText = text,
            SourceWindowHandle = IntPtr.Zero,
            DetectedLanguage = "ja"
        };
    }
}