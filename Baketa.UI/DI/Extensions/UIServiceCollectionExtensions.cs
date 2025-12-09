#pragma warning disable CS0618 // Type or member is obsolete
using System;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Application.Services.Translation;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Platform.Windows.Adapters;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Abstractions.UI;
using Baketa.Core.Events.EventTypes;
using Baketa.Core.Services;
// NOTE: [PP-OCRv5削除] BatchProcessing参照削除
using Baketa.UI.DI.Modules;
using Baketa.UI.Framework.Events; // 🔥 [DI_FIX] StartTranslationRequestEvent用
using Baketa.UI.Services;
using Baketa.UI.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.DI.Extensions;

/// <summary>
/// UIサービス登録の拡張メソッド
/// </summary>
internal static class UIServiceCollectionExtensions
{
    /// <summary>
    /// UIサービスとビューモデルを登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <param name="configuration">設定（オプション）</param>
    /// <returns>更新されたサービスコレクション</returns>
    public static IServiceCollection RegisterUIServices(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        // UIサービスの登録
        RegisterUISpecificServices(services, configuration);

        // ビューモデルの登録
        RegisterViewModels(services);

        // UI関連のイベントハンドラーの登録
        RegisterUIEventHandlers(services);

        return services;
    }

    /// <summary>
    /// UI固有のサービスを登録
    /// </summary>
    private static void RegisterUISpecificServices(IServiceCollection services, IConfiguration? _)
    {
        // 設定関連サービスの登録
        services.AddSettingsServices();

        // 📢 広告関連サービスの登録（Issue #174: WebView統合）
        // AdvertisementServiceの依存関係を先に登録
        services.AddSingleton<Baketa.UI.Services.IUserPlanService, Baketa.UI.Services.UserPlanService>();

        // 広告サービス本体
        services.AddSingleton<Baketa.Core.Abstractions.Services.IAdvertisementService, Baketa.UI.Services.AdvertisementService>();

        // 翻訳エンジン状態監視サービス（モック実装）
        services.AddSingleton<ITranslationEngineStatusService, MockTranslationEngineStatusService>();

        // 翻訳結果オーバーレイマネージャーは削除済み（ARシステムに置き換え）

        // ローディングオーバーレイマネージャー
        services.AddSingleton<LoadingOverlayManager>();

        // NOTE: [PP-OCRv5削除] NoOpBatchOcrProcessorを登録
        // Surya OCRに移行したため、バッチ処理インターフェースはNo-Op実装を使用
        services.AddSingleton<IBatchOcrProcessor, Baketa.Infrastructure.OCR.Services.NoOpBatchOcrProcessor>();

        // IOcrFailureManagerインターフェース登録（NoOpBatchOcrProcessorと同じインスタンス）
        services.AddSingleton<IOcrFailureManager>(provider =>
            provider.GetRequiredService<IBatchOcrProcessor>() as IOcrFailureManager
            ?? throw new InvalidOperationException("IBatchOcrProcessor must implement IOcrFailureManager"));

        // 翻訳フロー統合イベントプロセッサー
        services.AddSingleton<TranslationFlowEventProcessor>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<TranslationFlowEventProcessor>>();
            var eventAggregator = provider.GetRequiredService<IEventAggregator>();
            // 🔧 [OVERLAY_UNIFICATION] IInPlaceTranslationOverlayManager → IOverlayManager に統一
            var overlayManager = provider.GetRequiredService<Baketa.Core.Abstractions.UI.Overlays.IOverlayManager>();
            var captureService = provider.GetRequiredService<ICaptureService>();
            var translationService = provider.GetRequiredService<ITranslationOrchestrationService>();
            var settingsService = provider.GetRequiredService<ISettingsService>();
            var ocrEngine = provider.GetRequiredService<IOcrEngine>();
            var windowManager = provider.GetRequiredService<IWindowManagerAdapter>();
            var ocrFailureManager = provider.GetRequiredService<IOcrFailureManager>(); // クリーンアーキテクチャ準拠
            var processingStrategies = provider.GetRequiredService<IEnumerable<Baketa.Core.Abstractions.Processing.IProcessingStageStrategy>>(); // 🔥 [STOP_FIX]

            return new TranslationFlowEventProcessor(
                logger,
                eventAggregator,
                overlayManager,
                captureService,
                translationService,
                settingsService,
                ocrEngine,
                windowManager,
                ocrFailureManager,
                processingStrategies); // 🔥 [STOP_FIX] Strategy集合を渡す
        });

        // 🔥 [DI_FIX] EventAggregatorがIEventProcessor<>で取得できるようにインターフェース登録を追加
        services.AddSingleton<IEventProcessor<StartTranslationRequestEvent>>(provider =>
            provider.GetRequiredService<TranslationFlowEventProcessor>());
        services.AddSingleton<IEventProcessor<StopTranslationRequestEvent>>(provider =>
            provider.GetRequiredService<TranslationFlowEventProcessor>());

        // メインオーバーレイViewModel
        services.AddSingleton<Baketa.UI.ViewModels.MainOverlayViewModel>();

        // フォント管理サービス
        services.AddSingleton<IFontManagerService, FontManagerService>();

        // その他のUIサービス
        // 例: services.AddSingleton<INotificationService, NotificationService>();
        // 例: services.AddSingleton<IDialogService, DialogService>();
        // 例: services.AddSingleton<IClipboardService, ClipboardService>();
    }

    /// <summary>
    /// ビューモデルの登録
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    private static void RegisterViewModels(IServiceCollection services)
    {
        // 📢 広告ViewModel登録（Issue #174: WebView統合）
        services.AddTransient<Baketa.UI.ViewModels.AdViewModel>();

        // その他のビューモデル
        // 例: services.AddTransient<MainWindowViewModel>();
        // 例: services.AddTransient<OverlayViewModel>();
    }

    /// <summary>
    /// UI関連のイベントハンドラーを登録
    /// </summary>
    private static void RegisterUIEventHandlers(IServiceCollection _)
    {
        // UIイベントプロセッサー
        // 例: services.AddSingleton<ThemeChangedEventProcessor>();
        // 例: services.AddSingleton<LanguageChangedEventProcessor>();

        // 現時点では具体的なイベントハンドラーはコメントアウト
        // 必要に応じて実装時に追加
    }
}

/// <summary>
/// モック翻訳エンジン状態監視サービス（一時的な実装）
/// </summary>
/// <param name="logger">ロガー</param>
// CA1852: サブタイプがない場合はsealedにできます
internal sealed class MockTranslationEngineStatusService(ILogger<MockTranslationEngineStatusService> logger) : ITranslationEngineStatusService
{
    private readonly ILogger<MockTranslationEngineStatusService> _logger = logger;

    // CA1805: プロパティをauto-implementedで初期化し、明示的な初期化を省略
    public TranslationEngineStatus LocalEngineStatus { get; } = CreateMockLocalEngineStatus();
    public TranslationEngineStatus CloudEngineStatus { get; } = CreateMockCloudEngineStatus();
    public NetworkConnectionStatus NetworkStatus { get; } = CreateMockNetworkStatus();
    public FallbackInfo? LastFallback { get; }

    public IObservable<TranslationEngineStatusUpdate> StatusUpdates =>
        System.Reactive.Linq.Observable.Empty<TranslationEngineStatusUpdate>();

    public Task StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("モック状態監視サービスを開始しました");
        return Task.CompletedTask;
    }

    public Task StopMonitoringAsync()
    {
        _logger.LogDebug("モック状態監視サービスを停止しました");
        return Task.CompletedTask;
    }

    public Task RefreshStatusAsync()
    {
        _logger.LogDebug("モック状態監視サービスを更新しました");
        return Task.CompletedTask;
    }

    private static TranslationEngineStatus CreateMockLocalEngineStatus()
    {
        var status = new TranslationEngineStatus
        {
            IsOnline = true,
            IsHealthy = true,
            RemainingRequests = 1000
        };
        return status;
    }

    private static TranslationEngineStatus CreateMockCloudEngineStatus()
    {
        var status = new TranslationEngineStatus
        {
            IsOnline = true,
            IsHealthy = true,
            RemainingRequests = 100
        };
        return status;
    }

    private static NetworkConnectionStatus CreateMockNetworkStatus()
    {
        var status = new NetworkConnectionStatus
        {
            IsConnected = true,
            LatencyMs = 50
        };
        return status;
    }
}
