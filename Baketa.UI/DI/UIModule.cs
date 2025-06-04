using Baketa.UI.Framework;
using Baketa.UI.Framework.Events;
using Baketa.UI.ViewModels;
using Baketa.UI.ViewModels.Settings;
using Baketa.UI.Services;
using Baketa.UI.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.DI;

    /// <summary>
    /// UI関連の依存性注入を設定するモジュール
    /// </summary>
    internal static class UIModule
    {
        /// <summary>
        /// UIサービスを登録します
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <param name="configuration">設定</param>
        public static void RegisterUIServices(this IServiceCollection services, IConfiguration? configuration = null)
        {
            // イベント集約器
            services.AddSingleton<IEventAggregator, EventAggregator>();
            
            // ビューモデル
            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton<HomeViewModel>();
            services.AddSingleton<CaptureViewModel>();
            services.AddSingleton<TranslationViewModel>();
            services.AddSingleton<OverlayViewModel>();
            services.AddSingleton<HistoryViewModel>();
            
            // 翻訳設定UI関連サービス
            if (configuration != null)
            {
                services.AddTranslationSettingsUI(configuration);
            }
            else
            {
                // 設定なしの場合はテスト用設定で登録
                services.AddTranslationSettingsUIForTesting();
                services.AddTranslationSettingsViewModels();
            }
            
            // UI関連サービス（今後拡張予定）
            // services.AddSingleton<IDialogService, DialogService>();
            // services.AddSingleton<INotificationService, NotificationService>();
        }
    }
