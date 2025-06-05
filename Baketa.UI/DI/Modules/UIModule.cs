using Baketa.Application.DI.Modules;
using Baketa.Core.Abstractions.DI;
using Baketa.Core.DI;
using Baketa.Core.DI.Attributes;
using Baketa.UI.ViewModels;
using Baketa.UI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
            
            // 設定系UIの登録
            RegisterSettingsUI(services);
        }

        /// <summary>
        /// ViewModelを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterViewModels(IServiceCollection services)
        {
            // メインViewModelとオーバーレイViewModel
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<MainWindowViewModel>(); // デザイナー用
            // 例: services.AddSingleton<OverlayViewModel>();
            // 例: services.AddSingleton<SystemTrayViewModel>();
            
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
            
            // 将来的に実装される予定の内容：
            
            // ダイアログサービスやナビゲーションサービス
            // 例: services.AddSingleton<IDialogService, AvaloniaDialogService>();
            // 例: services.AddSingleton<INotificationService, AvaloniaNotificationService>();
            
            // ページ遷移とナビゲーション
            // 例: services.AddSingleton<INavigationService, AvaloniaNavigationService>();
            // 例: services.AddSingleton<IPageService, AvaloniaPageService>();
            
            // ウィンドウ管理
            // 例: services.AddSingleton<IWindowService, AvaloniaWindowService>();
            
            // UIヘルパー
            // 例: services.AddSingleton<IClipboardService, AvaloniaClipboardService>();
        }
        
        /// <summary>
        /// 設定系UIを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterSettingsUI(IServiceCollection services)
        {
            // 設定ViewModelと関連サービス
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<AccessibilitySettingsViewModel>();
            services.AddTransient<LanguagePairsViewModel>();
            
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
