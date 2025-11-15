using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;

namespace Baketa.Infrastructure.Platform.Windows.Capture;

/// <summary>
/// GDIスクリーンキャプチャ機能のサービス登録拡張メソッド
/// </summary>
[SupportedOSPlatform("windows")]
public static class GdiScreenCapturerExtensions
{
    /// <summary>
    /// GDIスクリーンキャプチャ機能をサービスに登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <returns>サービスコレクション</returns>
    public static IServiceCollection AddGdiScreenCapturer(this IServiceCollection services)
    {
        services.AddSingleton<IGdiScreenCapturer, GdiScreenCapturer>();
        return services;
    }
}
