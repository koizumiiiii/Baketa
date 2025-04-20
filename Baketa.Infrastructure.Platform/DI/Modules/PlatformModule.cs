using Baketa.Core.Abstractions.DI;
using Baketa.Core.DI;
using Baketa.Core.DI.Attributes;
using Baketa.Core.DI.Modules;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;

namespace Baketa.Infrastructure.Platform.DI.Modules
{
    /// <summary>
    /// プラットフォーム固有のサービスを登録するモジュール。
    /// Windowsプラットフォーム固有の実装が含まれます。
    /// </summary>
    [ModulePriority(ModulePriority.Platform)]
    public class PlatformModule : ServiceModuleBase
    {
        /// <summary>
        /// プラットフォーム固有サービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        public override void RegisterServices(IServiceCollection services)
        {
            // Windowsプラットフォーム固有の実装を登録
            if (OperatingSystem.IsWindows())
            {
                // キャプチャサービス
                RegisterCaptureServices(services);
                
                // 画像処理サービス
                RegisterImageServices(services);
                
                // その他のWindows固有サービス
                RegisterWindowsServices(services);
            }
            else
            {
                // 現在はWindows専用
                Console.WriteLine("警告: Baketaは現在Windowsプラットフォームのみをサポートしています。");
            }
        }

        /// <summary>
        /// キャプチャ関連サービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private void RegisterCaptureServices(IServiceCollection services)
        {
            // スクリーンキャプチャなどの登録
            // 例: services.AddSingleton<IWindowsCaptureService, WindowsCaptureService>();
            
            // 現時点では実際の実装はプレースホルダー
        }
        
        /// <summary>
        /// 画像処理サービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private void RegisterImageServices(IServiceCollection services)
        {
            // Windows画像処理関連の登録
            // 例: services.AddSingleton<IWindowsImageFactory, WindowsImageFactory>();
            
            // 現時点では実際の実装はプレースホルダー
        }
        
        /// <summary>
        /// その他のWindows固有サービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private void RegisterWindowsServices(IServiceCollection services)
        {
            // その他のWindows API関連サービス
            // 例: services.AddSingleton<IWindowsOverlayService, WindowsOverlayService>();
            
            // 現時点では実際の実装はプレースホルダー
        }
        
        /// <summary>
        /// このモジュールが依存する他のモジュールの型を取得します。
        /// </summary>
        /// <returns>依存モジュールの型のコレクション</returns>
        public override IEnumerable<Type> GetDependentModules()
        {
            yield return typeof(CoreModule);
            // InfrastructureModuleはまだ使用できないため、直接CoreModuleに依存
        }
    }
}