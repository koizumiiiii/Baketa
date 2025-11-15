using System.Drawing;
using Baketa.Core.Abstractions.OCR;

namespace Baketa.Infrastructure.OCR.TextGrouping;

/// <summary>
/// OCR結果のテキスト領域をグループ化するサービス
/// 画面レイアウト情報を活用して文章のまとまりを検出
/// </summary>
public class OcrTextGroupingService
{
    /// <summary>
    /// テキスト領域を文章グループにまとめる
    /// </summary>
    /// <param name="textRegions">OCRで検出されたテキスト領域</param>
    /// <param name="options">グループ化オプション</param>
    /// <returns>グループ化されたテキスト</returns>
    public List<TextGroup> GroupTextRegions(IReadOnlyList<OcrTextRegion> textRegions, TextGroupingOptions? options = null)
    {
        if (textRegions.Count == 0)
            return [];

        options ??= new TextGroupingOptions();

        // 1. Y座標でソート（上から下へ）
        var sortedRegions = textRegions
            .OrderBy(r => r.Bounds.Y)
            .ThenBy(r => r.Bounds.X)
            .ToList();

        // 2. 行レベルでグループ化
        var lineGroups = GroupIntoLines(sortedRegions, options);

        // 3. 段落レベルでグループ化
        var paragraphGroups = GroupIntoParagraphs(lineGroups, options);

        return paragraphGroups;
    }

    /// <summary>
    /// テキスト領域を行単位でグループ化
    /// </summary>
    private List<LineGroup> GroupIntoLines(List<OcrTextRegion> regions, TextGroupingOptions options)
    {
        var lines = new List<LineGroup>();
        var currentLine = new List<OcrTextRegion>();

        foreach (var region in regions)
        {
            if (currentLine.Count == 0)
            {
                currentLine.Add(region);
                continue;
            }

            var lastRegion = currentLine.Last();
            var verticalDistance = Math.Abs(region.Bounds.Y - lastRegion.Bounds.Y);
            var averageHeight = (region.Bounds.Height + lastRegion.Bounds.Height) / 2.0;

            // 同じ行判定：垂直距離が平均文字高の半分以下
            if (verticalDistance <= averageHeight * options.SameLineThreshold)
            {
                currentLine.Add(region);
            }
            else
            {
                // 新しい行を開始
                if (currentLine.Count > 0)
                {
                    lines.Add(new LineGroup(currentLine));
                }
                currentLine = [region];
            }
        }

        // 最後の行を追加
        if (currentLine.Count > 0)
        {
            lines.Add(new LineGroup(currentLine));
        }

        return lines;
    }

    /// <summary>
    /// 行グループを段落単位でグループ化
    /// </summary>
    private List<TextGroup> GroupIntoParagraphs(List<LineGroup> lines, TextGroupingOptions options)
    {
        var paragraphs = new List<TextGroup>();
        var currentParagraph = new List<LineGroup>();

        foreach (var line in lines)
        {
            if (currentParagraph.Count == 0)
            {
                currentParagraph.Add(line);
                continue;
            }

            var lastLine = currentParagraph.Last();
            var verticalGap = line.TopY - lastLine.BottomY;
            var averageLineHeight = (line.Height + lastLine.Height) / 2.0;

            // 段落区切り判定：行間が平均行高の一定倍率以上
            if (verticalGap >= averageLineHeight * options.ParagraphSeparationThreshold)
            {
                // 新しい段落を開始
                if (currentParagraph.Count > 0)
                {
                    paragraphs.Add(new TextGroup(currentParagraph));
                }
                currentParagraph = [line];
            }
            else
            {
                currentParagraph.Add(line);
            }
        }

        // 最後の段落を追加
        if (currentParagraph.Count > 0)
        {
            paragraphs.Add(new TextGroup(currentParagraph));
        }

        return paragraphs;
    }
}

/// <summary>
/// テキストグループ化のオプション
/// </summary>
public class TextGroupingOptions
{
    /// <summary>
    /// 同じ行と判定する垂直距離の閾値（平均文字高に対する比率）
    /// </summary>
    public double SameLineThreshold { get; set; } = 0.5;

    /// <summary>
    /// 段落区切りと判定する行間の閾値（平均行高に対する比率）
    /// </summary>
    public double ParagraphSeparationThreshold { get; set; } = 1.5;

    /// <summary>
    /// 単語間スペースの最小距離（平均文字幅に対する比率）
    /// </summary>
    public double WordSpacingThreshold { get; set; } = 0.3;
}

/// <summary>
/// 行レベルのテキストグループ
/// </summary>
public class LineGroup
{
    public List<OcrTextRegion> Regions { get; }
    public Rectangle Bounds { get; }
    public int TopY => Bounds.Top;
    public int BottomY => Bounds.Bottom;
    public int Height => Bounds.Height;

    public LineGroup(List<OcrTextRegion> regions)
    {
        Regions = [.. regions.OrderBy(r => r.Bounds.X)];

        if (regions.Count > 0)
        {
            var minX = regions.Min(r => r.Bounds.Left);
            var maxX = regions.Max(r => r.Bounds.Right);
            var minY = regions.Min(r => r.Bounds.Top);
            var maxY = regions.Max(r => r.Bounds.Bottom);

            Bounds = new Rectangle(minX, minY, maxX - minX, maxY - minY);
        }
        else
        {
            Bounds = Rectangle.Empty;
        }
    }

    /// <summary>
    /// 行内のテキストを結合（適切なスペース挿入あり）
    /// </summary>
    public string GetText()
    {
        if (Regions.Count == 0)
            return string.Empty;

        if (Regions.Count == 1)
            return Regions[0].Text;

        // 横方向に並んだテキストを適切な間隔で結合
        var result = new List<string>();
        var averageCharWidth = Regions.Average(r => r.Bounds.Width / Math.Max(1, r.Text.Length));

        for (int i = 0; i < Regions.Count; i++)
        {
            result.Add(Regions[i].Text);

            if (i < Regions.Count - 1)
            {
                var currentRegion = Regions[i];
                var nextRegion = Regions[i + 1];
                var horizontalGap = nextRegion.Bounds.Left - currentRegion.Bounds.Right;

                // 文字幅の0.3倍以上の間隔がある場合はスペースを挿入
                if (horizontalGap >= averageCharWidth * 0.3)
                {
                    result.Add(" ");
                }
            }
        }

        return string.Join("", result);
    }
}

/// <summary>
/// 段落レベルのテキストグループ
/// </summary>
public class TextGroup
{
    public List<LineGroup> Lines { get; }
    public Rectangle Bounds { get; }

    public TextGroup(List<LineGroup> lines)
    {
        Lines = lines;

        if (lines.Count > 0)
        {
            var minX = lines.Min(l => l.Bounds.Left);
            var maxX = lines.Max(l => l.Bounds.Right);
            var minY = lines.Min(l => l.Bounds.Top);
            var maxY = lines.Max(l => l.Bounds.Bottom);

            Bounds = new Rectangle(minX, minY, maxX - minX, maxY - minY);
        }
        else
        {
            Bounds = Rectangle.Empty;
        }
    }

    /// <summary>
    /// 段落内のテキストを結合（改行保持）
    /// </summary>
    public string GetText()
    {
        return string.Join(Environment.NewLine, Lines.Select(l => l.GetText()));
    }

    /// <summary>
    /// 段落内のテキストを1行に結合（スペース区切り）
    /// </summary>
    public string GetSingleLineText()
    {
        return string.Join(" ", Lines.Select(l => l.GetText()));
    }
}
