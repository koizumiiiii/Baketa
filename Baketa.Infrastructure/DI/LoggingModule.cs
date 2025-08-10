using Microsoft.Extensions.DependencyInjection;
using Baketa.Core.Abstractions.Dependency;
using Baketa.Core.Abstractions.Logging;
using Baketa.Infrastructure.Services.Logging;

namespace Baketa.Infrastructure.DI;

/// <summary>
/// ログ統一システムのサービス登録モジュール
/// </summary>
public sealed class LoggingModule : IServiceModule
{
    /// <summary>
    /// サービスを登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    public void RegisterServices(IServiceCollection services)
    {
        // 統一ログサービス（Console/File/ILoggerの統一）
        services.AddSingleton<IUnifiedLoggingService, UnifiedLoggingService>();
        
        Console.WriteLine("✅ LoggingModule: 統一ログサービス登録完了");
    }
}