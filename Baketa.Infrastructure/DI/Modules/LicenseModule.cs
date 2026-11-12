using System.Net.Http;
using Baketa.Core.Abstractions.Auth;
using Baketa.Core.Abstractions.License;
using Baketa.Core.DI;
using Baketa.Core.DI.Attributes;
using Baketa.Core.DI.Modules;
using Baketa.Core.Settings;
using Baketa.Infrastructure.Auth;
using Baketa.Infrastructure.Http;
using Baketa.Infrastructure.License.Clients;
using Baketa.Infrastructure.License.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;

namespace Baketa.Infrastructure.DI.Modules;

/// <summary>
/// ライセンス管理モジュール
/// 4プラン（Free/Standard/Pro/Premia）のライセンス管理サービスを登録
/// </summary>
[ModulePriority(ModulePriority.Infrastructure)]
public sealed class LicenseModule : ServiceModuleBase
{
    /// <summary>
    /// ライセンス管理サービスを登録
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    public override void RegisterServices(IServiceCollection services)
    {
        // No legacy API clients, timers, credential readers, or sync services are registered.
        services.AddSingleton<LocalLicenseManager>();
        services.AddSingleton<ILicenseManager>(provider => provider.GetRequiredService<LocalLicenseManager>());
        services.AddSingleton<ILicenseInfoProvider>(provider => provider.GetRequiredService<LocalLicenseManager>());
        services.AddSingleton<NullAccountServices>();
        services.AddSingleton<IPatreonOAuthService>(provider => provider.GetRequiredService<NullAccountServices>());
        services.AddSingleton<IPatreonCallbackHandler>(provider => provider.GetRequiredService<NullAccountServices>());
        services.AddSingleton<IPromotionCodeService>(provider => provider.GetRequiredService<NullAccountServices>());
        services.AddSingleton<IBonusTokenService>(provider => provider.GetRequiredService<NullAccountServices>());
        services.AddSingleton<IPromotionSettingsPersistence, License.PromotionSettingsPersistence>();
    }

    /// <summary>
    /// 自動同期サービスを登録
    /// </summary>
    private static void RegisterAutoSyncService(IServiceCollection services)
    {
        // Patreon自動同期サービス（30分間隔でライセンス状態を同期）
        services.AddSingleton<PatreonSyncHostedService>();
        services.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<PatreonSyncHostedService>());

        // Issue #280+#281: ボーナストークン自動同期サービス
        // ログイン時にサーバーから取得、定期的に消費量をサーバーへ同期
        services.AddSingleton<BonusSyncHostedService>();
        services.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<BonusSyncHostedService>());
    }

    /// <summary>
    /// 設定を登録
    /// </summary>
    private static void RegisterSettings(IServiceCollection services)
    {
        // LicenseSettings をオプションとして登録
        services.AddOptions<LicenseSettings>()
            .BindConfiguration(LicenseSettings.SectionName);

        // PatreonSettings をオプションとして登録
        services.AddOptions<PatreonSettings>()
            .BindConfiguration(PatreonSettings.SectionName);

        // 設定バリデータの登録
        services.AddSingleton<IValidateOptions<LicenseSettings>, LicenseSettingsValidator>();
        services.AddSingleton<IValidateOptions<PatreonSettings>, PatreonSettingsValidator>();
    }

    /// <summary>
    /// コアライセンスサービスを登録
    /// </summary>
    private static void RegisterCoreServices(IServiceCollection services)
    {
        // キャッシュサービス
        services.AddSingleton<LicenseCacheService>();
        services.AddSingleton<ILicenseCacheService>(provider =>
            provider.GetRequiredService<LicenseCacheService>());

        // ライセンスマネージャー
        services.AddSingleton<LicenseManager>();
        services.AddSingleton<ILicenseManager>(provider =>
            provider.GetRequiredService<LicenseManager>());
        // [Issue #297] 循環依存回避用の軽量インターフェース
        services.AddSingleton<ILicenseInfoProvider>(provider =>
            provider.GetRequiredService<LicenseManager>());

        // Issue #237 Phase 2: プロモーション設定永続化サービス
        services.AddSingleton<License.PromotionSettingsPersistence>();
        services.AddSingleton<IPromotionSettingsPersistence>(provider =>
            provider.GetRequiredService<License.PromotionSettingsPersistence>());

        // Issue #237 Phase 2: プロモーションコードサービス
        // DEBUGビルド: HybridPromotionCodeService（モック＋本番両対応）
        // RELEASEビルド: PromotionCodeService（本番のみ、モックコード拒否）
        // [Gemini Review] Pollyリトライポリシー追加（Issue #276）
        services.AddHttpClient<License.PromotionCodeService>()
            .ConfigureHttpClient((sp, client) =>
            {
                client.BaseAddress = new Uri("https://api.baketa.app");
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Add("User-Agent", "Baketa/1.0");
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                // [Issue #287] JWT認証へ移行（静的API Key削除）
            })
            .AddPolicyHandler(GetRetryPolicy());
        services.AddSingleton<License.MockPromotionCodeService>();
#if DEBUG
        services.AddSingleton<License.HybridPromotionCodeService>();
        services.AddSingleton<IPromotionCodeService>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<LicenseModule>>();
            logger.LogInformation("🔧 プロモーションコードサービス: HybridPromotionCodeService (DEBUG)");
            return provider.GetRequiredService<License.HybridPromotionCodeService>();
        });
#else
        services.AddSingleton<IPromotionCodeService>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<LicenseModule>>();
            logger.LogInformation("🔒 プロモーションコードサービス: PromotionCodeService (RELEASE)");
            return provider.GetRequiredService<License.PromotionCodeService>();
        });
#endif

        // [Issue #293] 循環依存回避: Lazy<IPromotionCodeService>を登録
        // PatreonOAuthServiceがコンストラクタ時ではなく、実際に使用するタイミングで解決する
        services.AddSingleton<Lazy<IPromotionCodeService>>(provider =>
            new Lazy<IPromotionCodeService>(() => provider.GetRequiredService<IPromotionCodeService>()));

        // Issue #280+#281: ボーナストークンサービス
        // Pollyリトライポリシー付きHttpClient
        services.AddHttpClient<License.BonusTokenService>()
            .ConfigureHttpClient((sp, client) =>
            {
                client.BaseAddress = new Uri("https://api.baketa.app");
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Add("User-Agent", "Baketa/1.0");
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                // [Issue #287] JWT認証へ移行（静的API Key削除）
            })
            .AddPolicyHandler(GetRetryPolicy());
        services.AddSingleton<IBonusTokenService>(provider =>
            provider.GetRequiredService<License.BonusTokenService>());
        services.AddSingleton<IDisposable>(provider =>
            provider.GetRequiredService<License.BonusTokenService>());

        // Issue #287: JWT認証サービス
        // JwtTokenAuthHandler（DelegatingHandler）をTransientで登録
        services.AddTransient<JwtTokenAuthHandler>();

        // JwtTokenService用のHttpClient
        services.AddHttpClient<JwtTokenService>()
            .ConfigureHttpClient((sp, client) =>
            {
                var config = sp.GetRequiredService<IConfiguration>();
                client.BaseAddress = new Uri(
                    config["CloudTranslation:RelayServerUrl"]
                    ?? "https://api.baketa.app");
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Add("User-Agent", "Baketa/1.0");
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                // Note: JWT認証はSessionTokenで行うためX-API-Keyは不要
            })
            .AddPolicyHandler(GetRetryPolicy());

        services.AddSingleton<JwtTokenService>();
        services.AddSingleton<IJwtTokenService>(provider =>
            provider.GetRequiredService<JwtTokenService>());
        services.AddSingleton<IDisposable>(provider =>
            provider.GetRequiredService<JwtTokenService>());

        // Disposable登録（アプリケーション終了時の適切なクリーンアップ）
        services.AddSingleton<IDisposable>(provider =>
            provider.GetRequiredService<LicenseCacheService>());
        services.AddSingleton<IDisposable>(provider =>
            provider.GetRequiredService<LicenseManager>());
    }

    /// <summary>
    /// APIクライアントを登録
    /// 設定に応じてモッククライアント、Patreon、またはSupabaseクライアントを使用
    /// </summary>
    private static void RegisterApiClient(IServiceCollection services)
    {
        // HttpClient登録（Patreon用）- IHttpClientFactory経由でソケット枯渇を防止
        services.AddHttpClient(PatreonOAuthService.HttpClientName)
            .ConfigureHttpClient((sp, client) =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                client.DefaultRequestHeaders.Add("User-Agent", "Baketa/1.0");
                // [Issue #287] JWT認証へ移行（静的API Key削除）
            });

        // PatreonOAuthService登録（IHttpClientFactory経由でHttpClientを取得）
        services.AddSingleton<PatreonOAuthService>();
        services.AddSingleton<IPatreonOAuthService>(provider =>
            provider.GetRequiredService<PatreonOAuthService>());

        // PatreonCallbackHandler登録（URIスキームコールバック処理）
        services.AddSingleton<IPatreonCallbackHandler, PatreonCallbackHandler>();

        // 各クライアントを登録
        services.AddSingleton<MockLicenseApiClient>();
        services.AddSingleton<PatreonLicenseClient>();
        // SupabaseLicenseApiClient は Patreon移行後は使用しないが、後方互換のため残す
        // services.AddSingleton<SupabaseLicenseApiClient>();

        // 設定に基づいて適切なクライアントを選択
        services.AddSingleton<ILicenseApiClient>(provider =>
        {
            var licenseSettings = provider.GetRequiredService<IOptions<LicenseSettings>>().Value;
            var patreonSettings = provider.GetRequiredService<IOptions<PatreonSettings>>().Value;
            var logger = provider.GetRequiredService<ILogger<LicenseModule>>();

            // モックモードが有効な場合
            if (licenseSettings.EnableMockMode)
            {
                logger.LogInformation("🔧 ライセンスAPIクライアント: MockLicenseApiClient");
                return provider.GetRequiredService<MockLicenseApiClient>();
            }

            // Patreon Client IDが設定されている場合はPatreonを使用
            if (!string.IsNullOrWhiteSpace(patreonSettings.ClientId))
            {
                logger.LogInformation("🔗 ライセンスAPIクライアント: PatreonLicenseClient");
                return provider.GetRequiredService<PatreonLicenseClient>();
            }

            // どちらも設定されていない場合はモッククライアントにフォールバック
            logger.LogWarning("⚠️ ライセンス設定が不完全です。モッククライアントを使用します。");
            return provider.GetRequiredService<MockLicenseApiClient>();
        });
    }

    /// <summary>
    /// 依存モジュールを取得
    /// </summary>
    public override IEnumerable<Type> GetDependentModules()
    {
        yield return typeof(CoreModule);
    }

    /// <summary>
    /// [Gemini Review] HTTPリトライポリシーを取得
    /// Issue #276: 一時的なネットワーク障害に対する回復性向上
    /// </summary>
    /// <returns>指数バックオフ付きリトライポリシー</returns>
    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()  // 5xx, 408, ネットワークエラー
            .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.TooManyRequests)  // 429
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),  // 2, 4, 8秒
                onRetry: (outcome, timespan, retryAttempt, context) =>
                {
                    // リトライログ出力（デバッグ用）
                    Console.WriteLine($"[Polly] Retry {retryAttempt} after {timespan.TotalSeconds}s due to {outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString()}");
                });
    }
}

/// <summary>
/// ライセンス設定バリデータ
/// </summary>
public sealed class LicenseSettingsValidator : IValidateOptions<LicenseSettings>
{
    /// <summary>
    /// ライセンス設定を検証
    /// </summary>
    public ValidateOptionsResult Validate(string? name, LicenseSettings options)
    {
        var validationResult = options.ValidateSettings();

        if (!validationResult.IsValid)
        {
            var errors = validationResult.GetErrorMessages();
            return ValidateOptionsResult.Fail($"ライセンス設定の検証に失敗しました: {errors}");
        }

        return ValidateOptionsResult.Success;
    }
}

/// <summary>
/// Patreon設定バリデータ
/// </summary>
public sealed class PatreonSettingsValidator : IValidateOptions<PatreonSettings>
{
    /// <summary>
    /// Patreon設定を検証
    /// </summary>
    public ValidateOptionsResult Validate(string? name, PatreonSettings options)
    {
        var validationResult = options.ValidateSettings();

        if (!validationResult.IsValid)
        {
            var errors = validationResult.GetErrorMessages();
            return ValidateOptionsResult.Fail($"Patreon設定の検証に失敗しました: {errors}");
        }

        return ValidateOptionsResult.Success;
    }
}

