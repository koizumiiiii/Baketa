using System.Net.Http;
using Baketa.Core.Abstractions.License;
using Baketa.Core.Abstractions.Payment;
using Baketa.Core.DI;
using Baketa.Core.DI.Attributes;
using Baketa.Core.DI.Modules;
using Baketa.Core.Settings;
using Baketa.Infrastructure.License.Clients;
using Baketa.Infrastructure.License.Services;
using Baketa.Infrastructure.Payment.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
        // 設定の登録
        RegisterSettings(services);

        // コアサービスの登録
        RegisterCoreServices(services);

        // APIクライアントの登録（モックモード対応）
        RegisterApiClient(services);

        // 決済サービスの登録
        RegisterPaymentService(services);

        // 自動同期サービスの登録
        RegisterAutoSyncService(services);

        // NOTE: IUserPlanService後方互換アダプタはUI層で登録（Clean Architecture準拠）
        // NOTE: ログ出力はLicenseManagerのコンストラクタで行う
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
    }

    /// <summary>
    /// 設定を登録
    /// </summary>
    private static void RegisterSettings(IServiceCollection services)
    {
        // LicenseSettings をオプションとして登録
        services.AddOptions<LicenseSettings>()
            .BindConfiguration(LicenseSettings.SectionName);

        // PaymentSettings をオプションとして登録
        services.AddOptions<PaymentSettings>()
            .BindConfiguration(PaymentSettings.SectionName);

        // PatreonSettings をオプションとして登録
        services.AddOptions<PatreonSettings>()
            .BindConfiguration(PatreonSettings.SectionName);

        // 設定バリデータの登録
        services.AddSingleton<IValidateOptions<LicenseSettings>, LicenseSettingsValidator>();
        services.AddSingleton<IValidateOptions<PaymentSettings>, PaymentSettingsValidator>();
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

        // Issue #237 Phase 2: プロモーション設定永続化サービス
        services.AddSingleton<License.PromotionSettingsPersistence>();
        services.AddSingleton<IPromotionSettingsPersistence>(provider =>
            provider.GetRequiredService<License.PromotionSettingsPersistence>());

        // Issue #237 Phase 2: プロモーションコードサービス（モック/本番切り替え）
        services.AddHttpClient<License.PromotionCodeService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("User-Agent", "Baketa/1.0");
        });
        services.AddSingleton<License.MockPromotionCodeService>();
        services.AddSingleton<IPromotionCodeService>(provider =>
        {
            var licenseSettings = provider.GetRequiredService<IOptions<LicenseSettings>>().Value;

            if (licenseSettings.EnableMockMode)
            {
                var logger = provider.GetRequiredService<ILogger<LicenseModule>>();
                logger.LogInformation("🔧 プロモーションコードサービス: MockPromotionCodeService");
                return provider.GetRequiredService<License.MockPromotionCodeService>();
            }

            return provider.GetRequiredService<License.PromotionCodeService>();
        });

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
        services.AddHttpClient(PatreonOAuthService.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("User-Agent", "Baketa/1.0");
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
    /// 決済サービスを登録
    /// </summary>
    private static void RegisterPaymentService(IServiceCollection services)
    {
        // HttpClientファクトリ登録
        services.AddHttpClient<SupabasePaymentService>();

        // 決済サービス登録（設定に基づく）
        services.AddSingleton<IPaymentService>(provider =>
        {
            var paymentSettings = provider.GetRequiredService<IOptions<PaymentSettings>>().Value;
            var licenseSettings = provider.GetRequiredService<IOptions<LicenseSettings>>().Value;

            if (paymentSettings.EnableMockMode)
            {
                // モックモードの場合はモック実装を返す
                // LicenseSettings.EnableMockModeも有効な場合はILicenseManagerを渡して
                // テストモード（決済スキップ＆プラン即時変更）を有効化
                var licenseManager = licenseSettings.EnableMockMode
                    ? provider.GetRequiredService<ILicenseManager>()
                    : null;

                return new MockPaymentService(
                    provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MockPaymentService>>(),
                    licenseManager);
            }

            // HttpClientをファクトリから取得
            var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(SupabasePaymentService));

            return new SupabasePaymentService(
                provider.GetRequiredService<Supabase.Client>(),
                httpClient,
                provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SupabasePaymentService>>(),
                provider.GetRequiredService<IOptions<PaymentSettings>>());
        });
    }

    /// <summary>
    /// 依存モジュールを取得
    /// </summary>
    public override IEnumerable<Type> GetDependentModules()
    {
        yield return typeof(CoreModule);
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
/// 決済設定バリデータ
/// </summary>
public sealed class PaymentSettingsValidator : IValidateOptions<PaymentSettings>
{
    /// <summary>
    /// 決済設定を検証
    /// </summary>
    public ValidateOptionsResult Validate(string? name, PaymentSettings options)
    {
        var validationResult = options.ValidateSettings();

        if (!validationResult.IsValid)
        {
            var errors = validationResult.GetErrorMessages();
            return ValidateOptionsResult.Fail($"決済設定の検証に失敗しました: {errors}");
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

