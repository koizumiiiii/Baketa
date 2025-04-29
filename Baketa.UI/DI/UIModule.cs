using Baketa.UI.Framework;
using Baketa.UI.Framework.Events;
using Baketa.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.DI
{
    /// <summary>
    /// UI関連の依存性注入を設定するモジュール
    /// </summary>
    internal static class UIModule
    {
        /// <summary>
        /// UIサービスを登録します
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        public static void RegisterUIServices(this IServiceCollection services)
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
            
            // UI関連サービス（今後拡張予定）
            // services.AddSingleton<IDialogService, DialogService>();
            // services.AddSingleton<INotificationService, NotificationService>();
        }
    }
}