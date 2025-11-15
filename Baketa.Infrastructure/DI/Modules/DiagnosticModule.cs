using System.IO;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.DI;
using Baketa.Core.Events.Diagnostics;
using Baketa.Infrastructure.Events.Processors;
using Baketa.Infrastructure.OCR.PaddleOCR.Diagnostics;
using Baketa.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Baketa.Infrastructure.DI.Modules;

/// <summary>
/// 診断レポートシステムのDI登録モジュール
/// α版テスト効率化のための診断基盤サービス
/// </summary>
public sealed class DiagnosticModule : ServiceModuleBase
{
    public override void RegisterServices(IServiceCollection services)
    {
        Console.WriteLine("🔍 [DIAGNOSTIC] 診断レポートシステム登録開始");

        // バックグラウンドタスクキュー
        services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
        services.AddHostedService<QueuedHostedService>();
        Console.WriteLine("🔍 [DIAGNOSTIC] BackgroundTaskQueue登録完了");

        // ROI画像自動クリーンアップサービス
        services.AddHostedService<RoiImageCleanupHostedService>();
        Console.WriteLine("🔍 [DIAGNOSTIC] RoiImageCleanupHostedService登録完了");

        // 診断サービス群
        services.AddSingleton<IDiagnosticReportGenerator, DiagnosticReportGenerator>();
        services.AddSingleton<IDiagnosticCollectionService, DiagnosticCollectionService>();
        Console.WriteLine("🔍 [DIAGNOSTIC] DiagnosticServices登録完了");

        // ROI画像診断保存サービス（設定ベース）
        services.AddSingleton<ImageDiagnosticsSaver>(serviceProvider =>
        {
            var roiOptionsAccessor = serviceProvider.GetService<Microsoft.Extensions.Options.IOptions<Baketa.Core.Settings.RoiDiagnosticsSettings>>();
            var roiSettings = roiOptionsAccessor?.Value;

            Console.WriteLine($"🔍 [DIAGNOSTIC] IOptions<RoiDiagnosticsSettings>取得結果: {(roiOptionsAccessor != null ? "成功" : "失敗")}");
            Console.WriteLine($"🔍 [DIAGNOSTIC] RoiDiagnosticsSettings値: {(roiSettings != null ? "存在" : "null")}");
            var logger = serviceProvider.GetService<Microsoft.Extensions.Logging.ILogger<ImageDiagnosticsSaver>>();

            // 設定が取得できない場合はフォールバック
            var outputDirectory = roiSettings?.GetExpandedOutputPath() ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Baketa", "ROI", "Images");

            Console.WriteLine($"🔍 [DIAGNOSTIC] ROI画像出力パス: {outputDirectory}");
            Console.WriteLine($"🔍 [DIAGNOSTIC] ROI画像出力有効: {roiSettings?.EnableRoiImageOutput ?? true}");

            return new ImageDiagnosticsSaver(outputDirectory, logger);
        });
        Console.WriteLine("🔍 [DIAGNOSTIC] ImageDiagnosticsSaver登録完了");

        // イベントプロセッサー登録
        services.AddScoped<IEventProcessor<PipelineDiagnosticEvent>, Baketa.Infrastructure.Events.Processors.DiagnosticEventProcessor>();
        services.AddScoped<IEventProcessor<DiagnosticReportGeneratedEvent>, Baketa.Infrastructure.Events.Processors.DiagnosticReportGeneratedEventProcessor>();
        Console.WriteLine("🔍 [DIAGNOSTIC] EventProcessors登録完了");

        Console.WriteLine("✅ [DIAGNOSTIC] 診断レポートシステム登録完了");
    }
}
