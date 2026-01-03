using Baketa.Core.Abstractions.CrashReporting;
using Baketa.Core.CrashReporting;
using Baketa.Core.DI.Attributes;
using Baketa.Core.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Baketa.Core.DI.Modules;

/// <summary>
/// [Issue #252] クラッシュレポートシステムのサービスを登録するモジュール
/// </summary>
[ModulePriority(ModulePriority.Core)]
public sealed class CrashReportingModule : ServiceModuleBase
{
    /// <summary>
    /// クラッシュレポート関連サービスを登録
    /// </summary>
    public override void RegisterServices(IServiceCollection services)
    {
        // リングバッファロガー（シングルトンインスタンスを使用）
        services.AddSingleton(_ => RingBufferLogger.Instance);

        // クラッシュレポートサービス
        services.AddSingleton<ICrashReportService, CrashReportService>();
    }
}
