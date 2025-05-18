using System;
using Baketa.Core.Abstractions.OCR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.Platform.Windows.OpenCv;

    /// <summary>
    /// OpenCVサービスの登録に関する拡張メソッドを提供します
    /// </summary>
    public static class OpenCvServiceExtensions
    {
        /// <summary>
        /// OpenCVサービスを依存性注入コンテナに登録します
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <param name="configureOptions">オプション設定アクション（省略可）</param>
        /// <returns>サービスコレクション</returns>
        public static IServiceCollection AddOpenCvServices(
            this IServiceCollection services, 
            Action<OpenCvOptions>? configureOptions = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            if (configureOptions != null)
            {
                services.Configure(configureOptions);
            }
            else
            {
                services.Configure<OpenCvOptions>(_ => { /* デフォルト設定を適用 */ });
            }
            
            // 基本サービスの登録
            services.TryAddSingleton<IWindowsOpenCvLibrary, WindowsOpenCvLibrary>();
            services.TryAddSingleton<WindowsOpenCvWrapper>();
            
            // アダプターの登録
            services.TryAddSingleton<OpenCvWrapperAdapter>();
            services.TryAddSingleton<OcrOpenCvWrapperAdapter>();
            
            // インターフェース実装の登録
            services.TryAddSingleton<Baketa.Core.Abstractions.Imaging.IOpenCvWrapper>(sp => 
                sp.GetRequiredService<OpenCvWrapperAdapter>());
                
            services.TryAddSingleton<Baketa.Core.Abstractions.OCR.IOpenCvWrapper>(sp => 
                sp.GetRequiredService<OcrOpenCvWrapperAdapter>());
            
            return services;
        }
    }

    /// <summary>
    /// OpenCV機能の設定オプション
    /// </summary>
    public class OpenCvOptions
    {
        /// <summary>
        /// 画像処理のデフォルトスレッド数 (0の場合はシステムが自動設定)
        /// </summary>
        public int DefaultThreadCount { get; set; }
        
        /// <summary>
        /// MSER検出のデフォルトパラメータ
        /// </summary>
        public TextDetectionParams DefaultMserParameters { get; set; } = TextDetectionParams.CreateForMethod(Baketa.Core.Abstractions.OCR.TextDetectionMethod.Mser);
        
        /// <summary>
        /// 連結成分検出のデフォルトパラメータ
        /// </summary>
        public TextDetectionParams DefaultConnectedComponentsParameters { get; set; } = TextDetectionParams.CreateForMethod(Baketa.Core.Abstractions.OCR.TextDetectionMethod.ConnectedComponents);
        
        /// <summary>
        /// 輪郭検出のデフォルトパラメータ
        /// </summary>
        public TextDetectionParams DefaultContoursParameters { get; set; } = TextDetectionParams.CreateForMethod(Baketa.Core.Abstractions.OCR.TextDetectionMethod.Contours);
        
        /// <summary>
        /// エッジベース検出のデフォルトパラメータ
        /// </summary>
        public TextDetectionParams DefaultEdgeBasedParameters { get; set; } = TextDetectionParams.CreateForMethod(Baketa.Core.Abstractions.OCR.TextDetectionMethod.EdgeBased);
    }
