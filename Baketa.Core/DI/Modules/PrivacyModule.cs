using Baketa.Core.Abstractions.Privacy;
using Baketa.Core.Services;
using Baketa.Core.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Baketa.Core.DI.Modules;

/// <summary>
/// プライバシー・GDPR準拠機能のDIモジュール
/// </summary>
public sealed class PrivacyModule : ServiceModuleBase
{
    private readonly IConfiguration? _configuration;

    /// <summary>
    /// デフォルトコンストラクタ
    /// </summary>
    public PrivacyModule()
    {
    }

    /// <summary>
    /// 設定を使用するコンストラクタ
    /// </summary>
    /// <param name="configuration">設定</param>
    public PrivacyModule(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// プライバシー関連サービスを登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    public override void RegisterServices(IServiceCollection services)
    {
        // プライバシー同意設定
        if (_configuration != null)
        {
            services.Configure<PrivacyConsentSettings>(_configuration.GetSection("Privacy:Consent"));
        }
        else
        {
            services.Configure<PrivacyConsentSettings>(options => { });
        }

        // プライバシー同意サービス
        services.AddSingleton<IPrivacyConsentService, PrivacyConsentService>();
    }

    /// <summary>
    /// このモジュールは FeatureFlagModule に依存します
    /// </summary>
    /// <returns>依存モジュールの型</returns>
    public override IEnumerable<Type> GetDependentModules()
    {
        yield return typeof(FeatureFlagModule);
    }
}
