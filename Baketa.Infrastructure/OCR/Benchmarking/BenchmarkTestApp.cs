using System;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Dependency;
using Baketa.Core.Abstractions.OCR;
using Baketa.Infrastructure.DI;
using Baketa.Infrastructure.OCR.AdaptivePreprocessing;
using Baketa.Infrastructure.OCR.MultiScale;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
            var report = await benchmarkRunner.RunAsync().ConfigureAwait(false);

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

            var initialized = await ocrEngine.InitializeAsync(settings).ConfigureAwait(false);
            if (!initialized)
            {
                Console.WriteLine("OCRエンジンの初期化に失敗しました");
                return;
            }

            // 簡単なテストケースの生成
            Console.WriteLine("テストケース生成中...");
            var testCases = await testCaseGenerator.GenerateErrorPatternTestCasesAsync().ConfigureAwait(false);
            var limitedTestCases = testCases.Take(5).ToList(); // 最初の5件のみ

            // ベンチマークの実行
            Console.WriteLine("ベンチマーク実行中...");
            var result = await benchmarkRunner.RunParameterOptimizationBenchmarkAsync(
                ocrEngine, limitedTestCases).ConfigureAwait(false);

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
    public static async Task Main(string[] _)
    {
        Console.WriteLine("OCRベンチマークテストアプリケーション");
        Console.WriteLine("1. Phase 1完全ベンチマーク");
        Console.WriteLine("2. クイックベンチマーク");
        Console.WriteLine("3. Phase 2 マルチスケールOCRテスト");
        Console.WriteLine("4. Phase 3 適応的前処理システムテスト");
        Console.WriteLine("5. Phase 3 適応的前処理ベンチマーク");
        Console.WriteLine("実行する番号を入力してください: ");

        var input = Console.ReadLine();

        switch (input)
        {
            case "1":
                await BenchmarkTestApp.RunPhase1BenchmarkAsync().ConfigureAwait(false);
                break;
            case "2":
                await BenchmarkTestApp.RunQuickBenchmarkAsync().ConfigureAwait(false);
                break;
            case "3":
                await RunMultiScaleBenchmarkAsync().ConfigureAwait(false);
                break;
            case "4":
                await RunPhase3ComprehensiveTestAsync().ConfigureAwait(false);
                break;
            case "5":
                await RunPhase3BenchmarkAsync().ConfigureAwait(false);
                break;
            default:
                Console.WriteLine("無効な選択です。クイックベンチマークを実行します。");
                await BenchmarkTestApp.RunQuickBenchmarkAsync().ConfigureAwait(false);
                break;
        }

        Console.WriteLine("\nEnterキーを押して終了...");
        Console.ReadLine();
    }

    /// <summary>
    /// Phase 2: マルチスケールOCRベンチマークを実行
    /// </summary>
    public static async Task RunMultiScaleBenchmarkAsync()
    {
        Console.WriteLine("🔍 Phase 2: マルチスケールOCRベンチマーク開始");

        try
        {
            // DI設定
            var services = new ServiceCollection();

            // ログ設定
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            // 全てのDIモジュールを登録
            var paddleOcrModule = new PaddleOcrModule();
            paddleOcrModule.RegisterServices(services);

            var serviceProvider = services.BuildServiceProvider();

            // マルチスケールテストを実行
            var testRunner = serviceProvider.GetRequiredService<MultiScaleTestRunner>();
            await testRunner.TestSmallTextRecognitionAsync().ConfigureAwait(false);

            // デバッグ用のキャプチャ画像でのテスト
            var debugImagePath = @"E:\dev\Baketa\Baketa.UI\bin\x64\Debug\net8.0-windows\debug_captured_1fc74558.png";
            if (System.IO.File.Exists(debugImagePath))
            {
                Console.WriteLine("\n🖼️ デバッグキャプチャ画像でのマルチスケールテスト");
                await MultiScaleTestApp.TestWithRealImageAsync(debugImagePath).ConfigureAwait(false);
            }

            Console.WriteLine("✅ マルチスケールベンチマーク完了");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ マルチスケールベンチマークエラー: {ex.Message}");
            Console.WriteLine($"詳細: {ex}");
        }
    }

    /// <summary>
    /// Phase 3: 適応的前処理システムの包括的テスト
    /// </summary>
    public static async Task RunPhase3ComprehensiveTestAsync()
    {
        Console.WriteLine("🔧 Phase 3: 適応的前処理システム包括的テスト開始");

        try
        {
            await AdaptivePreprocessing.Phase3TestApp.RunComprehensiveTestAsync().ConfigureAwait(false);

            // デバッグキャプチャ画像でのテスト
            var debugImagePath = @"E:\dev\Baketa\Baketa.UI\bin\x64\Debug\net8.0-windows\debug_captured_1fc74558.png";
            if (System.IO.File.Exists(debugImagePath))
            {
                Console.WriteLine("\n🖼️ デバッグキャプチャ画像での実地テスト");
                await AdaptivePreprocessing.Phase3TestApp.TestWithRealCaptureAsync(debugImagePath).ConfigureAwait(false);
            }

            Console.WriteLine("✅ Phase 3 包括的テスト完了");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Phase 3 包括的テストエラー: {ex.Message}");
            Console.WriteLine($"詳細: {ex}");
        }
    }

    /// <summary>
    /// Phase 3: 適応的前処理ベンチマークを実行
    /// </summary>
    public static async Task RunPhase3BenchmarkAsync()
    {
        Console.WriteLine("📊 Phase 3: 適応的前処理ベンチマーク開始");

        try
        {
            // DI設定
            var services = new ServiceCollection();

            // ログ設定
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            // 全てのDIモジュールを登録
            var paddleOcrModule = new PaddleOcrModule();
            paddleOcrModule.RegisterServices(services);

            var serviceProvider = services.BuildServiceProvider();

            // ベンチマーク実行
            var benchmark = serviceProvider.GetRequiredService<AdaptivePreprocessing.AdaptivePreprocessingBenchmark>();
            var ocrEngine = serviceProvider.GetRequiredService<IOcrEngine>();

            // OCRエンジン初期化
            var settings = new OcrEngineSettings
            {
                Language = "jpn",
                DetectionThreshold = 0.3,
                RecognitionThreshold = 0.3
            };

            var initialized = await ocrEngine.InitializeAsync(settings).ConfigureAwait(false);
            if (!initialized)
            {
                Console.WriteLine("❌ OCRエンジンの初期化に失敗しました");
                return;
            }

            // 包括的ベンチマーク実行
            Console.WriteLine("包括的ベンチマーク実行中...");
            var result = await benchmark.RunComprehensiveBenchmarkAsync(ocrEngine).ConfigureAwait(false);

            // 結果表示
            Console.WriteLine("\n📈 ベンチマーク結果:");
            Console.WriteLine($"  総テストケース数: {result.TotalTestCases}");
            Console.WriteLine($"  成功ケース数: {result.Analysis.SuccessfulTestCases}");
            Console.WriteLine($"  平均改善率: {result.Analysis.AverageImprovementPercentage:F2}%");
            Console.WriteLine($"  平均信頼度改善: {result.Analysis.AverageConfidenceImprovement:F3}");
            Console.WriteLine($"  平均最適化時間: {result.Analysis.AverageOptimizationTimeMs:F1}ms");
            Console.WriteLine($"  平均実行時間: {result.Analysis.AverageExecutionTimeMs:F1}ms");
            Console.WriteLine($"  総ベンチマーク時間: {result.TotalBenchmarkTimeMs}ms");

            Console.WriteLine("\n🎯 最適化戦略別分析:");
            foreach (var strategy in result.Analysis.OptimizationStrategies)
            {
                Console.WriteLine($"  {strategy.Key}: {strategy.Value}件");
            }

            Console.WriteLine("✅ Phase 3 ベンチマーク完了");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Phase 3 ベンチマークエラー: {ex.Message}");
            Console.WriteLine($"詳細: {ex}");
        }
    }
}
