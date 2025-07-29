#pragma warning disable CS0618 // Type or member is obsolete
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Baketa.UI.ViewModels;
using Baketa.UI.ViewModels.Controls;
using Baketa.UI.Services;
using Baketa.UI.DI.Modules;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Services;
using Baketa.Application.Services.Translation;
using Baketa.Core.Abstractions.Services;
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
    private static void RegisterUISpecificServices(IServiceCollection services, IConfiguration? _)
    {
        // 設定関連サービスの登録
        services.AddSettingsServices();
        
        // 翻訳エンジン状態監視サービス（モック実装）
        services.AddSingleton<ITranslationEngineStatusService, MockTranslationEngineStatusService>();
        
        // 翻訳結果オーバーレイマネージャー
        services.AddSingleton<TranslationResultOverlayManager>();
        
        // ローディングオーバーレイマネージャー
        services.AddSingleton<LoadingOverlayManager>();
        
        // 翻訳フロー統合イベントプロセッサー
        services.AddSingleton<TranslationFlowEventProcessor>();
        
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
    /// <param name="_">サービスコレクション（将来の拡張のため保持）</param>
    private static void RegisterViewModels(IServiceCollection _)
    {
        // ViewModelの登録はUIModuleで一元化するため、ここでは何も登録しない
        // UIModuleとの重複を避ける
        
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
