using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Baketa.UI.Configuration;
using Baketa.UI.Services;

namespace Baketa.UI.Extensions;

/// <summary>
/// UI層のサービス登録拡張メソッド
/// </summary>
public static class UIServiceCollectionExtensions
{
    /// <summary>
    /// 翻訳設定UI関連のサービスを登録
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <param name="configuration">設定</param>
    /// <returns>サービスコレクション</returns>
    public static IServiceCollection AddTranslationSettingsUI(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // 設定オプションの登録
        services.Configure<TranslationUIOptions>(
            configuration.GetSection(TranslationUIOptions.SectionName));
        services.Configure<Baketa.UI.Services.TranslationEngineStatusOptions>(
            configuration.GetSection("TranslationEngineStatus"));

        // 基本サービスの登録
        services.AddSingleton<IUserPlanService, UserPlanService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<INotificationService, AvaloniaNotificationService>();
        services.AddSingleton<ITranslationEngineStatusService, TranslationEngineStatusService>();

        // ViewModelの登録（後で追加）
        services.AddTranslationSettingsViewModels();

        return services;
    }

    /// <summary>
    /// 翻訳設定ViewModelを登録
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <returns>サービスコレクション</returns>
    public static IServiceCollection AddTranslationSettingsViewModels(
        this IServiceCollection services)
    {
        // ViewModelの登録
        services.AddTransient<Baketa.UI.ViewModels.Settings.TranslationSettingsViewModel>();
        services.AddTransient<Baketa.UI.ViewModels.Settings.EngineSelectionViewModel>();
        services.AddTransient<Baketa.UI.ViewModels.Settings.LanguagePairSelectionViewModel>();
        services.AddTransient<Baketa.UI.ViewModels.Settings.TranslationStrategyViewModel>();
        services.AddTransient<Baketa.UI.ViewModels.Settings.EngineStatusViewModel>();

        return services;
    }

    /// <summary>
    /// 開発・テスト用のサービス登録
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <returns>サービスコレクション</returns>
    public static IServiceCollection AddTranslationSettingsUIForTesting(
        this IServiceCollection services)
    {
        // テスト用設定
        services.Configure<TranslationUIOptions>(options =>
        {
            options.EnableNotifications = true;
            options.EnableVerboseLogging = true;
            options.StatusUpdateIntervalSeconds = 5; // テスト用に短縮
            options.AutoSaveSettings = false; // テスト時は自動保存無効
            options.DefaultEngineStrategy = "LocalOnly";
            options.DefaultLanguagePair = "ja-en";
            options.DefaultChineseVariant = "Simplified";
            options.DefaultTranslationStrategy = "Direct";
        });

        services.Configure<Baketa.UI.Services.TranslationEngineStatusOptions>(options =>
        {
            options.MonitoringIntervalSeconds = 5; // テスト用に短縮
            options.NetworkTimeoutMs = 2000;
            options.RateLimitWarningThreshold = 5;
            options.EnableHealthChecks = true;
        });

        // テスト用サービス
        services.AddSingleton<IUserPlanService, UserPlanService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<INotificationService, AvaloniaNotificationService>();
        services.AddSingleton<ITranslationEngineStatusService, TranslationEngineStatusService>();

        return services;
    }

    /// <summary>
    /// サービス登録の検証
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <returns>検証結果</returns>
    public static ServiceRegistrationValidationResult ValidateTranslationSettingsUI(
        this IServiceCollection services)
    {
        var result = new ServiceRegistrationValidationResult();

        // 必須サービスの存在確認
        var requiredServices = new[]
        {
            typeof(IUserPlanService),
            typeof(ILocalizationService),
            typeof(INotificationService),
            typeof(ITranslationEngineStatusService)
        };

        foreach (var serviceType in requiredServices)
        {
            if (services.Any(s => s.ServiceType == serviceType))
            {
                result.RegisteredServices.Add(serviceType.Name);
            }
            else
            {
                result.MissingServices.Add(serviceType.Name);
            }
        }

        result.IsValid = result.MissingServices.Count == 0;
        return result;
    }
}

/// <summary>
/// サービス登録検証結果
/// </summary>
public class ServiceRegistrationValidationResult
{
    public bool IsValid { get; set; }
    public ICollection<string> RegisteredServices { get; } = [];
    public ICollection<string> MissingServices { get; } = [];
    public ICollection<string> ValidationErrors { get; } = [];

    public override string ToString()
    {
        return $"Valid: {IsValid}, Registered: {RegisteredServices.Count}, Missing: {MissingServices.Count}";
    }
}
