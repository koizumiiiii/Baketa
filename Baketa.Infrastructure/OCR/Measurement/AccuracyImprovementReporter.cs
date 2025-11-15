using System.Globalization;
using System.Text;
using Baketa.Core.Abstractions.OCR;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.OCR.Measurement;

/// <summary>
/// OCR精度改善効果のレポート生成
/// </summary>
public sealed class AccuracyImprovementReporter(ILogger<AccuracyImprovementReporter> logger)
{
    private readonly ILogger<AccuracyImprovementReporter> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// 改善効果の詳細レポートを生成
    /// </summary>
    /// <param name="comparisonResults">比較結果のリスト</param>
    /// <param name="outputPath">レポート出力パス</param>
    /// <returns>生成されたレポートのパス</returns>
    public async Task<string> GenerateImprovementReportAsync(
        IReadOnlyList<(string ImprovementName, AccuracyComparisonResult Result)> comparisonResults,
        string outputPath)
    {
        var report = new StringBuilder();
        var timestamp = DateTime.Now;

        // ヘッダー
        report.AppendLine("# OCR精度改善効果測定レポート");
        report.AppendLine();
        report.AppendLine(CultureInfo.InvariantCulture, $"**測定日時**: {timestamp:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine(CultureInfo.InvariantCulture, $"**測定項目数**: {comparisonResults.Count}");
        report.AppendLine();

        // サマリー
        report.AppendLine("## 📊 改善効果サマリー");
        report.AppendLine();

        var totalImprovements = comparisonResults.Where(r => r.Result.AccuracyImprovement > 0).Count();
        var avgAccuracyImprovement = comparisonResults.Average(r => r.Result.AccuracyImprovement);
        var avgProcessingTimeChange = comparisonResults.Average(r => r.Result.ProcessingTimeChange);
        var significantImprovements = comparisonResults.Where(r => r.Result.IsSignificantImprovement).Count();

        report.AppendLine(CultureInfo.InvariantCulture, $"- **改善された項目**: {totalImprovements}/{comparisonResults.Count}");
        report.AppendLine(CultureInfo.InvariantCulture, $"- **平均精度改善**: {avgAccuracyImprovement:+0.00%;-0.00%;+0.00%}");
        report.AppendLine(CultureInfo.InvariantCulture, $"- **平均処理時間変化**: {avgProcessingTimeChange:+0.00%;-0.00%;+0.00%}");
        report.AppendLine(CultureInfo.InvariantCulture, $"- **統計的に有意な改善**: {significantImprovements}/{comparisonResults.Count}");
        report.AppendLine();

        // 詳細結果
        report.AppendLine("## 📋 詳細結果");
        report.AppendLine();

        foreach (var (improvementName, result) in comparisonResults)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"### {improvementName}");
            report.AppendLine();

            // 精度改善
            var accuracyStatus = result.AccuracyImprovement switch
            {
                > 0.05 => "🔥 大幅改善",
                > 0.02 => "✅ 改善",
                > -0.02 => "➖ 変化なし",
                _ => "❌ 低下"
            };

            // 処理時間変化
            var timeStatus = result.ProcessingTimeChange switch
            {
                < -0.1 => "⚡ 大幅高速化",
                < 0 => "🚀 高速化",
                < 0.1 => "➖ 変化なし",
                _ => "🐌 低速化"
            };

            report.AppendLine(CultureInfo.InvariantCulture, $"- **精度改善**: {result.AccuracyImprovement:+0.00%;-0.00%;+0.00%} {accuracyStatus}");
            report.AppendLine(CultureInfo.InvariantCulture, $"- **処理時間変化**: {result.ProcessingTimeChange:+0.00%;-0.00%;+0.00%} {timeStatus}");
            report.AppendLine(CultureInfo.InvariantCulture, $"- **統計的有意性**: {(result.IsSignificantImprovement ? "✅ 有意" : "❌ 非有意")}");
            report.AppendLine();

            // 詳細数値
            report.AppendLine("**基準設定結果**:");
            report.AppendLine(CultureInfo.InvariantCulture, $"- 全体精度: {result.BaselineResult.OverallAccuracy:P2}");
            report.AppendLine(CultureInfo.InvariantCulture, $"- 文字精度: {result.BaselineResult.CharacterAccuracy:P2}");
            report.AppendLine(CultureInfo.InvariantCulture, $"- 単語精度: {result.BaselineResult.WordAccuracy:P2}");
            report.AppendLine(CultureInfo.InvariantCulture, $"- 処理時間: {result.BaselineResult.ProcessingTime.TotalMilliseconds:F0}ms");
            report.AppendLine(CultureInfo.InvariantCulture, $"- 平均信頼度: {result.BaselineResult.AverageConfidence:P2}");
            report.AppendLine();

            report.AppendLine("**改善設定結果**:");
            report.AppendLine(CultureInfo.InvariantCulture, $"- 全体精度: {result.ImprovedResult.OverallAccuracy:P2}");
            report.AppendLine(CultureInfo.InvariantCulture, $"- 文字精度: {result.ImprovedResult.CharacterAccuracy:P2}");
            report.AppendLine(CultureInfo.InvariantCulture, $"- 単語精度: {result.ImprovedResult.WordAccuracy:P2}");
            report.AppendLine(CultureInfo.InvariantCulture, $"- 処理時間: {result.ImprovedResult.ProcessingTime.TotalMilliseconds:F0}ms");
            report.AppendLine(CultureInfo.InvariantCulture, $"- 平均信頼度: {result.ImprovedResult.AverageConfidence:P2}");
            report.AppendLine();
        }

        // 推奨事項
        report.AppendLine("## 💡 推奨事項");
        report.AppendLine();

        var (ImprovementName, Result) = comparisonResults
            .Where(r => r.Result.IsSignificantImprovement)
            .OrderByDescending(r => r.Result.AccuracyImprovement)
            .FirstOrDefault();

        if (Result != null)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"**最も効果的な改善**: {ImprovementName}");
            report.AppendLine(CultureInfo.InvariantCulture, $"- 精度向上: {Result.AccuracyImprovement:+0.00%;-0.00%;+0.00%}");
            report.AppendLine(CultureInfo.InvariantCulture, $"- パフォーマンス影響: {Result.ProcessingTimeChange:+0.00%;-0.00%;+0.00%}");
            report.AppendLine();
        }

        // 処理時間とのトレードオフ分析
        var goodTradeoffs = comparisonResults
            .Where(r => r.Result.AccuracyImprovement > 0.02 && r.Result.ProcessingTimeChange < 0.2)
            .OrderByDescending(r => r.Result.AccuracyImprovement / Math.Max(0.01, r.Result.ProcessingTimeChange))
            .Take(3);

        if (goodTradeoffs.Any())
        {
            report.AppendLine("**推奨する改善項目**（精度向上とパフォーマンスのバランス）:");
            foreach (var (name, result) in goodTradeoffs)
            {
                report.AppendLine(CultureInfo.InvariantCulture, $"- {name}: 精度{result.AccuracyImprovement:+0.0%}, 時間{result.ProcessingTimeChange:+0.0%}");
            }
            report.AppendLine();
        }

        // 警告事項
        var problematicItems = comparisonResults
            .Where(r => r.Result.AccuracyImprovement < -0.01 || r.Result.ProcessingTimeChange > 0.5)
            .ToList();

        if (problematicItems.Count > 0)
        {
            report.AppendLine("⚠️ **注意が必要な項目**:");
            foreach (var (name, result) in problematicItems)
            {
                var issue = result.AccuracyImprovement < -0.01 ? "精度低下" : "大幅な処理時間増加";
                report.AppendLine(CultureInfo.InvariantCulture, $"- {name}: {issue}");
            }
            report.AppendLine();
        }

        // フッター
        report.AppendLine("---");
        report.AppendLine("*このレポートは自動生成されました*");
        report.AppendLine(CultureInfo.InvariantCulture, $"*生成時刻: {timestamp:yyyy-MM-dd HH:mm:ss}*");

        // ファイルに保存
        var directory = System.IO.Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
        {
            System.IO.Directory.CreateDirectory(directory);
        }

        await System.IO.File.WriteAllTextAsync(outputPath, report.ToString(), Encoding.UTF8).ConfigureAwait(false);

        _logger.LogInformation("📄 改善効果レポート生成完了: {OutputPath}", outputPath);
        return outputPath;
    }

    /// <summary>
    /// 簡易サマリーをコンソール出力
    /// </summary>
    /// <param name="comparisonResults">比較結果のリスト</param>
    public void PrintSummaryToConsole(IReadOnlyList<(string ImprovementName, AccuracyComparisonResult Result)> comparisonResults)
    {
        Console.WriteLine("🔍 OCR精度改善効果測定結果");
        Console.WriteLine("═══════════════════════════════════");

        foreach (var (name, result) in comparisonResults)
        {
            var statusIcon = result.IsSignificantImprovement ? "✅" :
                           result.AccuracyImprovement > 0 ? "🔵" : "❌";

            Console.WriteLine($"{statusIcon} {name}:");
            Console.WriteLine($"   精度改善: {result.AccuracyImprovement:+0.00%;-0.00%;+0.00%}");
            Console.WriteLine($"   時間変化: {result.ProcessingTimeChange:+0.00%;-0.00%;+0.00%}");
            Console.WriteLine();
        }

        var totalSignificant = comparisonResults.Count(r => r.Result.IsSignificantImprovement);
        var avgImprovement = comparisonResults.Average(r => r.Result.AccuracyImprovement);

        Console.WriteLine("📊 総合評価:");
        Console.WriteLine($"   有意な改善: {totalSignificant}/{comparisonResults.Count}");
        Console.WriteLine($"   平均精度向上: {avgImprovement:+0.00%;-0.00%;+0.00%}");
        Console.WriteLine("═══════════════════════════════════");
    }

    /// <summary>
    /// CSV形式でのデータ出力
    /// </summary>
    /// <param name="comparisonResults">比較結果のリスト</param>
    /// <param name="csvPath">CSV出力パス</param>
    /// <returns>生成されたCSVファイルのパス</returns>
    public async Task<string> ExportToCsvAsync(
        IReadOnlyList<(string ImprovementName, AccuracyComparisonResult Result)> comparisonResults,
        string csvPath)
    {
        var csv = new StringBuilder();

        // ヘッダー
        csv.AppendLine("ImprovementName,AccuracyImprovement,ProcessingTimeChange,IsSignificantImprovement," +
                      "BaselineAccuracy,ImprovedAccuracy,BaselineTime,ImprovedTime," +
                      "BaselineCharAccuracy,ImprovedCharAccuracy,BaselineWordAccuracy,ImprovedWordAccuracy");

        // データ行
        foreach (var (name, result) in comparisonResults)
        {
            csv.AppendLine(CultureInfo.InvariantCulture, $"{EscapeCsv(name)}," +
                          $"{result.AccuracyImprovement:F4}," +
                          $"{result.ProcessingTimeChange:F4}," +
                          $"{result.IsSignificantImprovement}," +
                          $"{result.BaselineResult.OverallAccuracy:F4}," +
                          $"{result.ImprovedResult.OverallAccuracy:F4}," +
                          $"{result.BaselineResult.ProcessingTime.TotalMilliseconds:F0}," +
                          $"{result.ImprovedResult.ProcessingTime.TotalMilliseconds:F0}," +
                          $"{result.BaselineResult.CharacterAccuracy:F4}," +
                          $"{result.ImprovedResult.CharacterAccuracy:F4}," +
                          $"{result.BaselineResult.WordAccuracy:F4}," +
                          $"{result.ImprovedResult.WordAccuracy:F4}");
        }

        // ファイルに保存
        var directory = System.IO.Path.GetDirectoryName(csvPath);
        if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
        {
            System.IO.Directory.CreateDirectory(directory);
        }

        await System.IO.File.WriteAllTextAsync(csvPath, csv.ToString(), Encoding.UTF8).ConfigureAwait(false);

        _logger.LogInformation("📊 CSVデータエクスポート完了: {CsvPath}", csvPath);
        return csvPath;
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }
}
