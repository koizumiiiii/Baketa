using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Baketa.Core.Abstractions.Services;

public class ManualReportTest
{
    public static async Task TestReportGeneration()
    {
        try
        {
            Console.WriteLine("🧪 手動診断レポート生成テスト開始");
            
            if (Program.ServiceProvider == null)
            {
                Console.WriteLine("❌ ServiceProviderが利用できません");
                return;
            }
            
            var diagnosticService = Program.ServiceProvider.GetService<IDiagnosticCollectionService>();
            if (diagnosticService == null)
            {
                Console.WriteLine("❌ IDiagnosticCollectionServiceが取得できません");
                return;
            }
            
            Console.WriteLine("✅ DiagnosticCollectionService取得成功");
            
            var reportPath = await diagnosticService.GenerateReportAsync("manual_test_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            
            if (!string.IsNullOrEmpty(reportPath))
            {
                Console.WriteLine($"✅ 手動診断レポート生成成功: {reportPath}");
            }
            else
            {
                Console.WriteLine("⚠️ 手動診断レポート生成: データなし");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 手動診断レポート生成エラー: {ex.Message}");
        }
    }
}