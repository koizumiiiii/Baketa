using System;
using System.Collections.Generic;
using Baketa.Application.DI.Modules;
using Baketa.Core.Abstractions.DI;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.DI;
using Baketa.Core.DI.Attributes;
using Baketa.UI.DI.Extensions;
using Baketa.UI.DI.Modules;
using Baketa.UI.Security;
using Baketa.UI.License.Adapters;
using Baketa.UI.Services;
using Baketa.UI.Services.Monitor;
using Baketa.UI.ViewModels;
using Baketa.UI.ViewModels.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using EventTypes = Baketa.Core.Events.EventTypes;

namespace Baketa.UI.DI.Modules;

/// <summary>
/// UIレイヤーのサービスを登録するモジュール。
/// ViewModelやUI関連サービスの実装が含まれます。
/// </summary>
[ModulePriority(ModulePriority.UI)]
internal sealed class UIModule : ServiceModuleBase
{
    /// <summary>
    /// UIサービスを登録します。
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    public override void RegisterServices(IServiceCollection services)
    {
        // プロダクション環境をデフォルトとして使用
        // 環境設定は必要に応じてサービスプロバイダーから取得

        // ViewModelの登録
        RegisterViewModels(services);

        // UI関連サービスの登録
        RegisterUIServices(services);

        // UIServiceCollectionExtensionsのサービス登録
        services.RegisterUIServices();

        // 設定系UIの登録
        RegisterSettingsUI(services);

        // 翻訳フローモジュールをDIコンテナに登録
        services.AddSingleton<TranslationFlowModule>();

        // 翻訳フローイベントプロセッサーは UIServiceCollectionExtensions で登録済み
    }

    /// <summary>
    /// ViewModelを登録します。
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    private static void RegisterViewModels(IServiceCollection services)
    {
        // 基本ビューモデル（依存関係なし）
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<CaptureViewModel>();
        services.AddSingleton<TranslationViewModel>();
        services.AddSingleton<OverlayViewModel>();
        services.AddSingleton<HistoryViewModel>();

        // 🔧 [Issue #170] ローディング画面ViewModel登録
        services.AddSingleton<Baketa.UI.ViewModels.LoadingViewModel>();

        // 🗑️ [CLEANUP] OperationalControlViewModel削除 - 未使用コンポーネントのため除去
        // services.AddSingleton<Baketa.UI.ViewModels.Controls.OperationalControlViewModel>();

        // 設定ビューモデル
        services.AddSingleton<AccessibilitySettingsViewModel>();
        services.AddSingleton<LanguagePairsViewModel>();
        // SimpleSettingsViewModel削除 - SettingsWindowViewModelに統合（SettingsModuleで登録）

        // 認証ビューモデル
        services.AddTransient<LoginViewModel>();
        services.AddTransient<SignupViewModel>();

        // 🔥 [PHASE2_PROBLEM2] MainWindowViewModel削除 - MainOverlayViewModelに統合完了
        // MainOverlayViewModelがPythonServerStatusChangedEventを処理し、IsTranslationEngineInitializingを制御

        // 翻訳仕様を同期するサービス
        // 例: services.AddSingleton<IViewModelSynchronizationService, ViewModelSynchronizationService>();

        // 状態管理
        // 例: services.AddSingleton<IApplicationStateService, ApplicationStateService>();

        // メッセージバス
        // 例: services.AddSingleton<IMessageBusService, MessageBusService>();
    }

    /// <summary>
    /// UI関連サービスを登録します。
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    private static void RegisterUIServices(IServiceCollection services)
    {
        // 翻訳エンジン状態監視サービス
        // IConfigurationは既にProgram.csで登録済みなので、ここではサービスのみ登録
        services.AddSingleton<ITranslationEngineStatusService, TranslationEngineStatusService>();

        // Issue #77: 後方互換性のためのIUserPlanServiceアダプタ登録
        // 新しいILicenseManagerをラップして既存のIUserPlanServiceインターフェースを提供
        services.AddSingleton<UserPlanServiceAdapter>();
        services.AddSingleton<IUserPlanService>(provider =>
            provider.GetRequiredService<UserPlanServiceAdapter>());
        services.AddSingleton<IDisposable>(provider =>
            provider.GetRequiredService<UserPlanServiceAdapter>());

        // 📢 広告サービスの登録（Issue #174: WebView統合）
        // IUserPlanServiceに依存するため、UserPlanServiceAdapter登録後に配置
        services.AddSingleton<Baketa.Core.Abstractions.Services.IAdvertisementService, AdvertisementService>();

        // ウィンドウ選択ダイアログサービス（UIレイヤー）
        services.AddSingleton<Baketa.Application.Services.UI.IWindowSelectionDialogService, WindowSelectionDialogService>();

        // 🔥 [ISSUE#171] エラー通知サービス（画面中央最下部にエラーメッセージを表示）
        services.AddSingleton<Baketa.Core.Abstractions.Services.IErrorNotificationService, ErrorNotificationService>();

        // OPUS-MT削除済み: NLLB-200統一により事前起動サービス不要


        // オーバーレイ関連サービス
        services.AddTransient<Baketa.UI.Overlay.AvaloniaOverlayWindowAdapter>();

        // オーバーレイ位置管理システム
        OverlayPositioningModule.RegisterServices(services);

        // Phase 4.1: オーバーレイFactoryパターン - オーバーレイ作成と表示の専門化
        services.AddSingleton<Baketa.UI.Factories.IInPlaceOverlayFactory, Baketa.UI.Factories.InPlaceOverlayFactory>();
        // 🔥 [PHASE3_REFACTORING] OverlayCoordinateTransformer, OverlayDiagnosticService, OverlayCollectionManager削除
        // SimpleInPlaceOverlayManagerに移行したため不要

        // 🖥️ [PHASE1_MONITOR] 高度モニター判定・DPI補正システム（Gemini推奨：Avalonia Screen API優先）
        services.AddSingleton<IAdvancedMonitorService, AdvancedMonitorService>();

        // マルチモニターUIサポート
        services.AddUIMultiMonitorSupport();

        // 将来的に実装される予定の内容：

        // ダイアログサービスやナビゲーションサービス
        // 例: services.AddSingleton<IDialogService, AvaloniaDialogService>();
        // 例: services.AddSingleton<INotificationService, AvaloniaNotificationService>();

        // ページ遷移とナビゲーション
        services.AddSingleton<INavigationService, AvaloniaNavigationService>();
        // 例: services.AddSingleton<IPageService, AvaloniaPageService>();

        // セキュリティサービス
        services.AddSingleton<LoginAttemptTracker>();
        services.AddSingleton<SecurityAuditLogger>();
        services.AddSingleton<SecureSessionManager>();
        services.AddSingleton<PasswordResetManager>();
        services.AddSingleton<HijackingDetectionManager>();
        services.AddSingleton<SecurityNotificationService>();
        services.AddSingleton<RecoveryWorkflowManager>();

        // 🔥 [ISSUE#163_PHASE5] SingleshotEventProcessor登録 - シングルショット翻訳実行
        services.AddSingleton<Baketa.UI.Services.SingleshotEventProcessor>();
        services.AddSingleton<IEventProcessor<Baketa.UI.Framework.Events.ExecuteSingleshotRequestEvent>>(
            provider => provider.GetRequiredService<Baketa.UI.Services.SingleshotEventProcessor>());

        // [Gemini Review] ComponentDownloadFailedEventProcessor登録 - ダウンロード失敗時の再起動通知
        services.AddSingleton<Baketa.UI.Services.ComponentDownloadFailedEventProcessor>();
        services.AddSingleton<IEventProcessor<Baketa.Core.Events.Setup.ComponentDownloadFailedEvent>>(
            provider => provider.GetRequiredService<Baketa.UI.Services.ComponentDownloadFailedEventProcessor>());

        // 🔐 [Issue #168] TokenExpirationHandler - トークン失効時の処理ハンドラー
        services.AddSingleton<TokenExpirationHandler>();

        // 🔔 [Issue #78 Phase 5] TokenUsageAlertService - トークン使用量80%/90%/100%警告通知
        services.AddSingleton<TokenUsageAlertService>();

        // ウィンドウ管理
        // 例: services.AddSingleton<IWindowService, AvaloniaWindowService>();

        // UIヘルパー
        // 例: services.AddSingleton<IClipboardService, AvaloniaClipboardService>();
    }

    /// <summary>
    /// 設定系UIを登録します。
    /// </summary>
    /// <param name="_">サービスコレクション（将来の拡張のため保持）</param>
    private static void RegisterSettingsUI(IServiceCollection _)
    {
        // 設定ViewModelと関連サービス（重複登録を削除し、必要最小限のみ）
        // AccessibilitySettingsViewModel は UIServiceCollectionExtensions で登録済み

        // 将来的に実装される予定の内容：
        // 例: services.AddTransient<GeneralSettingsViewModel>();
        // 例: services.AddTransient<OcrSettingsViewModel>();
        // 例: services.AddTransient<TranslationSettingsViewModel>();
        // 例: services.AddTransient<UISettingsViewModel>();
        // 例: services.AddTransient<HotkeySettingsViewModel>();
        // 例: services.AddTransient<ProfileEditorViewModel>();

        // 設定関連サービス
        // 例: services.AddSingleton<ISettingsUIService, SettingsUIService>();
        // 例: services.AddSingleton<IProfileEditorService, ProfileEditorService>();

        // リアルタイムプレビュー
        // 例: services.AddSingleton<IPreviewService, PreviewService>();
    }

    /// <summary>
    /// このモジュールが依存する他のモジュールの型を取得します。
    /// </summary>
    /// <returns>依存モジュールの型のコレクション</returns>
    public override IEnumerable<Type> GetDependentModules()
    {
        yield return typeof(ApplicationModule);
        // 他のモジュールはApplicationModuleを通じて間接的に依存
    }
}
