using System;
using Baketa.Core.Abstractions.DI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation;

/// <summary>
/// 翻訳サービスのDI登録モジュール（更新版）
/// </summary>
/// <remarks>
/// コンストラクタ
/// </remarks>
/// <param name="configuration">設定</param>
public class TranslationModule(IConfiguration configuration) : IServiceModule
{
    private readonly IConfiguration _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

    /// <summary>
    /// サービスの登録
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    public void RegisterServices(IServiceCollection services)
    {
        // TranslationServiceExtensionsを使用してTransformersOpusMtEngineを登録
        services.AddTranslationServices();
    }

}
