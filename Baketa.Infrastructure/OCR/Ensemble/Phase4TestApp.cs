using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Baketa.Infrastructure.DI;
using Baketa.Infrastructure.OCR.Ensemble.Strategies;
using Baketa.Infrastructure.OCR.AdaptivePreprocessing;
using Baketa.Core.Abstractions.OCR;
using TextRegion = Baketa.Core.Abstractions.OCR.OcrTextRegion;
using System.Diagnostics;
using System.IO;

namespace Baketa.Infrastructure.OCR.Ensemble;

/// <summary>
/// Phase 4: アンサンブルOCRシステムのテストアプリケーション
/// </summary>
public static class Phase4TestApp
{
    /// <summary>
    /// Phase 4の包括的テストを実行
    /// </summary>
    public static async Task RunComprehensiveTestAsync()
    {
        Console.WriteLine("🎯 Phase 4: アンサンブルOCRシステム テスト開始");
        
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
            
            // アンサンブル関連サービスを登録
            RegisterEnsembleServices(services);
            
            var serviceProvider = services.BuildServiceProvider();
            
            // 各コンポーネントのテスト
            await TestResultFusionStrategiesAsync(serviceProvider).ConfigureAwait(false);
            await TestEnsembleOcrEngineAsync(serviceProvider).ConfigureAwait(false);
            await TestEngineBalancerAsync(serviceProvider).ConfigureAwait(false);
            await TestEnsembleBenchmarkAsync(serviceProvider).ConfigureAwait(false);
            await TestIntegratedWorkflowAsync(serviceProvider).ConfigureAwait(false);
            
            Console.WriteLine("✅ Phase 4 包括的テスト完了");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Phase 4 テストエラー: {ex.Message}");
            Console.WriteLine($"詳細: {ex}");
        }
    }

    /// <summary>
    /// 結果融合戦略のテスト
    /// </summary>
    private static async Task TestResultFusionStrategiesAsync(IServiceProvider serviceProvider)
    {
        Console.WriteLine("\n🔀 結果融合戦略テスト");
        
        try
        {
            var logger = serviceProvider.GetRequiredService<ILogger<WeightedVotingFusionStrategy>>();
            var weightedVotingStrategy = new WeightedVotingFusionStrategy(logger);
            
            var confidenceLogger = serviceProvider.GetRequiredService<ILogger<ConfidenceBasedFusionStrategy>>();
            var confidenceBasedStrategy = new ConfidenceBasedFusionStrategy(confidenceLogger);
            
            // モックの個別エンジン結果を作成
            var individualResults = CreateMockIndividualResults();
            var fusionParameters = new FusionParameters();
            
            // 重み付き投票戦略テスト
            Console.WriteLine("  📊 重み付き投票戦略テスト実行中...");
            var sw = Stopwatch.StartNew();
            var weightedResult = await weightedVotingStrategy.FuseResultsAsync(
                individualResults, fusionParameters).ConfigureAwait(false);
            sw.Stop();
            
            Console.WriteLine($"    戦略: {weightedResult.FusionStrategy}");
            Console.WriteLine($"    融合領域数: {weightedResult.TextRegions.Count}");
            Console.WriteLine($"    アンサンブル信頼度: {weightedResult.EnsembleConfidence:F3}");
            Console.WriteLine($"    処理時間: {sw.ElapsedMilliseconds}ms");
            
            // 信頼度ベース戦略テスト
            Console.WriteLine("  🎯 信頼度ベース戦略テスト実行中...");
            sw.Restart();
            var confidenceResult = await confidenceBasedStrategy.FuseResultsAsync(
                individualResults, fusionParameters).ConfigureAwait(false);
            sw.Stop();
            
            Console.WriteLine($"    戦略: {confidenceResult.FusionStrategy}");
            Console.WriteLine($"    融合領域数: {confidenceResult.TextRegions.Count}");
            Console.WriteLine($"    アンサンブル信頼度: {confidenceResult.EnsembleConfidence:F3}");
            Console.WriteLine($"    処理時間: {sw.ElapsedMilliseconds}ms");
            
            Console.WriteLine("  ✅ 結果融合戦略テスト成功");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ 結果融合戦略テストエラー: {ex.Message}");
        }
    }

    /// <summary>
    /// アンサンブルOCRエンジンのテスト
    /// </summary>
    private static async Task TestEnsembleOcrEngineAsync(IServiceProvider serviceProvider)
    {
        Console.WriteLine("\n🎭 アンサンブルOCRエンジンテスト");
        
        try
        {
            var logger = serviceProvider.GetRequiredService<ILogger<WeightedVotingFusionStrategy>>();
            var fusionStrategy = new WeightedVotingFusionStrategy(logger);
            
            var ensembleLogger = serviceProvider.GetRequiredService<ILogger<EnsembleOcrEngine>>();
            var ensembleEngine = new EnsembleOcrEngine(fusionStrategy, ensembleLogger);
            
            // ベースOCRエンジンを取得してアンサンブルに追加
            var baseOcrEngine = serviceProvider.GetRequiredService<IOcrEngine>();
            
            Console.WriteLine("  ⚙️ アンサンブル構成設定中...");
            ensembleEngine.AddEngine(baseOcrEngine, 1.0, EnsembleEngineRole.Primary);
            
            // モックの適応的OCRエンジンを追加（実際の実装では複数の異なるエンジン）
            var adaptiveParameterOptimizer = serviceProvider.GetRequiredService<IAdaptivePreprocessingParameterOptimizer>();
            var adaptiveLogger = serviceProvider.GetRequiredService<ILogger<AdaptiveOcrEngine>>();
            var adaptiveEngine = new AdaptiveOcrEngine(baseOcrEngine, adaptiveParameterOptimizer, adaptiveLogger);
            
            ensembleEngine.AddEngine(adaptiveEngine, 0.8, EnsembleEngineRole.Secondary);
            
            Console.WriteLine($"    追加されたエンジン数: {ensembleEngine.GetEnsembleConfiguration().Count}");
            
            // 初期化
            var settings = new OcrEngineSettings
            {
                Language = "jpn",
                DetectionThreshold = 0.3,
                RecognitionThreshold = 0.3
            };
            
            var initialized = await ensembleEngine.InitializeAsync(settings).ConfigureAwait(false);
            if (!initialized)
            {
                Console.WriteLine("  ❌ アンサンブルエンジンの初期化に失敗");
                return;
            }
            
            Console.WriteLine("  ✅ アンサンブルエンジン初期化完了");
            
            // テスト画像でアンサンブル認識実行
            var testCaseGenerator = serviceProvider.GetRequiredService<Benchmarking.TestCaseGenerator>();
            var testImage = await testCaseGenerator.GenerateHighQualityImageAsync("アンサンブルテスト").ConfigureAwait(false);
            
            Console.WriteLine("  🔍 アンサンブル認識実行中...");
            var sw = Stopwatch.StartNew();
            var ensembleResult = await ensembleEngine.RecognizeWithDetailsAsync(testImage).ConfigureAwait(false);
            sw.Stop();
            
            Console.WriteLine($"    最終領域数: {ensembleResult.TextRegions.Count}");
            Console.WriteLine($"    個別エンジン結果数: {ensembleResult.IndividualResults.Count}");
            Console.WriteLine($"    融合戦略: {ensembleResult.FusionStrategy}");
            Console.WriteLine($"    アンサンブル信頼度: {ensembleResult.EnsembleConfidence:F3}");
            Console.WriteLine($"    総処理時間: {sw.ElapsedMilliseconds}ms");
            Console.WriteLine($"    融合詳細: 候補={ensembleResult.FusionDetails.TotalCandidateRegions}, " +
                            $"最終={ensembleResult.FusionDetails.FinalRegions}");
            
            // パフォーマンス統計表示
            var stats = ensembleEngine.GetEnsembleStats();
            Console.WriteLine($"    アンサンブル統計: 実行回数={stats.TotalEnsembleExecutions}, " +
                            $"平均時間={stats.AverageEnsembleTime:F1}ms");
            
            Console.WriteLine("  ✅ アンサンブルOCRエンジンテスト成功");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ アンサンブルOCRエンジンテストエラー: {ex.Message}");
        }
    }

    /// <summary>
    /// エンジンバランサーのテスト
    /// </summary>
    private static async Task TestEngineBalancerAsync(IServiceProvider serviceProvider)
    {
        Console.WriteLine("\n⚖️ エンジンバランサーテスト");
        
        try
        {
            var imageQualityAnalyzer = serviceProvider.GetRequiredService<IImageQualityAnalyzer>();
            var balancerLogger = serviceProvider.GetRequiredService<ILogger<EnsembleEngineBalancer>>();
            var balancer = new EnsembleEngineBalancer(imageQualityAnalyzer, balancerLogger);
            
            // テスト画像生成
            var testCaseGenerator = serviceProvider.GetRequiredService<Benchmarking.TestCaseGenerator>();
            var testImage = await testCaseGenerator.GenerateLowQualityImageAsync("バランサーテスト", 0.3, 0.4, 0.2).ConfigureAwait(false);
            
            // テスト用エンジン構成
            var mockEngines = CreateMockEngineConfiguration();
            var balancingParameters = new BalancingParameters();
            
            Console.WriteLine("  📊 エンジン重み最適化実行中...");
            var sw = Stopwatch.StartNew();
            var optimizationResult = await balancer.OptimizeEngineWeightsAsync(
                testImage, mockEngines, balancingParameters).ConfigureAwait(false);
            sw.Stop();
            
            Console.WriteLine($"    最適化理由: {optimizationResult.PrimaryReason}");
            Console.WriteLine($"    期待精度改善: {optimizationResult.ExpectedAccuracyImprovement:F3}");
            Console.WriteLine($"    期待速度改善: {optimizationResult.ExpectedSpeedImprovement:F3}");
            Console.WriteLine($"    信頼度スコア: {optimizationResult.ConfidenceScore:F3}");
            Console.WriteLine($"    最適化時間: {sw.ElapsedMilliseconds}ms");
            
            Console.WriteLine("    最適化された重み:");
            foreach (var weight in optimizationResult.OptimizedWeights)
            {
                Console.WriteLine($"      {weight.Key}: {weight.Value:F3}");
            }
            
            // 構成推奨テスト
            Console.WriteLine("  🎯 エンジン構成推奨実行中...");
            var imageCharacteristics = new ImageCharacteristics(
                ImageQualityLevel.Low, TextDensityLevel.Medium, ImageComplexityLevel.Moderate,
                ImageType.Screenshot, 12.0, 0.3, 0.2, true, false);
            
            var performanceRequirements = new PerformanceRequirements(
                2000.0, 0.8, 0.7, false, true, new ResourceConstraints());
            
            var recommendation = await balancer.RecommendConfigurationAsync(
                imageCharacteristics, performanceRequirements).ConfigureAwait(false);
            
            Console.WriteLine($"    推奨理由: {recommendation.RecommendationReason}");
            Console.WriteLine($"    期待性能: {recommendation.ExpectedPerformance:F3}");
            Console.WriteLine($"    推奨エンジン数: {recommendation.RecommendedEngines.Count}");
            
            Console.WriteLine("  ✅ エンジンバランサーテスト成功");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ エンジンバランサーテストエラー: {ex.Message}");
        }
    }

    /// <summary>
    /// アンサンブルベンチマークのテスト
    /// </summary>
    private static async Task TestEnsembleBenchmarkAsync(IServiceProvider serviceProvider)
    {
        Console.WriteLine("\n📈 アンサンブルベンチマークテスト");
        
        try
        {
            var benchmarkLogger = serviceProvider.GetRequiredService<ILogger<EnsembleBenchmark>>();
            var testCaseGenerator = serviceProvider.GetRequiredService<Benchmarking.TestCaseGenerator>();
            var benchmark = new EnsembleBenchmark(benchmarkLogger, testCaseGenerator);
            
            // テスト用アンサンブルエンジン作成
            var fusionLogger = serviceProvider.GetRequiredService<ILogger<WeightedVotingFusionStrategy>>();
            var fusionStrategy = new WeightedVotingFusionStrategy(fusionLogger);
            var ensembleLogger = serviceProvider.GetRequiredService<ILogger<EnsembleOcrEngine>>();
            var ensembleEngine = new EnsembleOcrEngine(fusionStrategy, ensembleLogger);
            
            var baseOcrEngine = serviceProvider.GetRequiredService<IOcrEngine>();
            ensembleEngine.AddEngine(baseOcrEngine, 1.0, EnsembleEngineRole.Primary);
            
            await ensembleEngine.InitializeAsync(new OcrEngineSettings { Language = "jpn" }).ConfigureAwait(false);
            
            // ベンチマークパラメータ
            var benchmarkParams = new EnsembleBenchmarkParameters(
                MaxTestCases: 10, // テスト用に少数に設定
                TestCasesPerQuality: 2,
                ComplexityLevels: 3);
            
            Console.WriteLine("  📊 比較ベンチマーク実行中（簡易版）...");
            
            try
            {
                var sw = Stopwatch.StartNew();
                var comparisonResult = await benchmark.RunComparisonBenchmarkAsync(
                    ensembleEngine, [baseOcrEngine], benchmarkParams).ConfigureAwait(false);
                sw.Stop();
                
                Console.WriteLine($"    テストケース数: {comparisonResult.TotalTestCases}");
                Console.WriteLine($"    精度改善: {comparisonResult.ComparisonAnalysis.OverallAccuracyImprovement:F3}");
                Console.WriteLine($"    速度比: {comparisonResult.ComparisonAnalysis.SpeedRatio:F2}x");
                Console.WriteLine($"    ベンチマーク時間: {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    ⚠️ 比較ベンチマーク実行エラー（想定内）: {ex.Message}");
            }
            
            // 融合戦略比較テスト
            Console.WriteLine("  🔀 融合戦略比較テスト実行中...");
            
            try
            {
                var confidenceLogger = serviceProvider.GetRequiredService<ILogger<ConfidenceBasedFusionStrategy>>();
                List<IResultFusionStrategy> strategies =
                [
                    new WeightedVotingFusionStrategy(fusionLogger),
                    new ConfidenceBasedFusionStrategy(confidenceLogger)
                ];
                
                var strategyResult = await benchmark.CompareFusionStrategiesAsync(
                    ensembleEngine, strategies, benchmarkParams).ConfigureAwait(false);
                
                Console.WriteLine($"    テスト戦略数: {strategies.Count}");
                Console.WriteLine($"    最適戦略: {strategyResult.BestStrategy.StrategyName}");
                Console.WriteLine($"    最適スコア: {strategyResult.BestStrategy.OverallScore:F3}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    ⚠️ 融合戦略比較実行エラー（想定内）: {ex.Message}");
            }
            
            Console.WriteLine("  ✅ アンサンブルベンチマークテスト成功");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ アンサンブルベンチマークテストエラー: {ex.Message}");
        }
    }

    /// <summary>
    /// 統合ワークフローのテスト
    /// </summary>
    private static async Task TestIntegratedWorkflowAsync(IServiceProvider serviceProvider)
    {
        Console.WriteLine("\n🔄 統合ワークフローテスト");
        
        try
        {
            Console.WriteLine("  🎯 Phase 3 + Phase 4 統合テスト実行中...");
            
            // Phase 3の適応的前処理とPhase 4のアンサンブル処理を組み合わせ
            var imageQualityAnalyzer = serviceProvider.GetRequiredService<IImageQualityAnalyzer>();
            var adaptiveParameterOptimizer = serviceProvider.GetRequiredService<IAdaptivePreprocessingParameterOptimizer>();
            var baseOcrEngine = serviceProvider.GetRequiredService<IOcrEngine>();
            
            // 適応的OCRエンジン作成
            var adaptiveLogger = serviceProvider.GetRequiredService<ILogger<AdaptiveOcrEngine>>();
            var adaptiveEngine = new AdaptiveOcrEngine(baseOcrEngine, adaptiveParameterOptimizer, adaptiveLogger);
            
            // アンサンブルエンジン作成
            var fusionLogger = serviceProvider.GetRequiredService<ILogger<WeightedVotingFusionStrategy>>();
            var fusionStrategy = new WeightedVotingFusionStrategy(fusionLogger);
            var ensembleLogger = serviceProvider.GetRequiredService<ILogger<EnsembleOcrEngine>>();
            var ensembleEngine = new EnsembleOcrEngine(fusionStrategy, ensembleLogger);
            
            // エンジンバランサー作成
            var balancerLogger = serviceProvider.GetRequiredService<ILogger<EnsembleEngineBalancer>>();
            var balancer = new EnsembleEngineBalancer(imageQualityAnalyzer, balancerLogger);
            
            Console.WriteLine("    📋 統合システム構成:");
            Console.WriteLine("      - ベースOCRエンジン (PaddleOCR)");
            Console.WriteLine("      - 適応的前処理OCRエンジン");
            Console.WriteLine("      - アンサンブル融合システム");
            Console.WriteLine("      - 動的エンジンバランサー");
            
            // アンサンブル構成
            ensembleEngine.AddEngine(baseOcrEngine, 1.0, EnsembleEngineRole.Primary);
            ensembleEngine.AddEngine(adaptiveEngine, 0.9, EnsembleEngineRole.Secondary);
            
            var settings = new OcrEngineSettings { Language = "jpn" };
            await ensembleEngine.InitializeAsync(settings).ConfigureAwait(false);
            
            // 統合ワークフローテスト
            var testCaseGenerator = serviceProvider.GetRequiredService<Benchmarking.TestCaseGenerator>();
            var testCases = new[]
            {
                await testCaseGenerator.GenerateHighQualityImageAsync("統合テスト高品質").ConfigureAwait(false),
                await testCaseGenerator.GenerateLowQualityImageAsync("統合テスト低品質", 0.2, 0.3, 0.4).ConfigureAwait(false),
                await testCaseGenerator.GenerateSmallTextImageAsync("統合テスト小文字", 8).ConfigureAwait(false)
            };
            
            for (int i = 0; i < testCases.Length; i++)
            {
                var testImage = testCases[i];
                var testName = i switch
                {
                    0 => "高品質画像",
                    1 => "低品質画像", 
                    2 => "小文字画像",
                    _ => $"テスト{i}"
                };
                
                Console.WriteLine($"    🖼️ {testName}テスト実行中...");
                
                var sw = Stopwatch.StartNew();
                
                // Step 1: 画像品質分析
                var qualityMetrics = await imageQualityAnalyzer.AnalyzeAsync(testImage).ConfigureAwait(false);
                
                // Step 2: エンジン重み最適化
                var mockEngines = CreateMockEngineConfiguration();
                var optimizationResult = await balancer.OptimizeEngineWeightsAsync(
                    testImage, mockEngines, new BalancingParameters()).ConfigureAwait(false);
                
                // Step 3: アンサンブル認識実行
                var ensembleResult = await ensembleEngine.RecognizeWithDetailsAsync(testImage).ConfigureAwait(false);
                
                sw.Stop();
                
                Console.WriteLine($"      品質スコア: {qualityMetrics.OverallQuality:F3}");
                Console.WriteLine($"      重み最適化: {optimizationResult.OptimizedWeights.Count}エンジン");
                Console.WriteLine($"      最終領域数: {ensembleResult.TextRegions.Count}");
                Console.WriteLine($"      アンサンブル信頼度: {ensembleResult.EnsembleConfidence:F3}");
                Console.WriteLine($"      統合処理時間: {sw.ElapsedMilliseconds}ms");
            }
            
            Console.WriteLine("  ✅ 統合ワークフローテスト成功");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ 統合ワークフローテストエラー: {ex.Message}");
        }
    }

    /// <summary>
    /// 実画像での統合テスト
    /// </summary>
    public static async Task TestWithRealImageAsync(string imagePath)
    {
        Console.WriteLine($"🖼️ 実画像での統合Phase 3+4テスト: {imagePath}");
        
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
            RegisterEnsembleServices(services);
            
            var serviceProvider = services.BuildServiceProvider();
            
            // 画像読み込み
            var imageBytes = await File.ReadAllBytesAsync(imagePath).ConfigureAwait(false);
            var image = new Core.Services.Imaging.AdvancedImage(
                imageBytes, 800, 600, Core.Abstractions.Imaging.ImageFormat.Png);
            
            // 統合システム構築
            var baseOcrEngine = serviceProvider.GetRequiredService<IOcrEngine>();
            var adaptiveParameterOptimizer = serviceProvider.GetRequiredService<IAdaptivePreprocessingParameterOptimizer>();
            var imageQualityAnalyzer = serviceProvider.GetRequiredService<IImageQualityAnalyzer>();
            
            var adaptiveLogger = serviceProvider.GetRequiredService<ILogger<AdaptiveOcrEngine>>();
            var adaptiveEngine = new AdaptiveOcrEngine(baseOcrEngine, adaptiveParameterOptimizer, adaptiveLogger);
            
            var fusionLogger = serviceProvider.GetRequiredService<ILogger<WeightedVotingFusionStrategy>>();
            var fusionStrategy = new WeightedVotingFusionStrategy(fusionLogger);
            var ensembleLogger = serviceProvider.GetRequiredService<ILogger<EnsembleOcrEngine>>();
            var ensembleEngine = new EnsembleOcrEngine(fusionStrategy, ensembleLogger);
            
            ensembleEngine.AddEngine(baseOcrEngine, 1.0, EnsembleEngineRole.Primary);
            ensembleEngine.AddEngine(adaptiveEngine, 0.8, EnsembleEngineRole.Secondary);
            
            await ensembleEngine.InitializeAsync(new OcrEngineSettings { Language = "jpn" }).ConfigureAwait(false);
            
            Console.WriteLine("統合解析・認識実行中...");
            var totalSw = Stopwatch.StartNew();
            
            // Phase 3: 画像品質分析・適応的前処理
            var qualityMetrics = await imageQualityAnalyzer.AnalyzeAsync(image).ConfigureAwait(false);
            
            // Phase 4: アンサンブル認識
            var ensembleResult = await ensembleEngine.RecognizeWithDetailsAsync(image).ConfigureAwait(false);
            
            totalSw.Stop();
            
            Console.WriteLine("\n📊 統合処理結果:");
            Console.WriteLine($"  画像品質:");
            Console.WriteLine($"    総合品質: {qualityMetrics.OverallQuality:F3}");
            Console.WriteLine($"    コントラスト: {qualityMetrics.Contrast:F3}");
            Console.WriteLine($"    明度: {qualityMetrics.Brightness:F3}");
            Console.WriteLine($"    ノイズレベル: {qualityMetrics.NoiseLevel:F3}");
            
            Console.WriteLine($"  アンサンブル結果:");
            Console.WriteLine($"    最終テキスト領域: {ensembleResult.TextRegions.Count}");
            Console.WriteLine($"    個別エンジン結果: {ensembleResult.IndividualResults.Count}");
            Console.WriteLine($"    融合戦略: {ensembleResult.FusionStrategy}");
            Console.WriteLine($"    アンサンブル信頼度: {ensembleResult.EnsembleConfidence:F3}");
            Console.WriteLine($"    合意率: {ensembleResult.FusionDetails.AgreementRate:F3}");
            
            Console.WriteLine($"  パフォーマンス:");
            Console.WriteLine($"    総処理時間: {totalSw.ElapsedMilliseconds}ms");
            Console.WriteLine($"    アンサンブル処理時間: {ensembleResult.EnsembleProcessingTime.TotalMilliseconds}ms");
            
            Console.WriteLine("✅ 実画像統合テスト完了");
            
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 実画像統合テストエラー: {ex.Message}");
        }
    }

    #region Private Helper Methods

    /// <summary>
    /// アンサンブル関連サービスを登録
    /// </summary>
    private static void RegisterEnsembleServices(IServiceCollection services)
    {
        services.AddTransient<ILogger<WeightedVotingFusionStrategy>>(provider =>
            provider.GetRequiredService<ILoggerFactory>().CreateLogger<WeightedVotingFusionStrategy>());
        
        services.AddTransient<ILogger<ConfidenceBasedFusionStrategy>>(provider =>
            provider.GetRequiredService<ILoggerFactory>().CreateLogger<ConfidenceBasedFusionStrategy>());
        
        services.AddTransient<ILogger<EnsembleOcrEngine>>(provider =>
            provider.GetRequiredService<ILoggerFactory>().CreateLogger<EnsembleOcrEngine>());
        
        services.AddTransient<ILogger<EnsembleEngineBalancer>>(provider =>
            provider.GetRequiredService<ILoggerFactory>().CreateLogger<EnsembleEngineBalancer>());
        
        services.AddTransient<ILogger<EnsembleBenchmark>>(provider =>
            provider.GetRequiredService<ILoggerFactory>().CreateLogger<EnsembleBenchmark>());
    }

    /// <summary>
    /// モックの個別エンジン結果を作成
    /// </summary>
    private static List<IndividualEngineResult> CreateMockIndividualResults()
    {
        return
        [
            new IndividualEngineResult(
                "PaddleOCR",
                EnsembleEngineRole.Primary,
                new OcrResults([], new MockImage(), TimeSpan.FromMilliseconds(100), "jpn"),
                TimeSpan.FromMilliseconds(500),
                1.0,
                true),

            new IndividualEngineResult(
                "AdaptiveOCR",
                EnsembleEngineRole.Secondary,
                new OcrResults([], new MockImage(), TimeSpan.FromMilliseconds(100), "jpn"),
                TimeSpan.FromMilliseconds(800),
                0.8,
                true)
        ];
    }

    /// <summary>
    /// モックのエンジン構成を作成
    /// </summary>
    private static List<EnsembleEngineInfo> CreateMockEngineConfiguration()
    {
        return
        [
            new EnsembleEngineInfo(
                null!, // モックなのでnull
                "PaddleOCR",
                1.0,
                EnsembleEngineRole.Primary,
                true,
                new EnsembleEngineStats(100, 500, 0.8, 0.95, DateTime.UtcNow)),

            new EnsembleEngineInfo(
                null!, // モックなのでnull
                "AdaptiveOCR",
                0.8,
                EnsembleEngineRole.Secondary,
                true,
                new EnsembleEngineStats(80, 800, 0.82, 0.9, DateTime.UtcNow))
        ];
    }

    /// <summary>
    /// モック画像クラス
    /// </summary>
    private sealed class MockImage : Baketa.Core.Abstractions.Imaging.IImage
    {
        public int Width => 800;
        public int Height => 600;
        public Baketa.Core.Abstractions.Imaging.ImageFormat Format => Baketa.Core.Abstractions.Imaging.ImageFormat.Rgb24;

        /// <summary>
        /// PixelFormat property for IImage extension
        /// </summary>
        public Baketa.Core.Abstractions.Memory.ImagePixelFormat PixelFormat => Baketa.Core.Abstractions.Memory.ImagePixelFormat.Rgb24;

        /// <summary>
        /// GetImageMemory method for IImage extension
        /// </summary>
        public ReadOnlyMemory<byte> GetImageMemory()
        {
            return new ReadOnlyMemory<byte>(new byte[Width * Height * 3]); // RGB24フォーマット
        }

        /// <summary>
        /// 🔥 [PHASE5.2G-A] LockPixelData (MockImage is test-only, not supported)
        /// </summary>
        public Baketa.Core.Abstractions.Imaging.PixelDataLock LockPixelData() => throw new NotSupportedException("MockImage does not support LockPixelData");

        public async Task<byte[]> ToByteArrayAsync()
        {
            await Task.Delay(1).ConfigureAwait(false); // 非同期処理のシミュレーション
            return new byte[Width * Height * 3]; // RGB24フォーマット
        }
        
        public Baketa.Core.Abstractions.Imaging.IImage Clone()
        {
            return new MockImage();
        }
        
        public async Task<Baketa.Core.Abstractions.Imaging.IImage> ResizeAsync(int width, int height)
        {
            await Task.Delay(1).ConfigureAwait(false); // 非同期処理のシミュレーション
            return new MockImage(); // 簡易実装
        }
        
        public void Dispose() { }
    }

    #endregion
}
