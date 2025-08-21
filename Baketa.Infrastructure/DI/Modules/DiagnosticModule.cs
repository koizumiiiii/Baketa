using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.DI;
using Baketa.Core.Events.Diagnostics;
using Baketa.Infrastructure.Events.Processors;
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

        // 診断サービス群
        services.AddSingleton<IDiagnosticReportGenerator, DiagnosticReportGenerator>();
        services.AddSingleton<IDiagnosticCollectionService, DiagnosticCollectionService>();
        Console.WriteLine("🔍 [DIAGNOSTIC] DiagnosticServices登録完了");

        // イベントプロセッサー登録
        services.AddScoped<IEventProcessor<PipelineDiagnosticEvent>, Baketa.Infrastructure.Events.Processors.DiagnosticEventProcessor>();
        services.AddScoped<IEventProcessor<DiagnosticReportGeneratedEvent>, Baketa.Infrastructure.Events.Processors.DiagnosticReportGeneratedEventProcessor>();
        Console.WriteLine("🔍 [DIAGNOSTIC] EventProcessors登録完了");

        Console.WriteLine("✅ [DIAGNOSTIC] 診断レポートシステム登録完了");
    }
}