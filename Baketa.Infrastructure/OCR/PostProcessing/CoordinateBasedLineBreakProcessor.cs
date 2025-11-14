using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using Baketa.Core.Abstractions.Translation;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.OCR.PostProcessing;

/// <summary>
/// 座標情報ベースの改行処理プロセッサー
/// OCR精度向上ロードマップ Phase 1 - 高優先度実装
/// </summary>
public sealed class CoordinateBasedLineBreakProcessor(
    ILogger<CoordinateBasedLineBreakProcessor> logger,
    LineBreakSettings? settings = null)
{
    private readonly ILogger<CoordinateBasedLineBreakProcessor> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    // 改行判定のための設定値
    private readonly LineBreakSettings _settings = settings ?? LineBreakSettings.Default;

    /// <summary>
    /// TextChunkリストの改行処理を最適化
    /// </summary>
    public string ProcessLineBreaks(IReadOnlyList<TextChunk> textChunks)
    {
        if (textChunks == null || textChunks.Count == 0)
            return string.Empty;

        _logger.LogDebug("座標ベース改行処理開始: {ChunkCount}個のチャンク", textChunks.Count);

        // Y座標でソートして行にグループ化
        var lines = GroupChunksIntoLines(textChunks);

        // 各行を処理して最終テキストを構築
        var result = new StringBuilder();
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var lineText = MergeLineChunks(line);

            if (i > 0)
            {
                // 前の行との改行判定
                var shouldBreak = ShouldInsertLineBreak(
                    lines[i - 1],
                    line,
                    result.ToString(),
                    lineText);

                if (shouldBreak)
                {
                    result.Append('\n');
                    _logger.LogDebug("行 {LineNumber}: 改行挿入", i + 1);
                }
                else
                {
                    _logger.LogDebug("行 {LineNumber}: 前行と結合", i + 1);
                }
            }

            result.Append(lineText);
        }

        var processedText = result.ToString();
        _logger.LogDebug("座標ベース改行処理完了: {OriginalLines}行 → {ProcessedLines}行",
            lines.Count, processedText.Split('\n').Length);

        return processedText;
    }

    /// <summary>
    /// TextChunkを行単位でグループ化
    /// </summary>
    private List<List<TextChunk>> GroupChunksIntoLines(IReadOnlyList<TextChunk> chunks)
    {
        var lines = new List<List<TextChunk>>();
        var processed = new HashSet<TextChunk>();

        // Y座標でソート
        var sortedChunks = chunks.OrderBy(c => c.CombinedBounds.Y).ToList();

        foreach (var chunk in sortedChunks)
        {
            if (processed.Contains(chunk))
                continue;

            // 新しい行を開始
            var line = new List<TextChunk> { chunk };
            processed.Add(chunk);

            // 同じ行に属する他のチャンクを検索
            var baseY = chunk.CombinedBounds.Y + chunk.CombinedBounds.Height / 2f;
            var threshold = chunk.CombinedBounds.Height * _settings.SameLineThreshold;

            foreach (var other in sortedChunks)
            {
                if (processed.Contains(other))
                    continue;

                var otherY = other.CombinedBounds.Y + other.CombinedBounds.Height / 2f;

                // Y座標の差が閾値以内なら同じ行
                if (Math.Abs(baseY - otherY) <= threshold)
                {
                    line.Add(other);
                    processed.Add(other);
                }
            }

            // X座標でソート
            line.Sort((a, b) => a.CombinedBounds.X.CompareTo(b.CombinedBounds.X));
            lines.Add(line);
        }

        return lines;
    }

    /// <summary>
    /// 同一行内のチャンクを結合
    /// </summary>
    private string MergeLineChunks(List<TextChunk> lineChunks)
    {
        if (lineChunks.Count == 0)
            return string.Empty;

        if (lineChunks.Count == 1)
            return lineChunks[0].CombinedText;

        var result = new StringBuilder();

        for (int i = 0; i < lineChunks.Count; i++)
        {
            var chunk = lineChunks[i];
            result.Append(chunk.CombinedText);

            // 次のチャンクとの間隔を確認
            if (i < lineChunks.Count - 1)
            {
                var nextChunk = lineChunks[i + 1];
                var gap = nextChunk.CombinedBounds.X - chunk.CombinedBounds.Right;

                // 平均文字幅を計算
                var avgCharWidth = CalculateAverageCharacterWidth(chunk, nextChunk);

                // 文字幅の閾値以上の間隔があればスペースを挿入
                if (gap > avgCharWidth * _settings.SpaceInsertionThreshold)
                {
                    result.Append(' ');
                    _logger.LogTrace("スペース挿入: チャンク間隔 {Gap}px > 閾値 {Threshold}px",
                        gap, avgCharWidth * _settings.SpaceInsertionThreshold);
                }
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// 改行を挿入すべきかを判定（高度な座標分析）
    /// </summary>
    private bool ShouldInsertLineBreak(
        List<TextChunk> previousLine,
        List<TextChunk> currentLine,
        string previousText,
        string currentText)
    {
        // 1. 垂直距離分析
        var verticalGap = CalculateVerticalGap(previousLine, currentLine);
        var avgHeight = CalculateAverageHeight(previousLine, currentLine);
        var verticalRatio = verticalGap / avgHeight;

        _logger.LogTrace("垂直距離分析: 間隔={Gap}px, 平均高さ={Height}px, 比率={Ratio:F2}",
            verticalGap, avgHeight, verticalRatio);

        // 明確な段落区切り（高さの1.2倍以上）
        if (verticalRatio > _settings.ParagraphBreakThreshold)
        {
            _logger.LogDebug("段落区切り検出: 垂直比率 {Ratio:F2} > 閾値 {Threshold}",
                verticalRatio, _settings.ParagraphBreakThreshold);
            return true;
        }

        // 2. 水平位置分析（インデント検出）
        var indentSize = CalculateIndentation(previousLine, currentLine);
        if (indentSize > _settings.IndentThreshold)
        {
            _logger.LogDebug("インデント検出: {IndentSize}px > 閾値 {Threshold}px",
                indentSize, _settings.IndentThreshold);
            return true;
        }

        // 3. テキスト内容分析
        if (IsNaturalBreakPoint(previousText, currentText))
        {
            _logger.LogDebug("自然な改行位置を検出");
            return true;
        }

        // 4. 行の長さ分析
        var lengthRatio = CalculateLineLengthRatio(previousLine, currentLine);
        if (lengthRatio < _settings.ShortLineThreshold)
        {
            _logger.LogDebug("短い行を検出: 長さ比率 {Ratio:F2} < 閾値 {Threshold}",
                lengthRatio, _settings.ShortLineThreshold);
            return true;
        }

        // デフォルト: 改行を保持（自動折り返しと意図的改行を区別）
        return verticalRatio > _settings.MinimumLineBreakThreshold;
    }

    /// <summary>
    /// 垂直方向の間隔を計算
    /// </summary>
    private float CalculateVerticalGap(List<TextChunk> previousLine, List<TextChunk> currentLine)
    {
        if (previousLine.Count == 0 || currentLine.Count == 0)
            return 0;

        var prevBottom = previousLine.Max(c => c.CombinedBounds.Bottom);
        var currentTop = currentLine.Min(c => c.CombinedBounds.Top);

        return Math.Max(0, currentTop - prevBottom);
    }

    /// <summary>
    /// 平均高さを計算
    /// </summary>
    private float CalculateAverageHeight(List<TextChunk> line1, List<TextChunk> line2)
    {
        var allChunks = line1.Concat(line2);
        return (float)allChunks.Average(c => c.CombinedBounds.Height);
    }

    /// <summary>
    /// インデントサイズを計算
    /// </summary>
    private float CalculateIndentation(List<TextChunk> previousLine, List<TextChunk> currentLine)
    {
        if (previousLine.Count == 0 || currentLine.Count == 0)
            return 0;

        var prevLeft = previousLine.Min(c => c.CombinedBounds.Left);
        var currentLeft = currentLine.Min(c => c.CombinedBounds.Left);

        return Math.Max(0, currentLeft - prevLeft);
    }

    /// <summary>
    /// 自然な改行位置かどうかを判定
    /// </summary>
    private bool IsNaturalBreakPoint(string previousText, string currentText)
    {
        if (string.IsNullOrWhiteSpace(previousText) || string.IsNullOrWhiteSpace(currentText))
            return true;

        var lastChar = previousText.TrimEnd().LastOrDefault();
        var firstChar = currentText.TrimStart().FirstOrDefault();

        // 文末記号で終わっている
        if ("。！？．!?」』）)".Contains(lastChar))
            return true;

        // 箇条書きの開始
        if ("・◆◇■□●○123456789".Contains(firstChar))
            return true;

        // 継続を示す記号で終わっていない
        if (!"、,「『（(".Contains(lastChar))
            return true;

        return false;
    }

    /// <summary>
    /// 行の長さ比率を計算
    /// </summary>
    private float CalculateLineLengthRatio(List<TextChunk> previousLine, List<TextChunk> currentLine)
    {
        if (previousLine.Count == 0 || currentLine.Count == 0)
            return 1.0f;

        var prevWidth = previousLine.Max(c => c.CombinedBounds.Right) - previousLine.Min(c => c.CombinedBounds.Left);
        var maxWidth = Math.Max(prevWidth, 1);

        return prevWidth / (float)maxWidth;
    }

    /// <summary>
    /// 平均文字幅を計算
    /// </summary>
    private float CalculateAverageCharacterWidth(TextChunk chunk1, TextChunk chunk2)
    {
        var totalWidth = chunk1.CombinedBounds.Width + chunk2.CombinedBounds.Width;
        var totalChars = chunk1.CombinedText.Length + chunk2.CombinedText.Length;

        if (totalChars == 0)
            return 10; // デフォルト値

        return totalWidth / (float)totalChars;
    }
}

/// <summary>
/// 改行処理の設定
/// </summary>
public sealed class LineBreakSettings
{
    /// <summary>同一行判定の閾値（文字高さに対する比率）</summary>
    public float SameLineThreshold { get; init; } = 0.5f;

    /// <summary>スペース挿入の閾値（平均文字幅に対する比率）</summary>
    public float SpaceInsertionThreshold { get; init; } = 0.5f;

    /// <summary>段落区切り判定の閾値（行間/文字高さ）</summary>
    public float ParagraphBreakThreshold { get; init; } = 1.2f;

    /// <summary>最小改行判定の閾値</summary>
    public float MinimumLineBreakThreshold { get; init; } = 0.8f;

    /// <summary>インデント判定の閾値（ピクセル）</summary>
    public float IndentThreshold { get; init; } = 20f;

    /// <summary>短い行判定の閾値（前行に対する比率）</summary>
    public float ShortLineThreshold { get; init; } = 0.7f;

    /// <summary>デフォルト設定</summary>
    public static LineBreakSettings Default => new();

    /// <summary>厳密な改行保持設定（ゲーム向け）</summary>
    public static LineBreakSettings Strict => new()
    {
        SameLineThreshold = 0.3f,
        ParagraphBreakThreshold = 1.0f,
        MinimumLineBreakThreshold = 0.6f,
        ShortLineThreshold = 0.8f
    };

    /// <summary>緩い改行結合設定（小説向け）</summary>
    public static LineBreakSettings Relaxed => new()
    {
        SameLineThreshold = 0.7f,
        ParagraphBreakThreshold = 1.5f,
        MinimumLineBreakThreshold = 1.0f,
        ShortLineThreshold = 0.5f
    };
}
