using Baketa.Core.Abstractions.OCR;
using Baketa.Core.DI;
using Baketa.Infrastructure.OCR.Clients;
using Baketa.Infrastructure.OCR.Engines;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.DI;

/// <summary>
/// Surya OCR エンジン DIモジュール
/// Issue #189: Surya OCR gRPCクライアント統合
/// PP-OCRv5で検出できなかったビジュアルノベルの日本語ダイアログを高精度検出
/// </summary>
public sealed class SuryaOcrModule : ServiceModuleBase
{
    /// <summary>
    /// デフォルトのgRPCサーバーアドレス
    /// </summary>
    private const string DefaultServerAddress = "http://localhost:50052";

    public override void RegisterServices(IServiceCollection services)
    {
        // Surya OCR設定登録
        RegisterSettings(services);

        // gRPCクライアント登録
        RegisterGrpcClient(services);

        // Surya OCRエンジン登録
        RegisterSuryaOcrEngine(services);
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
                    ServerAddress = DefaultServerAddress
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

            var serverAddress = string.IsNullOrWhiteSpace(settings.ServerAddress)
                ? DefaultServerAddress
                : settings.ServerAddress;

            Console.WriteLine($"🔌 [Issue #189] GrpcOcrClient初期化: {serverAddress}");

            return new GrpcOcrClient(serverAddress, logger);
        });
    }

    private static void RegisterSuryaOcrEngine(IServiceCollection services)
    {
        // SuryaOcrEngineをSingletonとして登録
        services.AddSingleton<SuryaOcrEngine>(serviceProvider =>
        {
            var client = serviceProvider.GetRequiredService<GrpcOcrClient>();
            var logger = serviceProvider.GetRequiredService<ILogger<SuryaOcrEngine>>();

            return new SuryaOcrEngine(client, logger);
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
    public string ServerAddress { get; set; } = "http://localhost:50052";

    /// <summary>
    /// デフォルト言語
    /// </summary>
    public string DefaultLanguage { get; set; } = "ja";
}
