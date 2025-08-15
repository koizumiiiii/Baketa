using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Core.Settings;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation.Local;
using Baketa.Infrastructure.Translation.Local.ConnectionPool;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Demo;

/// <summary>
/// 接続プール効果のデモンストレーション
/// Issue #147の効果測定
/// </summary>
public class ConnectionPoolDemo
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("🚀 Issue #147 接続プール効果測定デモ");
        Console.WriteLine("=========================================");

        // サービス設定
        var services = new ServiceCollection();
        
        // ロギング設定
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // 設定管理
        var configurationData = new Dictionary<string, string?>
        {
            ["Translation:MaxConnections"] = "4",
            ["Translation:MinConnections"] = "2",
            ["Translation:OptimalChunksPerConnection"] = "4",
            ["Translation:ConnectionTimeoutMs"] = "30000",
            ["Translation:HealthCheckIntervalMs"] = "30000"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationData)
            .Build();

        services.AddSingleton<IConfiguration>(configuration);

        // TranslationSettings設定
        services.Configure<TranslationSettings>(options =>
        {
            options.MaxConnections = 4;
            options.MinConnections = 2;
            options.OptimalChunksPerConnection = 4;
            options.ConnectionTimeoutMs = 30000;
            options.HealthCheckIntervalMs = 30000;
        });

        // 接続プールとエンジンの登録
        services.AddSingleton<FixedSizeConnectionPool>();
        services.AddSingleton<OptimizedPythonTranslationEngine>();

        using var serviceProvider = services.BuildServiceProvider();
        var engine = serviceProvider.GetRequiredService<OptimizedPythonTranslationEngine>();
        var connectionPool = serviceProvider.GetRequiredService<FixedSizeConnectionPool>();

        Console.WriteLine("📊 接続プール初期化完了");
        var initialMetrics = connectionPool.GetMetrics();
        Console.WriteLine($"Max接続数: {initialMetrics.MaxConnections}, Min接続数: {initialMetrics.MinConnections}");

        // 翻訳リクエスト作成
        var requests = new List<TranslationRequest>
        {
            TranslationRequest.Create("Hello World", 
                new Language { Code = "en", DisplayName = "English" },
                new Language { Code = "ja", DisplayName = "Japanese" }),
            TranslationRequest.Create("Good morning", 
                new Language { Code = "en", DisplayName = "English" },
                new Language { Code = "ja", DisplayName = "Japanese" }),
            TranslationRequest.Create("Thank you", 
                new Language { Code = "en", DisplayName = "English" },
                new Language { Code = "ja", DisplayName = "Japanese" }),
            TranslationRequest.Create("Goodbye", 
                new Language { Code = "en", DisplayName = "English" },
                new Language { Code = "ja", DisplayName = "Japanese" }),
            TranslationRequest.Create("How are you?", 
                new Language { Code = "en", DisplayName = "English" },
                new Language { Code = "ja", DisplayName = "Japanese" })
        };

        Console.WriteLine($"\n🔥 並列翻訳テスト開始 ({requests.Count}件)");
        Console.WriteLine("Issue #147対策: 接続プールによる並列処理");

        // 並列翻訳実行
        var stopwatch = Stopwatch.StartNew();
        var tasks = requests.Select(request => engine.TranslateAsync(request)).ToArray();
        var responses = await Task.WhenAll(tasks);
        stopwatch.Stop();

        // 結果分析
        var successCount = responses.Count(r => r.IsSuccess);
        var averageTime = stopwatch.ElapsedMilliseconds / (double)requests.Count;

        Console.WriteLine("\n📈 パフォーマンス結果");
        Console.WriteLine("===================");
        Console.WriteLine($"総処理時間: {stopwatch.ElapsedMilliseconds}ms");
        Console.WriteLine($"平均処理時間: {averageTime:F2}ms/件");
        Console.WriteLine($"成功率: {successCount}/{requests.Count} ({successCount * 100.0 / requests.Count:F1}%)");

        // 接続プールメトリクス
        var finalMetrics = connectionPool.GetMetrics();
        Console.WriteLine($"\n📊 接続プール統計");
        Console.WriteLine($"最大接続数: {finalMetrics.MaxConnections}");
        Console.WriteLine($"アクティブ接続数: {finalMetrics.ActiveConnections}");
        Console.WriteLine($"利用可能接続数: {finalMetrics.AvailableConnections}");
        Console.WriteLine($"総作成接続数: {finalMetrics.TotalConnectionsCreated}");
        Console.WriteLine($"接続利用率: {finalMetrics.ConnectionUtilization:P1}");

        // Issue #147の目標評価
        Console.WriteLine($"\n🎯 Issue #147目標評価");
        Console.WriteLine("===================");
        
        if (averageTime < 1000)
        {
            var improvement = (5000 - averageTime) / 5000 * 100; // 5秒から改善された割合
            Console.WriteLine($"✅ 目標達成！平均処理時間 {averageTime:F2}ms < 1000ms");
            Console.WriteLine($"💡 推定改善率: {improvement:F1}% (5000ms → {averageTime:F2}ms)");
        }
        else
        {
            Console.WriteLine($"⚠️ 目標未達成: 平均処理時間 {averageTime:F2}ms > 1000ms");
        }

        if (successCount > requests.Count * 0.8)
        {
            Console.WriteLine($"✅ 成功率良好: {successCount * 100.0 / requests.Count:F1}% > 80%");
        }
        else
        {
            Console.WriteLine($"⚠️ 成功率低下: {successCount * 100.0 / requests.Count:F1}% < 80%");
        }

        // 翻訳結果サンプル表示
        Console.WriteLine($"\n📝 翻訳結果サンプル");
        Console.WriteLine("==================");
        for (int i = 0; i < Math.Min(3, responses.Length); i++)
        {
            var response = responses[i];
            var request = requests[i];
            Console.WriteLine($"{i+1}. '{request.SourceText}' → '{response.TranslatedText}' (Success: {response.IsSuccess})");
        }

        Console.WriteLine("\n🎉 接続プール効果測定完了！");
    }
}