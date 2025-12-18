using Baketa.Core.Abstractions.License;
using Baketa.Core.DI;
using Baketa.Core.DI.Attributes;
using Baketa.Core.DI.Modules;
using Baketa.Core.Settings;
using Baketa.Infrastructure.License.Clients;
using Baketa.Infrastructure.License.Services;
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

        // NOTE: IUserPlanService後方互換アダプタはUI層で登録（Clean Architecture準拠）
    }

    /// <summary>
    /// 設定を登録
    /// </summary>
    private static void RegisterSettings(IServiceCollection services)
    {
        // LicenseSettings をオプションとして登録
        services.AddOptions<LicenseSettings>()
            .BindConfiguration(LicenseSettings.SectionName);

        // 設定バリデータの登録
        services.AddSingleton<IValidateOptions<LicenseSettings>, LicenseSettingsValidator>();
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
        // モッククライアントを登録（現時点では常にモック）
        // TODO: 本番環境ではSupabaseLicenseClientに切り替え
        services.AddSingleton<MockLicenseApiClient>();
        services.AddSingleton<ILicenseApiClient>(provider =>
        {
            var settings = provider.GetRequiredService<IOptions<LicenseSettings>>().Value;

            if (settings.EnableMockMode)
            {
                return provider.GetRequiredService<MockLicenseApiClient>();
            }

            // TODO: 本番クライアント実装後に切り替え
            // return provider.GetRequiredService<SupabaseLicenseClient>();
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
