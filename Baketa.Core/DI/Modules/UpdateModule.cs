using Baketa.Core.Abstractions.Update;
using Baketa.Core.Services;
using Baketa.Core.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Baketa.Core.DI.Modules;

/// <summary>
/// 更新チェック機能のDIモジュール
/// GitHub Releases API連携とSemverバージョン管理
/// </summary>
public sealed class UpdateModule : ServiceModuleBase
{
    private readonly IConfiguration? _configuration;

    /// <summary>
    /// デフォルトコンストラクタ
    /// </summary>
    public UpdateModule()
    {
    }

    /// <summary>
    /// 設定を使用するコンストラクタ
    /// </summary>
    /// <param name="configuration">設定</param>
    public UpdateModule(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// 更新チェック関連サービスを登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    public override void RegisterServices(IServiceCollection services)
    {
        // 更新チェック設定
        if (_configuration != null)
        {
            services.Configure<UpdateCheckSettings>(_configuration.GetSection("Update"));
        }
        else
        {
            services.Configure<UpdateCheckSettings>(options => { });
        }

        // HTTPクライアント（更新チェック専用）
        services.AddHttpClient<UpdateCheckService>(client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "Baketa-UpdateChecker/1.0");
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // バージョン比較サービス
        services.AddSingleton<VersionComparisonService>();

        // 更新チェックサービス
        services.AddSingleton<IUpdateCheckService, UpdateCheckService>();
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