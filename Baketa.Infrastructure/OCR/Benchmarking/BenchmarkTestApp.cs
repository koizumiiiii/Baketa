using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Dependency;
using Baketa.Infrastructure.DI;

namespace Baketa.Infrastructure.OCR.Benchmarking;

/// <summary>
/// OCRベンチマークのテストアプリケーション
/// </summary>
public static class BenchmarkTestApp
{
    /// <summary>
    /// Phase 1ベンチマークのテスト実行
    /// </summary>
    public static async Task RunPhase1BenchmarkAsync()
    {
        Console.WriteLine("=== OCR Phase 1 ベンチマーク実行 ===");
        
        // DIコンテナの設定
        var services = new ServiceCollection();
        
        // ロギング設定
        services.AddLogging(builder =>
        {
            builder.AddConsole()
                   .SetMinimumLevel(LogLevel.Information);
        });
        
        // 必要なモジュールの登録
        var paddleOcrModule = new PaddleOcrModule();
        paddleOcrModule.RegisterServices(services);
        
        // 画像処理モジュールの登録（必要に応じて）
        // var imagingModule = new ImagingModule();
        // imagingModule.RegisterServices(services);
        
        // OCR前処理モジュールの登録（必要に応じて）
        // var ocrPreprocessingModule = new OcrPreprocessingModule();
        // ocrPreprocessingModule.RegisterServices(services);
        
        var serviceProvider = services.BuildServiceProvider();
        
        try
        {
            // Phase 1ベンチマークランナーの取得
            var benchmarkRunner = serviceProvider.GetRequiredService<Phase1BenchmarkRunner>();
            
            // ベンチマークの実行
            var report = await benchmarkRunner.RunAsync();
            
            // 結果の表示
            Console.WriteLine("=== ベンチマーク実行完了 ===");
            Console.WriteLine($"最適手法: {report.BestMethodName}");
            Console.WriteLine($"精度改善: {report.AccuracyImprovement * 100:F2}%");
            Console.WriteLine($"速度変化: {report.SpeedChange:F1}文字/秒");
            
            // 推奨設定の表示
            Console.WriteLine("\n=== 推奨設定 ===");
            foreach (var recommendation in report.Recommendations)
            {
                Console.WriteLine($"• {recommendation}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ベンチマーク実行エラー: {ex.Message}");
            Console.WriteLine($"詳細: {ex.StackTrace}");
        }
        finally
        {
            serviceProvider.Dispose();
        }
    }
    
    /// <summary>
    /// 単体テストとしてのベンチマーク実行
    /// </summary>
    public static async Task RunQuickBenchmarkAsync()
    {
        Console.WriteLine("=== OCR クイックベンチマーク実行 ===");
        
        var services = new ServiceCollection();
        
        // 最小限のロギング設定
        services.AddLogging(builder =>
        {
            builder.AddConsole()
                   .SetMinimumLevel(LogLevel.Warning);
        });
        
        var paddleOcrModule = new PaddleOcrModule();
        paddleOcrModule.RegisterServices(services);
        
        var serviceProvider = services.BuildServiceProvider();
        
        try
        {
            // 必要なサービスの取得
            var benchmarkRunner = serviceProvider.GetRequiredService<OcrParameterBenchmarkRunner>();
            var testCaseGenerator = serviceProvider.GetRequiredService<TestCaseGenerator>();
            var ocrEngine = serviceProvider.GetRequiredService<Baketa.Core.Abstractions.OCR.IOcrEngine>();
            
            // OCRエンジンの初期化
            Console.WriteLine("OCRエンジン初期化中...");
            var settings = new Baketa.Core.Abstractions.OCR.OcrEngineSettings
            {
                Language = "jpn",
                DetectionThreshold = 0.3,
                RecognitionThreshold = 0.3
            };
            
            var initialized = await ocrEngine.InitializeAsync(settings);
            if (!initialized)
            {
                Console.WriteLine("OCRエンジンの初期化に失敗しました");
                return;
            }
            
            // 簡単なテストケースの生成
            Console.WriteLine("テストケース生成中...");
            var testCases = await testCaseGenerator.GenerateErrorPatternTestCasesAsync();
            var limitedTestCases = testCases.Take(5).ToList(); // 最初の5件のみ
            
            // ベンチマークの実行
            Console.WriteLine("ベンチマーク実行中...");
            var result = await benchmarkRunner.RunParameterOptimizationBenchmarkAsync(
                ocrEngine, limitedTestCases);
            
            // 結果の表示
            Console.WriteLine("\n=== クイックベンチマーク結果 ===");
            Console.WriteLine($"最適手法: {result.BestMethod.MethodName}");
            Console.WriteLine($"精度: {result.BestMethod.AverageAccuracy * 100:F2}%");
            Console.WriteLine($"処理速度: {result.BestMethod.ProcessingSpeed:F1}文字/秒");
            
            Console.WriteLine("\n=== 各手法の結果 ===");
            foreach (var method in result.Results)
            {
                Console.WriteLine($"{method.MethodName}: {method.AverageAccuracy * 100:F2}%");
            }
            
            Console.WriteLine("\n=== 改善概要 ===");
            Console.WriteLine(result.ImprovementSummary);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"クイックベンチマーク実行エラー: {ex.Message}");
            Console.WriteLine($"詳細: {ex.StackTrace}");
        }
        finally
        {
            serviceProvider.Dispose();
        }
    }
}

/// <summary>
/// ベンチマーク用のプログラムエントリポイント
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("OCRベンチマークテストアプリケーション");
        Console.WriteLine("1. Phase 1完全ベンチマーク");
        Console.WriteLine("2. クイックベンチマーク");
        Console.WriteLine("実行する番号を入力してください: ");
        
        var input = Console.ReadLine();
        
        switch (input)
        {
            case "1":
                await BenchmarkTestApp.RunPhase1BenchmarkAsync();
                break;
            case "2":
                await BenchmarkTestApp.RunQuickBenchmarkAsync();
                break;
            default:
                Console.WriteLine("無効な選択です。クイックベンチマークを実行します。");
                await BenchmarkTestApp.RunQuickBenchmarkAsync();
                break;
        }
        
        Console.WriteLine("\nEnterキーを押して終了...");
        Console.ReadLine();
    }
}