using Baketa.Application.DI.Modules;
using Baketa.Core.Abstractions.DI;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.DI;
using Baketa.Core.DI.Attributes;
using Baketa.UI.ViewModels;
using Baketa.UI.ViewModels.Auth;
using Baketa.UI.Services;
using Baketa.UI.Security;
using Baketa.UI.DI.Modules;
using Baketa.UI.DI.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

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
            
            // 翻訳フローイベントプロセッサーを登録
            services.AddSingleton<TranslationFlowEventProcessor>();
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
            
            // 操作UI ViewModel（依存関係あり）
            services.AddSingleton<Baketa.UI.ViewModels.Controls.OperationalControlViewModel>();
            
            // 設定ビューモデル
            services.AddSingleton<AccessibilitySettingsViewModel>();
            services.AddSingleton<LanguagePairsViewModel>();
            services.AddSingleton<SimpleSettingsViewModel>(provider =>
            {
                var eventAggregator = provider.GetRequiredService<IEventAggregator>();
                var logger = provider.GetRequiredService<ILogger<SimpleSettingsViewModel>>();
                
                // TranslationOrchestrationServiceを必須サービスとして取得
                Baketa.Application.Services.Translation.TranslationOrchestrationService? translationOrchestrationService = null;
                try
                {
                    Console.WriteLine($"🔍 [DI_DEBUG] SimpleSettingsViewModel作成 - TranslationOrchestrationService取得開始");
                    
                    // より具体的な型で取得を試行
                    var serviceDescriptor = provider.GetService(typeof(Baketa.Application.Services.Translation.TranslationOrchestrationService));
                    Console.WriteLine($"🔍 [DI_DEBUG] ServiceDescriptor結果: {serviceDescriptor?.GetType().Name ?? "null"}");
                    
                    translationOrchestrationService = provider.GetRequiredService<Baketa.Application.Services.Translation.TranslationOrchestrationService>();
                    Console.WriteLine($"🔧 [DI_DEBUG] SimpleSettingsViewModel作成 - TranslationOrchestrationService: {translationOrchestrationService?.GetType().Name ?? "null"}");
                    Console.WriteLine($"🔍 [DI_DEBUG] TranslationOrchestrationService取得成功 - Hash: {translationOrchestrationService?.GetHashCode() ?? -1}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"💥 [DI_DEBUG] SimpleSettingsViewModel作成 - TranslationOrchestrationService取得失敗: {ex.GetType().Name}: {ex.Message}");
                    Console.WriteLine($"💥 [DI_DEBUG] スタックトレース: {ex.StackTrace}");
                    translationOrchestrationService = null;
                }
                
                // ISettingsServiceを取得
                Baketa.Core.Services.ISettingsService? settingsService = null;
                try
                {
                    settingsService = provider.GetRequiredService<Baketa.Core.Services.ISettingsService>();
                    Console.WriteLine($"🔧 [DI_DEBUG] SimpleSettingsViewModel作成 - ISettingsService: {settingsService.GetType().Name}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"💥 [DI_DEBUG] SimpleSettingsViewModel作成 - ISettingsService取得失敗: {ex.Message}");
                    settingsService = null;
                }
                
                return new SimpleSettingsViewModel(eventAggregator, logger, translationOrchestrationService, settingsService);
            });
            
            // 認証ビューモデル
            services.AddTransient<LoginViewModel>();
            services.AddTransient<SignupViewModel>();
            
            // メインウィンドウビューモデル
            services.AddSingleton<MainWindowViewModel>();
            
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
            
            // オーバーレイ関連サービス
            services.AddTransient<Baketa.UI.Overlay.AvaloniaOverlayWindowAdapter>();
            
            // オーバーレイ位置管理システム
            OverlayPositioningModule.RegisterServices(services);
            
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
