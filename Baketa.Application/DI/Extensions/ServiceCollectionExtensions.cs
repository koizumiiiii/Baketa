using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Baketa.Core.Abstractions.DI;
using Baketa.Core.DI;
using Baketa.Core.DI.Attributes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Baketa.Application.DI.Extensions;

/// <summary>
/// サービスコレクション拡張メソッド。
/// Baketaサービスモジュールの登録を簡略化します。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// UI層の全サービスモジュールを登録します。
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <param name="environment">アプリケーション実行環境</param>
    /// <param name="configuration">設定</param>
    /// <returns>サービスコレクション</returns>
    public static IServiceCollection AddUIModule(
        this IServiceCollection services,
        BaketaEnvironment environment,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // 環境設定をサービスコンテナに登録
        services.AddSingleton(new BaketaEnvironmentSettings { Environment = environment });

        // 設定を登録
        services.AddSingleton<IConfiguration>(configuration);

        // 環境固有のサービス設定
        ConfigureEnvironmentSpecificServices(services, environment);

        // UI関連サービスの登録
        RegisterUIServices(services, configuration);

        // 翻訳設定UIサービスの登録
        RegisterTranslationUIServices(services, configuration);

        return services;
    }

    /// <summary>
    /// UI関連サービスを登録します。
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <param name="configuration">設定</param>
    private static void RegisterUIServices(IServiceCollection services, IConfiguration configuration)
    {
        // UI層のイベント集約器とViewModelを登録
        try
        {
            var uiModuleType = Type.GetType("Baketa.UI.DI.UIModule, Baketa.UI");
            if (uiModuleType != null)
            {
                var method = uiModuleType.GetMethod("RegisterUIServices",
                    BindingFlags.Public | BindingFlags.Static);
                method?.Invoke(null, [services, configuration]);
            }
        }
        catch (Exception ex) when (ex is not (TargetInvocationException or ArgumentException or TypeLoadException))
        {
            // UIモジュール登録に失敗した場合のフォールバック
            Console.WriteLine($"UIモジュール登録でエラーが発生しました: {ex.Message}");
        }
    }

    /// <summary>
    /// 翻訳設定UI関連サービスを登録します。
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <param name="configuration">設定</param>
    private static void RegisterTranslationUIServices(IServiceCollection services, IConfiguration configuration)
    {
        // UIServiceCollectionExtensionsを使用して翻訳UI関連サービスを登録
        try
        {
            var extensionType = Type.GetType("Baketa.UI.Extensions.UIServiceCollectionExtensions, Baketa.UI");
            if (extensionType != null)
            {
                var method = extensionType.GetMethod("AddTranslationSettingsUI",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    [typeof(IServiceCollection), typeof(IConfiguration)],
                    null);
                method?.Invoke(null, [services, configuration]);
            }
        }
        catch (Exception ex) when (ex is not (TargetInvocationException or ArgumentException or TypeLoadException))
        {
            // 翻訳UIサービス登録に失敗した場合のフォールバック
            Console.WriteLine($"翻訳UIサービス登録でエラーが発生しました: {ex.Message}");
        }
    }

    /// <summary>
    /// Baketaの全サービスモジュールを登録します。
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <param name="scanForModules">アセンブリスキャンによるモジュール自動検出を有効にするかどうか</param>
    /// <param name="environment">アプリケーション実行環境</param>
    /// <param name="customModules">追加の手動登録モジュール</param>
    /// <returns>サービスコレクション</returns>
    public static IServiceCollection AddBaketaServices(
        this IServiceCollection services,
        bool scanForModules = false,
        BaketaEnvironment environment = BaketaEnvironment.Production,
        params IServiceModule[] customModules)
    {
        // 環境設定をサービスコンテナに登録
        // enum型はサービスとして登録できないので設定クラスを使用
        services.AddSingleton(new BaketaEnvironmentSettings { Environment = environment });

        // 環境に応じた設定
        ConfigureEnvironmentSpecificServices(services, environment);

        // カスタムモジュールと基本モジュールを統合
        var modules = customModules.ToList();

        // スキャンによるモジュール検出（オプション）
        if (scanForModules)
        {
            var scannedModules = DiscoverModules();
            modules.AddRange(scannedModules);
        }

        // 優先度でソート
        var sortedModules = modules
            .Select(m => new
            {
                Module = m,
                Priority = m.GetType().GetCustomAttribute<ModulePriorityAttribute>()?.Priority
                          ?? ModulePriority.Custom
            })
            .OrderByDescending(x => (int)x.Priority)
            .Select(x => x.Module)
            .ToArray();

        // 登録
        RegisterModules(services, sortedModules);

        return services;
    }

    /// <summary>
    /// 環境固有のサービス設定を行います。
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <param name="environment">アプリケーション実行環境</param>
    private static void ConfigureEnvironmentSpecificServices(
        IServiceCollection services,
        BaketaEnvironment environment)
    {
        switch (environment)
        {
            case BaketaEnvironment.Development:
                // 開発環境固有の設定
                // ロギング設定は各レイヤーで個別に設定
                ConfigureLoggingForDevelopment(services);
                break;

            case BaketaEnvironment.Test:
                // テスト環境固有の設定
                ConfigureLoggingForTest(services);
                break;

            case BaketaEnvironment.Production:
            default:
                // 本番環境固有の設定
                ConfigureLoggingForProduction(services);
                break;
        }
    }

    /// <summary>
    /// 開発環境用のロギング設定
    /// </summary>
    private static void ConfigureLoggingForDevelopment(IServiceCollection services)
    {
        // 開発環境ではDebugレベルからログを出力
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddConsole();
        });
    }

    /// <summary>
    /// テスト環境用のロギング設定
    /// </summary>
    private static void ConfigureLoggingForTest(IServiceCollection services)
    {
        // テスト環境ではInformationレベルからログを出力
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddConsole();
        });
    }

    /// <summary>
    /// 本番環境用のロギング設定
    /// </summary>
    private static void ConfigureLoggingForProduction(IServiceCollection services)
    {
        // 本番環境ではWarningレベルからログを出力
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Warning);
            builder.AddConsole();
        });
    }

    /// <summary>
    /// モジュールを登録します。
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <param name="modules">登録するモジュール</param>
    private static void RegisterModules(IServiceCollection services, IEnumerable<IServiceModule> modules)
    {
        var registeredModules = new HashSet<Type>();
        // 未使用変数を削除

        foreach (var module in modules)
        {
            if (module is ServiceModuleBase moduleBase)
            {
                // ServiceModuleBase を継承している場合は依存関係を考慮して登録
                moduleBase.RegisterWithDependencies(services, registeredModules, new Stack<Type>());
            }
            else
            {
                // 通常のモジュールは直接登録
                if (!registeredModules.Contains(module.GetType()))
                {
                    module.RegisterServices(services);
                    registeredModules.Add(module.GetType());
                }
            }
        }

        // オプショナル: 登録されたモジュールをデバッグログに出力
        LogRegisteredModules(services, registeredModules);
    }

    /// <summary>
    /// 登録されたモジュールをログに出力します。
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <param name="registeredModules">登録されたモジュールの型</param>
    // LoggerMessageデリゲートの定義
    private static readonly Action<ILogger, string, Exception?> _logRegisteredModules =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(1, nameof(LogRegisteredModules)),
            "登録されたモジュール: {RegisteredModules}");

    private static readonly Action<ILogger, int, Exception?> _logServiceCount =
        LoggerMessage.Define<int>(
            LogLevel.Trace,
            new EventId(2, "ServiceCount"),
            "登録されたサービス: {ServiceCount}個");

    private static readonly Action<ILogger, string, Exception?> _logServiceDetail =
        LoggerMessage.Define<string>(
            LogLevel.Trace,
            new EventId(3, "ServiceDetail"),
            "  {Service}");

    /// <summary>
    /// 登録されたモジュールをログに出力します。
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <param name="registeredModules">登録されたモジュールの型</param>
    private static void LogRegisteredModules(
        IServiceCollection services,
        HashSet<Type> registeredModules)
    {
        try
        {
            var serviceProvider = services.BuildServiceProvider();
            var logger = serviceProvider.GetService<ILogger<IServiceModule>>();

            if (logger != null)
            {
                // LoggerMessageデリゲートを使用
                _logRegisteredModules(logger,
                    string.Join(", ", registeredModules.Select(t => t.Name)),
                    null);

                // 詳細なサービス登録情報（デバッグ用）
                if (logger.IsEnabled(LogLevel.Trace))
                {
                    // ディスカード変数を使用して警告を抑制
                    var _ = services
                        .Select(s => $"{s.ServiceType.Name} => {s.ImplementationType?.Name ?? "Factory"} ({s.Lifetime})")
                        .ToList();

                    _logServiceCount(logger, services.Count, null);

                    // 必要な場合はクエリを再実行
                    foreach (var service in services.Select(s =>
                        $"{s.ServiceType.Name} => {s.ImplementationType?.Name ?? "Factory"} ({s.Lifetime})"))
                    {
                        _logServiceDetail(logger, service, null);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
        {
            // ロギングに失敗しても処理を継続
            // 重大なシステム例外の場合は再スロー
            Console.WriteLine($"モジュール登録のログ出力に失敗: {ex.Message}");
        }
    }

    /// <summary>
    /// アセンブリから自動登録対象のモジュールを検出します。
    /// </summary>
    /// <returns>検出されたモジュール</returns>
    private static IEnumerable<IServiceModule> DiscoverModules()
    {
        // アプリケーションドメイン内のアセンブリから IServiceModule 実装を検索
        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes())
            .Where(t => typeof(IServiceModule).IsAssignableFrom(t) &&
                       !t.IsAbstract &&
                       !t.IsInterface &&
                       t.GetCustomAttribute<AutoRegisterAttribute>() != null)
            .Select(t => Activator.CreateInstance(t) as IServiceModule)
            .Where(m => m != null)!;
    }
}
