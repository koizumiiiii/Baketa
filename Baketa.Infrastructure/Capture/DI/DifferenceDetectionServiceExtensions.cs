using System;
using Baketa.Core.Abstractions.Capture;
using Baketa.Infrastructure.Capture.DifferenceDetection;
using Microsoft.Extensions.DependencyInjection;

namespace Baketa.Infrastructure.Capture.DI;

    /// <summary>
    /// 差分検出サービスの依存性注入拡張
    /// </summary>
    public static class DifferenceDetectionServiceExtensions
    {
        /// <summary>
        /// 差分検出サービスを登録します
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <returns>更新されたサービスコレクション</returns>
        public static IServiceCollection AddDifferenceDetectionServices(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services, nameof(services));
                
            // 各アルゴリズムの登録
            services.AddSingleton<HistogramDifferenceAlgorithm>();
            services.AddSingleton<SamplingDifferenceAlgorithm>();
            services.AddSingleton<EdgeDifferenceAlgorithm>();
            services.AddSingleton<BlockDifferenceAlgorithm>();
            services.AddSingleton<PixelDifferenceAlgorithm>();
            services.AddSingleton<HybridDifferenceAlgorithm>();
            
            // アルゴリズムをIDetectionAlgorithmとしても登録
            services.AddSingleton<IDetectionAlgorithm>(sp => sp.GetRequiredService<HistogramDifferenceAlgorithm>());
            services.AddSingleton<IDetectionAlgorithm>(sp => sp.GetRequiredService<SamplingDifferenceAlgorithm>());
            services.AddSingleton<IDetectionAlgorithm>(sp => sp.GetRequiredService<EdgeDifferenceAlgorithm>());
            services.AddSingleton<IDetectionAlgorithm>(sp => sp.GetRequiredService<BlockDifferenceAlgorithm>());
            services.AddSingleton<IDetectionAlgorithm>(sp => sp.GetRequiredService<PixelDifferenceAlgorithm>());
            services.AddSingleton<IDetectionAlgorithm>(sp => sp.GetRequiredService<HybridDifferenceAlgorithm>());
            
            // メインの差分検出サービスを登録
            services.AddSingleton<IDifferenceDetector, EnhancedDifferenceDetector>();
            
            // デバッグ用可視化ツールを登録
            services.AddSingleton<DifferenceVisualizerTool>();
            
            return services;
        }
    }
