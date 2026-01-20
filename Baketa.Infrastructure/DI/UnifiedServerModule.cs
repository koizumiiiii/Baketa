using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Server;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.DI;
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
    /// <summary>
    /// デフォルトの統合サーバーポート
    /// </summary>
    private const int DefaultPort = 50053;

    public override void RegisterServices(IServiceCollection services)
    {
        // 統合サーバー設定登録
        RegisterSettings(services);

        // 統合サーバーマネージャー登録
        RegisterUnifiedServerManager(services);

        // アダプター登録（設定に応じて有効化）
        RegisterAdapters(services);

        Console.WriteLine("✅ [Issue #292] UnifiedServerModule登録完了");
    }

    private static void RegisterSettings(IServiceCollection services)
    {
        services.AddSingleton<UnifiedServerSettings>(serviceProvider =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var settings = configuration.GetSection("UnifiedServer").Get<UnifiedServerSettings>();

            if (settings == null)
            {
                settings = new UnifiedServerSettings
                {
                    Enabled = false, // デフォルトは無効（既存の分離サーバーを使用）
                    Port = DefaultPort
                };
            }

            Console.WriteLine($"🔧 [Issue #292] UnifiedServer設定: Enabled={settings.Enabled}, Port={settings.Port}");
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

            Console.WriteLine($"🔧 [Issue #292] UnifiedServerManager初期化: Port={settings.Port}");
            return new UnifiedServerManager(settings.Port, logger, eventAggregator);
        });

        // IUnifiedAIServerManager インターフェースとして登録
        services.AddSingleton<IUnifiedAIServerManager>(serviceProvider =>
            serviceProvider.GetRequiredService<UnifiedServerManager>());
    }

    private static void RegisterAdapters(IServiceCollection services)
    {
        // 統合サーバー用のPythonアダプター登録
        services.AddSingleton<UnifiedServerPythonAdapter>(serviceProvider =>
        {
            var unifiedServer = serviceProvider.GetRequiredService<IUnifiedAIServerManager>();
            var logger = serviceProvider.GetRequiredService<ILogger<UnifiedServerPythonAdapter>>();
            return new UnifiedServerPythonAdapter(unifiedServer, logger);
        });

        // 統合サーバー用のOCRアダプター登録
        services.AddSingleton<UnifiedServerOcrAdapter>(serviceProvider =>
        {
            var unifiedServer = serviceProvider.GetRequiredService<IUnifiedAIServerManager>();
            var logger = serviceProvider.GetRequiredService<ILogger<UnifiedServerOcrAdapter>>();
            return new UnifiedServerOcrAdapter(unifiedServer, logger);
        });

        // 設定に応じてIPythonServerManagerとIOcrServerManagerの実装を切り替え
        // Keyed Serviceとして登録（"unified"キー）
        services.AddKeyedSingleton<IPythonServerManager, UnifiedServerPythonAdapter>(
            "unified",
            (serviceProvider, _) => serviceProvider.GetRequiredService<UnifiedServerPythonAdapter>());

        services.AddKeyedSingleton<IOcrServerManager, UnifiedServerOcrAdapter>(
            "unified",
            (serviceProvider, _) => serviceProvider.GetRequiredService<UnifiedServerOcrAdapter>());

        Console.WriteLine("✅ [Issue #292] 統合サーバーアダプター登録完了（Keyed Service: 'unified'）");
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
            Console.WriteLine("🔀 [Issue #292] IPythonServerManager → UnifiedServerPythonAdapter");
            return adapter;
        });

        // IOcrServerManagerを統合サーバーアダプターで上書き
        services.AddSingleton<IOcrServerManager>(serviceProvider =>
        {
            var adapter = serviceProvider.GetRequiredService<UnifiedServerOcrAdapter>();
            Console.WriteLine("🔀 [Issue #292] IOcrServerManager → UnifiedServerOcrAdapter");
            return adapter;
        });

        Console.WriteLine("✅ [Issue #292] 統合サーバーアダプターを有効化しました");
    }
}

/// <summary>
/// 統合サーバー設定
/// </summary>
public sealed class UnifiedServerSettings
{
    /// <summary>
    /// 統合サーバーを有効にするか
    /// true: OCRと翻訳を単一プロセスで実行
    /// false: 既存の分離サーバー（SuryaOcrServer + TranslationServer）を使用
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 統合サーバーのポート番号
    /// </summary>
    public int Port { get; set; } = 50053;

    /// <summary>
    /// サーバー起動タイムアウト（秒）
    /// </summary>
    public int StartupTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// ヘルスチェック間隔（秒）
    /// </summary>
    public int HealthCheckIntervalSeconds { get; set; } = 30;
}
