using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Baketa.UI.ViewModels;
using Baketa.UI.ViewModels.Controls;
using Baketa.UI.Services;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

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
    private static void RegisterUISpecificServices(IServiceCollection services, IConfiguration? configuration)
    {
        // 翻訳エンジン状態監視サービス（モック実装）
        services.AddSingleton<ITranslationEngineStatusService, MockTranslationEngineStatusService>();
        
        // その他のUIサービス
        // 例: services.AddSingleton<INotificationService, NotificationService>();
        // 例: services.AddSingleton<IDialogService, DialogService>();
        // 例: services.AddSingleton<IClipboardService, ClipboardService>();
    }
    
    /// <summary>
    /// ビューモデルの登録
    /// </summary>
    private static void RegisterViewModels(IServiceCollection services)
    {
        // メインビューモデル
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<AccessibilitySettingsViewModel>();
        services.AddTransient<LanguagePairsViewModel>();
        
        // コントロール用ビューモデル
        services.AddTransient<OperationalControlViewModel>();
        
        // その他のビューモデル
        // 例: services.AddTransient<MainWindowViewModel>();
        // 例: services.AddTransient<OverlayViewModel>();
    }
    
    /// <summary>
    /// UI関連のイベントハンドラーを登録
    /// </summary>
    private static void RegisterUIEventHandlers(IServiceCollection services)
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
// CA1852: サブタイプがない場合はsealedにできます
internal sealed class MockTranslationEngineStatusService : ITranslationEngineStatusService
{
    private readonly ILogger<MockTranslationEngineStatusService> _logger;

    public MockTranslationEngineStatusService(ILogger<MockTranslationEngineStatusService> logger)
    {
        _logger = logger;
    }

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
        var status = new TranslationEngineStatus();
        status.IsOnline = true;
        status.IsHealthy = true;
        status.RemainingRequests = 1000;
        return status;
    }
    
    private static TranslationEngineStatus CreateMockCloudEngineStatus()
    {
        var status = new TranslationEngineStatus();
        status.IsOnline = true;
        status.IsHealthy = true;
        status.RemainingRequests = 100;
        return status;
    }
    
    private static NetworkConnectionStatus CreateMockNetworkStatus()
    {
        var status = new NetworkConnectionStatus();
        status.IsConnected = true;
        status.LatencyMs = 50;
        return status;
    }
}
