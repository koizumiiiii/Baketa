using Baketa.Infrastructure.Platform.Adapters;
using Baketa.Infrastructure.Platform.Adapters.Factory;
using Microsoft.Extensions.DependencyInjection;

namespace Baketa.Infrastructure.Platform.DI;

/// <summary>
/// アダプターファクトリーサービス登録のための拡張メソッド群
/// </summary>
public static class AdapterFactoryServiceExtensions
{
    /// <summary>
    /// 標準環境（Windows実装）用のアダプターファクトリーサービスを登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <returns>サービスコレクション（チェーン呼び出し用）</returns>
    public static IServiceCollection AddAdapterFactoryServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services, nameof(services));
        
        // 基本アダプターとファクトリーを登録
        services.AddSingleton<IAdapterFactory, WindowsAdapterFactory>();
        
        // 各アダプターをTransientで登録
        services.AddTransient<IWindowsImageAdapter, DefaultWindowsImageAdapter>();
        services.AddTransient<ICaptureAdapter, CaptureAdapterStub>();  // 実際の実装に置き換え予定
        services.AddTransient<IWindowManagerAdapter, WindowManagerAdapterStub>();  // 実際の実装に置き換え予定
        
        return services;
    }
    
    /// <summary>
    /// テスト環境用のモックアダプターファクトリーサービスを登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <returns>サービスコレクション（チェーン呼び出し用）</returns>
    public static IServiceCollection AddMockAdapterFactoryServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services, nameof(services));
        
        // モックアダプターとファクトリーを登録
        services.AddSingleton<IAdapterFactory, MockAdapterFactory>();
        
        // 各モックアダプターをSingletonで登録
        services.AddSingleton<IWindowsImageAdapter, WindowsImageAdapterStub>();
        services.AddSingleton<ICaptureAdapter, CaptureAdapterStub>();
        services.AddSingleton<IWindowManagerAdapter, WindowManagerAdapterStub>();
        
        return services;
    }
    
    /// <summary>
    /// 開発環境用のスタブアダプターファクトリーサービスを登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <returns>サービスコレクション（チェーン呼び出し用）</returns>
    public static IServiceCollection AddStubAdapterFactoryServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services, nameof(services));
        
        // スタブアダプターとファクトリーを登録
        services.AddSingleton<IAdapterFactory, StubAdapterFactory>();
        
        // 各スタブアダプターを登録
        services.AddTransient<IWindowsImageAdapter>(sp => {
            var imageFactory = sp.GetRequiredService<Baketa.Core.Abstractions.Factories.IWindowsImageFactory>();
            return new WindowsImageAdapterStub(imageFactory);
        });
        services.AddTransient<ICaptureAdapter, CaptureAdapterStub>();
        services.AddTransient<IWindowManagerAdapter, WindowManagerAdapterStub>();
        
        return services;
    }
}
