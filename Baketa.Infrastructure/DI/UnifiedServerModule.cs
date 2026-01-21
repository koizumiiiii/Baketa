using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Server;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.DI;
using Baketa.Core.Settings;
using Baketa.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.DI;

/// <summary>
/// Issue #292: 統合AIサーバー DIモジュール
/// OCR + 翻訳を単一プロセスで実行する統合サーバーの登録
///
/// 使用方法:
/// - UnifiedServer:Enabled = true の場合、IPythonServerManager と IOcrServerManager を
///   UnifiedServerManager経由で提供するアダプターとして登録
/// - UnifiedServer:Enabled = false の場合、既存の分離サーバーを使用
/// </summary>
public sealed class UnifiedServerModule : ServiceModuleBase
{
    private static ILogger? _moduleLogger;

    public override void RegisterServices(IServiceCollection services)
    {
        // 統合サーバー設定登録
        RegisterSettings(services);

        // 統合サーバーマネージャー登録
        RegisterUnifiedServerManager(services);

        // アダプター登録（設定に応じて有効化）
        RegisterAdapters(services);

        // 登録完了ログ（モジュールロガーが初期化されている場合のみ）
        _moduleLogger?.LogInformation("[Issue #292] UnifiedServerModule登録完了");
    }

    private static void RegisterSettings(IServiceCollection services)
    {
        services.AddSingleton<UnifiedServerSettings>(serviceProvider =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<UnifiedServerModule>();
            _moduleLogger = logger;

            var settings = configuration.GetSection(UnifiedServerSettings.SectionName).Get<UnifiedServerSettings>();

            if (settings == null)
            {
                settings = new UnifiedServerSettings
                {
                    Enabled = false, // デフォルトは無効（既存の分離サーバーを使用）
                    Port = UnifiedServerSettings.DefaultPort
                };
            }

            // 設定検証
            var validationResult = settings.ValidateSettings();
            if (!validationResult.IsValid)
            {
                foreach (var error in validationResult.Errors)
                {
                    logger.LogError("[Issue #292] UnifiedServer設定エラー: {Error}", error);
                }
            }
            foreach (var warning in validationResult.Warnings)
            {
                logger.LogWarning("[Issue #292] UnifiedServer設定警告: {Warning}", warning);
            }

            logger.LogInformation("[Issue #292] UnifiedServer設定: Enabled={Enabled}, Port={Port}, StartupTimeout={StartupTimeoutSeconds}s",
                settings.Enabled, settings.Port, settings.StartupTimeoutSeconds);
            return settings;
        });
    }

    private static void RegisterUnifiedServerManager(IServiceCollection services)
    {
        // UnifiedServerManagerを常にSingletonとして登録
        // 有効/無効に関わらず、直接利用したい場合のために登録
        services.AddSingleton<UnifiedServerManager>(serviceProvider =>
        {
            var settings = serviceProvider.GetRequiredService<UnifiedServerSettings>();
            var logger = serviceProvider.GetRequiredService<ILogger<UnifiedServerManager>>();
            var eventAggregator = serviceProvider.GetService<IEventAggregator>();

            logger.LogInformation("[Issue #292] UnifiedServerManager初期化: Port={Port}, StartupTimeout={StartupTimeoutSeconds}s",
                settings.Port, settings.StartupTimeoutSeconds);
            return new UnifiedServerManager(settings, logger, eventAggregator);
        });

        // IUnifiedAIServerManager インターフェースとして登録
        services.AddSingleton<IUnifiedAIServerManager>(serviceProvider =>
            serviceProvider.GetRequiredService<UnifiedServerManager>());
    }

    private static void RegisterAdapters(IServiceCollection services)
    {
        // =====================================================================
        // 二重登録パターンの説明:
        // 1. 具象型としての直接登録 - テストやデバッグ時に直接参照したい場合に使用
        // 2. Keyed Service登録 - EnableUnifiedServerAdapters()による動的切り替え用
        // =====================================================================

        // [直接参照用] 統合サーバー用のPythonアダプター登録
        // 用途: テスト時やデバッグ時に具象型を直接DIで取得したい場合
        services.AddSingleton<UnifiedServerPythonAdapter>(serviceProvider =>
        {
            var unifiedServer = serviceProvider.GetRequiredService<IUnifiedAIServerManager>();
            var logger = serviceProvider.GetRequiredService<ILogger<UnifiedServerPythonAdapter>>();
            return new UnifiedServerPythonAdapter(unifiedServer, logger);
        });

        // [直接参照用] 統合サーバー用のOCRアダプター登録
        // 用途: テスト時やデバッグ時に具象型を直接DIで取得したい場合
        services.AddSingleton<UnifiedServerOcrAdapter>(serviceProvider =>
        {
            var unifiedServer = serviceProvider.GetRequiredService<IUnifiedAIServerManager>();
            var logger = serviceProvider.GetRequiredService<ILogger<UnifiedServerOcrAdapter>>();
            return new UnifiedServerOcrAdapter(unifiedServer, logger);
        });

        // [Keyed Service登録] インターフェース経由での動的切り替え用
        // 用途: EnableUnifiedServerAdapters()で既存の分離サーバーから統合サーバーに切り替える際に使用
        // キー "unified" で取得可能: serviceProvider.GetKeyedService<IPythonServerManager>("unified")
        services.AddKeyedSingleton<IPythonServerManager, UnifiedServerPythonAdapter>(
            "unified",
            (serviceProvider, _) => serviceProvider.GetRequiredService<UnifiedServerPythonAdapter>());

        services.AddKeyedSingleton<IOcrServerManager, UnifiedServerOcrAdapter>(
            "unified",
            (serviceProvider, _) => serviceProvider.GetRequiredService<UnifiedServerOcrAdapter>());

        _moduleLogger?.LogInformation("[Issue #292] 統合サーバーアダプター登録完了（Keyed Service: 'unified'）");
    }

    /// <summary>
    /// 統合サーバーを有効化する際に呼び出すヘルパーメソッド
    /// 既存のIPythonServerManagerとIOcrServerManagerを統合サーバーアダプターで上書き
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <remarks>
    /// 注意: このメソッドはInfrastructureModuleやSuryaOcrModuleより後に呼び出す必要がある
    /// </remarks>
    public static void EnableUnifiedServerAdapters(IServiceCollection services)
    {
        // IPythonServerManagerを統合サーバーアダプターで上書き
        services.AddSingleton<IPythonServerManager>(serviceProvider =>
        {
            var adapter = serviceProvider.GetRequiredService<UnifiedServerPythonAdapter>();
            var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<UnifiedServerModule>();
            logger.LogInformation("[Issue #292] IPythonServerManager → UnifiedServerPythonAdapter");
            return adapter;
        });

        // IOcrServerManagerを統合サーバーアダプターで上書き
        services.AddSingleton<IOcrServerManager>(serviceProvider =>
        {
            var adapter = serviceProvider.GetRequiredService<UnifiedServerOcrAdapter>();
            var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<UnifiedServerModule>();
            logger.LogInformation("[Issue #292] IOcrServerManager → UnifiedServerOcrAdapter");
            return adapter;
        });

        _moduleLogger?.LogInformation("[Issue #292] 統合サーバーアダプターを有効化しました");
    }
}
