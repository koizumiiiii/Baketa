using System;
using Baketa.Core.Abstractions.DI;
using Baketa.Infrastructure.Translation.Complete;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation;

/// <summary>
/// 翻訳サービスのDI登録モジュール（更新版）
/// </summary>
public class TranslationModule : IServiceModule
{
    private readonly IConfiguration _configuration;
    
    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="configuration">設定</param>
    public TranslationModule(IConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }
    
    /// <summary>
    /// サービスの登録
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    public void RegisterServices(IServiceCollection services)
    {
        // 完全な翻訳システムを登録（OPUS-MT + Gemini + ハイブリッド + 中国語対応）
        services.AddCompleteTranslationSystem(_configuration);
    }

}