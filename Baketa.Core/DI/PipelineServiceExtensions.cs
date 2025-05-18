using Baketa.Core.Abstractions.Imaging.Filters;
using Baketa.Core.Abstractions.Imaging.Pipeline;
using Baketa.Core.Services.Imaging.Filters.OCR;
using Baketa.Core.Services.Imaging.Pipeline;
using Baketa.Core.Services.Imaging.Pipeline.Conditions;
using Microsoft.Extensions.DependencyInjection;

namespace Baketa.Core.DI;

    /// <summary>
    /// パイプライン関連サービスの登録拡張メソッドを提供します
    /// </summary>
    public static class PipelineServiceExtensions
    {
        /// <summary>
        /// パイプライン関連のサービスをDIコンテナに登録します
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <returns>サービスコレクション</returns>
        public static IServiceCollection AddPipelineServices(this IServiceCollection services)
        {
            // パイプラインの基本サービス
            services.AddSingleton<IImagePipeline, ImagePipeline>();
            services.AddSingleton<IPipelineProfileManager, PipelineProfileManager>();

            // 条件評価クラスのファクトリーを登録
            services.AddTransient<IAndConditionFactory, AndConditionFactory>();
            services.AddTransient<IOrConditionFactory, OrConditionFactory>();
            services.AddTransient<INotConditionFactory, NotConditionFactory>();
            services.AddTransient<IImagePropertyConditionFactory, ImagePropertyConditionFactory>();

            // OCR最適化フィルターを登録
            RegisterOcrFilters(services);
            
            // OCRパイプラインビルダーを登録
            services.AddSingleton<IOcrPipelineBuilder, OcrPipelineBuilder>();

            return services;
        }

        /// <summary>
        /// OCR最適化フィルターをDIコンテナに登録します
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <returns>サービスコレクション</returns>
        public static IServiceCollection RegisterOcrFilters(this IServiceCollection services)
        {
            // OCRフィルタークラスを登録
            services.AddSingleton<OcrGrayscaleFilter>();
            services.AddSingleton<OcrContrastEnhancementFilter>();
            services.AddSingleton<OcrNoiseReductionFilter>();
            services.AddSingleton<OcrThresholdFilter>();
            services.AddSingleton<OcrMorphologyFilter>();
            services.AddSingleton<OcrEdgeDetectionFilter>();
            
            // OCRフィルターファクトリーを登録
            services.AddSingleton<IOcrFilterFactory, OcrFilterFactory>();

            return services;
        }
    }

    /// <summary>
    /// AND条件のファクトリーインターフェース
    /// </summary>
    public interface IAndConditionFactory
    {
        /// <summary>
        /// AND条件を作成します
        /// </summary>
        /// <param name="left">左辺条件</param>
        /// <param name="right">右辺条件</param>
        /// <returns>AND条件</returns>
        IPipelineCondition Create(IPipelineCondition left, IPipelineCondition right);
    }

    /// <summary>
    /// AND条件のファクトリークラス
    /// </summary>
    internal sealed class AndConditionFactory : IAndConditionFactory
    {
        /// <inheritdoc/>
        public IPipelineCondition Create(IPipelineCondition left, IPipelineCondition right)
        {
            return new AndCondition(left, right);
        }
    }

    /// <summary>
    /// OR条件のファクトリーインターフェース
    /// </summary>
    public interface IOrConditionFactory
    {
        /// <summary>
        /// OR条件を作成します
        /// </summary>
        /// <param name="left">左辺条件</param>
        /// <param name="right">右辺条件</param>
        /// <returns>OR条件</returns>
        IPipelineCondition Create(IPipelineCondition left, IPipelineCondition right);
    }

    /// <summary>
    /// OR条件のファクトリークラス
    /// </summary>
    internal sealed class OrConditionFactory : IOrConditionFactory
    {
        /// <inheritdoc/>
        public IPipelineCondition Create(IPipelineCondition left, IPipelineCondition right)
        {
            return new OrCondition(left, right);
        }
    }

    /// <summary>
    /// NOT条件のファクトリーインターフェース
    /// </summary>
    public interface INotConditionFactory
    {
        /// <summary>
        /// NOT条件を作成します
        /// </summary>
        /// <param name="condition">対象条件</param>
        /// <returns>NOT条件</returns>
        IPipelineCondition Create(IPipelineCondition condition);
    }

    /// <summary>
    /// NOT条件のファクトリークラス
    /// </summary>
    internal sealed class NotConditionFactory : INotConditionFactory
    {
        /// <inheritdoc/>
        public IPipelineCondition Create(IPipelineCondition condition)
        {
            return new NotCondition(condition);
        }
    }

    /// <summary>
    /// 画像プロパティ条件のファクトリーインターフェース
    /// </summary>
    public interface IImagePropertyConditionFactory
    {
        /// <summary>
        /// 画像プロパティ条件を作成します
        /// </summary>
        /// <param name="propertyName">プロパティ名</param>
        /// <param name="comparisonOperator">比較演算子</param>
        /// <param name="value">比較値</param>
        /// <returns>画像プロパティ条件</returns>
        IPipelineCondition Create(string propertyName, ComparisonOperator comparisonOperator, object value);
    }

    /// <summary>
    /// 画像プロパティ条件のファクトリークラス
    /// </summary>
    internal sealed class ImagePropertyConditionFactory : IImagePropertyConditionFactory
    {
        /// <inheritdoc/>
        public IPipelineCondition Create(string propertyName, ComparisonOperator comparisonOperator, object value)
        {
            return new ImagePropertyCondition(
                ImagePropertyCondition.PropertyType.Width, // プロパティ名から適切な値に変換する必要あり
                (ImagePropertyCondition.ComparisonOperator)comparisonOperator, // 列挙型の変換
                value);
        }
    }
