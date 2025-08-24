using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Baketa.Core.Abstractions.Dependency;
using Baketa.Core.Settings;
using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Performance;
using Baketa.Core.Abstractions.Logging;
using Baketa.Core.Abstractions.Events;
using Baketa.Infrastructure.OCR.PaddleOCR.Models;
using Baketa.Infrastructure.OCR.PaddleOCR.Initialization;
using Baketa.Infrastructure.OCR.PaddleOCR.Engine;
using Baketa.Infrastructure.OCR.TextProcessing;
using Baketa.Infrastructure.OCR.PostProcessing;
using Baketa.Infrastructure.OCR.PostProcessing.NgramModels;
using Baketa.Infrastructure.OCR.Benchmarking;
using Baketa.Infrastructure.OCR.PaddleOCR.Enhancement;
using Baketa.Infrastructure.OCR.MultiScale;
using Baketa.Infrastructure.OCR.AdaptivePreprocessing;
using Baketa.Infrastructure.OCR.Ensemble;
using Baketa.Infrastructure.OCR.Ensemble.Strategies;
using System.IO;
using Microsoft.Extensions.ObjectPool;
using Baketa.Infrastructure.OCR.PaddleOCR.Factory;
using Baketa.Infrastructure.OCR.PaddleOCR.Pool;
using Baketa.Infrastructure.OCR.PaddleOCR.Services;
using System.Net.Http;
using System.Diagnostics;

namespace Baketa.Infrastructure.DI;

/// <summary>
/// PaddleOCR統合基盤のサービス登録モジュール（更新版）
/// </summary>
public sealed class PaddleOcrModule : IServiceModule
{
    /// <summary>
    /// サービスを登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    public void RegisterServices(IServiceCollection services)
    {
        // モデル管理基盤
        services.AddSingleton<IModelPathResolver>(serviceProvider =>
        {
            var logger = serviceProvider.GetService<ILogger<DefaultModelPathResolver>>();
            
            // デフォルトのベースディレクトリは実行ファイルのディレクトリ下のmodelsフォルダ
            var baseDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models");
            
            return new DefaultModelPathResolver(baseDirectory, logger);
        });
        
        // PaddleOCR初期化サービス
        services.AddSingleton<PaddleOcrInitializer>(serviceProvider =>
        {
            var modelPathResolver = serviceProvider.GetRequiredService<IModelPathResolver>();
            var logger = serviceProvider.GetService<ILogger<PaddleOcrInitializer>>();
            
            // デフォルトのベースディレクトリは実行ファイルのディレクトリ
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            
            return new PaddleOcrInitializer(baseDirectory, modelPathResolver, logger);
        });
        
        // OCRモデル管理
        services.AddSingleton<IOcrModelManager>(serviceProvider =>
        {
            var modelPathResolver = serviceProvider.GetRequiredService<IModelPathResolver>();
            var logger = serviceProvider.GetService<ILogger<OcrModelManager>>();
            
            // HttpClientの取得（既存のHttpClientFactoryから、または新規作成）
            var httpClientFactory = serviceProvider.GetService<IHttpClientFactory>();
            var httpClient = httpClientFactory?.CreateClient("OcrModelDownloader") ?? new HttpClient();
            
            // 一時ディレクトリの設定
            var tempDirectory = Path.Combine(Path.GetTempPath(), "BaketaOcrModels");
            
            return new OcrModelManager(modelPathResolver, httpClient, tempDirectory, logger);
        });
        
        // テキスト結合アルゴリズム
        services.AddSingleton<ITextMerger>(serviceProvider =>
        {
            var logger = serviceProvider.GetService<ILogger<JapaneseTextMerger>>() ?? 
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<JapaneseTextMerger>.Instance;
            return new JapaneseTextMerger(logger);
        });
        
        // N-gram学習サービス
        services.AddSingleton<NgramTrainingService>(serviceProvider =>
        {
            var logger = serviceProvider.GetService<ILogger<NgramTrainingService>>() ?? 
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<NgramTrainingService>.Instance;
            return new NgramTrainingService(logger);
        });
        
        // ハイブリッドOCR後処理ファクトリ
        services.AddSingleton<HybridOcrPostProcessorFactory>(serviceProvider =>
        {
            var logger = serviceProvider.GetService<ILogger<HybridOcrPostProcessorFactory>>() ?? 
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<HybridOcrPostProcessorFactory>.Instance;
            var trainingService = serviceProvider.GetRequiredService<NgramTrainingService>();
            return new HybridOcrPostProcessorFactory(logger, trainingService);
        });
        
        // N-gramベンチマーク
        services.AddSingleton<NgramPostProcessingBenchmark>(serviceProvider =>
        {
            var logger = serviceProvider.GetService<ILogger<NgramPostProcessingBenchmark>>() ?? 
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<NgramPostProcessingBenchmark>.Instance;
            var trainingService = serviceProvider.GetRequiredService<NgramTrainingService>();
            return new NgramPostProcessingBenchmark(logger, trainingService);
        });
        
        // OCR後処理 (ハイブリッド版を使用)
        services.AddSingleton<IOcrPostProcessor>(serviceProvider =>
        {
            // 環境に応じて処理を選択
            var environmentVar = Environment.GetEnvironmentVariable("BAKETA_USE_HYBRID_POSTPROCESSING");
            var useHybrid = environmentVar == "true" || string.IsNullOrEmpty(environmentVar); // デフォルトでハイブリッド使用
            
            if (useHybrid)
            {
                var factory = serviceProvider.GetRequiredService<HybridOcrPostProcessorFactory>();
                // 非同期初期化のため、タスクを同期的に実行
                return factory.CreateAsync().GetAwaiter().GetResult();
            }
            else
            {
                // 従来の辞書ベースのみ
                var logger = serviceProvider.GetService<ILogger<JapaneseOcrPostProcessor>>() ?? 
                            Microsoft.Extensions.Logging.Abstractions.NullLogger<JapaneseOcrPostProcessor>.Instance;
                return new JapaneseOcrPostProcessor(logger);
            }
        });
        
        // OCRベンチマーク機能
        services.AddSingleton<IOcrBenchmark>(serviceProvider =>
        {
            var logger = serviceProvider.GetService<ILogger<OcrBenchmark>>() ?? 
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<OcrBenchmark>.Instance;
            return new OcrBenchmark(logger);
        });
        
        // PaddleOCR高度最適化機能
        services.AddSingleton<AdvancedPaddleOcrOptimizer>(serviceProvider =>
        {
            var logger = serviceProvider.GetService<ILogger<AdvancedPaddleOcrOptimizer>>() ?? 
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<AdvancedPaddleOcrOptimizer>.Instance;
            return new AdvancedPaddleOcrOptimizer(logger);
        });
        
        // OCRパラメータベンチマークランナー
        services.AddSingleton<OcrParameterBenchmarkRunner>(serviceProvider =>
        {
            var benchmark = serviceProvider.GetRequiredService<IOcrBenchmark>();
            var optimizer = serviceProvider.GetRequiredService<AdvancedPaddleOcrOptimizer>();
            var logger = serviceProvider.GetService<ILogger<OcrParameterBenchmarkRunner>>() ?? 
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<OcrParameterBenchmarkRunner>.Instance;
            return new OcrParameterBenchmarkRunner(benchmark, optimizer, logger);
        });
        
        // テストケース生成器
        services.AddSingleton<TestCaseGenerator>(serviceProvider =>
        {
            var logger = serviceProvider.GetService<ILogger<TestCaseGenerator>>() ?? 
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<TestCaseGenerator>.Instance;
            return new TestCaseGenerator(logger);
        });
        
        // ハイブリッドOCR設定（appsettings.jsonから読み込み）
        services.AddSingleton<HybridOcrSettings>(serviceProvider =>
        {
            var configuration = serviceProvider.GetService<IConfiguration>();
            
            // デフォルト値
            var fastDetectionModel = PaddleOcrModelVersion.V3;
            var highQualityModel = PaddleOcrModelVersion.V5;
            var imageQualityThreshold = 0.6;
            var regionCountThreshold = 5;
            var fastDetectionTimeoutMs = 500;
            var highQualityTimeoutMs = 3000;
            
            if (configuration != null)
            {
                var hybridSection = configuration.GetSection("OCR:HybridStrategy");
                if (hybridSection.Exists())
                {
                    // FastDetectionModelをV3とV5からマッピング
                    var fastModel = hybridSection["FastDetectionModel"];
                    if (fastModel == "V3")
                        fastDetectionModel = PaddleOcrModelVersion.V3;
                    else if (fastModel == "V5")
                        fastDetectionModel = PaddleOcrModelVersion.V5;
                        
                    // HighQualityModelをV3とV5からマッピング
                    var highQualityModelStr = hybridSection["HighQualityModel"];
                    if (highQualityModelStr == "V3")
                        highQualityModel = PaddleOcrModelVersion.V3;
                    else if (highQualityModelStr == "V5")
                        highQualityModel = PaddleOcrModelVersion.V5;
                    
                    // 数値設定を読み込み
                    if (double.TryParse(hybridSection["ImageQualityThreshold"], out var qualityThreshold))
                        imageQualityThreshold = qualityThreshold;
                        
                    if (int.TryParse(hybridSection["RegionCountThreshold"], out var regionThreshold))
                        regionCountThreshold = regionThreshold;
                        
                    if (int.TryParse(hybridSection["FastDetectionTimeoutMs"], out var fastTimeout))
                        fastDetectionTimeoutMs = fastTimeout;
                        
                    if (int.TryParse(hybridSection["HighQualityTimeoutMs"], out var highTimeout))
                        highQualityTimeoutMs = highTimeout;
                        
                    Console.WriteLine($"✅ ハイブリッドOCR設定をappsettings.jsonから読み込み完了 - FastModel: {fastDetectionModel}, HighQualityModel: {highQualityModel}");
                }
                else
                {
                    Console.WriteLine("⚠️ appsettings.json OCR:HybridStrategy セクションが見つかりません - デフォルト値を使用");
                }
            }
            
            // オブジェクト初期化子で設定を作成
            return new HybridOcrSettings
            {
                FastDetectionModel = fastDetectionModel,
                HighQualityModel = highQualityModel,
                ImageQualityThreshold = imageQualityThreshold,
                RegionCountThreshold = regionCountThreshold,
                FastDetectionTimeoutMs = fastDetectionTimeoutMs,
                HighQualityTimeoutMs = highQualityTimeoutMs
            };
        });
        
        // ハイブリッドPaddleOCRサービス
        services.AddSingleton<HybridPaddleOcrService>(serviceProvider =>
        {
            var logger = serviceProvider.GetService<ILogger<HybridPaddleOcrService>>() ?? 
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<HybridPaddleOcrService>.Instance;
            var eventAggregator = serviceProvider.GetRequiredService<IEventAggregator>();
            var hybridSettings = serviceProvider.GetRequiredService<HybridOcrSettings>();
            return new HybridPaddleOcrService(logger, eventAggregator, hybridSettings);
        });
        
        // Phase 1ベンチマークランナー
        services.AddSingleton<Phase1BenchmarkRunner>(serviceProvider =>
        {
            var logger = serviceProvider.GetService<ILogger<Phase1BenchmarkRunner>>() ?? 
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<Phase1BenchmarkRunner>.Instance;
            return new Phase1BenchmarkRunner(serviceProvider, logger);
        });
        
        // マルチスケールOCR処理（Phase 2）
        services.AddSingleton<IMultiScaleOcrProcessor>(serviceProvider =>
        {
            var logger = serviceProvider.GetService<ILogger<SimpleMultiScaleOcrProcessor>>() ?? 
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<SimpleMultiScaleOcrProcessor>.Instance;
            return new SimpleMultiScaleOcrProcessor(logger);
        });
        
        // マルチスケールテストランナー
        services.AddSingleton<MultiScaleTestRunner>(serviceProvider =>
        {
            var multiScaleProcessor = serviceProvider.GetRequiredService<IMultiScaleOcrProcessor>();
            var ocrEngine = serviceProvider.GetRequiredService<IOcrEngine>();
            var logger = serviceProvider.GetService<ILogger<MultiScaleTestRunner>>() ?? 
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<MultiScaleTestRunner>.Instance;
            return new MultiScaleTestRunner(multiScaleProcessor, ocrEngine, logger);
        });
        
        // Phase 3: 適応的前処理パラメータ決定システム
        services.AddSingleton<IImageQualityAnalyzer>(serviceProvider =>
        {
            var logger = serviceProvider.GetService<ILogger<ImageQualityAnalyzer>>() ?? 
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<ImageQualityAnalyzer>.Instance;
            return new ImageQualityAnalyzer(logger);
        });
        
        services.AddSingleton<IAdaptivePreprocessingParameterOptimizer>(serviceProvider =>
        {
            var imageQualityAnalyzer = serviceProvider.GetRequiredService<IImageQualityAnalyzer>();
            var logger = serviceProvider.GetService<ILogger<AdaptivePreprocessingParameterOptimizer>>() ?? 
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<AdaptivePreprocessingParameterOptimizer>.Instance;
            return new AdaptivePreprocessingParameterOptimizer(imageQualityAnalyzer, logger);
        });
        
        services.AddSingleton<AdaptivePreprocessingBenchmark>(serviceProvider =>
        {
            var parameterOptimizer = serviceProvider.GetRequiredService<IAdaptivePreprocessingParameterOptimizer>();
            var testCaseGenerator = serviceProvider.GetRequiredService<TestCaseGenerator>();
            var logger = serviceProvider.GetService<ILogger<AdaptivePreprocessingBenchmark>>() ?? 
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<AdaptivePreprocessingBenchmark>.Instance;
            return new AdaptivePreprocessingBenchmark(parameterOptimizer, testCaseGenerator, logger);
        });
        
        // Phase 4: アンサンブルOCR処理システム
        
        // 結果融合戦略
        services.AddTransient<WeightedVotingFusionStrategy>(serviceProvider =>
        {
            var logger = serviceProvider.GetService<ILogger<WeightedVotingFusionStrategy>>() ?? 
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<WeightedVotingFusionStrategy>.Instance;
            return new WeightedVotingFusionStrategy(logger);
        });
        
        services.AddTransient<ConfidenceBasedFusionStrategy>(serviceProvider =>
        {
            var logger = serviceProvider.GetService<ILogger<ConfidenceBasedFusionStrategy>>() ?? 
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfidenceBasedFusionStrategy>.Instance;
            return new ConfidenceBasedFusionStrategy(logger);
        });
        
        // デフォルトの融合戦略（重み付き投票）
        services.AddSingleton<IResultFusionStrategy>(serviceProvider =>
        {
            var logger = serviceProvider.GetService<ILogger<WeightedVotingFusionStrategy>>() ?? 
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<WeightedVotingFusionStrategy>.Instance;
            return new WeightedVotingFusionStrategy(logger);
        });
        
        // エンジンバランサー
        services.AddSingleton<IEnsembleEngineBalancer>(serviceProvider =>
        {
            var imageQualityAnalyzer = serviceProvider.GetRequiredService<IImageQualityAnalyzer>();
            var logger = serviceProvider.GetService<ILogger<EnsembleEngineBalancer>>() ?? 
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<EnsembleEngineBalancer>.Instance;
            return new EnsembleEngineBalancer(imageQualityAnalyzer, logger);
        });
        
        // アンサンブルOCRエンジン
        services.AddSingleton<IEnsembleOcrEngine>(serviceProvider =>
        {
            var defaultFusionStrategy = serviceProvider.GetRequiredService<IResultFusionStrategy>();
            var logger = serviceProvider.GetService<ILogger<EnsembleOcrEngine>>() ?? 
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<EnsembleOcrEngine>.Instance;
            return new EnsembleOcrEngine(defaultFusionStrategy, logger);
        });
        
        // アンサンブルベンチマーク
        services.AddSingleton<EnsembleBenchmark>(serviceProvider =>
        {
            var logger = serviceProvider.GetService<ILogger<EnsembleBenchmark>>() ?? 
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<EnsembleBenchmark>.Instance;
            var testCaseGenerator = serviceProvider.GetRequiredService<TestCaseGenerator>();
            return new EnsembleBenchmark(logger, testCaseGenerator);
        });
        
        // OCRエンジン設定（appsettings.jsonから読み込み）
        services.AddSingleton<OcrEngineSettings>(serviceProvider =>
        {
            var configuration = serviceProvider.GetService<IConfiguration>();
            var settings = new OcrEngineSettings();
            
            if (configuration != null)
            {
                Console.WriteLine($"🔍 [CONFIG_DEBUG] Configuration available: {configuration != null}");
                
                // 全設定セクションをデバッグ出力
                foreach (var section in configuration.GetChildren())
                {
                    Console.WriteLine($"🔍 [CONFIG_DEBUG] Top-level section: {section.Key}");
                    if (section.Key == "OCR")
                    {
                        foreach (var ocrChild in section.GetChildren())
                        {
                            Console.WriteLine($"🔍 [CONFIG_DEBUG] OCR child: {ocrChild.Key}");
                        }
                    }
                }
                
                var ocrSection = configuration.GetSection("OCR:PaddleOCR");
                Console.WriteLine($"🔍 [CONFIG_DEBUG] OCR:PaddleOCR section exists: {ocrSection.Exists()}");
                Console.WriteLine($"🔍 [CONFIG_DEBUG] EnableHybridMode raw value: '{ocrSection["EnableHybridMode"]}'");
                
                // 別の方法でもアクセステスト
                var ocrSectionAlt = configuration.GetSection("OCR").GetSection("PaddleOCR");
                Console.WriteLine($"🔍 [CONFIG_DEBUG] Alternative access OCR.PaddleOCR exists: {ocrSectionAlt.Exists()}");
                Console.WriteLine($"🔍 [CONFIG_DEBUG] Alternative EnableHybridMode: '{ocrSectionAlt["EnableHybridMode"]}'");
                
                if (ocrSection.Exists())
                {
                    // 基本設定を読み込み
                    settings.Language = ocrSection["Language"] ?? "jpn";
                    
                    if (double.TryParse(ocrSection["DetectionThreshold"], out var detectionThreshold))
                        settings.DetectionThreshold = detectionThreshold;
                        
                    if (double.TryParse(ocrSection["RecognitionThreshold"], out var recognitionThreshold))
                        settings.RecognitionThreshold = recognitionThreshold;
                        
                    if (bool.TryParse(ocrSection["UseGpu"], out var useGpu))
                        settings.UseGpu = useGpu;
                        
                    if (bool.TryParse(ocrSection["EnableMultiThread"], out var enableMultiThread))
                        settings.EnableMultiThread = enableMultiThread;
                        
                    if (int.TryParse(ocrSection["WorkerCount"], out var workerCount))
                        settings.WorkerCount = workerCount;
                        
                    if (bool.TryParse(ocrSection["UseLanguageModel"], out var useLanguageModel))
                        settings.UseLanguageModel = useLanguageModel;
                        
                    if (bool.TryParse(ocrSection["EnablePreprocessing"], out var enablePreprocessing))
                        settings.EnablePreprocessing = enablePreprocessing;
                        
                    // ★ 重要：EnableHybridModeを読み込み
                    if (bool.TryParse(ocrSection["EnableHybridMode"], out var enableHybridMode))
                        settings.EnableHybridMode = enableHybridMode;
                        
                    Console.WriteLine($"✅ OCRエンジン設定をappsettings.jsonから読み込み完了 - EnableHybridMode: {settings.EnableHybridMode}, Language: {settings.Language}");
                }
                else
                {
                    Console.WriteLine("⚠️ appsettings.json OCR:PaddleOCR セクションが見つかりません - デフォルト値を使用");
                }
            }
            
            return settings;
        });

        // OCRエンジン（IOcrEngineインターフェース準拠）
        services.AddSingleton<IOcrEngine>(serviceProvider =>
        {
            var modelPathResolver = serviceProvider.GetRequiredService<IModelPathResolver>();
            var ocrPreprocessingService = serviceProvider.GetRequiredService<IOcrPreprocessingService>();
            var textMerger = serviceProvider.GetRequiredService<ITextMerger>();
            var ocrPostProcessor = serviceProvider.GetRequiredService<IOcrPostProcessor>();
            var logger = serviceProvider.GetService<ILogger<PaddleOcrEngine>>();
            
            // 環境判定を実行
            Console.WriteLine("🔍 PaddleOCR環境判定開始");
            
            // 環境変数で本番モードを強制できるようにする
            string? envValue = Environment.GetEnvironmentVariable("BAKETA_FORCE_PRODUCTION_OCR");
            bool forceProduction = envValue == "true";
            
            // 🎯 高機能版OCR構成：環境変数が設定されていない場合も高機能版エンジンを使用
            if (string.IsNullOrEmpty(envValue))
            {
                Console.WriteLine("🎯 高機能版OCR構成：環境変数が設定されていないため、高機能版PaddleOcrEngineを使用");
                forceProduction = true; // 🎯 高機能版：V3+V5ハイブリッド戦略エンジンを使用
            }
            Console.WriteLine($"📊 BAKETA_FORCE_PRODUCTION_OCR環境変数: '{envValue}' (強制本番モード: {forceProduction})");
            if (forceProduction)
            {
                Console.WriteLine("⚠️ BAKETA_FORCE_PRODUCTION_OCR=true - 本番OCRエンジンを強制使用");
                logger?.LogInformation("環境変数により本番OCRエンジンを強制使用");
                
                // OCR設定からハイブリッドモードを確認
                var oldOcrSettings = serviceProvider.GetService<OcrEngineSettings>();
                bool enableHybridMode = oldOcrSettings?.EnableHybridMode ?? false;
                
                Console.WriteLine($"🔍 ハイブリッドモード設定: {enableHybridMode}");
                logger?.LogInformation($"ハイブリッドモード設定: {enableHybridMode}");
                
                var gpuMemoryManager = serviceProvider.GetRequiredService<IGpuMemoryManager>();
                var unifiedSettingsService = serviceProvider.GetRequiredService<IUnifiedSettingsService>();
                var eventAggregator = serviceProvider.GetRequiredService<IEventAggregator>();
                var unifiedLoggingService = serviceProvider.GetService<IUnifiedLoggingService>();
                
                // PaddleOcrEngineは内部でハイブリッドモード処理を行います
                var ocrSettings = serviceProvider.GetRequiredService<IOptionsMonitor<OcrSettings>>();
                return new PaddleOcrEngine(modelPathResolver, ocrPreprocessingService, textMerger, ocrPostProcessor, gpuMemoryManager, unifiedSettingsService, eventAggregator, ocrSettings, unifiedLoggingService, logger);
            }
            
            // 🎯 高機能版構成：常に最適化されたPaddleOcrEngineを使用
            Console.WriteLine("✅ 高機能版OCR構成 - 最適化PaddleOcrEngineを使用");
            logger?.LogInformation("高機能版OCR構成 - 最適化PaddleOcrEngineを使用");
            return new PaddleOcrEngine(modelPathResolver, ocrPreprocessingService, textMerger, ocrPostProcessor, 
                serviceProvider.GetRequiredService<IGpuMemoryManager>(),
                serviceProvider.GetRequiredService<IUnifiedSettingsService>(), 
                serviceProvider.GetRequiredService<IEventAggregator>(),
                serviceProvider.GetRequiredService<IOptionsMonitor<OcrSettings>>(),
                serviceProvider.GetService<IUnifiedLoggingService>(), logger);
        });
        
        // 🚀 シングルトン統一: PaddleOcrEngineは常にIOcrEngineと同じインスタンスを返す
        services.AddSingleton<PaddleOcrEngine>(serviceProvider =>
        {
            // IOcrEngineとして登録されているインスタンスを取得
            var ocrEngine = serviceProvider.GetRequiredService<IOcrEngine>();
            
            // PaddleOcrEngineの場合はそのまま返却（シングルトン保証）
            if (ocrEngine is PaddleOcrEngine paddleEngine)
            {
                Console.WriteLine("🔗 PaddleOcrEngine: IOcrEngineと同じインスタンスを再利用（多重初期化防止）");
                return paddleEngine;
            }
            
            // 高機能版構成では常にPaddleOcrEngineが使用される
            Console.WriteLine("⚠️ PaddleOcrEngine要求: 期待されるPaddleOcrEngineインスタンスが見つかりません");
            throw new InvalidOperationException("高機能版OCR構成でPaddleOcrEngineインスタンスが取得できませんでした。");
        });
        
        // 🏭 安定性改善戦略: ファクトリパターン実装（Immutable OCR Engine Pattern）
        RegisterOcrEngineFactory(services);
        
        // HttpClient設定（HttpClientFactoryが利用可能な場合）
        if (services.Any(s => s.ServiceType == typeof(IHttpClientFactory)))
        {
            services.AddHttpClient("OcrModelDownloader", client =>
            {
                client.Timeout = TimeSpan.FromMinutes(30); // モデルダウンロード用の長いタイムアウト
                client.DefaultRequestHeaders.Add("User-Agent", "Baketa-OCR-ModelManager/1.0");
            });
        }
    }

    /// <summary>
    /// αテスト環境・開発環境・WSL環境を検出します
    /// </summary>
    /// <returns>テスト用エンジンを使用すべき環境の場合true</returns>
    private static bool IsAlphaTestOrDevelopmentEnvironment()
    {
        try
        {
            // 1. デバッガーがアタッチされている場合（開発環境）
            bool debuggerAttached = Debugger.IsAttached;
            
            // 2. WSL環境を検出
            bool isWslEnvironment = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WSL_DISTRO_NAME"));
            
            // 3. αテスト環境変数を検出
            bool isAlphaTest = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BAKETA_ALPHA_TEST"));
            
            // 4. 開発環境を示すその他の環境変数
            string aspNetEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "";
            bool isDevelopmentAspNet = aspNetEnvironment.Equals("Development", StringComparison.OrdinalIgnoreCase);
            
            string dotNetEnvironment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "";
            bool isDevelopmentDotNet = dotNetEnvironment.Equals("Development", StringComparison.OrdinalIgnoreCase);
            
            // 5. Visual Studio環境を検出
            bool isVisualStudio = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VSAPPIDDIR"));
            
            // 6. 現在のディレクトリがソース管理下にあるかチェック（開発環境の可能性）
            // bool isSourceControlled = IsUnderSourceControl(); // 🚨 無効化: ソース管理下でも本番OCRを使用可能に
            bool isSourceControlled = false; // 🚨 緊急修正: ソース管理環境判定を無効化
            
            bool shouldUseSafeEngine = debuggerAttached || isWslEnvironment || isAlphaTest || 
                                     isDevelopmentAspNet || isDevelopmentDotNet || isVisualStudio || 
                                     isSourceControlled;
            
            // ログ出力用の環境情報
            var environmentInfo = new
            {
                DebuggerAttached = debuggerAttached,
                WSLEnvironment = isWslEnvironment,
                AlphaTest = isAlphaTest,
                AspNetDevelopment = isDevelopmentAspNet,
                DotNetDevelopment = isDevelopmentDotNet,
                VisualStudio = isVisualStudio,
                SourceControlled = isSourceControlled,
                ShouldUseSafeEngine = shouldUseSafeEngine
            };
            
            // ログ出力（環境が利用可能な場合のみ）
            Console.WriteLine($"環境判定結果: {System.Text.Json.JsonSerializer.Serialize(environmentInfo)}");
            
            return shouldUseSafeEngine;
        }
        catch (Exception ex)
        {
            // 環境判定でエラーが発生した場合は安全な選択肢を選ぶ
            Console.WriteLine($"環境判定エラー - 安全のためSafeTestPaddleOcrEngineを使用: {ex.Message}");
            return true;
        }
    }
    
    /// <summary>
    /// 現在のディレクトリがソース管理下にあるかをチェック
    /// </summary>
    /// <returns>ソース管理下の場合true</returns>
    private static bool IsUnderSourceControl()
    {
        try
        {
            // 現在のディレクトリから上位へ向かって.gitディレクトリを探す
            var currentDirectory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            
            while (currentDirectory != null)
            {
                // .gitディレクトリの存在確認
                if (Directory.Exists(Path.Combine(currentDirectory.FullName, ".git")))
                {
                    return true;
                }
                
                // .svnディレクトリの存在確認（SVN）
                if (Directory.Exists(Path.Combine(currentDirectory.FullName, ".svn")))
                {
                    return true;
                }
                
                // .hgディレクトリの存在確認（Mercurial）
                if (Directory.Exists(Path.Combine(currentDirectory.FullName, ".hg")))
                {
                    return true;
                }
                
                // 親ディレクトリへ移動
                currentDirectory = currentDirectory.Parent;
                
                // ルートディレクトリに到達したら終了
                if (currentDirectory?.Parent == null)
                {
                    break;
                }
            }
            
            return false;
        }
        catch (Exception)
        {
            // エラーが発生した場合は false を返す
            return false;
        }
    }

    /// <summary>
    /// OCRエンジンファクトリシステムを登録（安定性改善戦略）
    /// プール化によるメモリリークと状態汚染を根本的に解決
    /// </summary>
    private static void RegisterOcrEngineFactory(IServiceCollection services)
    {
        Console.WriteLine("🏭 安定性改善: OCRエンジンファクトリシステム登録開始");
        
        // 1. OCRエンジンファクトリー登録
        services.AddSingleton<IPaddleOcrEngineFactory, PaddleOcrEngineFactory>();
        Console.WriteLine("✅ IPaddleOcrEngineFactory登録完了（ファクトリパターン）");
        
        // 2. ファクトリベースのIOcrEngine登録を遅延実行
        // 既存のシングルトンIOcrEngine登録を置き換え
        var existingOcrEngineDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IOcrEngine));
        if (existingOcrEngineDescriptor != null)
        {
            services.Remove(existingOcrEngineDescriptor);
            Console.WriteLine("🔄 既存IOcrEngineシングルトン登録を削除");
        }
        
        // 3. ファクトリベースのIOcrEngine登録（非ブロッキング版）
        // デッドロック回避のため、直接インスタンス生成を行う
        services.AddTransient<IOcrEngine>(serviceProvider =>
        {
            // 🔥 重要: デッドロック回避のため、直接インスタンス生成を実行
            // PaddleOcrEngineFactory.CreateAsync()の実装を直接実行し、同期待機を回避
            
            var modelPathResolver = serviceProvider.GetRequiredService<IModelPathResolver>();
            var ocrPreprocessingService = serviceProvider.GetRequiredService<IOcrPreprocessingService>();
            var textMerger = serviceProvider.GetRequiredService<ITextMerger>();
            var ocrPostProcessor = serviceProvider.GetRequiredService<IOcrPostProcessor>();
            var gpuMemoryManager = serviceProvider.GetRequiredService<IGpuMemoryManager>();
            var unifiedSettingsService = serviceProvider.GetRequiredService<IUnifiedSettingsService>();
            var eventAggregator = serviceProvider.GetRequiredService<IEventAggregator>();
            var ocrSettings = serviceProvider.GetRequiredService<IOptionsMonitor<OcrSettings>>();
            var unifiedLoggingService = serviceProvider.GetService<IUnifiedLoggingService>();
            var engineLogger = serviceProvider.GetService<ILogger<PaddleOcrEngine>>();
            
            // 直接NonSingletonPaddleOcrEngineを作成（デッドロック回避）
            var engine = new NonSingletonPaddleOcrEngine(
                modelPathResolver, 
                ocrPreprocessingService, 
                textMerger, 
                ocrPostProcessor, 
                gpuMemoryManager,
                unifiedSettingsService,
                eventAggregator,
                ocrSettings,
                unifiedLoggingService,
                engineLogger);
            
            Console.WriteLine("🆕 新しいOCRエンジンインスタンス作成: {0}（非ブロッキング）", engine.GetType().Name);
            return engine;
        });
        Console.WriteLine("✅ IOcrEngineファクトリベース登録完了");
        
        Console.WriteLine("🎉 安定性改善: OCRエンジンファクトリシステム登録完了");
        Console.WriteLine("📊 期待効果: PaddlePredictor run failed エラー根絶 + メモリリーク防止");
    }
}
