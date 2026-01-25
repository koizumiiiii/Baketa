using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Monitoring;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.DI;
using Baketa.Core.Settings;
using Baketa.Infrastructure.OCR.Clients;
using Baketa.Infrastructure.OCR.Engines;
using Baketa.Infrastructure.OCR.Services;
using Baketa.Infrastructure.Translation.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.DI;

/// <summary>
/// Surya OCR エンジン DIモジュール
/// Issue #189: Surya OCR gRPCクライアント統合
/// PP-OCRv5で検出できなかったビジュアルノベルの日本語ダイアログを高精度検出
///
/// [Issue #292] 統合サーバーモード対応:
/// - 統合サーバー有効時はSuryaServerManagerのサーバー起動をスキップ
/// - GrpcOcrClientは統合サーバーのポートを使用
/// </summary>
public sealed class SuryaOcrModule : ServiceModuleBase
{

    public override void RegisterServices(IServiceCollection services)
    {
        // Surya OCR設定登録
        RegisterSettings(services);

        // Suryaサーバーマネージャー登録（自動起動対応）
        RegisterServerManager(services);

        // gRPCクライアント登録
        RegisterGrpcClient(services);

        // Surya OCRエンジン登録
        RegisterSuryaOcrEngine(services);

        // Issue #293: 投機的OCRサービス登録
        RegisterSpeculativeOcrService(services);
    }

    private static void RegisterServerManager(IServiceCollection services)
    {
        services.AddSingleton<SuryaServerManager>(serviceProvider =>
        {
            var settings = serviceProvider.GetRequiredService<SuryaOcrSettings>();
            var logger = serviceProvider.GetRequiredService<ILogger<SuryaServerManager>>();
            // [Issue #264] IEventAggregatorを取得（存在しない場合はnull）
            var eventAggregator = serviceProvider.GetService<IEventAggregator>();
            // [Issue #292] GrpcPortProviderを取得（統合サーバー待機用）
            var grpcPortProvider = serviceProvider.GetService<GrpcPortProvider>();

            // [Issue #292] 統合サーバー設定を取得
            var unifiedSettings = serviceProvider.GetService<UnifiedServerSettings>();
            var isUnifiedMode = unifiedSettings?.Enabled ?? false;

            // ポート番号をアドレスから抽出
            // [Issue #292] 統合サーバーモードでは統合ポートを使用
            int port;
            if (isUnifiedMode)
            {
                port = unifiedSettings?.Port ?? ServerPortConstants.UnifiedServerPort;
                logger.LogInformation("[Issue #292] SuryaServerManager初期化: 統合サーバーモード - Port {Port}", port);
            }
            else
            {
                port = ServerPortConstants.OcrServerPort;
                if (!string.IsNullOrEmpty(settings.ServerAddress))
                {
                    var uri = new Uri(settings.ServerAddress);
                    port = uri.Port;
                }
                logger.LogInformation("[Issue #189] SuryaServerManager初期化: 分離サーバーモード - Port {Port}", port);
            }

            var manager = new SuryaServerManager(port, logger, eventAggregator, grpcPortProvider, unifiedSettings);

            // [Issue #292] 統合サーバーモードではサーバー起動をスキップするフラグを設定
            if (isUnifiedMode)
            {
                manager.SetUnifiedMode(true);
                logger.LogInformation("[Issue #292] SuryaServerManager: 統合サーバーモード有効 - サーバー起動スキップ");
            }

            return manager;
        });
    }

    private static void RegisterSettings(IServiceCollection services)
    {
        services.AddSingleton<SuryaOcrSettings>(serviceProvider =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var settings = configuration.GetSection("SuryaOcr").Get<SuryaOcrSettings>();

            if (settings == null)
            {
                settings = new SuryaOcrSettings
                {
                    Enabled = true,
                    ServerAddress = ServerPortConstants.OcrServerAddress
                };
            }

            return settings;
        });
    }

    private static void RegisterGrpcClient(IServiceCollection services)
    {
        services.AddSingleton<GrpcOcrClient>(serviceProvider =>
        {
            var settings = serviceProvider.GetRequiredService<SuryaOcrSettings>();
            var logger = serviceProvider.GetRequiredService<ILogger<GrpcOcrClient>>();

            // [Issue #292] 統合サーバー設定を取得
            var unifiedSettings = serviceProvider.GetService<UnifiedServerSettings>();
            var isUnifiedMode = unifiedSettings?.Enabled ?? false;

            string serverAddress;
            if (isUnifiedMode)
            {
                // 統合サーバーモード: 統合ポートを使用
                var port = unifiedSettings?.Port ?? ServerPortConstants.UnifiedServerPort;
                serverAddress = $"http://127.0.0.1:{port}";
                logger.LogInformation("[Issue #292] GrpcOcrClient初期化: 統合サーバーモード - {ServerAddress}", serverAddress);
            }
            else
            {
                // 分離サーバーモード: 既存の設定を使用
                serverAddress = string.IsNullOrWhiteSpace(settings.ServerAddress)
                    ? ServerPortConstants.OcrServerAddress
                    : settings.ServerAddress;
                logger.LogInformation("[Issue #189] GrpcOcrClient初期化: 分離サーバーモード - {ServerAddress}", serverAddress);
            }

            return new GrpcOcrClient(serverAddress, logger);
        });
    }

    private static void RegisterSuryaOcrEngine(IServiceCollection services)
    {
        // SuryaOcrEngineをSingletonとして登録（サーバー自動起動対応）
        // Issue #300: IEventAggregator追加で自動復旧機能対応
        services.AddSingleton<SuryaOcrEngine>(serviceProvider =>
        {
            var client = serviceProvider.GetRequiredService<GrpcOcrClient>();
            var serverManager = serviceProvider.GetRequiredService<SuryaServerManager>();
            var eventAggregator = serviceProvider.GetService<IEventAggregator>();
            var logger = serviceProvider.GetRequiredService<ILogger<SuryaOcrEngine>>();

            return new SuryaOcrEngine(client, serverManager, eventAggregator, logger);
        });

        // SuryaOcrEngineをKeyed Serviceとしても登録
        services.AddKeyedSingleton<IOcrEngine, SuryaOcrEngine>("surya", (serviceProvider, _) =>
        {
            return serviceProvider.GetRequiredService<SuryaOcrEngine>();
        });

        // Issue #189: SuryaOcrEngineをデフォルトIOcrEngineとして登録
        // フォールバックなし - Suryaのみ使用
        services.AddSingleton<IOcrEngine>(serviceProvider =>
        {
            var settings = serviceProvider.GetRequiredService<SuryaOcrSettings>();

            if (settings.Enabled)
            {
                var suryaEngine = serviceProvider.GetRequiredService<SuryaOcrEngine>();
                Console.WriteLine($"✅ [Issue #189] IOcrEngine → SuryaOcrEngine 登録完了");
                Console.WriteLine($"   → エンジン: {suryaEngine.EngineName} v{suryaEngine.EngineVersion}");
                Console.WriteLine($"   → 日本語ビジュアルノベル対応");
                return suryaEngine;
            }

            // Surya無効時もSuryaOcrEngineを返す（初期化時にエラーハンドリング）
            Console.WriteLine("⚠️ [Issue #189] Surya OCR設定が無効ですが、SuryaOcrEngineを使用します");
            return serviceProvider.GetRequiredService<SuryaOcrEngine>();
        });

        Console.WriteLine("✅ [Issue #189] SuryaOcrModule登録完了");
    }

    /// <summary>
    /// Issue #293: 投機的OCRサービス登録
    /// GPU余裕時にOCRを先行実行し、Shot翻訳の応答時間を短縮
    /// </summary>
    private static void RegisterSpeculativeOcrService(IServiceCollection services)
    {
        // 投機的OCR設定をIOptionsMonitorで登録
        services.Configure<SpeculativeOcrSettings>(options =>
        {
            // デフォルト設定（appsettings.jsonで上書き可能）
            options = new SpeculativeOcrSettings();
        });

        // 設定をappsettings.jsonから読み込み
        services.AddSingleton<IConfigureOptions<SpeculativeOcrSettings>>(serviceProvider =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            return new ConfigureFromConfigurationOptions<SpeculativeOcrSettings>(
                configuration.GetSection("SpeculativeOcr"));
        });

        // 投機的OCRサービス登録
        services.AddSingleton<ISpeculativeOcrService>(serviceProvider =>
        {
            var ocrEngine = serviceProvider.GetRequiredService<IOcrEngine>();
            var resourceMonitor = serviceProvider.GetRequiredService<IResourceMonitor>();
            var translationModeService = serviceProvider.GetRequiredService<ITranslationModeService>();
            var settingsMonitor = serviceProvider.GetRequiredService<IOptionsMonitor<SpeculativeOcrSettings>>();
            var logger = serviceProvider.GetRequiredService<ILogger<SpeculativeOcrService>>();

            Console.WriteLine("✅ [Issue #293] ISpeculativeOcrService → SpeculativeOcrService 登録完了");
            Console.WriteLine("   → GPU余裕時に投機的OCR実行");
            Console.WriteLine("   → Shot翻訳応答時間短縮機能");

            return new SpeculativeOcrService(
                ocrEngine,
                resourceMonitor,
                translationModeService,
                settingsMonitor,
                logger);
        });

        Console.WriteLine("✅ [Issue #293] SpeculativeOcrService登録完了");
    }
}

/// <summary>
/// Surya OCR設定
/// </summary>
public sealed class SuryaOcrSettings
{
    /// <summary>
    /// Surya OCRを有効にするか
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// gRPCサーバーアドレス
    /// </summary>
    public string ServerAddress { get; set; } = ServerPortConstants.OcrServerAddress;

    /// <summary>
    /// デフォルト言語
    /// </summary>
    public string DefaultLanguage { get; set; } = "ja";
}
