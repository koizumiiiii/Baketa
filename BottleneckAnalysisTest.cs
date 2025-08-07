using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Infrastructure.OCR.PaddleOCR.Factory;
using Baketa.Infrastructure.OCR.PaddleOCR.Models;
using Baketa.Infrastructure.OCR.PaddleOCR.Engine;
using Baketa.Infrastructure.OCR.TextProcessing;
using Baketa.Infrastructure.OCR.PostProcessing;
using Baketa.Core.Abstractions.Performance;

namespace Baketa.Tests;

/// <summary>
/// NonSingletonPaddleOcrEngineボトルネック分析専用テスト
/// Gemini推奨のボトルネック詳細測定を実行
/// </summary>
public class BottleneckAnalysisTest
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("🔍 【緊急】NonSingletonPaddleOcrEngine ボトルネック分析開始");
        
        // 本番環境エンジン強制設定
        Environment.SetEnvironmentVariable("BAKETA_FORCE_PRODUCTION_OCR", "true");
        
        // ロギング設定
        using var serviceProvider = CreateServiceProvider();
        var factory = serviceProvider.GetRequiredService<IPaddleOcrEngineFactory>();
        
        try
        {
            Console.WriteLine("⚡ NonSingletonPaddleOcrEngine初期化開始...");
            var totalWatch = System.Diagnostics.Stopwatch.StartNew();
            
            // 🚨 ここで17秒の初期化ボトルネックが発生するはず
            var engine = await factory.CreateAsync();
            
            totalWatch.Stop();
            Console.WriteLine($"🔍 【結果】初期化完了: {totalWatch.ElapsedMilliseconds}ms");
            
            // エンジンクリーンアップ
            engine.Dispose();
            
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ エラー: {ex.Message}");
            Console.WriteLine($"📊 スタックトレース: {ex.StackTrace}");
        }
        
        Console.WriteLine("🏁 ボトルネック分析完了");
    }
    
    private static ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        
        // ロギング設定（詳細レベル）
        services.AddLogging(builder => 
            builder.AddConsole()
                   .SetMinimumLevel(LogLevel.Debug));
        
        // 必要な依存関係を登録
        services.AddSingleton<IModelPathResolver>(provider => 
        {
            var logger = provider.GetRequiredService<ILogger<DefaultModelPathResolver>>();
            return new DefaultModelPathResolver("E:\\dev\\Baketa\\Baketa.UI\\bin\\Debug\\net8.0-windows10.0.19041.0\\models", logger);
        });
        
        services.AddTransient<IOcrPreprocessingService, DefaultOcrPreprocessingService>();
        services.AddTransient<ITextMerger, DefaultTextMerger>();
        services.AddTransient<IOcrPostProcessor, DefaultOcrPostProcessor>();
        services.AddTransient<IGpuMemoryManager, DefaultGpuMemoryManager>();
        services.AddTransient<IPaddleOcrEngineFactory, PaddleOcrEngineFactory>();
        
        return services.BuildServiceProvider();
    }
}

// ダミー実装クラス（必要最小限）
public class DefaultOcrPreprocessingService : IOcrPreprocessingService
{
    public Task<byte[]> ProcessImageAsync(byte[] imageData) => Task.FromResult(imageData);
}

public class DefaultTextMerger : ITextMerger  
{
    public string MergeTextRegions(System.Collections.Generic.IEnumerable<object> regions) => "";
}

public class DefaultOcrPostProcessor : IOcrPostProcessor
{
    public string ProcessText(string text) => text;
}

public class DefaultGpuMemoryManager : IGpuMemoryManager
{
    public bool IsMemoryAvailable(long requiredBytes) => true;
    public void ReleaseUnusedMemory() { }
}