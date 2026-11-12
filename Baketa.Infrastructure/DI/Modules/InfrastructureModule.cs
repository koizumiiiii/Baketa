using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Baketa.Core.Abstractions.CrashReporting;
using Baketa.Core.Abstractions.DI;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Logging;
using Baketa.Core.Abstractions.Memory;
using Baketa.Core.Abstractions.Monitoring;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Patterns;
using Baketa.Core.Abstractions.Performance;
using Baketa.Core.Abstractions.Processing;
using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.DI;
using Baketa.Core.DI.Attributes;
using Baketa.Core.DI.Modules;
using Baketa.Core.Services;
using Baketa.Core.Settings;
using Baketa.Core.Translation.Abstractions;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Analytics;
using Baketa.Infrastructure.Imaging.ChangeDetection;
using Baketa.Infrastructure.Logging;
using Baketa.Infrastructure.OCR;
using Baketa.Infrastructure.Patterns;
using Baketa.Infrastructure.Performance;
using Baketa.Infrastructure.ResourceManagement;
using Baketa.Infrastructure.Services;
using Baketa.Infrastructure.Services.Memory;
using Baketa.Infrastructure.Services.Settings;
using Baketa.Infrastructure.Services.Setup;
using Baketa.Infrastructure.Services.Translation;
using Baketa.Infrastructure.Translation;
using Baketa.Infrastructure.Validation;
using Baketa.Core.Abstractions.Validation;
using Baketa.Infrastructure.Translation.Services;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Baketa.Core.Abstractions.Hardware;
using CoreTranslation = Baketa.Core.Translation.Abstractions;
using EventTypes = Baketa.Core.Events.EventTypes;

namespace Baketa.Infrastructure.DI.Modules;

/// <summary>
/// インフラストラクチャレイヤーのサービスを登録するモジュール。
/// 外部サービス連携やプラットフォーム非依存の実装が含まれます。
/// </summary>
[ModulePriority(ModulePriority.Infrastructure)]
public class InfrastructureModule : ServiceModuleBase
{
    /// <summary>
    /// インフラストラクチャサービスを登録します（基本版）。
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <remarks>
    /// ⚠️ [Gemini Review v0.2.17] 登録ロジック統一に関する注意:
    /// このメソッドと RegisterServices(services, configuration) の2つのオーバーロードが存在します。
    ///
    /// 【重要】新しいサービスを追加する場合は、必ず両方のメソッドに追加してください。
    /// 片方のみに追加すると、呼び出し元によって動作が異なる問題が発生します。
    /// （例: Issue #78 Cloud AI翻訳が1パラメータ版で動作しなかった問題）
    ///
    /// 【差分一覧】
    /// - 2パラメータ版のみ: RegisterNllb200TranslationServices, RegisterTranslationSettings
    /// - 1パラメータ版のみ: RegisterMemoryServices
    /// - configuration依存: RegisterStagedFilteringServices, RegisterComponentDownloadServices
    ///
    /// 【将来のリファクタリング候補】
    /// 共通ロジックを RegisterCoreServices(services) に抽出し、
    /// configuration依存部分のみをオーバーロードで分岐させることを検討。
    /// </remarks>
    public override void RegisterServices(IServiceCollection services)
    {
        Console.WriteLine("🔍🔍🔍 [DIAGNOSTIC] InfrastructureModule.RegisterServices(1-parameter) 開始");

        // 🎯 Phase 3.1: NOTE: ISafeImageFactory/IImageLifecycleManagerはApplicationModuleで登録済み
        // Clean Architecture原則により、InfrastructureがApplicationを参照することはできない
        // SafeImageFactoryはSafeImageの内部コンストラクタアクセスのためApplication層必須

        // 環境確認は、BuildServiceProviderが存在しないか必要なパッケージがないため
        // コメントアウトし、デフォルト値を使用
        //var environment = services.BuildServiceProvider().GetService<Core.DI.BaketaEnvironment>()
        //    ?? Core.DI.BaketaEnvironment.Production;
        var environment = Core.DI.BaketaEnvironment.Production;

        // 設定ファイル統一: OCR設定をappsettings.jsonから読み込み
        // 注意: これはServiceModuleの外で設定される想定（Startup.cs等）

        // OCR関連サービス
        RegisterOcrServices(services);

        // 🔄 Phase 1: 画像変化検知システム
        RegisterImageChangeDetectionServices(services);

        // 🔄 P1: 段階的フィルタリングシステム
        RegisterStagedFilteringServices(services);

        // [ROI_DELETION] スティッキーROIシステム登録削除 - レガシー機能除去

        // [Issue #293] ROI Manager サービス登録
        RegisterRoiManagerServices(services);

        // 🔥 [PHASE12.5.3_FIX] NLLB-200翻訳サービス登録を削除
        // 理由: RegisterServices(services, config)メソッドで既に登録されるため重複を回避
        // RegisterNllb200TranslationServices(services);

        // 翻訳サービス（エンジン登録後）
        RegisterTranslationServices(services);

        // Issue #78: Cloud AI翻訳サービス（Pro/Premia向け）
        // [Fix v0.2.17] 1パラメータ版にも追加 - Cloud AI翻訳が動作しない問題を修正
        RegisterCloudAIServices(services);

        // [Issue #445] Python翻訳インフラ廃止: PortManagement/PythonEnvironment/NllbFix/Phase2 は不要

        // ウォームアップサービス（Issue #143: コールドスタート遅延根絶）
        RegisterWarmupServices(services);

        // GPU統合サービス（Issue #143 Week 2: DI統合とMulti-GPU対応）
        RegisterGpuServices(services);

        // パフォーマンス管理サービス
        RegisterPerformanceServices(services);

        // Phase3: リソース監視システム
        RegisterResourceMonitoringServices(services);

        // Phase 3.13: Memory変換サービス
        RegisterMemoryServices(services);

        // Phase2: ハイブリッドリソース管理システム - PlatformModuleに移動（循環依存解決）

        // 初回起動判定サービス
        RegisterFirstRunServices(services);

        // [Issue #185] コンポーネントダウンロードサービス
        RegisterComponentDownloadServices(services);

        // データ永続化
        RegisterPersistenceServices(services, environment);

        // [Issue #287] クラッシュレポート送信サービス（JWT認証付き）
        RegisterCrashReportingInfrastructure(services);
    }

    /// <summary>
    /// インフラストラクチャサービスを登録します（appsettings.json対応版）。
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <param name="configuration">設定オブジェクト</param>
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // 環境確認は、BuildServiceProviderが存在しないか必要なパッケージがないため
        // コメントアウトし、デフォルト値を使用
        //var environment = services.BuildServiceProvider().GetService<Core.DI.BaketaEnvironment>()
        //    ?? Core.DI.BaketaEnvironment.Production;
        var environment = Core.DI.BaketaEnvironment.Production;

        // 設定ファイル統一: OCR設定をappsettings.jsonから読み込み
        // 注意: これはServiceModuleの外で設定される想定（Startup.cs等）

        // OCR関連サービス
        RegisterOcrServices(services);

        // 🔄 Phase 1: 画像変化検知システム
        RegisterImageChangeDetectionServices(services);

        // 🔄 P1: 段階的フィルタリングシステム（appsettings.json対応）
        RegisterStagedFilteringServices(services, configuration);

        // [ROI_DELETION] スティッキーROIシステム登録削除 - レガシー機能除去

        // [Issue #293] ROI Manager サービス登録（configuration付き）
        RegisterRoiManagerServices(services, configuration);

        // [Issue #445] Python翻訳インフラ廃止: PortManagement不要、NLLB-200をONNX直接推論に統一
        RegisterNllb200TranslationServices(services);

        // 🚀 Phase 2.3: TranslationSettings登録（ONNX設定対応）
        RegisterTranslationSettings(services);

        // 翻訳サービス（エンジン登録後）
        RegisterTranslationServices(services);

        // Issue #78: Cloud AI翻訳サービス（Pro/Premia向け）
        RegisterCloudAIServices(services);

        // [Issue #445] Phase 2 (CacheManagement等) は Python翻訳インフラ廃止に伴い不要

        // ウォームアップサービス（Issue #143: コールドスタート遅延根絶）
        RegisterWarmupServices(services);

        // GPU統合サービス（Issue #143 Week 2: DI統合とMulti-GPU対応）
        RegisterGpuServices(services);

        // パフォーマンス管理サービス
        RegisterPerformanceServices(services);

        // Phase3: リソース監視システム
        RegisterResourceMonitoringServices(services);

        // Phase2: ハイブリッドリソース管理システム - PlatformModuleに移動（循環依存解決）

        // 初回起動判定サービス
        RegisterFirstRunServices(services);

        // [Issue #185] コンポーネントダウンロードサービス（appsettings.json対応）
        RegisterComponentDownloadServices(services, configuration);

        // データ永続化
        RegisterPersistenceServices(services, environment);

        // [Issue #287] クラッシュレポート送信サービス（JWT認証付き）
        RegisterCrashReportingInfrastructure(services);
    }

    /// <summary>
    /// OCR関連サービスを登録します。
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    private static void RegisterOcrServices(IServiceCollection services)
    {
        // Sprint 2 Fix: IImageFactory登録はPlatformModuleで実施
        // （アーキテクチャ原則に従い、Platform固有実装はPlatformModuleに配置）

        // NOTE: PP-OCRv5は削除済み、Surya OCRがメインOCRエンジンとして使用される
        // Surya OCRサービスはSuryaOcrModuleで登録

        // NOTE: [PP-OCRv5削除] AdaptiveTileStrategy, ITileStrategy, OcrRegionGenerator削除
        // Surya OCRでは異なるアプローチを使用（SuryaOcrModuleで登録）

        // [Issue #316] OCR精度測定システム削除
        // 開発時ベンチマーク用であり、実際のOCRを使用せずシミュレート結果を生成していたため削除

        // Phase 3.4A: OCRテキスト領域グルーピング戦略（Union-Find）
        services.AddSingleton<IRegionGroupingStrategy, Baketa.Infrastructure.OCR.Clustering.UnionFindRegionGroupingStrategy>();
        Console.WriteLine("✅ Phase 3.4A: UnionFindRegionGroupingStrategy登録完了 - グラフベース連結成分検出");

        // [Issue #335] ハードウェアスペックチェッカー
        services.AddSingleton<IHardwareChecker, HardwareChecker>();
        Console.WriteLine("✅ [Issue #335] HardwareChecker登録完了 - 起動時ハードウェアチェック");

        // UltraThink Phase 1: 近接度ベースグループ化サービス
        RegisterProximityGroupingServices(services);
    }

    // [Issue #445] RegisterPortManagementServices, RegisterPythonEnvironmentServices, RegisterNllbFixServices は
    // Python翻訳インフラ廃止に伴い削除

    /// <summary>
    /// 翻訳サービスを登録します。
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    private static void RegisterTranslationServices(IServiceCollection services)
    {
        // 統一言語設定サービス（Clean Architecture準拠）
        services.AddScoped<ILanguageConfigurationService, UnifiedLanguageConfigurationService>();
        Console.WriteLine("✅ ILanguageConfigurationService登録完了 - 統一言語設定管理");

        // 🚀 Phase 2.3: TranslationSettings登録は RegisterServices(2-parameter) で既に実行済み（重複削除）

        // Phase2: サーキットブレーカー設定とサービス登録
        Console.WriteLine("🔧 [PHASE2] サーキットブレーカー登録開始");

        // サーキットブレーカー設定 - appsettings.jsonから読み込み
        RegisterCircuitBreakerSettings(services);

        // 🆕 Gemini推奨: フォールバック機能付きサーキットブレーカー登録
        services.AddSingleton<ICircuitBreaker<Baketa.Core.Translation.Models.TranslationResponse>, EnhancedTranslationCircuitBreaker>();
        Console.WriteLine("✅ [PHASE2] EnhancedTranslationCircuitBreaker登録完了 - FailureThreshold: 5, RecoveryTimeout: 60s, フォールバック機能付き");

        // パフォーマンス監視サービスを登録 (Issue #144)
        services.AddSingleton<Baketa.Infrastructure.Translation.Services.ITranslationPerformanceMonitor, Baketa.Infrastructure.Translation.Services.TranslationPerformanceMonitor>();

        // [Issue #445] PythonServerHealthMonitor は Python翻訳インフラ廃止に伴い削除

        // 🚀 Issue #147 Phase 3.2: ハイブリッド翻訳戦略システム統合
        Console.WriteLine("🚀 Issue #147 Phase 3.2: ハイブリッド翻訳戦略システム登録開始");

        // ハイブリッド戦略設定
        // TODO: appsettings.jsonへの外部化推奨（Geminiコードレビュー指摘事項）
        // IOptions<HybridStrategySettings>パターンを使用して設定を外部化し、
        // 本番環境で負荷に応じた閾値調整を可能にする
        services.AddSingleton<Baketa.Infrastructure.Translation.Strategies.HybridStrategySettings>(provider =>
        {
            // デフォルト設定で初期化
            return new Baketa.Infrastructure.Translation.Strategies.HybridStrategySettings
            {
                BatchThreshold = 5,      // 5件以上でバッチ処理
                ParallelThreshold = 2,   // 2件以上で並列処理
                MaxDegreeOfParallelism = 4,  // 並列度（接続プールサイズと協調必要）
                EnableMetrics = true
            };
        });

        // 翻訳メトリクス収集サービス
        services.AddSingleton<Baketa.Infrastructure.Translation.Metrics.TranslationMetricsCollector>();
        Console.WriteLine("✅ TranslationMetricsCollector登録完了");

        // 翻訳戦略登録（優先度順）

        // 1. 単一翻訳戦略（最低優先度、フォールバック用）
        services.AddSingleton<Baketa.Infrastructure.Translation.Strategies.SingleTranslationStrategy>();
        services.AddSingleton<Baketa.Core.Abstractions.Translation.ITranslationStrategy>(provider =>
        {
            var strategy = provider.GetRequiredService<Baketa.Infrastructure.Translation.Strategies.SingleTranslationStrategy>();
            Console.WriteLine("✅ SingleTranslationStrategy登録完了 - Priority: 10");
            return strategy;
        });

        // 2. 並列翻訳戦略（中優先度）
        services.AddSingleton<Baketa.Infrastructure.Translation.Strategies.ParallelTranslationStrategy>();
        services.AddSingleton<Baketa.Core.Abstractions.Translation.ITranslationStrategy>(provider =>
        {
            var strategy = provider.GetRequiredService<Baketa.Infrastructure.Translation.Strategies.ParallelTranslationStrategy>();
            Console.WriteLine("✅ ParallelTranslationStrategy登録完了 - Priority: 50");
            return strategy;
        });

        // 3. バッチ翻訳戦略（最高優先度）
        services.AddSingleton<Baketa.Infrastructure.Translation.Strategies.BatchTranslationStrategy>();
        services.AddSingleton<Baketa.Core.Abstractions.Translation.ITranslationStrategy>(provider =>
        {
            var strategy = provider.GetRequiredService<Baketa.Infrastructure.Translation.Strategies.BatchTranslationStrategy>();
            Console.WriteLine("✅ BatchTranslationStrategy登録完了 - Priority: 100");
            return strategy;
        });

        // ハイブリッド翻訳戦略（戦略統合オーケストレーター）
        services.AddSingleton<Baketa.Infrastructure.Translation.Strategies.HybridTranslationStrategy>(provider =>
        {
            var strategies = provider.GetServices<Baketa.Core.Abstractions.Translation.ITranslationStrategy>();
            var metricsCollector = provider.GetRequiredService<Baketa.Infrastructure.Translation.Metrics.TranslationMetricsCollector>();
            var integratedMetricsCollector = provider.GetService<Baketa.Core.Abstractions.Monitoring.IPerformanceMetricsCollector>(); // Phase 4.1
            var settings = provider.GetRequiredService<Baketa.Infrastructure.Translation.Strategies.HybridStrategySettings>();
            var logger = provider.GetRequiredService<ILogger<Baketa.Infrastructure.Translation.Strategies.HybridTranslationStrategy>>();

            var hybridStrategy = new Baketa.Infrastructure.Translation.Strategies.HybridTranslationStrategy(
                strategies, metricsCollector, settings, logger, integratedMetricsCollector);

            var metricsStatus = integratedMetricsCollector != null ? "Phase 4.1統合メトリクス統合完了" : "従来メトリクスのみ";
            Console.WriteLine($"🎯 HybridTranslationStrategy登録完了 - 戦略統合オーケストレーター ({metricsStatus})");
            return hybridStrategy;
        });

        // 🔧 UltraThink Phase 9.18: 重複登録削除 - TranslationModuleで既に登録済み
        // services.AddSingleton<ITranslationService, DefaultTranslationService>(); // TranslationServiceExtensions.AddTranslationServices()で登録

        Console.WriteLine("✅ Issue #147 Phase 3.2: ハイブリッド翻訳戦略システム登録完了");

        // 🆕 Issue #273: Cloud翻訳可用性統合サービス
        // ライセンス状態とユーザー設定を統合して一貫した可用性判定を提供
        services.AddSingleton<Baketa.Core.Abstractions.Translation.ICloudTranslationAvailabilityService,
            Baketa.Infrastructure.Translation.Services.DisabledCloudTranslationAvailabilityService>();
        Console.WriteLine("✅ [Issue #273] CloudTranslationAvailabilityService登録完了 - ライセンス×設定統合");
    }

    /// <summary>
    /// ウォームアップサービスを登録します（Issue #143: コールドスタート遅延根絶）。
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    private static void RegisterWarmupServices(IServiceCollection services)
    {
        Console.WriteLine("🚀 ウォームアップサービス登録開始 - Issue #143");

        // バックグラウンドウォームアップサービス（OCR・翻訳エンジンの非同期初期化）
        services.AddSingleton<Baketa.Core.Abstractions.GPU.IWarmupService, BackgroundWarmupService>();
        Console.WriteLine("✅ IWarmupService登録完了");

        // ウォームアップサービスをホストサービスとしても登録（アプリケーション開始時に自動実行）
        services.AddHostedService<WarmupHostedService>();
        Console.WriteLine("✅ WarmupHostedService登録完了");
    }

    /// <summary>
    /// GPU関連サービスを登録します（Issue #143 Week 2: DI統合とMulti-GPU対応）。
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    private static void RegisterGpuServices(IServiceCollection services)
    {
        Console.WriteLine("🎮 GPU統合サービス登録開始 - Issue #143 Week 2");

        // NOTE: [PP-OCRv5削除] ONNX Runtime セッション管理は削除
        // Surya OCRではPython側でモデル管理を行うため不要

        // OCR Circuit Breaker設定
        services.Configure<Baketa.Infrastructure.Patterns.OcrCircuitBreakerOptions>(options =>
        {
            options.FailureThreshold = 3;          // GPU失敗3回でオープン
            options.OpenTimeout = TimeSpan.FromMinutes(2); // 2分オープン
            options.HalfOpenRetryInterval = TimeSpan.FromSeconds(30);
            options.AutoFallbackEnabled = true;    // CPU自動フォールバック
            options.ImmediateFallbackOnGpuError = true;
            options.EnableVerboseLogging = false;  // 本番では無効
        });

        // OCR Circuit Breaker実装
        services.AddSingleton<Baketa.Core.Abstractions.Patterns.ICircuitBreaker<Baketa.Core.Abstractions.OCR.OcrResults>,
            Baketa.Infrastructure.Patterns.OcrCircuitBreaker>();
        Console.WriteLine("✅ OcrCircuitBreaker登録完了 - GPU→CPU自動フォールバック対応");

        // Sprint 2 Phase 1完了: Mock除去準備完了（次スプリントでIntelligentOcrEngine完全実装）
        Console.WriteLine("🚧 Sprint 2 Phase 1: Mock除去準備・基盤整備完了");
        Console.WriteLine("📋 IntelligentOcrEngine完全実装は Sprint 3で実施");

        // [ROI_DELETION] IGpuOcrEngine登録削除 - レガシーROI機能除去
        Console.WriteLine("✅ GPU統合サービス登録完了");
    }

    /// <summary>
    /// NLLB-200翻訳サービスを登録します。
    /// Meta社開発の高品質多言語ニューラル翻訳モデルです。
    /// Issue #147: 接続プール統合による接続ロック競合問題解決
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    private static void RegisterNllb200TranslationServices(IServiceCollection services)
    {
        // 既存のITranslationEngine登録を全て削除して、最適化されたエンジンを登録
        var existingTranslationEngines = services
            .Where(s => s.ServiceType == typeof(Baketa.Core.Abstractions.Translation.ITranslationEngine))
            .ToList();

        foreach (var service in existingTranslationEngines)
        {
            services.Remove(service);
        }

        // [Issue #445] ConnectionPool / GrpcTranslationClient は Python翻訳インフラ廃止に伴い削除

        // GrpcPortProvider登録（OCR用 UnifiedServer のポート管理で引き続き使用）
        services.AddSingleton<Baketa.Infrastructure.Translation.Services.GrpcPortProvider>();

        // ServerManagerHostedService登録（UnifiedServer起動・OCR用）
        services.AddHostedService<Baketa.Infrastructure.Translation.Services.ServerManagerHostedService>();

        // [Issue #445] ONNX Runtime 直接推論モード（Python/gRPC 翻訳サーバー不要）
        services.AddSingleton<Baketa.Core.Abstractions.Translation.ITranslationEngine>(provider =>
        {
            var translationSettings = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<TranslationSettings>>().Value;
            var onnxLogger = provider.GetRequiredService<ILogger<Baketa.Infrastructure.Translation.Onnx.OnnxTranslationEngine>>();
            var modelDir = !string.IsNullOrEmpty(translationSettings.OnnxModelDirectory)
                ? translationSettings.OnnxModelDirectory
                : ResolveDefaultOnnxModelDirectory();

            onnxLogger.LogInformation("[Issue #445] OnnxTranslationEngine を登録: ModelDir={ModelDir}", modelDir);
            return new Baketa.Infrastructure.Translation.Onnx.OnnxTranslationEngine(modelDir, onnxLogger);
        });

        Console.WriteLine("🚀 [Issue #445] OnnxTranslationEngine 登録完了（Python/gRPC翻訳廃止）");
        Console.WriteLine($"🚀 [Issue #445] Clean Architecture実現: 通信層抽象化完了（削除した既存登録数: {existingTranslationEngines.Count}）");
    }

    /// <summary>
    /// ONNX モデルのデフォルトディレクトリを解決（int8量子化版を優先）
    /// </summary>
    private static string ResolveDefaultOnnxModelDirectory()
    {
        // 1. BaseDirectory（リリース版: ComponentDownloadServiceのダウンロード先）
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var int8Dir = System.IO.Path.Combine(baseDir, "Models", "nllb-200-onnx-int8");
        if (System.IO.Directory.Exists(int8Dir))
            return int8Dir;

        // 2. カレントディレクトリ（開発時: dotnet run のワーキングディレクトリ）
        var currentDir = System.IO.Directory.GetCurrentDirectory();
        var devInt8Dir = System.IO.Path.Combine(currentDir, "Models", "nllb-200-onnx-int8");
        if (System.IO.Directory.Exists(devInt8Dir))
            return devInt8Dir;

        // フォールバック: BaseDirectory配下（ダウンロード前の初回起動時はまだ存在しない）
        return int8Dir;
    }

    /// <summary>
    /// パフォーマンス管理サービスを登録します。
    /// Phase 4.1: パフォーマンスメトリクス収集統合
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    private static void RegisterPerformanceServices(IServiceCollection services)
    {
        Console.WriteLine("🚀 統合パフォーマンス管理システム登録開始 - Issue #143 Week 3 Phase 2 + Phase 4.1");

        // Phase 4.1: パフォーマンスメトリクス設定を登録
        services.Configure<Baketa.Infrastructure.Monitoring.PerformanceMetricsSettings>(config =>
        {
            var serviceProvider = services.BuildServiceProvider();
            var configuration = serviceProvider.GetService<IConfiguration>();

            if (configuration != null)
            {
                configuration.GetSection("PerformanceMetrics").Bind(config);
            }
            else
            {
                // フォールバック設定
                config.Enabled = true;
                config.BatchSize = 50;
                config.FlushIntervalSeconds = 5;
                config.MaxQueueSize = 1000;
                config.LogRetentionDays = 30;
                config.EnableStructuredReports = true;
                config.LogLevel = "Information";
                Console.WriteLine("⚠️ [PHASE4.1] フォールバックメトリクス設定を使用");
            }
        });
        Console.WriteLine("✅ [PHASE4.1] PerformanceMetricsSettings設定完了");

        // Phase 4.1: 統合パフォーマンスメトリクスコレクター登録
        services.AddSingleton<Baketa.Core.Abstractions.Monitoring.IPerformanceMetricsCollector>(
            provider =>
            {
                var logger = provider.GetRequiredService<ILogger<Baketa.Infrastructure.Monitoring.IntegratedPerformanceMetricsCollector>>();
                var baketaLogger = provider.GetRequiredService<IBaketaLogger>();
                var translationMetricsCollector = provider.GetRequiredService<Baketa.Infrastructure.Translation.Metrics.TranslationMetricsCollector>();
                var settings = provider.GetRequiredService<IOptions<Baketa.Infrastructure.Monitoring.PerformanceMetricsSettings>>();

                var collector = new Baketa.Infrastructure.Monitoring.IntegratedPerformanceMetricsCollector(
                    logger, baketaLogger, translationMetricsCollector, settings);

                logger.LogInformation("📊 [PHASE4.1] 統合パフォーマンスメトリクスコレクター初期化完了");
                return collector;
            });
        Console.WriteLine("✅ [PHASE4.1] IntegratedPerformanceMetricsCollector登録完了 - 既存TranslationMetricsCollector統合");

        // GPUメモリ管理
        services.AddSingleton<IGpuMemoryManager, GpuMemoryManager>();

        // パフォーマンス分析サービス
        services.AddSingleton<IAsyncPerformanceAnalyzer, AsyncPerformanceAnalyzer>();

        // [ROI_DELETION] IntegratedPerformanceOrchestrator削除 - レガシーROI機能除去
        // services.AddSingleton<Baketa.Core.Abstractions.Performance.IPerformanceOrchestrator, Baketa.Infrastructure.Performance.IntegratedPerformanceOrchestrator>();
        // Console.WriteLine("✅ IPerformanceOrchestrator登録完了 - 統合最適化システム");

        // NOTE: PooledGpuOptimizationOrchestratorは削除済み（PP-OCRv5依存）

        // 📊 Phase 3.2: VRAM監視5-tier圧迫度レベル判定システム
        services.AddHostedService<Baketa.Infrastructure.Services.ResourceMonitoringHostedService>();
        Console.WriteLine("📊 Phase 3.2: ResourceMonitoringHostedService登録完了 - VRAM監視5-tier圧迫度レベル判定システム");

        // 翻訳精度検証システム（デバッグビルドのみ）
        // TODO: 翻訳精度検証システムは将来実装予定
        // #if DEBUG
        // services.AddSingleton<ITranslationAccuracyValidator, TranslationAccuracyValidator>();
        // #endif

        Console.WriteLine("✅ 統合パフォーマンス管理システム登録完了（プール化×GPU最適化 + Phase 4.1 メトリクス収集含む）");
    }

    /// <summary>
    /// 初回起動判定サービスを登録します。
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    private static void RegisterFirstRunServices(IServiceCollection services)
    {
        Console.WriteLine("🚀 初回起動判定サービス登録開始");

        services.AddSingleton<Baketa.Infrastructure.Services.IFirstRunService, Baketa.Infrastructure.Services.FirstRunService>();

        Console.WriteLine("✅ FirstRunService登録完了 - 初回起動フラグ管理");
    }

    /// <summary>
    /// データ永続化サービスを登録します。
    /// </summary>
    /// <param name="_1">サービスコレクション（将来の実装で使用予定）</param>
    /// <param name="_2">アプリケーション実行環境（将来の実装で使用予定）</param>
    private static void RegisterPersistenceServices(IServiceCollection _1, Core.DI.BaketaEnvironment _2)
    {
        // 設定保存サービス（SettingsModuleで登録されるため、ここでは削除）
        // _1.AddSingleton<ISettingsService, JsonSettingsService>();

        // 例: _1.AddSingleton<IProfileStorage, JsonProfileStorage>();

        // 将来実装予定のキャッシュサービス（環境別設定）
        // if (_2 == Core.DI.BaketaEnvironment.Development || _2 == Core.DI.BaketaEnvironment.Test)
        // {
        //     // 開発/テスト環境ではメモリキャッシュを使用
        //     _1.AddSingleton<IOcrResultCache, MemoryOcrResultCache>();
        //     _1.AddSingleton<IDictionaryCache, MemoryDictionaryCache>();
        // }
        // else
        // {
        //     // 本番環境ではSQLiteキャッシュを使用
        //     _1.AddSingleton<IOcrResultCache, SqliteOcrResultCache>();
        //     _1.AddSingleton<IDictionaryCache, SqliteDictionaryCache>();
        // }

        // バックグラウンド同期
        // 例: _1.AddSingleton<IBackgroundSyncService, BackgroundSyncService>();

        // 統一設定管理サービス
        RegisterUnifiedSettings(_1);

        // [Issue #261] 同意管理サービス
        RegisterConsentServices(_1);

        // Analytics is disabled for both infrastructure registration overloads.
        _1.AddSingleton<Baketa.Core.Abstractions.Services.IUsageAnalyticsService,
            Baketa.Infrastructure.Analytics.NullUsageAnalyticsService>();

        // 統一ログサービス
        RegisterLoggingServices(_1);
    }

    /// <summary>
    /// 統一設定管理サービスを登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    private static void RegisterUnifiedSettings(IServiceCollection services)
    {
        // 統一設定管理サービス（Singleton: アプリケーション全体で共有）
        services.AddSingleton<IUnifiedSettingsService, UnifiedSettingsService>();
    }

    /// <summary>
    /// [Issue #261] 同意管理サービスを登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    private static void RegisterConsentServices(IServiceCollection services)
    {
        // 利用規約・プライバシーポリシー同意管理サービス（Singleton: アプリケーション全体で共有）
        services.AddSingleton<ConsentService>();
        services.AddSingleton<IConsentService, LocalConsentService>();
    }

    /// <summary>
    /// 統一ログサービスを登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    private static void RegisterLoggingServices(IServiceCollection services)
    {
        // 統一ログサービス（Singleton: アプリケーション全体で共有）
        services.AddSingleton<IBaketaLogger, BaketaLogger>();

        // [Issue #299] API重複呼び出し削減マネージャー
        services.AddSingleton<IApiRequestDeduplicator, ApiRequestDeduplicator>();
        Console.WriteLine("✅ [Issue #299] ApiRequestDeduplicator登録完了 - API重複呼び出し削減");

        // [Issue #299] 認証状態変更時にAPIキャッシュを無効化
        services.AddHostedService<AuthStateCacheInvalidator>();
        Console.WriteLine("✅ [Issue #299] AuthStateCacheInvalidator登録完了 - 認証変更時キャッシュ無効化");

        // 🔧 [Issue #193/#194] バックグラウンドタスクキュー（DiagnosticCollectionServiceの依存）
        services.AddSingleton<Baketa.Core.Abstractions.Services.IBackgroundTaskQueue, BackgroundTaskQueue>();
        Console.WriteLine("✅ IBackgroundTaskQueue登録完了 - バックグラウンドタスク処理");

        // 🔧 [Issue #193/#194] Gemini調査指摘: QueuedHostedService登録漏れ修正
        // キューに追加されたタスクを処理するHostedServiceを登録
        services.AddHostedService<QueuedHostedService>();
        Console.WriteLine("✅ QueuedHostedService登録完了 - バックグラウンドタスクワーカー");

        // 🔧 [Issue #193/#194] Gemini調査指摘: IDiagnosticReportGenerator登録漏れ修正
        // DiagnosticCollectionServiceの依存関係
        services.AddSingleton<Baketa.Core.Abstractions.Services.IDiagnosticReportGenerator, DiagnosticReportGenerator>();
        Console.WriteLine("✅ IDiagnosticReportGenerator登録完了 - 診断レポート生成");

        // 診断データ収集サービス（Singleton: パイプライン診断イベント収集）
        services.AddSingleton<Baketa.Core.Abstractions.Services.IDiagnosticCollectionService, DiagnosticCollectionService>();
        Console.WriteLine("✅ IDiagnosticCollectionService登録完了 - 診断データ収集");

        // 🔧 [Issue #199] DiagnosticEventProcessor登録 - PipelineDiagnosticEventハンドラ
        services.AddSingleton<Baketa.Infrastructure.Events.Processors.DiagnosticEventProcessor>();
        services.AddSingleton<IEventProcessor<Baketa.Core.Events.Diagnostics.PipelineDiagnosticEvent>>(
            provider => provider.GetRequiredService<Baketa.Infrastructure.Events.Processors.DiagnosticEventProcessor>());
        Console.WriteLine("✅ DiagnosticEventProcessor登録完了 - PipelineDiagnosticEventハンドラ");
    }

    /// <summary>
    /// Phase3: リソース監視システムを登録します
    /// CPU・メモリ・GPU使用率をリアルタイム監視し、翻訳システムの動的最適化を支援
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    private static void RegisterResourceMonitoringServices(IServiceCollection services)
    {
        Console.WriteLine("🔧 [PHASE3] 動的リソース監視システム登録開始");

        // リソース監視設定（デフォルト値でシングルトン登録）
        var defaultSettings = new Baketa.Core.Abstractions.Monitoring.ResourceMonitoringSettings(
            MonitoringIntervalMs: 5000,        // 5秒間隔で監視
            HistoryRetentionMinutes: 60,       // 1時間分の履歴保持
            CpuWarningThreshold: 85.0,         // CPU使用率85%で警告
            MemoryWarningThreshold: 90.0,      // メモリ使用率90%で警告
            GpuWarningThreshold: 95.0,         // GPU使用率95%で警告
            EnableGpuMonitoring: true,         // GPU監視有効
            EnableNetworkMonitoring: false,    // ネットワーク監視無効（将来実装）
            EnableDiskMonitoring: false        // ディスク監視無効（将来実装）
        );

        // 設定をシングルトンとして登録（IOptionsパターンとダイレクト参照の両方をサポート）
        services.AddSingleton(defaultSettings);
        services.AddSingleton<IOptions<Baketa.Core.Abstractions.Monitoring.ResourceMonitoringSettings>>(
            provider => Options.Create(defaultSettings));
        Console.WriteLine("✅ [PHASE3] ResourceMonitoringSettings設定完了 - 監視間隔:5s, 履歴保持:60分");

        // 注意: WindowsSystemResourceMonitorは Platform プロジェクトで登録される
        // Infrastructure レイヤーでは抽象インターフェースのみ認識
        // 実際の実装は PlatformModule で登録される予定
        Console.WriteLine("ℹ️ [PHASE3] IResourceMonitor実装はPlatformModuleで登録されます");

        // 🔥 Phase 12.1: HybridResourceSettings設定登録
        services.Configure<Baketa.Infrastructure.ResourceManagement.HybridResourceSettings>(options =>
        {
            // デフォルト設定を使用
        });
        Console.WriteLine("✅ [PHASE12.1] HybridResourceSettings登録完了 - デフォルト設定使用");

        // 🔥 Phase 12.1: HybridResourceManager登録（30秒待機問題解決）
        services.AddSingleton<Baketa.Infrastructure.ResourceManagement.IResourceManager, Baketa.Infrastructure.ResourceManagement.HybridResourceManager>();
        Console.WriteLine("🔥🔥🔥 [PHASE12.1] HybridResourceManager登録完了 - Translation Channel Reader有効化");

        Console.WriteLine("🎉 [PHASE3] 動的リソース監視システム登録完了");
    }

    /// <summary>
    /// Phase 1: 画像変化検知システムを登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    private static void RegisterImageChangeDetectionServices(IServiceCollection services)
    {
        Console.WriteLine("🔄 [PHASE1] 画像変化検知システム登録開始");

        // 画像変化検知メトリクスサービス
        services.AddSingleton<Baketa.Core.Abstractions.Services.IImageChangeMetricsService, ImageChangeMetricsService>();
        Console.WriteLine("✅ IImageChangeMetricsService登録完了 - 変化検知メトリクス収集");

        // 画像変化検知サービス（拡張実装を優先）
        services.AddSingleton<Baketa.Core.Abstractions.Services.IImageChangeDetectionService, Baketa.Infrastructure.Imaging.ChangeDetection.EnhancedImageChangeDetectionService>();
        Console.WriteLine("✅ IImageChangeDetectionService登録完了 - 拡張3段階フィルタリング実装");

        // Perceptual Hash サービス
        services.AddSingleton<Baketa.Core.Abstractions.Services.IPerceptualHashService, Baketa.Infrastructure.Imaging.ChangeDetection.OptimizedPerceptualHashService>();
        Console.WriteLine("✅ IPerceptualHashService登録完了 - OpenCV SIMD最適化実装");

        Console.WriteLine("🎉 [PHASE1] 画像変化検知システム登録完了");
    }

    /// <summary>
    /// [Issue #293] ROI Manager サービスを登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <param name="configuration">設定オブジェクト（nullの場合はデフォルト設定を使用）</param>
    private static void RegisterRoiManagerServices(IServiceCollection services, IConfiguration? configuration = null)
    {
        Console.WriteLine("🔄 [Issue #293] ROI Manager サービス登録開始");

        // ROI Manager 設定 - appsettings.jsonからバインド
        if (configuration != null)
        {
            var roiManagerSection = configuration.GetSection("RoiManager");
            if (roiManagerSection.Exists())
            {
                services.Configure<RoiManagerSettings>(roiManagerSection);
                Console.WriteLine($"✅ RoiManagerSettings: appsettings.jsonからバインド (Enabled={roiManagerSection["Enabled"]})");
            }
            else
            {
                services.Configure<RoiManagerSettings>(options => { });
                Console.WriteLine("⚠️ RoiManagerSettings: セクションなし - デフォルト設定を使用");
            }

            // ROI Gatekeeper 設定 - appsettings.jsonからバインド
            var roiGatekeeperSection = configuration.GetSection("RoiGatekeeper");
            if (roiGatekeeperSection.Exists())
            {
                services.Configure<RoiGatekeeperSettings>(roiGatekeeperSection);
                Console.WriteLine($"✅ RoiGatekeeperSettings: appsettings.jsonからバインド (Enabled={roiGatekeeperSection["Enabled"]})");
            }
            else
            {
                services.Configure<RoiGatekeeperSettings>(options => { });
                Console.WriteLine("⚠️ RoiGatekeeperSettings: セクションなし - デフォルト設定を使用");
            }
        }
        else
        {
            // configurationがnullの場合はデフォルト設定
            services.Configure<RoiManagerSettings>(options => { });
            services.Configure<RoiGatekeeperSettings>(options => { });
            Console.WriteLine("⚠️ ROI設定: IConfiguration未指定 - デフォルト設定を使用");
        }

        // ROI学習エンジン
        services.AddSingleton<Baketa.Core.Abstractions.Roi.IRoiLearningEngine,
            Baketa.Infrastructure.Roi.Services.RoiLearningEngine>();
        Console.WriteLine("✅ IRoiLearningEngine登録完了 - ヒートマップ学習エンジン");

        // ROI Manager
        services.AddSingleton<Baketa.Core.Abstractions.Roi.IRoiManager,
            Baketa.Infrastructure.Roi.RoiManager>();
        Console.WriteLine("✅ IRoiManager登録完了 - ROI管理サービス");

        // ROI閾値プロバイダー（動的閾値統合用）
        services.AddSingleton<Baketa.Core.Abstractions.Roi.IRoiThresholdProvider,
            Baketa.Infrastructure.Roi.Services.RoiThresholdProvider>();
        Console.WriteLine("✅ IRoiThresholdProvider登録完了 - 動的閾値プロバイダー");

        // [Issue #293 Phase 8] IRoiGatekeeper廃止 - TextChangeDetectionServiceに統合済み

        // [Issue #293 Phase 5] ROI Profile Service（永続化）
        services.AddSingleton<Baketa.Core.Abstractions.Roi.IRoiProfileService,
            Baketa.Infrastructure.Roi.Persistence.RoiProfileRepository>();
        Console.WriteLine("✅ IRoiProfileService登録完了 - プロファイル永続化");

        // [Issue #293 Phase 6] ROI Region Merger（隣接領域結合）
        services.AddSingleton<Baketa.Infrastructure.Roi.Services.IRoiRegionMerger,
            Baketa.Infrastructure.Roi.Services.RoiRegionMerger>();
        Console.WriteLine("✅ IRoiRegionMerger登録完了 - 隣接領域結合サービス");

        // [Issue #324] ROI Change Monitor Service（変化監視）
        services.AddSingleton<Baketa.Core.Abstractions.Roi.IRoiChangeMonitorService,
            Baketa.Infrastructure.Roi.Services.RoiChangeMonitorService>();
        Console.WriteLine("✅ IRoiChangeMonitorService登録完了 - ROI変化監視サービス");

        Console.WriteLine("🎉 [Issue #293] ROI Manager サービス登録完了");
    }

    /// <summary>
    /// P1: 段階的フィルタリングシステムを登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <param name="configuration">設定オブジェクト（nullの場合はハードコード設定を使用）</param>
    private static void RegisterStagedFilteringServices(IServiceCollection services, IConfiguration? configuration = null)
    {
        Console.WriteLine("🔄 [P1] 段階的フィルタリングシステム登録開始");

        // テキスト変化検知サービス（新規）
        services.AddSingleton<Baketa.Core.Abstractions.Processing.ITextChangeDetectionService, Baketa.Infrastructure.Text.ChangeDetection.TextChangeDetectionService>();
        Console.WriteLine("✅ ITextChangeDetectionService登録完了 - Edit Distance実装");

        // 🎯 Strategy A: パイプライン排他制御マネージャー
        services.AddSingleton<Baketa.Core.Abstractions.Processing.IPipelineExecutionManager, Baketa.Infrastructure.Processing.PipelineExecutionManager>();
        Console.WriteLine("✅ IPipelineExecutionManager登録完了 - Strategy A並行実行防止");

        // メイン処理パイプラインサービス
        services.AddSingleton<Baketa.Core.Abstractions.Processing.ISmartProcessingPipelineService, Baketa.Infrastructure.Processing.SmartProcessingPipelineService>();
        Console.WriteLine("✅ ISmartProcessingPipelineService登録完了 - 段階的処理パイプライン");

        // 段階別戦略実装（IProcessingStageStrategyインターフェースとして登録 - Geminiフィードバック反映）
        // 🔧 UltraThink修正: AddSingletonで状態保持（_previousImage）を正常化
        services.AddSingleton<IProcessingStageStrategy, Baketa.Infrastructure.Processing.Strategies.ImageChangeDetectionStageStrategy>();

        // 🔥 [PHASE13.2.31H_FIX] OcrExecutionStageStrategy標準DI登録（ファクトリーラムダ削除） - Gemini推奨⭐5/5
        // 根本修正: コンストラクタのデフォルト値を削除し、DIコンテナが自動的に依存関係を解決
        // 利点: Clean Architecture準拠、他のStrategyクラスとDI登録方法を統一、再発防止
        services.AddTransient<IProcessingStageStrategy, Baketa.Infrastructure.Processing.Strategies.OcrExecutionStageStrategy>();

        services.AddTransient<IProcessingStageStrategy, Baketa.Infrastructure.Processing.Strategies.TextChangeDetectionStageStrategy>();
        // 🔥 [OLD_FLOW_REMOVAL] TranslationExecutionStageStrategy削除 - Phase 12.2新アーキテクチャ移行完了
        // 理由: CoordinateBasedTranslationService + AggregatedChunksReadyEventHandlerに統一
        Console.WriteLine("✅ 段階別戦略登録完了 - 3段階処理戦略（TranslationExecutionStageStrategy削除済み）");

        // 段階的処理設定（appsettings.json対応 - ハードコード削除済み）
        if (configuration != null)
        {
            // appsettings.jsonからSmartProcessingPipeline設定を読み込み
            services.Configure<Baketa.Core.Models.Processing.ProcessingPipelineSettings>(
                configuration.GetSection("SmartProcessingPipeline"));
            Console.WriteLine("✅ ProcessingPipelineSettings設定完了 - appsettings.jsonから読み込み");
        }
        else
        {
            // フォールバック: configuration=nullの場合はハードコード設定を使用
            services.Configure<Baketa.Core.Models.Processing.ProcessingPipelineSettings>(options =>
            {
                options.EnableStaging = true;
                options.EnableEarlyTermination = true;
                options.TextChangeThreshold = 0.1f; // 10%の変化で翻訳実行
                options.EnablePerformanceMetrics = true;
                options.StopOnFirstError = false;
            });
            Console.WriteLine("⚠️ ProcessingPipelineSettings設定完了 - フォールバック（ハードコード設定）");
        }

        Console.WriteLine("🎉 [P1] 段階的フィルタリングシステム登録完了");
    }

    // Phase2: ハイブリッドリソース管理システム登録はPlatformModuleに移動済み（循環依存解決）

    /// <summary>
    /// このモジュールが依存する他のモジュールの型を取得します。
    /// </summary>
    /// <returns>依存モジュールの型のコレクション</returns>
    public override IEnumerable<Type> GetDependentModules()
    {
        yield return typeof(CoreModule);
        yield return typeof(ObjectPoolModule);
        // 🔧 UltraThink Phase 29: TimedAggregatorModule依存追加 - ITextChunkAggregatorService登録確保
        yield return typeof(TimedAggregatorModule);
    }

    /// <summary>
    /// Issue #78: Cloud AI翻訳サービスを登録します
    /// Pro/Premiaプラン向けのCloud AI翻訳機能
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    private static void RegisterCloudAIServices(IServiceCollection services)
    {
        Console.WriteLine("🚀 Issue #78: Cloud AI翻訳サービス登録開始");

        // Phase 1: 基盤サービス
        // エンジン状態管理（フォールバック制御）
        services.AddSingleton<IEngineStatusManager, EngineStatusManager>();
        Console.WriteLine("✅ IEngineStatusManager登録完了 - フォールバック状態管理");

        // トークン使用量リポジトリ（永続化）
        services.AddSingleton<ITokenUsageRepository, TokenUsageRepository>();
        Console.WriteLine("✅ ITokenUsageRepository登録完了 - トークン使用量永続化");

        // トークン消費追跡
        services.AddSingleton<CoreTranslation.ITokenConsumptionTracker, TokenConsumptionTracker>();
        Console.WriteLine("✅ ITokenConsumptionTracker登録完了 - トークン消費追跡");

        // エンジンアクセス制御（プラン別制限）
        services.AddSingleton<IEngineAccessController, EngineAccessController>();
        Console.WriteLine("✅ IEngineAccessController登録完了 - プラン別アクセス制御");

        // Phase 2: Cloud AI画像翻訳
        RegisterCloudAIPhase2Services(services);

        // Phase 3: 相互検証ロジック
        RegisterCloudAIPhase3Services(services);

        Console.WriteLine("🎉 Issue #78: Cloud AI翻訳サービス登録完了");
    }

    /// <summary>
    /// Issue #78 Phase 2: Cloud AI画像翻訳サービスを登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    private static void RegisterCloudAIPhase2Services(IServiceCollection services)
    {
        Console.WriteLine("🚀 Issue #78 Phase 2: Cloud AI画像翻訳サービス登録開始");

        // CloudTranslationSettings設定登録
        var configurationDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IConfiguration));
        if (configurationDescriptor?.ImplementationInstance is IConfiguration configuration)
        {
            services.Configure<Core.Settings.CloudTranslationSettings>(
                configuration.GetSection(Core.Settings.CloudTranslationSettings.SectionName));
            Console.WriteLine("✅ CloudTranslationSettings登録完了 - appsettings.jsonから読み込み");
        }
        else
        {
            services.Configure<Core.Settings.CloudTranslationSettings>(options =>
            {
                options.RelayServerUrl = "https://api.baketa.app";
                options.TimeoutSeconds = 30;
                options.MaxRetries = 2;
                options.PrimaryProviderId = "gemini";
            });
            Console.WriteLine("⚠️ CloudTranslationSettings登録完了 - デフォルト値使用");
        }

        // Phase 1 keeps the abstraction while both cloud providers are unavailable.
        services.AddKeyedSingleton<CoreTranslation.ICloudImageTranslator>(
            "primary", new Translation.Cloud.DisabledCloudImageTranslator("primary"));
        services.AddKeyedSingleton<CoreTranslation.ICloudImageTranslator>(
            "secondary", new Translation.Cloud.DisabledCloudImageTranslator("secondary"));

        // FallbackOrchestrator登録
        services.AddSingleton<CoreTranslation.IFallbackOrchestrator, Translation.Services.FallbackOrchestrator>();
        Console.WriteLine("✅ IFallbackOrchestrator登録完了 - 3段階フォールバック制御");

        Console.WriteLine("🎉 Issue #78 Phase 2: Cloud AI画像翻訳サービス登録完了");
    }

    /// <summary>
    /// Issue #78 Phase 3: 相互検証ロジックを登録します
    /// ローカルOCRとCloud AI結果の相互検証・ハルシネーション除去
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    private static void RegisterCloudAIPhase3Services(IServiceCollection services)
    {
        Console.WriteLine("🚀 Issue #78 Phase 3/3.5: 相互検証ロジック登録開始");

        // ファジーテキストマッチング（レーベンシュタイン距離）
        services.AddSingleton<IFuzzyTextMatcher, FuzzyTextMatcher>();
        Console.WriteLine("✅ IFuzzyTextMatcher登録完了 - レーベンシュタイン距離マッチング");

        // 低信頼度テキスト救済
        services.AddSingleton<IConfidenceRescuer, ConfidenceRescuer>();
        Console.WriteLine("✅ IConfidenceRescuer登録完了 - 低信頼度テキスト救済");

        // Phase 3.5: 包含マッチング（双方向マッチング）
        services.AddSingleton<IContainmentMatcher, ContainmentMatcher>();
        Console.WriteLine("✅ IContainmentMatcher登録完了 - 包含マッチング（統合/分割）");

        // 相互検証（ローカルOCR × Cloud AI）- Phase 3.5対応コンストラクタ
        services.AddSingleton<ICrossValidator>(provider =>
        {
            var fuzzyMatcher = provider.GetRequiredService<IFuzzyTextMatcher>();
            var rescuer = provider.GetRequiredService<IConfidenceRescuer>();
            var containmentMatcher = provider.GetRequiredService<IContainmentMatcher>();
            var logger = provider.GetRequiredService<ILogger<CrossValidator>>();
            return new CrossValidator(fuzzyMatcher, rescuer, containmentMatcher, logger);
        });
        Console.WriteLine("✅ ICrossValidator登録完了 - 相互検証ロジック（Phase 3.5統合）");

        Console.WriteLine("🎉 Issue #78 Phase 3/3.5: 相互検証ロジック登録完了");
    }

    // [Issue #445] RegisterPhase2Services は Python翻訳インフラ廃止に伴い削除
    // (CacheManagementService / SmartConnectionEstablisher は不要)

    /// <summary>
    /// Modelsディレクトリを確実に見つけるためのヘルパーメソッド
    /// 開発環境と本番環境の両方で動作する
    /// </summary>
    /// <param name="appRoot">アプリケーションのBaseDirectory</param>
    /// <returns>Modelsディレクトリの絶対パス</returns>
    private static string FindModelsDirectory(string appRoot)
    {
        // 候補パスのリスト（優先順）
        var candidatePaths = new[]
        {
                // 開発環境：プロジェクトルートのModelsディレクトリ
                Path.Combine(appRoot, "..", "..", "..", "..", "Models"),
                Path.Combine(appRoot, "..", "..", "..", "Models"),
                Path.Combine(appRoot, "..", "..", "Models"),
                Path.Combine(appRoot, "..", "Models"),
                
                // 本番環境：アプリケーションと同じディレクトリ
                Path.Combine(appRoot, "Models"),
                
                // フォールバック：現在のディレクトリ
                Path.Combine(Directory.GetCurrentDirectory(), "Models")
            };

        // 最初に見つかったディレクトリを使用
        foreach (var path in candidatePaths)
        {
            var normalizedPath = Path.GetFullPath(path);
            if (Directory.Exists(normalizedPath))
            {
                return normalizedPath;
            }
        }

        // どこにも見つからない場合はデフォルト（プロジェクトルート想定）
        var defaultPath = Path.GetFullPath(Path.Combine(appRoot, "..", "..", "..", "..", "Models"));
        return defaultPath;
    }

    /// <summary>
    /// Phase 3.13: Memory変換サービスを登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    private static void RegisterMemoryServices(IServiceCollection services)
    {
        // IImage → SafeImage 変換コンバーター
        services.AddSingleton<IImageToSafeImageConverter, ImageToSafeImageConverter>();

        // IImage → ReferencedSafeImage 変換コンバーター (Phase 3.14)
        services.AddSingleton<IImageToReferencedSafeImageConverter, ImageToReferencedSafeImageConverter>();

        Console.WriteLine("🎯 [PHASE3.13-14] Memory変換サービス登録完了");
    }

    /// <summary>
    /// UltraThink Phase 1: 近接度ベースグループ化サービスを登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    private static void RegisterProximityGroupingServices(IServiceCollection services)
    {
        Console.WriteLine("🚀 UltraThink Phase 1: 近接度ベースグループ化サービス登録開始");

        // ChunkProximityAnalyzer: 文字サイズ自動検出・近接判定（設定ファクトリ付き）
        services.AddSingleton<Baketa.Infrastructure.OCR.PostProcessing.ChunkProximityAnalyzer>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<Baketa.Infrastructure.OCR.PostProcessing.ChunkProximityAnalyzer>>();

            // 🔥🔥🔥 [CRITICAL_TRACE] IConfigurationから直接値を読み取る
            var configuration = provider.GetRequiredService<IConfiguration>();
            var configValue = configuration["TimedAggregator:ProximityGrouping:VerticalDistanceFactor"];
            Console.WriteLine($"🔥🔥🔥 [CRITICAL_TRACE] IConfiguration直接読取: VerticalDistanceFactor={configValue}");
            Console.WriteLine($"🔥🔥🔥 [CRITICAL_TRACE] IConfiguration直接読取: VerticalDistanceFactor={configValue}");

            // 🔥 [VALUE_TRACE] IOptionsMonitor<TimedAggregatorSettings>の完全追跡
            Console.WriteLine("🔥 [VALUE_TRACE] IOptionsMonitor<TimedAggregatorSettings>取得開始");
            Console.WriteLine("🔥 [VALUE_TRACE] IOptionsMonitor<TimedAggregatorSettings>取得開始");

            var settingsMonitor = provider.GetRequiredService<IOptionsMonitor<TimedAggregatorSettings>>();
            Console.WriteLine($"🔥 [VALUE_TRACE] IOptionsMonitor型: {settingsMonitor?.GetType().FullName ?? "NULL"}");
            Console.WriteLine($"🔥 [VALUE_TRACE] IOptionsMonitor型: {settingsMonitor?.GetType().FullName ?? "NULL"}");

            var settings = settingsMonitor.CurrentValue;
            Console.WriteLine($"🔥 [VALUE_TRACE] CurrentValue型: {settings?.GetType().FullName ?? "NULL"}");
            Console.WriteLine($"🔥 [VALUE_TRACE] CurrentValue == null: {settings == null}");
            Console.WriteLine($"🔥 [VALUE_TRACE] CurrentValue型: {settings?.GetType().FullName ?? "NULL"}");

            if (settings != null)
            {
                // 🔥 [VALUE_TRACE] TimedAggregatorSettings全体をJSON出力
                var settingsJson = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                Console.WriteLine($"🔥 [VALUE_TRACE] TimedAggregatorSettings全体:\n{settingsJson}");
                Console.WriteLine($"🔥 [VALUE_TRACE] TimedAggregatorSettings JSON: {settingsJson}");

                Console.WriteLine($"🔥 [VALUE_TRACE] TimedAggregatorSettings.IsFeatureEnabled: {settings.IsFeatureEnabled}");
                Console.WriteLine($"🔥 [VALUE_TRACE] TimedAggregatorSettings.BufferDelayMs: {settings.BufferDelayMs}");
                Console.WriteLine($"🔥 [VALUE_TRACE] TimedAggregatorSettings.IsFeatureEnabled: {settings.IsFeatureEnabled}");

                var proximitySettings = settings.ProximityGrouping;
                Console.WriteLine($"🔥 [VALUE_TRACE] ProximityGrouping == null: {proximitySettings == null}");
                Console.WriteLine($"🔥 [VALUE_TRACE] ProximityGrouping型: {proximitySettings?.GetType().FullName ?? "NULL"}");
                Console.WriteLine($"🔥 [VALUE_TRACE] ProximityGrouping == null: {proximitySettings == null}");

                if (proximitySettings != null)
                {
                    Console.WriteLine($"🔥 [VALUE_TRACE] ProximityGrouping.VerticalDistanceFactor: {proximitySettings.VerticalDistanceFactor}");
                    Console.WriteLine($"🔥 [VALUE_TRACE] ProximityGrouping.HorizontalDistanceFactor: {proximitySettings.HorizontalDistanceFactor}");
                    Console.WriteLine($"🔥 [VALUE_TRACE] ProximityGrouping.Enabled: {proximitySettings.Enabled}");
                    Console.WriteLine($"🔥 [VALUE_TRACE] VerticalDistanceFactor={proximitySettings.VerticalDistanceFactor}, HorizontalDistanceFactor={proximitySettings.HorizontalDistanceFactor}");

                    // 🔥 [VALUE_TRACE] 新規インスタンス作成との比較
                    var freshInstance = new Baketa.Core.Settings.ProximityGroupingSettings();
                    Console.WriteLine($"🔥 [COMPARE] new ProximityGroupingSettings().VerticalDistanceFactor: {freshInstance.VerticalDistanceFactor}");
                    Console.WriteLine($"🔥 [COMPARE] DI注入={proximitySettings.VerticalDistanceFactor}, 新規作成={freshInstance.VerticalDistanceFactor}");
                }

                return new Baketa.Infrastructure.OCR.PostProcessing.ChunkProximityAnalyzer(logger, proximitySettings ?? new Baketa.Core.Settings.ProximityGroupingSettings());
            }
            else
            {
                Console.WriteLine("🔥 [VALUE_TRACE] settings == null のためデフォルトインスタンス作成");
                Console.WriteLine("🔥 [VALUE_TRACE] settings == null のためデフォルトインスタンス作成");
                return new Baketa.Infrastructure.OCR.PostProcessing.ChunkProximityAnalyzer(logger, new Baketa.Core.Settings.ProximityGroupingSettings());
            }
        });
        Console.WriteLine("✅ ChunkProximityAnalyzer登録完了 - 自動閾値計算 + 設定連携");

        // ProximityGroupingService: 連結成分アルゴリズムによるグループ化
        services.AddSingleton<Baketa.Infrastructure.OCR.PostProcessing.ProximityGroupingService>();
        Console.WriteLine("✅ ProximityGroupingService登録完了 - 連結成分グループ化");

        Console.WriteLine("✅ UltraThink Phase 1: 近接度ベースグループ化サービス登録完了");
    }

    /// <summary>
    /// 🚀 Phase 2.3: TranslationSettings（gRPC Client設定含む）をDIコンテナに登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    private static void RegisterTranslationSettings(IServiceCollection services)
    {
#if DEBUG
        Console.WriteLine("🚀 [PHASE2.3] TranslationSettings登録開始 - gRPC Client対応");
#endif

        try
        {
            // IConfigurationがDIコンテナに登録されているか確認
            var configurationDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IConfiguration));

            if (configurationDescriptor?.ImplementationInstance is IConfiguration configuration)
            {
                // appsettings.jsonのTranslationセクションから設定をバインド
                services.Configure<TranslationSettings>(configuration.GetSection("Translation"));

#if DEBUG
                // 開発時のみgRPC設定値を出力
                var useGrpc = configuration["Translation:UseGrpcClient"];
                var serverAddr = configuration["Translation:GrpcServerAddress"];
                Console.WriteLine($"✅ [PHASE2.3] TranslationSettings登録完了 - UseGrpcClient: {useGrpc ?? "NULL"}, ServerAddress: {serverAddr ?? "NULL"}");
#endif
            }
            else
            {
                // フォールバック: IConfiguration未登録時はデフォルト値を使用
#if DEBUG
                Console.WriteLine("⚠️ [FALLBACK] IConfiguration未登録 - TranslationSettingsデフォルト設定を使用");
#endif
                services.Configure<TranslationSettings>(options =>
                {
                    options.UseGrpcClient = true; // [PHASE3.3] gRPCクライアントがデフォルト
                    options.GrpcServerAddress = ServerPortConstants.TranslationServerAddress;
                });
            }
        }
        catch (InvalidOperationException ex)
        {
            // IConfiguration解決失敗時
            Console.WriteLine($"⚠️ [PHASE2.3] IConfiguration resolution failed: {ex.Message}");

            // フォールバック処理
            services.Configure<TranslationSettings>(options =>
            {
                options.UseGrpcClient = true; // [PHASE3.3] gRPCクライアントがデフォルト
                options.GrpcServerAddress = ServerPortConstants.TranslationServerAddress;
            });
        }
        catch (Exception ex)
        {
            // 予期しないエラー
            Console.WriteLine($"💥 [PHASE2.3] RegisterTranslationSettings failed: {ex.GetType().Name} - {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// CircuitBreaker設定を安全に登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    private static void RegisterCircuitBreakerSettings(IServiceCollection services)
    {
        // ConfigurableServiceModuleBaseパターンを使用した安全なConfiguration取得
        var configurationDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IConfiguration));

        if (configurationDescriptor?.ImplementationInstance is IConfiguration configuration)
        {
            // appsettings.jsonから設定をバインド
            services.Configure<CircuitBreakerSettings>(configuration.GetSection("CircuitBreaker"));

            // 設定値検証とサニタイゼーション
            services.PostConfigure<CircuitBreakerSettings>(options =>
            {
                var originalTimeout = options.TimeoutMs;
                var originalThreshold = options.FailureThreshold;

                // セキュアな範囲制限 (5秒 - 5分)
                if (options.TimeoutMs < 5000 || options.TimeoutMs > 300000)
                {
                    Console.WriteLine($"⚠️ [SECURITY] CircuitBreaker.TimeoutMs値が範囲外({originalTimeout}ms) - デフォルト値(120000ms)を使用");
                    options.TimeoutMs = 120000;
                }

                if (options.FailureThreshold < 1 || options.FailureThreshold > 50)
                {
                    Console.WriteLine($"⚠️ [SECURITY] CircuitBreaker.FailureThreshold値が範囲外({originalThreshold}) - デフォルト値(5)を使用");
                    options.FailureThreshold = 5;
                }

                Console.WriteLine($"✅ [CONFIG] CircuitBreaker設定確定 - TimeoutMs: {options.TimeoutMs}ms, FailureThreshold: {options.FailureThreshold}");
            });
        }
        else
        {
            // フォールバック: 設定ファイル不在時のセキュアなデフォルト値
            Console.WriteLine("⚠️ [FALLBACK] appsettings.json不在 - CircuitBreakerデフォルト設定を使用");
            services.Configure<CircuitBreakerSettings>(options =>
            {
                options.FailureThreshold = 5;
                options.TimeoutMs = 120000; // 120秒 - NLLB-200初回ロード対応
                options.RecoveryTimeoutMs = 60000;
            });
        }
    }

    /// <summary>
    /// [Issue #185] コンポーネントダウンロードサービスを登録します（設定なし版）
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    private static void RegisterComponentDownloadServices(IServiceCollection services)
    {
        Console.WriteLine("🚀 [ISSUE185] コンポーネントダウンロードサービス登録開始（設定なし版）");

        // デフォルト設定で登録
        services.Configure<ComponentDownloadSettings>(options =>
        {
            // デフォルト値はクラス定義に含まれている
        });

        // [Issue #185] 設定値バリデーション登録（IValidateOptionsパターン）
        services.AddSingleton<IValidateOptions<ComponentDownloadSettings>, ComponentDownloadSettingsValidator>();

        // HttpClient登録（名前付きHttpClient推奨）
        services.AddHttpClient<Baketa.Core.Abstractions.Services.IComponentDownloader, ComponentDownloadService>();

        Console.WriteLine("✅ [ISSUE185] ComponentDownloadService登録完了 - デフォルト設定使用");
    }

    /// <summary>
    /// [Issue #185] コンポーネントダウンロードサービスを登録します（appsettings.json対応版）
    /// 🔥 [FIX] Singleton/Scoped競合問題修正 - HttpClientFactory + Singleton登録に変更
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <param name="configuration">設定オブジェクト</param>
    private static void RegisterComponentDownloadServices(IServiceCollection services, IConfiguration configuration)
    {
        Console.WriteLine("🚀 [ISSUE185] コンポーネントダウンロードサービス登録開始（appsettings.json対応版）");

        // appsettings.jsonからComponentDownload設定をバインド
        services.Configure<ComponentDownloadSettings>(
            configuration.GetSection(ComponentDownloadSettings.SectionName));

        // [Issue #185] 設定値バリデーション登録（IValidateOptionsパターン）
        services.AddSingleton<IValidateOptions<ComponentDownloadSettings>, ComponentDownloadSettingsValidator>();

        // 🔥 [FIX] HttpClientFactory登録（名前付きクライアント）
        services.AddHttpClient("ComponentDownload")
            .ConfigureHttpClient(client =>
            {
                // デフォルトタイムアウトは ComponentDownloadService で上書きされる
                client.DefaultRequestHeaders.Add("User-Agent", "Baketa-Downloader/1.0");
            });

        // 🔥 [FIX] Singleton登録でApplicationInitializer（Singleton）からの注入を可能に
        // AddHttpClient<T>はScopedになるため、手動でSingleton登録
        services.AddSingleton<Baketa.Core.Abstractions.Services.IComponentDownloader>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<ComponentDownloadService>>();
            logger.LogDebug("[Issue185] IComponentDownloader Factory実行開始");

            var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
            logger.LogDebug("[Issue185] HttpClientFactory取得成功");
            var httpClient = httpClientFactory.CreateClient("ComponentDownload");
            logger.LogDebug("[Issue185] HttpClient作成成功");
            var settings = provider.GetRequiredService<IOptions<ComponentDownloadSettings>>();
            logger.LogDebug("[Issue185] ComponentDownloadSettings取得成功");

            // 設定内容をログ出力
            var settingsValue = settings.Value;
            logger.LogDebug("[Issue185] Settings - BaseUrl: {BaseUrl}", settingsValue.GitHubReleasesBaseUrl);
            logger.LogDebug("[Issue185] Settings - Version: {Version}", settingsValue.ReleaseVersion);
            logger.LogDebug("[Issue185] Settings - Components Count: {Count}", settingsValue.Components?.Count ?? 0);

            if (settingsValue.Components != null && settingsValue.Components.Count > 0)
            {
                foreach (var component in settingsValue.Components)
                {
                    logger.LogDebug("[Issue185] Component: {Id} - {DisplayName}", component.Id, component.DisplayName);
                }
            }
            else
            {
                logger.LogWarning("[Issue185] Components配列が空です！appsettings.jsonを確認してください");
            }

            // [Issue #210] GPU検出器を取得（オプショナル - 登録されていない場合はnull）
            var gpuDetector = provider.GetService<Baketa.Core.Abstractions.GPU.IGpuEnvironmentDetector>();
            logger.LogDebug("[Issue #210] GPU detector: {Status}", gpuDetector != null ? "Available" : "Not available");

            logger.LogDebug("[Issue185] ComponentDownloadService インスタンス作成 - Singleton");
            return new ComponentDownloadService(logger, httpClient, settings, gpuDetector);
        });

        // 設定値のログ出力はファクトリー内のILoggerで実行されます

        // [Issue #256] Phase 3.5: ComponentInstallerService登録
        services.AddSingleton<Baketa.Core.Abstractions.Services.IComponentInstallerService>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<ComponentInstallerService>>();
            var versionService = provider.GetRequiredService<IComponentVersionService>();
            return new ComponentInstallerService(logger, versionService);
        });
        Console.WriteLine("✅ [Issue #256] ComponentInstallerService登録完了");


    }

    /// <summary>
    /// translation_ports_global.jsonから動的に利用可能なポート番号を検出します
    /// </summary>
    /// <param name="logger">ロガー</param>
    /// <returns>検出されたポート番号（見つからない場合はServerPortConstants.UnifiedServerPort）</returns>
    private static int DetectDynamicPortFromGlobalRegistry(ILogger logger)
    {
        var defaultPort = ServerPortConstants.UnifiedServerPort;
        var globalRegistryPath = Path.Combine(Environment.CurrentDirectory, "translation_ports_global.json");

        try
        {
            if (!File.Exists(globalRegistryPath))
            {
                logger.LogWarning("🔍 [PHASE3.1_FIX] translation_ports_global.json が見つかりません: {Path}", globalRegistryPath);
                return defaultPort;
            }

            var json = File.ReadAllText(globalRegistryPath);
            using var document = System.Text.Json.JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.TryGetProperty("ports", out var portsElement))
            {
                foreach (var portProperty in portsElement.EnumerateObject())
                {
                    if (int.TryParse(portProperty.Name, out var availablePort))
                    {
                        logger.LogInformation("🎯 [PHASE3.1_FIX] 動的ポート検出成功: {Port}", availablePort);
                        return availablePort;
                    }
                }
            }

            logger.LogWarning("🔍 [PHASE3.1_FIX] translation_ports_global.json に有効なポートが見つかりませんでした");
            return defaultPort;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "💥 [PHASE3.1_FIX] translation_ports_global.json 読み込みエラー");
            return defaultPort;
        }
    }

    /// <summary>
    /// [Issue #287] クラッシュレポート送信インフラストラクチャを登録
    /// IHttpClientFactory + JwtTokenAuthHandler経由でJWT認証付き送信を実現
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    private static void RegisterCrashReportingInfrastructure(IServiceCollection services)
    {
        services.AddSingleton<ICrashReportSender, CrashReporting.NullCrashReportSender>();
    }
}
