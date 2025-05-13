using Baketa.Core.Abstractions.DI;
using Baketa.Infrastructure.Capture.DI;
using Microsoft.Extensions.DependencyInjection;

namespace Baketa.Infrastructure.DI.Modules
{
    /// <summary>
    /// キャプチャ関連サービスのDIモジュール
    /// </summary>
    public class CaptureModule : IServiceModule
    {
        /// <summary>
        /// キャプチャ関連サービスを登録します
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        public void RegisterServices(IServiceCollection services)
        {
            // 差分検出サービスを登録
            services.AddDifferenceDetectionServices();
            
            // その他のキャプチャ関連サービスの登録
            // ...
        }
    }
}