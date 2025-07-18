using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Baketa.Infrastructure.DI;
using Baketa.Core.Abstractions.OCR;
using System.Diagnostics;
using System.IO;

namespace Baketa.Infrastructure.OCR.AdaptivePreprocessing;

/// <summary>
/// Phase 3: 適応的前処理システムのテストアプリケーション
/// </summary>
public static class Phase3TestApp
{
    /// <summary>
    /// Phase 3の包括的テストを実行
    /// </summary>
    public static async Task RunComprehensiveTestAsync()
    {
        Console.WriteLine("🔧 Phase 3: 適応的前処理システム テスト開始");
        
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
            
            // 各コンポーネントのテスト
            await TestImageQualityAnalyzerAsync(serviceProvider).ConfigureAwait(false);
            await TestAdaptiveParameterOptimizerAsync(serviceProvider).ConfigureAwait(false);
            await TestAdaptiveOcrEngineAsync(serviceProvider).ConfigureAwait(false);
            await TestAdaptivePreprocessingBenchmarkAsync(serviceProvider).ConfigureAwait(false);
            
            Console.WriteLine("✅ Phase 3 包括的テスト完了");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Phase 3 テストエラー: {ex.Message}");
            Console.WriteLine($"詳細: {ex}");
        }
    }

    /// <summary>
    /// 画像品質分析機能のテスト
    /// </summary>
    private static async Task TestImageQualityAnalyzerAsync(IServiceProvider serviceProvider)
    {
        Console.WriteLine("\n🔍 画像品質分析機能テスト");
        
        var analyzer = serviceProvider.GetRequiredService<IImageQualityAnalyzer>();
        var testCaseGenerator = serviceProvider.GetRequiredService<Benchmarking.TestCaseGenerator>();
        
        try
        {
            // テスト画像生成
            var testImage = await testCaseGenerator.GenerateSmallTextImageAsync("品質テスト", 12).ConfigureAwait(false);
            
            // 品質分析実行
            var sw = Stopwatch.StartNew();
            var qualityMetrics = await analyzer.AnalyzeAsync(testImage).ConfigureAwait(false);
            var textDensityMetrics = await analyzer.AnalyzeTextDensityAsync(testImage).ConfigureAwait(false);
            sw.Stop();
            
            Console.WriteLine($"  品質分析結果:");
            Console.WriteLine($"    総合品質: {qualityMetrics.OverallQuality:F3}");
            Console.WriteLine($"    コントラスト: {qualityMetrics.Contrast:F3}");
            Console.WriteLine($"    明度: {qualityMetrics.Brightness:F3}");
            Console.WriteLine($"    ノイズ: {qualityMetrics.NoiseLevel:F3}");
            Console.WriteLine($"    シャープネス: {qualityMetrics.Sharpness:F3}");
            Console.WriteLine($"  テキスト密度分析結果:");
            Console.WriteLine($"    エッジ密度: {textDensityMetrics.EdgeDensity:F3}");
            Console.WriteLine($"    推定テキストサイズ: {textDensityMetrics.EstimatedTextSize:F1}px");
            Console.WriteLine($"    テキスト領域割合: {textDensityMetrics.TextAreaRatio:F3}");
            Console.WriteLine($"  処理時間: {sw.ElapsedMilliseconds}ms");
            Console.WriteLine("  ✅ 画像品質分析テスト成功");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ 画像品質分析テストエラー: {ex.Message}");
        }
    }

    /// <summary>
    /// 適応的パラメータ最適化機能のテスト
    /// </summary>
    private static async Task TestAdaptiveParameterOptimizerAsync(IServiceProvider serviceProvider)
    {
        Console.WriteLine("\n⚙️ 適応的パラメータ最適化機能テスト");
        
        var optimizer = serviceProvider.GetRequiredService<IAdaptivePreprocessingParameterOptimizer>();
        var testCaseGenerator = serviceProvider.GetRequiredService<Benchmarking.TestCaseGenerator>();
        
        try
        {
            // 各種品質の画像でテスト
            var testCases = new List<(string Name, Func<Task<Core.Abstractions.Imaging.IAdvancedImage>> Generator)>
            {
                ("高品質", () => testCaseGenerator.GenerateHighQualityImageAsync("高品質テスト")),
                ("低品質", () => testCaseGenerator.GenerateLowQualityImageAsync("低品質テスト", 0.2, 0.3, 0.4)),
                ("小文字", () => testCaseGenerator.GenerateSmallTextImageAsync("小文字テスト", 8)),
                ("ノイズ", () => testCaseGenerator.GenerateNoisyImageAsync("ノイズテスト", "Gaussian", 0.3))
            };

            foreach (var (Name, Generator) in testCases)
            {
                var testImage = await Generator().ConfigureAwait(false);
                var sw = Stopwatch.StartNew();
                var result = await optimizer.OptimizeWithDetailsAsync(testImage).ConfigureAwait(false);
                sw.Stop();

                Console.WriteLine($"  {Name}画像の最適化結果:");
                Console.WriteLine($"    戦略: {result.OptimizationStrategy}");
                Console.WriteLine($"    理由: {result.OptimizationReason}");
                Console.WriteLine($"    改善予想: {result.ExpectedImprovement:F2}");
                Console.WriteLine($"    信頼度: {result.ParameterConfidence:F2}");
                Console.WriteLine($"    ガンマ: {result.Parameters.Gamma:F2}");
                Console.WriteLine($"    コントラスト: {result.Parameters.Contrast:F2}");
                Console.WriteLine($"    明度: {result.Parameters.Brightness:F2}");
                Console.WriteLine($"    ノイズ除去: {result.Parameters.NoiseReduction:F2}");
                Console.WriteLine($"    処理時間: {sw.ElapsedMilliseconds}ms");
            }
            
            Console.WriteLine("  ✅ 適応的パラメータ最適化テスト成功");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ 適応的パラメータ最適化テストエラー: {ex.Message}");
        }
    }

    /// <summary>
    /// 適応的OCRエンジンのテスト
    /// </summary>
    private static async Task TestAdaptiveOcrEngineAsync(IServiceProvider serviceProvider)
    {
        Console.WriteLine("\n🤖 適応的OCRエンジンテスト");
        
        try
        {
            var baseOcrEngine = serviceProvider.GetRequiredService<IOcrEngine>();
            var parameterOptimizer = serviceProvider.GetRequiredService<IAdaptivePreprocessingParameterOptimizer>();
            var logger = serviceProvider.GetRequiredService<ILogger<AdaptiveOcrEngine>>();
            var testCaseGenerator = serviceProvider.GetRequiredService<Benchmarking.TestCaseGenerator>();
            
            // 適応的OCRエンジンを作成
            var adaptiveEngine = new AdaptiveOcrEngine(baseOcrEngine, parameterOptimizer, logger);
            
            // OCRエンジン初期化
            var settings = new OcrEngineSettings
            {
                Language = "jpn",
                DetectionThreshold = 0.3,
                RecognitionThreshold = 0.3
            };
            
            var initialized = await adaptiveEngine.InitializeAsync(settings).ConfigureAwait(false);
            if (!initialized)
            {
                Console.WriteLine("  ❌ 適応的OCRエンジンの初期化に失敗");
                return;
            }
            
            // テスト画像でOCR実行
            var testImage = await testCaseGenerator.GenerateSmallTextImageAsync("適応的OCRテスト", 10).ConfigureAwait(false);
            
            var sw = Stopwatch.StartNew();
            var result = await adaptiveEngine.RecognizeAsync(testImage, progressCallback: null, cancellationToken: default).ConfigureAwait(false);
            sw.Stop();
            
            Console.WriteLine($"  適応的OCR結果:");
            Console.WriteLine($"    検出リージョン数: {result.TextRegions.Count}");
            Console.WriteLine($"    処理時間: {sw.ElapsedMilliseconds}ms");
            Console.WriteLine($"    処理完了: 適応的前処理が実行されました");
            
            // メタデータの表示（現在のOcrResultsにはMetadataプロパティがないため省略）
            Console.WriteLine($"    最適化情報: ログを参照してください");
            
            Console.WriteLine("  ✅ 適応的OCRエンジンテスト成功");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ 適応的OCRエンジンテストエラー: {ex.Message}");
        }
    }

    /// <summary>
    /// 適応的前処理ベンチマークのテスト
    /// </summary>
    private static async Task TestAdaptivePreprocessingBenchmarkAsync(IServiceProvider serviceProvider)
    {
        Console.WriteLine("\n📊 適応的前処理ベンチマークテスト");
        
        try
        {
            var benchmark = serviceProvider.GetRequiredService<AdaptivePreprocessingBenchmark>();
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
                Console.WriteLine("  ❌ OCRエンジンの初期化に失敗");
                return;
            }
            
            // 簡易ベンチマーク実行（時間短縮のため品質特化テスト）
            Console.WriteLine("  低品質画像特化ベンチマーク実行中...");
            var qualityResult = await benchmark.RunQualitySpecificBenchmarkAsync(ocrEngine, ImageQualityLevel.Low).ConfigureAwait(false);
            
            Console.WriteLine($"  低品質画像ベンチマーク結果:");
            Console.WriteLine($"    テストケース数: {qualityResult.TestCaseCount}");
            Console.WriteLine($"    成功率: {qualityResult.QualityAnalysis.SuccessRate:F2}");
            Console.WriteLine($"    平均信頼度: {qualityResult.QualityAnalysis.AverageConfidence:F3}");
            Console.WriteLine($"    平均最適化時間: {qualityResult.QualityAnalysis.AverageOptimizationTime:F1}ms");
            Console.WriteLine($"    最多戦略: {qualityResult.QualityAnalysis.MostCommonStrategy}");
            
            // パフォーマンステスト
            Console.WriteLine("  パフォーマンスベンチマーク実行中...");
            var performanceResult = await benchmark.RunPerformanceBenchmarkAsync(ocrEngine).ConfigureAwait(false);
            
            Console.WriteLine($"  パフォーマンスベンチマーク結果:");
            Console.WriteLine($"    平均実行時間: {performanceResult.PerformanceAnalysis.AverageExecutionTime:F1}ms");
            Console.WriteLine($"    平均最適化時間: {performanceResult.PerformanceAnalysis.AverageOptimizationTime:F1}ms");
            Console.WriteLine($"    最適化オーバーヘッド: {performanceResult.PerformanceAnalysis.OptimizationOverhead:F2}x");
            Console.WriteLine($"    最速テストケース: {performanceResult.PerformanceAnalysis.FastestTestCase}");
            Console.WriteLine($"    最遅テストケース: {performanceResult.PerformanceAnalysis.SlowestTestCase}");
            
            Console.WriteLine("  ✅ 適応的前処理ベンチマークテスト成功");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ 適応的前処理ベンチマークテストエラー: {ex.Message}");
        }
    }

    /// <summary>
    /// デバッグキャプチャ画像での実地テスト
    /// </summary>
    public static async Task TestWithRealCaptureAsync(string imagePath)
    {
        Console.WriteLine($"🖼️ 実画像での適応的前処理テスト: {imagePath}");
        
        if (!File.Exists(imagePath))
        {
            Console.WriteLine($"❌ 画像ファイルが見つかりません: {imagePath}");
            return;
        }
        
        try
        {
            // DI設定
            var services = new ServiceCollection();
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });
            
            var paddleOcrModule = new PaddleOcrModule();
            paddleOcrModule.RegisterServices(services);
            var serviceProvider = services.BuildServiceProvider();
            
            // サービス取得
            var baseOcrEngine = serviceProvider.GetRequiredService<IOcrEngine>();
            var parameterOptimizer = serviceProvider.GetRequiredService<IAdaptivePreprocessingParameterOptimizer>();
            var logger = serviceProvider.GetRequiredService<ILogger<AdaptiveOcrEngine>>();
            
            // 画像読み込み
            var imageBytes = await File.ReadAllBytesAsync(imagePath).ConfigureAwait(false);
            var image = new Core.Services.Imaging.AdvancedImage(
                imageBytes, 800, 600, Core.Abstractions.Imaging.ImageFormat.Png);
            
            // OCRエンジン初期化
            var settings = new OcrEngineSettings
            {
                Language = "jpn",
                DetectionThreshold = 0.3,
                RecognitionThreshold = 0.3
            };
            
            var initialized = await baseOcrEngine.InitializeAsync(settings).ConfigureAwait(false);
            if (!initialized)
            {
                Console.WriteLine("❌ OCRエンジンの初期化に失敗");
                return;
            }
            
            // 通常のOCR処理
            Console.WriteLine("通常のOCR処理実行中...");
            var normalStart = DateTime.Now;
            var normalResult = await baseOcrEngine.RecognizeAsync(image).ConfigureAwait(false);
            var normalTime = DateTime.Now - normalStart;
            
            // 適応的OCR処理
            Console.WriteLine("適応的OCR処理実行中...");
            var adaptiveEngine = new AdaptiveOcrEngine(baseOcrEngine, parameterOptimizer, logger);
            var adaptiveStart = DateTime.Now;
            var adaptiveResult = await adaptiveEngine.RecognizeAsync(image, progressCallback: null, cancellationToken: default).ConfigureAwait(false);
            var adaptiveTime = DateTime.Now - adaptiveStart;
            
            // 結果比較
            Console.WriteLine("\n📈 結果比較:");
            Console.WriteLine($"  通常OCR:");
            Console.WriteLine($"    検出リージョン: {normalResult.TextRegions.Count}");
            Console.WriteLine($"    処理時間: {normalTime.TotalMilliseconds:F0}ms");
            Console.WriteLine($"    平均信頼度: {(normalResult.TextRegions.Any() ? normalResult.TextRegions.Average(r => r.Confidence) : 0):F3}");
            
            Console.WriteLine($"  適応的OCR:");
            Console.WriteLine($"    検出リージョン: {adaptiveResult.TextRegions.Count}");
            Console.WriteLine($"    処理時間: {adaptiveTime.TotalMilliseconds:F0}ms");
            Console.WriteLine($"    平均信頼度: {(adaptiveResult.TextRegions.Any() ? adaptiveResult.TextRegions.Average(r => r.Confidence) : 0):F3}");
            
            // 改善効果
            var regionImprovement = adaptiveResult.TextRegions.Count - normalResult.TextRegions.Count;
            var timeRatio = adaptiveTime.TotalMilliseconds / normalTime.TotalMilliseconds;
            
            Console.WriteLine($"  改善効果:");
            Console.WriteLine($"    リージョン増減: {regionImprovement:+0;-0;0}");
            Console.WriteLine($"    処理時間比: {timeRatio:F1}x");
            
            if (regionImprovement > 0)
            {
                Console.WriteLine("    ✅ 適応的前処理により追加のテキストが検出されました");
            }
            else if (regionImprovement == 0)
            {
                Console.WriteLine("    ➡️ 検出リージョン数は同じでした");
            }
            else
            {
                Console.WriteLine("    ⚠️ 検出リージョン数が減少しました");
            }
            
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 実画像テストエラー: {ex.Message}");
        }
    }
}
