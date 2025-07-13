using Baketa.Core.Abstractions.Feedback;
using Baketa.Core.Services;
using Baketa.Core.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Baketa.Core.DI.Modules;

/// <summary>
/// フィードバック収集機能のDIモジュール
/// GitHub Issues API連携とプライバシー準拠データ収集
/// </summary>
public sealed class FeedbackModule : ServiceModuleBase
{
    private readonly IConfiguration? _configuration;

    /// <summary>
    /// デフォルトコンストラクタ
    /// </summary>
    public FeedbackModule()
    {
    }

    /// <summary>
    /// 設定を使用するコンストラクタ
    /// </summary>
    /// <param name="configuration">設定</param>
    public FeedbackModule(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>
    /// フィードバック収集関連サービスを登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    public override void RegisterServices(IServiceCollection services)
    {
        // フィードバック設定
        if (_configuration != null)
        {
            services.Configure<FeedbackSettings>(_configuration.GetSection("Feedback"));
        }
        else
        {
            services.Configure<FeedbackSettings>(options => { });
        }

        // HTTPクライアント（フィードバック専用）
        services.AddHttpClient<FeedbackService>(client =>
        {
            client.DefaultRequestHeaders.Add("User-Agent", "Baketa-FeedbackCollector/1.0");
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // フィードバックサービス
        services.AddSingleton<IFeedbackService, FeedbackService>();
    }

    /// <summary>
    /// このモジュールは PrivacyModule に依存します
    /// </summary>
    /// <returns>依存モジュールの型</returns>
    public override IEnumerable<Type> GetDependentModules()
    {
        yield return typeof(PrivacyModule);
    }
}