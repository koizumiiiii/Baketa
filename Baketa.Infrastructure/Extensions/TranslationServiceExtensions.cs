using Baketa.Core.Abstractions.DI;
using Baketa.Infrastructure.Translation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Baketa.Infrastructure.Extensions;

/// <summary>
/// 翻訳サービスの拡張メソッド
/// </summary>
public static class TranslationServiceExtensions
{
    /// <summary>
    /// 翻訳サービスをDIコンテナに追加します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <param name="configuration">設定</param>
    /// <returns>サービスコレクション</returns>
    public static IServiceCollection AddTranslationServices(this IServiceCollection services, IConfiguration configuration)
    {
        // 翻訳モジュールの登録
        var translationModule = new TranslationModule(configuration);
        translationModule.RegisterServices(services);

        return services;
    }
}
