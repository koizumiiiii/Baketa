using System;
using System.Threading.Tasks;
using Baketa.Core.Events.Diagnostics;
using Baketa.Infrastructure.Services;
using Microsoft.Extensions.Logging;

/// <summary>
/// 診断システムの独立テストプログラム
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("🧪 診断システム独立テスト開始");
        
        try
        {
            // ダミーロガーを作成
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var logger = loggerFactory.CreateLogger<DiagnosticReportGenerator>();
            
            // DiagnosticReportGeneratorの直接テスト
            var reportGenerator = new DiagnosticReportGenerator(logger);
            
            // テスト診断イベント作成
            var testEvent = new PipelineDiagnosticEvent
            {
                Stage = "TestStage",
                IsSuccess = true,
                ProcessingTimeMs = 100,
                Severity = DiagnosticSeverity.Information
            };
            
            Console.WriteLine("🧪 テストイベント作成完了");
            
            // レポート生成テスト（引数順序を修正）
            var reportPath = await reportGenerator.GenerateReportAsync(new[] { testEvent }, "standalone_test");
            
            Console.WriteLine($"🧪 ✅ レポート生成成功: {reportPath}");
            
            // ファイルが実際に作成されたか確認
            if (System.IO.File.Exists(reportPath))
            {
                Console.WriteLine("🧪 ✅ レポートファイル存在確認済み");
                
                var content = await System.IO.File.ReadAllTextAsync(reportPath);
                Console.WriteLine($"🧪 レポート内容プレビュー: {content.Substring(0, Math.Min(200, content.Length))}...");
            }
            else
            {
                Console.WriteLine("🧪 ❌ レポートファイルが見つかりません");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"🧪 ❌ テスト失敗: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"🧪 スタックトレース: {ex.StackTrace}");
        }
        
        Console.WriteLine("🧪 診断システム独立テスト完了");
    }
}