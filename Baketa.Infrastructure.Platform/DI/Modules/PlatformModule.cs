using Baketa.Core.Abstractions.DI;
using Baketa.Core.DI;
using Baketa.Core.DI.Attributes;
using Baketa.Core.DI.Modules;
using Baketa.Infrastructure.Platform.Resources;
using Baketa.Infrastructure.Platform.Windows.OpenCv;
using Baketa.Infrastructure.Platform.DI.Modules;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;

namespace Baketa.Infrastructure.Platform.DI.Modules;

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
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303:Do not pass literals as localized parameters",
            Justification = "プラットフォーム警告に静的リソースを使用")]
        public override void RegisterServices(IServiceCollection services)
        {
            // Windowsプラットフォーム固有の実装を登録
            if (OperatingSystem.IsWindows())
            {
                // キャプチャサービス
                RegisterCaptureServices(services);
                
                // フルスクリーンサービス
                services.AddFullscreenServices();
                
                // 画像処理サービス
                RegisterImageServices(services);
                
                // UI関連のWindowsサービス
                RegisterWindowsUIServices(services);
                
                // その他のWindows固有サービス
                RegisterWindowsServices(services);
            }
            else
            {
                // 現在はWindows専用
                Console.WriteLine(Resources.ModuleResources.PlatformWarning);
            }
        }

        /// <summary>
        /// キャプチャ関連サービスを登録します。
        /// </summary>
        /// <param name="_">サービスコレクション</param>
        private static void RegisterCaptureServices(IServiceCollection _)
        {
            // スクリーンキャプチャサービス
            // 例: services.AddSingleton<IWindowsCaptureService, WindowsCaptureService>();
            // 例: services.AddSingleton<IWindowEnumerator, WindowEnumerator>();
            // 例: services.AddSingleton<IWindowFinder, WindowFinder>();
            
            // キャプチャ設定
            // 例: services.AddSingleton<ICaptureSettings, DefaultCaptureSettings>();
            
            // 差分検出
            // 例: services.AddSingleton<IDifferenceDetector, OpenCvDifferenceDetector>();
        }
        
        /// <summary>
        /// 画像処理サービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterImageServices(IServiceCollection services)
        {
            // Windows画像処理関連の登録
            // 例: services.AddSingleton<IWindowsImageFactory, WindowsImageFactory>();
            // 例: services.AddSingleton<IImageConverter, WindowsImageConverter>();
            
            // OpenCV関連
            // 拡張メソッドを使用して登録
            services.AddOpenCvServices();
            
            // ファクトリー
            // 例: services.AddSingleton<IImageFactory>(sp => sp.GetRequiredService<DefaultImageFactory>());
        }
        
        /// <summary>
        /// Windows UI関連サービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterWindowsUIServices(IServiceCollection services)
        {
            // オーバーレイ関連
            services.RegisterOverlayServices();
            
            // マルチモニターサポート
            services.AddMultiMonitorSupport();
            
            // その他のUI関連サービス
            // 例: services.AddSingleton<IWindowsNotificationService, WindowsNotificationService>();
            
            // システムトレイ
            // 例: services.AddSingleton<ISystemTrayService, Win32SystemTrayService>();
        }
        
        /// <summary>
        /// その他のWindows固有サービスを登録します。
        /// </summary>
        /// <param name="_">サービスコレクション</param>
        private static void RegisterWindowsServices(IServiceCollection _)
        {
            // その他のWindows API関連サービス
            // 例: services.AddSingleton<IWindowsProcessService, WindowsProcessService>();
            // 例: services.AddSingleton<IHotkeyService, Win32HotkeyService>();
            // 例: services.AddSingleton<IClipboardService, WindowsClipboardService>();
            
            // Windows固有の設定サービス
            // 例: services.AddSingleton<IWindowsRegistryService, WindowsRegistryService>();
            
            // アプリケーション起動関連
            // 例: services.AddSingleton<IStartupManager, WindowsStartupManager>();
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
