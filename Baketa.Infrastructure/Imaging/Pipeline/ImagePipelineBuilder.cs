using System;
using System.Collections.Generic;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Pipeline;
using Baketa.Core.Abstractions.Imaging.Pipeline.Settings;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Imaging.Pipeline;

/// <summary>
/// 画像処理パイプラインビルダーの実装
/// </summary>
/// <remarks>
/// コンストラクタ
/// </remarks>
/// <param name="filterFactory">フィルターファクトリー</param>
/// <param name="logger">ロガー</param>
public class ImagePipelineBuilder(IFilterFactory filterFactory, ILogger<ImagePipelineBuilder>? logger = null) : IImagePipelineBuilder
{
    private readonly ImagePipeline _pipeline = new("新しいパイプライン", "");
    private readonly IFilterFactory _filterFactory = filterFactory ?? throw new ArgumentNullException(nameof(filterFactory));
    private readonly ILogger<ImagePipelineBuilder>? _logger = logger;

    /// <summary>
    /// パイプラインに名前を設定します
    /// </summary>
    /// <param name="name">パイプライン名</param>
    /// <returns>ビルダー</returns>
    public IImagePipelineBuilder WithName(string name)
    {
        // プロパティを内部メソッド経由で更新
        typeof(ImagePipeline).GetProperty("Name")?.SetValue(_pipeline, name);
        return this;
    }

    /// <summary>
    /// パイプラインに説明を設定します
    /// </summary>
    /// <param name="description">パイプライン説明</param>
    /// <returns>ビルダー</returns>
    public IImagePipelineBuilder WithDescription(string description)
    {
        // プロパティを内部メソッド経由で更新
        typeof(ImagePipeline).GetProperty("Description")?.SetValue(_pipeline, description);
        return this;
    }

    /// <summary>
    /// パイプラインにフィルターを追加します
    /// </summary>
    /// <param name="filter">追加するフィルター</param>
    /// <returns>ビルダー</returns>
    public IImagePipelineBuilder AddFilter(IImageFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        // IImageFilterからIImagePipelineFilterへの変換が必要
        if (filter is IImagePipelineFilter pipelineFilter)
        {
            _pipeline.AddFilter(pipelineFilter);
        }
        else
        {
            // アダプター経由で変換
            var adapter = new ImageFilterAdapter(filter);
            _pipeline.AddFilter(adapter);
        }
        return this;
    }

    /// <summary>
    /// パイプラインから指定位置のフィルターを削除します
    /// </summary>
    /// <param name="index">削除するフィルターのインデックス</param>
    /// <returns>ビルダー</returns>
    public IImagePipelineBuilder RemoveFilterAt(int index)
    {
        _pipeline.RemoveFilterAt(index);
        return this;
    }

    /// <summary>
    /// パイプラインの全フィルターをクリアします
    /// </summary>
    /// <returns>ビルダー</returns>
    public IImagePipelineBuilder ClearFilters()
    {
        _pipeline.ClearFilters();
        return this;
    }

    /// <summary>
    /// パイプラインの中間結果モードを設定します
    /// </summary>
    /// <param name="mode">中間結果モード</param>
    /// <returns>ビルダー</returns>
    public IImagePipelineBuilder WithIntermediateResultMode(IntermediateResultMode mode)
    {
        _pipeline.IntermediateResultMode = mode;
        return this;
    }

    /// <summary>
    /// パイプラインのエラーハンドリング戦略を設定します
    /// </summary>
    /// <param name="strategy">エラーハンドリング戦略</param>
    /// <returns>ビルダー</returns>
    public IImagePipelineBuilder WithErrorHandlingStrategy(StepErrorHandlingStrategy strategy)
    {
        _pipeline.GlobalErrorHandlingStrategy = strategy;
        return this;
    }

    /// <summary>
    /// 設定からパイプラインを構築します
    /// </summary>
    /// <param name="settings">パイプライン設定</param>
    /// <returns>ビルダー</returns>
    public IImagePipelineBuilder FromSettings(ImagePipelineSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // 内部メソッド経由で名前を設定
        var nameField = typeof(ImagePipeline).GetField("<Name>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        nameField?.SetValue(_pipeline, settings.Name);

        // 内部メソッド経由で説明を設定
        var descField = typeof(ImagePipeline).GetField("<Description>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        descField?.SetValue(_pipeline, settings.Description);
        _pipeline.IntermediateResultMode = settings.IntermediateResultMode;
        _pipeline.GlobalErrorHandlingStrategy = settings.ErrorHandlingStrategy;
        _pipeline.ClearFilters();

        foreach (var filterSetting in settings.Filters)
        {
            var filter = _filterFactory.CreateFilter(filterSetting.TypeName);
            if (filter != null)
            {
                foreach (var param in filterSetting.Parameters)
                {
                    filter.SetParameter(param.Key, param.Value);
                }
                // IImageFilterからIImagePipelineFilterへの変換
                if (filter is IImagePipelineFilter pipelineFilter)
                {
                    _pipeline.AddFilter(pipelineFilter);
                }
                else
                {
                    var adapter = new ImageFilterAdapter(filter);
                    _pipeline.AddFilter(adapter);
                }
            }
            else
            {
                _logger?.LogWarning("フィルター '{FilterType}' を生成できませんでした", filterSetting.TypeName);
            }
        }

        return this;
    }

    /// <summary>
    /// パイプラインを構築します
    /// </summary>
    /// <returns>構築されたパイプライン</returns>
    public IImagePipeline Build()
    {
        return _pipeline;
    }
}
