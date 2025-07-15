using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Services;
using Baketa.Core.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Baketa.Core.DI.Modules;

/// <summary>
/// フィーチャーフラグ機能のDIモジュール
/// </summary>
public sealed class FeatureFlagModule : ServiceModuleBase
{
    private readonly IConfiguration? _configuration;

    /// <summary>
    /// デフォルトコンストラクタ
    /// </summary>
    public FeatureFlagModule()
    {
    }

    /// <summary>
    /// 設定を使用するコンストラクタ
    /// </summary>
    /// <param name="configuration">設定</param>
    public FeatureFlagModule(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// フィーチャーフラグサービスを登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    public override void RegisterServices(IServiceCollection services)
    {
        // フィーチャーフラグ設定
        if (_configuration != null)
        {
            services.Configure<FeatureFlagSettings>(_configuration.GetSection("FeatureFlags"));
        }
        else
        {
            services.Configure<FeatureFlagSettings>(options => { });
        }
        
        // フィーチャーフラグサービス
        services.AddSingleton<IFeatureFlagService, FeatureFlagService>();
    }
}