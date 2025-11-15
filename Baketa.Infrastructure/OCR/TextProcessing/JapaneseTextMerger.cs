using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Baketa.Core.Abstractions.OCR;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.OCR.TextProcessing;

/// <summary>
/// 日本語テキストに特化した結合アルゴリズム
/// </summary>
public sealed class JapaneseTextMerger(ILogger<JapaneseTextMerger> logger) : ITextMerger
{
    private readonly ILogger<JapaneseTextMerger> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    // 日本語の文末文字パターン
    private static readonly HashSet<char> SentenceEndings =
    [
        '。', '！', '？', '．', '!', '?', '」', '』', '）', ')'
    ];

    // 継続が期待される文字パターン
    private static readonly HashSet<char> ContinuationExpected =
    [
        '、', ',', '「', '『', '（', '(', '・', '：', ':'
    ];

    /// <summary>
    /// テキスト領域を適切に結合して文章を再構成
    /// </summary>
    public string MergeTextRegions(IReadOnlyList<OcrTextRegion> textRegions)
    {
        if (textRegions == null || textRegions.Count == 0)
        {
            return string.Empty;
        }

        _logger.LogDebug("テキスト結合開始: {Count}個の領域", textRegions.Count);

        // 詳細なテキスト領域情報をログ出力
        // 直接ファイル書き込みでテキスト結合開始を記録
        try
        {
            // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
        }
        catch (Exception fileEx)
        {
            System.Diagnostics.Debug.WriteLine($"JapaneseTextMerger ファイル書き込みエラー: {fileEx.Message}");
        }

        for (int i = 0; i < textRegions.Count; i++)
        {
            var region = textRegions[i];
            _logger.LogInformation("🔤 TextMerger入力[{Index}]: Text='{Text}' | Bounds=({X},{Y},{Width},{Height}) | Confidence={Confidence:F3}",
                i, region.Text, region.Bounds.X, region.Bounds.Y, region.Bounds.Width, region.Bounds.Height, region.Confidence);

            // 直接ファイル書き込みでテキスト結合の詳細を記録
            try
            {
                // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化 | Confidence={region.Confidence:F3}{Environment.NewLine}");
            }
            catch (Exception fileEx)
            {
                System.Diagnostics.Debug.WriteLine($"JapaneseTextMerger 詳細ログ書き込みエラー: {fileEx.Message}");
            }
        }

        // 位置順にソート（上から下、左から右）
        var sortedRegions = textRegions
            .OrderBy(r => r.Bounds.Y)
            .ThenBy(r => r.Bounds.X)
            .ToList();

        // 行ごとにグループ化
        var lines = GroupTextRegionsByLine(sortedRegions);

        // 行グループ化結果をログ出力
        _logger.LogInformation("📑 行グループ化結果: {LineCount}行に分割", lines.Count);

        // 直接ファイル書き込みで行グループ化結果を記録
        try
        {
            // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
        }
        catch (Exception fileEx)
        {
            System.Diagnostics.Debug.WriteLine($"JapaneseTextMerger 行グループ化ログ書き込みエラー: {fileEx.Message}");
        }

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var lineTexts = string.Join(" | ", line.Select(r => $"'{r.Text}'"));
            _logger.LogInformation("📑 行{LineIndex}: {RegionCount}個のリージョン - {Texts}",
                i + 1, line.Count, lineTexts);

            // 直接ファイル書き込みで行詳細を記録
            try
            {
                // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            }
            catch (Exception fileEx)
            {
                System.Diagnostics.Debug.WriteLine($"JapaneseTextMerger 行詳細ログ書き込みエラー: {fileEx.Message}");
            }
        }

        // 各行内でテキストを結合し、行間の結合判定を行う
        var result = new StringBuilder();
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var lineText = MergeLineRegions(line);

            if (i > 0)
            {
                // 前の行との結合判定
                var shouldMerge = ShouldMergeWithPreviousLine(result.ToString(), lineText, lines[i - 1], line);
                _logger.LogInformation("🔗 行結合判定[行{LineNumber}]: 前行='{PrevText}' 現行='{CurrText}' 結合={ShouldMerge}",
                    i + 1, result.ToString().Replace("\n", "\\n"), lineText, shouldMerge);

                // 直接ファイル書き込みで行結合判定を記録
                try
                {
                    // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化.Replace("\n", "\\n")}' 現行='{lineText}' 結合={shouldMerge}{Environment.NewLine}");
                }
                catch (Exception fileEx)
                {
                    System.Diagnostics.Debug.WriteLine($"JapaneseTextMerger 行結合判定ログ書き込みエラー: {fileEx.Message}");
                }

                if (shouldMerge)
                {
                    // スペースなしで結合（日本語の場合）
                    result.Append(lineText);
                    _logger.LogDebug("行 {LineNumber} を前の行に結合", i + 1);
                }
                else
                {
                    // 改行を挿入
                    result.AppendLine();
                    result.Append(lineText);
                    _logger.LogDebug("行 {LineNumber} を新しい行として追加", i + 1);
                }
            }
            else
            {
                result.Append(lineText);
            }
        }

        var mergedText = result.ToString();
        _logger.LogDebug("テキスト結合完了: 元の行数={OriginalLines}, 結果の行数={ResultLines}",
            lines.Count, mergedText.Split('\n').Length);

        // 直接ファイル書き込みで最終結果を記録
        try
        {
            // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化}'{Environment.NewLine}");
        }
        catch (Exception fileEx)
        {
            System.Diagnostics.Debug.WriteLine($"JapaneseTextMerger 最終結果ログ書き込みエラー: {fileEx.Message}");
        }

        return mergedText;
    }

    /// <summary>
    /// テキスト領域を行単位でグループ化
    /// </summary>
    public List<List<OcrTextRegion>> GroupTextRegionsByLine(IReadOnlyList<OcrTextRegion> textRegions)
    {
        List<List<OcrTextRegion>> lines = [];
        var processed = new HashSet<OcrTextRegion>();

        foreach (var region in textRegions)
        {
            if (processed.Contains(region))
                continue;

            // 同じ行と判定される領域を収集
            List<OcrTextRegion> sameLine = [region];
            processed.Add(region);

            var baseY = region.Bounds.Y + region.Bounds.Height / 2; // 中央Y座標
            var threshold = region.Bounds.Height * 0.5; // 高さの50%を閾値とする

            foreach (var other in textRegions)
            {
                if (processed.Contains(other))
                    continue;

                var otherY = other.Bounds.Y + other.Bounds.Height / 2;

                // Y座標の差が閾値以内なら同じ行
                if (Math.Abs(baseY - otherY) <= threshold)
                {
                    sameLine.Add(other);
                    processed.Add(other);
                }
            }

            // X座標でソート
            sameLine.Sort((a, b) => a.Bounds.X.CompareTo(b.Bounds.X));
            lines.Add(sameLine);
        }

        // Y座標でソート
        lines.Sort((a, b) => a[0].Bounds.Y.CompareTo(b[0].Bounds.Y));

        return lines;
    }

    /// <summary>
    /// 同一行内のテキスト領域を結合
    /// </summary>
    private string MergeLineRegions(List<OcrTextRegion> lineRegions)
    {
        if (lineRegions.Count == 0)
            return string.Empty;

        if (lineRegions.Count == 1)
            return lineRegions[0].Text;

        var result = new StringBuilder();

        for (int i = 0; i < lineRegions.Count; i++)
        {
            var region = lineRegions[i];
            result.Append(region.Text);

            // 次の領域との間隔を確認
            if (i < lineRegions.Count - 1)
            {
                var nextRegion = lineRegions[i + 1];
                var gap = nextRegion.Bounds.X - (region.Bounds.X + region.Bounds.Width);
                var avgCharWidth = (region.Bounds.Width + nextRegion.Bounds.Width) /
                                   (region.Text.Length + nextRegion.Text.Length);

                // 文字幅の1.5倍以上の間隔があればスペースを挿入
                if (gap > avgCharWidth * 1.5)
                {
                    // 日本語と英語の混在を考慮
                    if (IsAlphanumeric(region.Text.LastOrDefault()) ||
                        IsAlphanumeric(nextRegion.Text.FirstOrDefault()))
                    {
                        result.Append(' ');
                    }
                }
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// 前の行と結合すべきかを判定
    /// </summary>
    private bool ShouldMergeWithPreviousLine(
        string previousText,
        string currentText,
        List<OcrTextRegion> previousLine,
        List<OcrTextRegion> currentLine)
    {
        if (string.IsNullOrWhiteSpace(previousText) || string.IsNullOrWhiteSpace(currentText))
            return false;

        var lastChar = previousText.TrimEnd().LastOrDefault();
        var firstChar = currentText.TrimStart().FirstOrDefault();

        // 前の行が文末文字で終わっている場合は結合しない
        if (SentenceEndings.Contains(lastChar))
        {
            return false;
        }

        // 前の行が継続を示す文字で終わっている場合は結合する
        if (ContinuationExpected.Contains(lastChar))
        {
            return true;
        }

        // 行間の距離を確認
        if (previousLine.Count > 0 && currentLine.Count > 0)
        {
            var prevBottom = previousLine.Max(r => r.Bounds.Y + r.Bounds.Height);
            var currentTop = currentLine.Min(r => r.Bounds.Y);
            var lineGap = currentTop - prevBottom;
            var avgHeight = (previousLine.Average(r => r.Bounds.Height) +
                             currentLine.Average(r => r.Bounds.Height)) / 2;

            // 行間が平均文字高さの1.5倍以上なら段落の切れ目と判定
            if (lineGap > avgHeight * 1.5)
            {
                return false;
            }
        }

        // インデントを確認（段落の開始を検出）
        if (currentLine.Count > 0 && previousLine.Count > 0)
        {
            var prevLeftMost = previousLine.Min(r => r.Bounds.X);
            var currentLeftMost = currentLine.Min(r => r.Bounds.X);

            // 20ピクセル以上のインデントがあれば新しい段落
            if (currentLeftMost - prevLeftMost > 20)
            {
                return false;
            }
        }

        // 保守的判定：不確実な場合は改行を保持
        // 翻訳品質向上のため、確実でない結合は避ける
        _logger.LogDebug("🔗 保守的判定により結合しない: 前行末='{LastChar}' 現行頭='{FirstChar}'", lastChar, firstChar);
        return false;
    }

    /// <summary>
    /// 文字が英数字かどうかを判定
    /// </summary>
    private static bool IsAlphanumeric(char c)
    {
        return (c >= 'a' && c <= 'z') ||
               (c >= 'A' && c <= 'Z') ||
               (c >= '0' && c <= '9');
    }
}
