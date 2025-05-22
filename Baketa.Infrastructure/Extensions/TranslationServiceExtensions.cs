using Baketa.Core.Abstractions.DI;
using Baketa.Infrastructure.Translation;
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
    /// <returns>サービスコレクション</returns>
    public static IServiceCollection AddTranslationServices(this IServiceCollection services)
    {
        // 翻訳モジュールの登録
        var translationModule = new TranslationModule();
        translationModule.RegisterServices(services);
        
        return services;
    }
}