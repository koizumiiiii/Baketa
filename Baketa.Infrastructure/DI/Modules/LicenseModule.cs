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

        // NOTE: IUserPlanService後方互換アダプタはUI層で登録（Clean Architecture準拠）
        // NOTE: ログ出力はLicenseManagerのコンストラクタで行う
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

        // 設定バリデータの登録
        services.AddSingleton<IValidateOptions<LicenseSettings>, LicenseSettingsValidator>();
        services.AddSingleton<IValidateOptions<PaymentSettings>, PaymentSettingsValidator>();
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

        // Disposable登録（アプリケーション終了時の適切なクリーンアップ）
        services.AddSingleton<IDisposable>(provider =>
            provider.GetRequiredService<LicenseCacheService>());
        services.AddSingleton<IDisposable>(provider =>
            provider.GetRequiredService<LicenseManager>());
    }

    /// <summary>
    /// APIクライアントを登録
    /// 設定に応じてモッククライアントまたは本番クライアントを使用
    /// </summary>
    private static void RegisterApiClient(IServiceCollection services)
    {
        // 両方のクライアントを登録
        services.AddSingleton<MockLicenseApiClient>();
        services.AddSingleton<SupabaseLicenseApiClient>();

        // 設定に基づいて適切なクライアントを選択
        services.AddSingleton<ILicenseApiClient>(provider =>
        {
            var settings = provider.GetRequiredService<IOptions<LicenseSettings>>().Value;

            if (settings.EnableMockMode)
            {
                return provider.GetRequiredService<MockLicenseApiClient>();
            }

            // 本番環境ではSupabaseLicenseApiClientを使用
            return provider.GetRequiredService<SupabaseLicenseApiClient>();
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
            var settings = provider.GetRequiredService<IOptions<PaymentSettings>>().Value;

            if (settings.EnableMockMode)
            {
                // モックモードの場合はモック実装を返す
                return new MockPaymentService(
                    provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<MockPaymentService>>());
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

