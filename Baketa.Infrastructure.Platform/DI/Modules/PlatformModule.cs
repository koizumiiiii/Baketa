using System;
using System.Collections.Generic;
using Baketa.Core.Abstractions.DI;
using Baketa.Core.Abstractions.Monitoring;
using Baketa.Core.DI;
using Baketa.Core.DI.Attributes;
using Baketa.Core.DI.Modules;
using Baketa.Infrastructure.Platform.DI.Modules;
using Baketa.Infrastructure.Platform.Resources;
using Baketa.Infrastructure.Platform.Windows.OpenCv;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DefaultWindowsImageAdapter = Baketa.Infrastructure.Platform.Adapters.DefaultWindowsImageAdapter;
using IWindowsImageAdapter = Baketa.Core.Abstractions.Platform.Windows.Adapters.IWindowsImageAdapter;

namespace Baketa.Infrastructure.Platform.DI.Modules;

/// <summary>
/// プラットフォーム固有のサービスを登録するモジュール。
/// Windowsプラットフォーム固有の実装が含まれます。
/// </summary>
[ModulePriority(ModulePriority.Platform)]
public class PlatformModule : ServiceModuleBase
{
    /// <summary>
    /// プラットフォーム固有サービスを登録します。
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303:Do not pass literals as localized parameters",
        Justification = "プラットフォーム警告に静的リソースを使用")]
    public override void RegisterServices(IServiceCollection services)
    {
        // Windowsプラットフォーム固有の実装を登録
        if (OperatingSystem.IsWindows())
        {
            // キャプチャサービス
            RegisterCaptureServices(services);

            // フルスクリーンサービス
            services.AddFullscreenServices();

            // 画像処理サービス
            RegisterImageServices(services);

            // UI関連のWindowsサービス
            RegisterWindowsUIServices(services);

            // GPU環境検出サービス（Issue #143対応）
            RegisterGpuServices(services);

            // Phase3: リソース監視サービス（Windows固有実装）
            RegisterResourceMonitoringServices(services);

            // Phase3: ハイブリッドリソース管理システム（循環依存解決済み）
            RegisterHybridResourceManagementServices(services);

            // その他のWindows固有サービス
            RegisterWindowsServices(services);
        }
        else
        {
            // 現在はWindows専用
            Console.WriteLine(Resources.ModuleResources.PlatformWarning);
        }
    }

    /// <summary>
    /// キャプチャ関連サービスを登録します。
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    private static void RegisterCaptureServices(IServiceCollection services)
    {
        // スクリーンキャプチャサービス
        services.AddSingleton<Baketa.Infrastructure.Platform.Windows.Capture.IGdiScreenCapturer,
            Baketa.Infrastructure.Platform.Windows.Capture.GdiScreenCapturer>();

        // ウィンドウマネージャー
        services.AddSingleton<Baketa.Infrastructure.Platform.Windows.WindowsManager>();
        services.AddSingleton<Baketa.Core.Abstractions.Platform.Windows.IWindowManager>(
            sp => sp.GetRequiredService<Baketa.Infrastructure.Platform.Windows.WindowsManager>());
        // [Issue #448] CoordinateBasedTranslationServiceが使用するPlatform.IWindowManagerも同一インスタンスで登録
        services.AddSingleton<Baketa.Core.Abstractions.Platform.IWindowManager>(
            sp => sp.GetRequiredService<Baketa.Infrastructure.Platform.Windows.WindowsManager>());

        // 画像ファクトリー（Phase 3.1: SafeImage統合対応）
        services.AddSingleton<Baketa.Core.Abstractions.Factories.IWindowsImageFactory,
            Baketa.Infrastructure.Platform.Windows.WindowsImageFactory>();

        // 差分検出器
        services.AddSingleton<Baketa.Core.Abstractions.Capture.IDifferenceDetector,
            Baketa.Infrastructure.Capture.DifferenceDetection.EnhancedDifferenceDetector>();
    }

    /// <summary>
    /// 画像処理サービスを登録します。
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    private static void RegisterImageServices(IServiceCollection services)
    {
        // Windows画像処理関連の登録
        // 例: services.AddSingleton<IWindowsImageFactory, WindowsImageFactory>();
        // 例: services.AddSingleton<IImageConverter, WindowsImageConverter>();

        // 🔧 [CAPTURE_FIX] WindowsImageAdapter登録は後で実装
        // DIコンテナ型解決問題を回避するため、AdaptiveCaptureServiceで直接作成

        // OpenCV関連
        // 拡張メソッドを使用して登録
        services.AddOpenCvServices();

        // ファクトリー - Sprint 2 Fix: IImageFactory登録（PaddleOCR連続失敗解決）
        services.AddSingleton<Baketa.Core.Abstractions.Factories.IImageFactory, Baketa.Infrastructure.Platform.Adapters.WindowsImageAdapterFactory>();
    }

    /// <summary>
    /// Windows UI関連サービスを登録します。
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    private static void RegisterWindowsUIServices(IServiceCollection services)
    {
        // オーバーレイ関連
        services.RegisterOverlayServices();

        // マルチモニターサポート
        services.AddMultiMonitorSupport();

        // その他のUI関連サービス
        // 例: services.AddSingleton<IWindowsNotificationService, WindowsNotificationService>();

        // システムトレイ
        // 例: services.AddSingleton<ISystemTrayService, Win32SystemTrayService>();
    }

    /// <summary>
    /// GPU環境検出サービスを登録します（Issue #143対応）。
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    private static void RegisterGpuServices(IServiceCollection services)
    {
        Console.WriteLine("🎮 Windows GPU サービス登録開始 - Issue #143");

        // GPU環境検出サービス
        services.AddSingleton<Baketa.Core.Abstractions.GPU.IGpuEnvironmentDetector,
            Baketa.Infrastructure.Platform.Windows.GPU.WindowsGpuEnvironmentDetector>();
        Console.WriteLine("✅ WindowsGpuEnvironmentDetector登録完了");

        // GPU デバイス管理サービス（Issue #143 Week 2: Multi-GPU対応）
        services.AddSingleton<Baketa.Core.Abstractions.GPU.IGpuDeviceManager,
            Baketa.Infrastructure.Platform.Windows.GPU.WindowsGpuDeviceManager>();
        Console.WriteLine("✅ WindowsGpuDeviceManager登録完了");

        // TDR回復システム（Issue #143 Week 2 Phase 3: 高可用性）
        services.AddSingleton<Baketa.Core.Abstractions.GPU.ITdrRecoveryManager,
            Baketa.Infrastructure.Platform.Windows.GPU.WindowsTdrRecoveryManager>();
        Console.WriteLine("✅ WindowsTdrRecoveryManager登録完了");

        Console.WriteLine("✅ Windows GPU サービス登録完了");
    }

    /// <summary>
    /// その他のWindows固有サービスを登録します。
    /// </summary>
    /// <param name="_">サービスコレクション</param>
    private static void RegisterWindowsServices(IServiceCollection services)
    {
        // 🔥 [PHASE2.1_CLEAN_ARCH] 座標変換サービス（ROI→スクリーン座標変換）
        // Clean Architecture準拠: Platform層でWindows固有API依存サービスを登録
        services.AddSingleton<Baketa.Core.Abstractions.Services.ICoordinateTransformationService,
            Baketa.Infrastructure.Platform.Windows.Services.CoordinateTransformationService>();
        Console.WriteLine("✅ [PHASE2.1_CLEAN_ARCH] CoordinateTransformationService登録完了 - ROI→スクリーン座標変換（DWM Hybrid検出対応）");

        // トークンストレージ（Windows Credential Manager）
        services.AddSingleton<Baketa.Core.Abstractions.Auth.ITokenStorage,
            Baketa.Infrastructure.Platform.Windows.Credentials.WindowsCredentialStorage>();
        Console.WriteLine("✅ WindowsCredentialStorage登録完了 - 認証トークンの安全な永続化");

        // [Issue #497] カーソル状態プロバイダー（ファントムカーソル用）
        services.AddSingleton<Baketa.Core.Abstractions.Services.ICursorStateProvider,
            Baketa.Infrastructure.Platform.Windows.Services.CursorStateProvider>();

        // その他のWindows API関連サービス
        // 例: services.AddSingleton<IWindowsProcessService, WindowsProcessService>();
        // 例: services.AddSingleton<IHotkeyService, Win32HotkeyService>();
        // 例: services.AddSingleton<IClipboardService, WindowsClipboardService>();

        // Windows固有の設定サービス
        // 例: services.AddSingleton<IWindowsRegistryService, WindowsRegistryService>();

        // アプリケーション起動関連
        // 例: services.AddSingleton<IStartupManager, WindowsStartupManager>();
    }

    /// <summary>
    /// Phase3: Windows固有のリソース監視サービスを登録します
    /// PerformanceCounterとWMIを使用したシステムリソース監視
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    private static void RegisterResourceMonitoringServices(IServiceCollection services)
    {
        Console.WriteLine("🔧 [PHASE3 Platform] Windows リソース監視サービス登録開始");

        // Windows固有リソース監視実装を登録
        services.AddSingleton<Baketa.Core.Abstractions.Monitoring.IResourceMonitor,
            Baketa.Infrastructure.Platform.Windows.Monitoring.WindowsSystemResourceMonitor>();
        Console.WriteLine("✅ [PHASE3 Platform] WindowsSystemResourceMonitor登録完了 - パフォーマンスカウンター統合");

        Console.WriteLine("🎉 [PHASE3 Platform] Windows リソース監視サービス登録完了");
    }

    /// <summary>
    /// Phase3: ハイブリッドリソース管理システム登録（循環依存解決済み）
    /// IResourceMonitor依存を解決できるPlatformModuleで登録
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    private static void RegisterHybridResourceManagementServices(IServiceCollection services)
    {
        Console.WriteLine("🔧 [PHASE3 Platform] ハイブリッドリソース管理システム登録開始（循環依存解決済み）");

        // HybridResourceSettings の設定バインディング（Phase 3: ホットリロード対応）
        services.Configure<Baketa.Infrastructure.ResourceManagement.HybridResourceSettings>(
            config =>
            {
                var serviceProvider = services.BuildServiceProvider();
                var configuration = serviceProvider.GetService<IConfiguration>();

                if (configuration != null)
                {
                    configuration.GetSection("HybridResourceManagement").Bind(config);
                }
                else
                {
                    // フォールバック設定（Phase 3拡張）
                    config.OcrChannelCapacity = 100;
                    config.TranslationChannelCapacity = 50;
                    config.InitialOcrParallelism = 2;
                    config.MaxOcrParallelism = 4;
                    config.InitialTranslationParallelism = 1;
                    config.MaxTranslationParallelism = 2;
                    config.EnableDynamicParallelism = true;
                    config.EnableDetailedLogging = false;
                    config.EnableVerboseLogging = true; // Phase 3.2テスト: VRAM監視ログ有効化
                    config.EnableHotReload = true; // Phase 3
                    config.ConfigurationPollingIntervalMs = 5000; // Phase 3
                    Console.WriteLine("⚠️ [PHASE3 Platform] フォールバック設定を使用（ホットリロード機能付き）");
                }
            });

        // HybridResourceManager をシングルトンとして登録（Phase 3: IOptionsMonitor対応）
        services.AddSingleton<Baketa.Infrastructure.ResourceManagement.IResourceManager>(provider =>
        {
            var resourceMonitor = provider.GetRequiredService<IResourceMonitor>();
            var optionsMonitor = provider.GetRequiredService<IOptionsMonitor<Baketa.Infrastructure.ResourceManagement.HybridResourceSettings>>();
            var logger = provider.GetRequiredService<ILogger<Baketa.Infrastructure.ResourceManagement.HybridResourceManager>>();
            var gpuEnvironmentDetector = provider.GetService<Baketa.Core.Abstractions.GPU.IGpuEnvironmentDetector>();

            logger.LogInformation("🎯 [PHASE3 Platform] HybridResourceManager初期化 - ホットリロード対応VRAM検出: {GpuDetectorAvailable}",
                gpuEnvironmentDetector != null);

            return new Baketa.Infrastructure.ResourceManagement.HybridResourceManager(resourceMonitor, optionsMonitor, logger, gpuEnvironmentDetector);
        });

        Console.WriteLine("✅ [PHASE3 Platform] HybridResourceManager 登録完了 - ホットリロード対応動的リソース制御システム");
        Console.WriteLine("ℹ️ [PHASE3 Platform] IResourceMonitor依存は同一モジュール内で解決済み");
        Console.WriteLine("🎉 [PHASE3 Platform] ハイブリッドリソース管理システム登録完了（循環依存解決済み）");
    }

    /// <summary>
    /// このモジュールが依存する他のモジュールの型を取得します。
    /// </summary>
    /// <returns>依存モジュールの型のコレクション</returns>
    public override IEnumerable<Type> GetDependentModules()
    {
        yield return typeof(CoreModule);
        // Phase3: ResourceMonitoringSettingsの依存のためInfrastructureModuleに依存
        yield return typeof(Baketa.Infrastructure.DI.Modules.InfrastructureModule);
    }
}
