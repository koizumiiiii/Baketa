using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Monitoring;
using Baketa.Infrastructure.DI.Modules;

// Phase 4.1 統合パフォーマンスメトリクス収集システム動作確認テスト
class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("🎯 Phase 4.1: 統合パフォーマンスメトリクス収集システム動作確認テスト開始");

        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                // InfrastructureModule登録（Phase 4.1実装を含む）
                var infrastructureModule = new InfrastructureModule();
                infrastructureModule.RegisterServices(services, context.Configuration);
            })
            .Build();

        var metricsCollector = host.Services.GetService<IPerformanceMetricsCollector>();
        
        if (metricsCollector == null)
        {
            Console.WriteLine("❌ IPerformanceMetricsCollector が取得できませんでした。DI設定を確認してください。");
            return;
        }

        Console.WriteLine("✅ IPerformanceMetricsCollector正常に取得");

        // テスト用メトリクス記録
        Console.WriteLine("📊 テスト用メトリクス記録開始...");

        // OCRメトリクステスト
        var ocrMetrics = new OcrPerformanceMetrics
        {
            ProcessingDuration = TimeSpan.FromMilliseconds(150),
            ImageWidth = 800,
            ImageHeight = 600,
            DetectedRegions = 5,
            ConfidenceScore = 0.85,
            MemoryUsageMB = 45,
            IsSuccess = true,
            OcrEngine = "PaddleOCR-V5",
            Timestamp = DateTime.UtcNow
        };

        metricsCollector.RecordOcrMetrics(ocrMetrics);
        Console.WriteLine("✅ OCRメトリクス記録完了");

        // 翻訳メトリクステスト
        var translationMetrics = new TranslationPerformanceMetrics
        {
            Engine = "NLLB-200",
            InputTextLength = 120,
            OutputTextLength = 140,
            TranslationDuration = TimeSpan.FromMilliseconds(800),
            TotalDuration = TimeSpan.FromMilliseconds(850),
            MemoryUsageMB = 120,
            GpuUtilization = 0.6,
            IsSuccess = true,
            Timestamp = DateTime.UtcNow
        };

        metricsCollector.RecordTranslationMetrics(translationMetrics);
        Console.WriteLine("✅ 翻訳メトリクス記録完了");

        // リソース調整メトリクステスト
        var resourceMetrics = new ResourceAdjustmentMetrics
        {
            ComponentName = "HybridResourceManager",
            AdjustmentType = "ParallelismIncrease",
            OldValue = 2,
            NewValue = 4, 
            Reason = "CPU usage dropped below threshold",
            CpuUsage = 35.5,
            MemoryUsage = 60.2,
            GpuUtilization = 0.3,
            Timestamp = DateTime.UtcNow
        };

        metricsCollector.RecordResourceAdjustment(resourceMetrics);
        Console.WriteLine("✅ リソース調整メトリクス記録完了");

        // フラッシュ待機
        Console.WriteLine("⏳ メトリクスフラッシュ待機中... (10秒)");
        await Task.Delay(10000);

        // 手動フラッシュ実行
        await metricsCollector.FlushAsync();
        Console.WriteLine("✅ 手動フラッシュ完了");

        // 統合レポート生成テスト
        Console.WriteLine("📊 統合レポート生成テスト...");
        var report = await metricsCollector.GenerateReportAsync();
        
        Console.WriteLine($"📈 統合レポート生成完了:");
        Console.WriteLine($"  - 生成日時: {report.GeneratedAt}");
        Console.WriteLine($"  - 総翻訳数: {report.TotalTranslations}");
        Console.WriteLine($"  - OCR操作数: {report.TotalOcrOperations}");
        Console.WriteLine($"  - リソース調整回数: {report.ResourceAdjustmentCount}");
        Console.WriteLine($"  - ログファイルサイズ: {report.LogFileSizeBytes} bytes");

        metricsCollector.Dispose();
        Console.WriteLine("🎉 Phase 4.1 統合メトリクス収集システム動作確認完了!");
    }
}