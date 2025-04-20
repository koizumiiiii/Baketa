using Baketa.Application.DI.Modules;
using Baketa.Core.Abstractions.DI;
using Baketa.Core.DI;
using Baketa.Core.DI.Attributes;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;

namespace Baketa.UI.DI.Modules
{
    /// <summary>
    /// UIレイヤーのサービスを登録するモジュール。
    /// ViewModelやUI関連サービスの実装が含まれます。
    /// </summary>
    [ModulePriority(ModulePriority.UI)]
    public class UIModule : ServiceModuleBase
    {
        /// <summary>
        /// UIサービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        public override void RegisterServices(IServiceCollection services)
        {
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
        private void RegisterViewModels(IServiceCollection services)
        {
            // メインViewModelとオーバーレイViewModel
            // 例: services.AddSingleton<MainViewModel>();
            // 例: services.AddSingleton<OverlayViewModel>();
            
            // 現時点では実際の実装はプレースホルダー
        }
        
        /// <summary>
        /// UI関連サービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private void RegisterUIServices(IServiceCollection services)
        {
            // ダイアログサービスやナビゲーションサービス
            // 例: services.AddSingleton<IDialogService, AvaloniaDialogService>();
            // 例: services.AddSingleton<INotificationService, AvaloniaNotificationService>();
            
            // 現時点では実際の実装はプレースホルダー
        }
        
        /// <summary>
        /// 設定系UIを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private void RegisterSettingsUI(IServiceCollection services)
        {
            // 設定ViewModelと関連サービス
            // 例: services.AddTransient<SettingsViewModel>();
            // 例: services.AddTransient<ProfileEditorViewModel>();
            
            // 現時点では実際の実装はプレースホルダー
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
}