using Microsoft.Extensions.DependencyInjection;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.DI;
using Baketa.Infrastructure.Platform.Windows.Services;

namespace Baketa.Infrastructure.Platform.DI.Modules;

/// <summary>
/// P2統合リアルタイム更新システム用プラットフォーム固有サービスのDI登録
/// Gemini改善提案: プラットフォーム固有ロジック分離
/// </summary>
public sealed class RealTimePlatformModule : ServiceModuleBase
{
    public override void RegisterServices(IServiceCollection services)
    {
        Console.WriteLine("🖥️ RealTimePlatformModule登録開始");

        // Windows固有サービス登録
        RegisterWindowsServices(services);

        Console.WriteLine("✅ RealTimePlatformModule登録完了");
    }

    /// <summary>
    /// Windows固有サービス登録
    /// </summary>
    private static void RegisterWindowsServices(IServiceCollection services)
    {
        Console.WriteLine("  🖼️ Windows固有サービス登録中...");
        
        // ゲーム状態監視
        services.AddSingleton<IGameStateProvider, WindowsGameStateProvider>();
        Console.WriteLine("    ✅ WindowsGameStateProvider登録 - ゲーム検出・フルスクリーン判定");
        
        // システム状態監視
        services.AddSingleton<ISystemStateMonitor, WindowsSystemStateMonitor>();
        Console.WriteLine("    ✅ WindowsSystemStateMonitor登録 - リソース監視・バッテリー状態");
        
        Console.WriteLine("  ✅ Windows固有サービス登録完了");
    }

}