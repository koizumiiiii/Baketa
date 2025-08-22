using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Dependency;
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
            
            // 🚨 緊急OCRハング対策：環境変数が設定されていない場合はSafeエンジンを使用
            if (string.IsNullOrEmpty(envValue))
            {
                Console.WriteLine("🔄 緊急OCRハング対策：環境変数が設定されていないため、SafePaddleOcrEngineを使用");
                forceProduction = false; // 🚨 緊急対策：ハング防止のため安全エンジンを使用
            }
            Console.WriteLine($"📊 BAKETA_FORCE_PRODUCTION_OCR環境変数: '{envValue}' (強制本番モード: {forceProduction})");
            if (forceProduction)
            {
                Console.WriteLine("⚠️ BAKETA_FORCE_PRODUCTION_OCR=true - 本番OCRエンジンを強制使用");
                logger?.LogInformation("環境変数により本番OCRエンジンを強制使用");
                var gpuMemoryManager = serviceProvider.GetRequiredService<IGpuMemoryManager>();
                var unifiedSettingsService = serviceProvider.GetRequiredService<IUnifiedSettingsService>();
                var eventAggregator = serviceProvider.GetRequiredService<IEventAggregator>();
                var unifiedLoggingService = serviceProvider.GetService<IUnifiedLoggingService>();
                return new PaddleOcrEngine(modelPathResolver, ocrPreprocessingService, textMerger, ocrPostProcessor, gpuMemoryManager, unifiedSettingsService, eventAggregator, unifiedLoggingService, logger);
            }
            
            bool isAlphaTestOrDevelopment = IsAlphaTestOrDevelopmentEnvironment();
            Console.WriteLine($"🔍 環境判定結果: isAlphaTestOrDevelopment = {isAlphaTestOrDevelopment}");
            
            if (isAlphaTestOrDevelopment)
            {
                Console.WriteLine("✅ αテスト・開発・WSL環境検出 - SafePaddleOcrEngineを使用");
                Console.WriteLine("💡 ヒント: 実際のOCRを使用するには環境変数 BAKETA_FORCE_PRODUCTION_OCR=true を設定してください");
                logger?.LogInformation("αテスト・開発・WSL環境検出 - SafePaddleOcrEngineを使用");
                return new SafePaddleOcrEngine(modelPathResolver, logger, skipRealInitialization: true);
            }
            else
            {
                Console.WriteLine("✅ 本番環境検出 - PaddleOcrEngineを使用");
                logger?.LogInformation("本番環境検出 - PaddleOcrEngineを使用");
                var gpuMemoryManager = serviceProvider.GetRequiredService<IGpuMemoryManager>();
                var unifiedSettingsService = serviceProvider.GetRequiredService<IUnifiedSettingsService>();
                var eventAggregator = serviceProvider.GetRequiredService<IEventAggregator>();
                var unifiedLoggingService = serviceProvider.GetService<IUnifiedLoggingService>();
                return new PaddleOcrEngine(modelPathResolver, ocrPreprocessingService, textMerger, ocrPostProcessor, gpuMemoryManager, unifiedSettingsService, eventAggregator, unifiedLoggingService, logger);
            }
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
            
            // SafePaddleOcrEngineが使用されている場合はnullを返す（型安全性を保つ）
            Console.WriteLine("⚠️ PaddleOcrEngine要求: SafePaddleOcrEngineが使用中のため、PaddleOcrEngineインスタンスは利用不可");
            // 実際にはSafePaddleOcrEngineなので、PaddleOcrEngineとしてキャストするのは型安全性に問題がある
            // 代わりに適切なエラーハンドリングまたはnull返却を推奨
            throw new InvalidOperationException("現在SafePaddleOcrEngineが使用されているため、PaddleOcrEngineインスタンスは利用できません。");
        });
        
        // 🚀 Step 1: OCRエンジンプール化実装（Gemini推奨アプローチ）
        RegisterOcrEnginePooling(services);
        
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
    /// OCRエンジンプール化システムを登録（Step 1実装）
    /// 14秒→5-8秒への性能向上を目標とした根本的解決策
    /// </summary>
    private static void RegisterOcrEnginePooling(IServiceCollection services)
    {
        Console.WriteLine("🚀 Step 1: OCRエンジンプール化システム登録開始");
        
        // 1. OCRエンジンファクトリー登録
        services.AddSingleton<IPaddleOcrEngineFactory, PaddleOcrEngineFactory>();
        Console.WriteLine("✅ IPaddleOcrEngineFactory登録完了");
        
        // 2. プールポリシー登録
        services.AddSingleton<PaddleOcrEnginePoolPolicy>();
        Console.WriteLine("✅ PaddleOcrEnginePoolPolicy登録完了");
        
        // 3. ObjectPoolの設定
        services.AddSingleton<ObjectPool<IOcrEngine>>(serviceProvider =>
        {
            var policy = serviceProvider.GetRequiredService<PaddleOcrEnginePoolPolicy>();
            var provider = serviceProvider.GetRequiredService<ObjectPoolProvider>();
            
            // プール設定：最大プールサイズ、並列度に基づいて調整
            var pool = provider.Create(policy);
            
            Console.WriteLine("🏊 ObjectPool<IOcrEngine>初期化完了 - プール化OCRシステム開始");
            return pool;
        });
        
        // 4. プール化OCRサービス登録（既存IOcrEngineを置き換え）
        // 既存のシングルトン登録を削除し、プール化サービスで置き換え
        var existingOcrEngineDescriptor = services.FirstOrDefault(s => s.ServiceType == typeof(IOcrEngine));
        if (existingOcrEngineDescriptor != null)
        {
            services.Remove(existingOcrEngineDescriptor);
            Console.WriteLine("🔄 既存IOcrEngineシングルトン登録を削除");
        }
        
        services.AddSingleton<IOcrEngine, PooledOcrService>();
        Console.WriteLine("✅ PooledOcrService登録完了 - IOcrEngineをプール化実装に置き換え");
        
        // 5. ObjectPoolProviderを登録（Microsoft.Extensions.ObjectPoolのデフォルト実装）
        services.AddSingleton<ObjectPoolProvider, DefaultObjectPoolProvider>();
        Console.WriteLine("✅ DefaultObjectPoolProvider登録完了");
        
        Console.WriteLine("🎉 Step 1: OCRエンジンプール化システム登録完了");
        Console.WriteLine("📊 予想効果: 14-18秒 → 5-8秒（並列競合解消）");
    }
}
