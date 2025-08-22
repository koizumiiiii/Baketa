using System;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Events.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace DiagnosticTest;

public class ManualDiagnosticReportTest
{
    public static async Task TestDiagnosticReportGeneration()
    {
        try
        {
            Console.WriteLine("🧪 手動診断レポート生成テスト開始");
            
            // サービスプロバイダーからサービスを取得
            if (Baketa.UI.Program.ServiceProvider == null)
            {
                Console.WriteLine("❌ ServiceProviderがnull");
                return;
            }
            
            var diagnosticService = Baketa.UI.Program.ServiceProvider.GetService<IDiagnosticCollectionService>();
            if (diagnosticService == null)
            {
                Console.WriteLine("❌ IDiagnosticCollectionServiceが取得できない");
                return;
            }
            
            Console.WriteLine("✅ 診断サービス取得成功");
            
            // テストイベントを発行
            await diagnosticService.LogEventAsync(new PipelineDiagnosticEvent
            {
                Stage = "ManualTest",
                IsSuccess = true,
                ProcessingTimeMs = 100,
                Severity = DiagnosticSeverity.Information
            });
            
            Console.WriteLine("✅ テストイベント発行完了");
            
            // レポート生成実行
            var reportPath = await diagnosticService.GenerateReportAsync("manual_test");
            Console.WriteLine($"✅ 診断レポート生成完了: {reportPath}");
            
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ エラー: {ex.Message}");
            Console.WriteLine($"❌ スタックトレース: {ex.StackTrace}");
        }
    }
}