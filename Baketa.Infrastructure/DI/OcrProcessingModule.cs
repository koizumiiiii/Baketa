using System;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Pipeline;
using Baketa.Core.Abstractions.OCR.TextDetection;
using Baketa.Core.Abstractions.Dependency;
using Baketa.Infrastructure.Imaging.Filters;
using Baketa.Infrastructure.Imaging.Pipeline;
using Baketa.Infrastructure.OCR.TextDetection;
using Microsoft.Extensions.DependencyInjection;

namespace Baketa.Infrastructure.DI;

    /// <summary>
    /// OCR処理関連のサービス登録モジュール
    /// </summary>
    public class OcrProcessingModule : IServiceModule
    {
        /// <summary>
        /// サービスを登録します
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        public void RegisterServices(IServiceCollection services)
        {
            // パイプライン関連
            services.AddSingleton<IFilterFactory, FilterFactory>();
            services.AddTransient<IImagePipelineBuilder, ImagePipelineBuilder>();
            
            // テキスト検出関連 - 検出器ごとに登録するが、実行時に選択可能
            services.AddTransient<MserTextRegionDetector>();
            services.AddTransient<SwtTextRegionDetector>();
            
            // ファクトリーを通じて適切な検出器を選択できるようにする
            services.AddTransient<Func<string, ITextRegionDetector>>(sp => detectorType =>
            {
                return detectorType switch
                {
                    "mser" => sp.GetRequiredService<MserTextRegionDetector>(),
                    "swt" => sp.GetRequiredService<SwtTextRegionDetector>(),
                    _ => throw new ArgumentException($"不明な検出器タイプ: {detectorType}")
                };
            });
            
            // テキスト領域集約器
            services.AddTransient<ITextRegionAggregator, TextRegionAggregator>();
            
            // フィルター登録
            services.AddTransient<TextRegionDetectionFilter>();
            
            // パイプラインのイベントリスナー
            services.AddTransient<IPipelineEventListener, DefaultPipelineEventListener>();
        }
    }
    
    /// <summary>
    /// デフォルトのパイプラインイベントリスナー
    /// </summary>
    public class DefaultPipelineEventListener : IPipelineEventListener
    {
        /// <summary>
        /// パイプライン開始時に呼ばれます
        /// </summary>
        /// <param name="pipeline">パイプライン</param>
        /// <param name="input">入力画像</param>
        /// <returns>非同期タスク</returns>
        public Task OnPipelineStartAsync(IImagePipeline pipeline, IAdvancedImage input)
        {
            // 実装はプロジェクト要件に応じて拡張可能
            return Task.CompletedTask;
        }
        
        /// <summary>
        /// パイプライン完了時に呼ばれます
        /// </summary>
        /// <param name="pipeline">パイプライン</param>
        /// <param name="result">実行結果</param>
        /// <returns>非同期タスク</returns>
        public Task OnPipelineCompleteAsync(IImagePipeline pipeline, PipelineResult result)
        {
            // 実装はプロジェクト要件に応じて拡張可能
            return Task.CompletedTask;
        }
        
        /// <summary>
        /// パイプラインエラー時に呼ばれます
        /// </summary>
        /// <param name="pipeline">パイプライン</param>
        /// <param name="exception">例外</param>
        /// <param name="context">パイプラインコンテキスト</param>
        /// <returns>非同期タスク</returns>
        public Task OnPipelineErrorAsync(IImagePipeline pipeline, Exception exception, PipelineContext context)
        {
            // 実装はプロジェクト要件に応じて拡張可能
            return Task.CompletedTask;
        }
        
        /// <summary>
        /// ステップ開始時に呼ばれます
        /// </summary>
        /// <param name="pipelineStep">パイプラインステップ</param>
        /// <param name="input">入力画像</param>
        /// <param name="context">パイプラインコンテキスト</param>
        /// <returns>非同期タスク</returns>
        public Task OnStepStartAsync(IImagePipelineStep pipelineStep, IAdvancedImage input, PipelineContext context)
        {
            // 実装はプロジェクト要件に応じて拡張可能
            return Task.CompletedTask;
        }
        
        /// <summary>
        /// ステップ完了時に呼ばれます
        /// </summary>
        /// <param name="pipelineStep">パイプラインステップ</param>
        /// <param name="output">出力画像</param>
        /// <param name="context">パイプラインコンテキスト</param>
        /// <param name="elapsedMilliseconds">実行時間（ミリ秒）</param>
        /// <returns>非同期タスク</returns>
        public Task OnStepCompleteAsync(IImagePipelineStep pipelineStep, IAdvancedImage output, PipelineContext context, long elapsedMilliseconds)
        {
            // 実装はプロジェクト要件に応じて拡張可能
            return Task.CompletedTask;
        }
        
        /// <summary>
        /// ステップエラー時に呼ばれます
        /// </summary>
        /// <param name="pipelineStep">パイプラインステップ</param>
        /// <param name="exception">例外</param>
        /// <param name="context">パイプラインコンテキスト</param>
        /// <returns>代替画像（あれば）</returns>
        public Task<IAdvancedImage?> OnStepErrorAsync(IImagePipelineStep pipelineStep, Exception exception, PipelineContext context)
        {
            // 実装はプロジェクト要件に応じて拡張可能
            return Task.FromResult<IAdvancedImage?>(null);
        }
    }
