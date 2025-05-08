using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Filters;
using Baketa.Core.Abstractions.Imaging.Pipeline;
using Microsoft.Extensions.Logging;

namespace Baketa.Core.Services.Imaging.Pipeline
{
    /// <summary>
    /// OCR最適化パイプラインを構築するビルダークラス
    /// </summary>
    public class OcrPipelineBuilder : IOcrPipelineBuilder
    {
        private readonly IImagePipeline _pipeline;
        private readonly IOcrFilterFactory _filterFactory;
        private readonly ILogger<OcrPipelineBuilder> _logger;

        /// <summary>
        /// 新しいOcrPipelineBuilderを作成します
        /// </summary>
        /// <param name="pipeline">パイプラインインスタンス</param>
        /// <param name="filterFactory">OCRフィルターファクトリー</param>
        /// <param name="logger">ロガー</param>
        public OcrPipelineBuilder(
            IImagePipeline pipeline,
            IOcrFilterFactory filterFactory,
            ILogger<OcrPipelineBuilder> logger)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _filterFactory = filterFactory ?? throw new ArgumentNullException(nameof(filterFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc/>
        public IImagePipeline BuildStandardPipeline()
        {
            _logger.LogInformation("標準OCRパイプラインを構築しています");
            
            // パイプラインをクリア
            _pipeline.ClearSteps();
            
            // 標準フィルターを追加
            var filters = _filterFactory.CreateStandardOcrPipeline();
            AddFiltersToPipeline(filters);
            
            // グローバル設定を構成
            _pipeline.IntermediateResultMode = IntermediateResultMode.None;
            _pipeline.GlobalErrorHandlingStrategy = StepErrorHandlingStrategy.LogAndContinue;
            
            return _pipeline;
        }

        /// <inheritdoc/>
        public IImagePipeline BuildMinimalPipeline()
        {
            _logger.LogInformation("最小限のOCRパイプラインを構築しています");
            
            // パイプラインをクリア
            _pipeline.ClearSteps();
            
            // 最小限のフィルターを追加
            var filters = _filterFactory.CreateMinimalOcrPipeline();
            AddFiltersToPipeline(filters);
            
            // グローバル設定を構成
            _pipeline.IntermediateResultMode = IntermediateResultMode.None;
            _pipeline.GlobalErrorHandlingStrategy = StepErrorHandlingStrategy.LogAndContinue;
            
            return _pipeline;
        }

        /// <inheritdoc/>
        public IImagePipeline BuildEdgeBasedPipeline()
        {
            _logger.LogInformation("エッジベースのOCRパイプラインを構築しています");
            
            // パイプラインをクリア
            _pipeline.ClearSteps();
            
            // エッジベースのフィルターを追加
            var filters = _filterFactory.CreateEdgeBasedOcrPipeline();
            AddFiltersToPipeline(filters);
            
            // グローバル設定を構成
            _pipeline.IntermediateResultMode = IntermediateResultMode.None;
            _pipeline.GlobalErrorHandlingStrategy = StepErrorHandlingStrategy.LogAndContinue;
            
            return _pipeline;
        }

        /// <inheritdoc/>
        public IImagePipeline BuildCustomPipeline(params OcrFilterType[] filterTypes)
        {
            _logger.LogInformation("カスタムOCRパイプラインを構築しています ({FilterCount}フィルター)", filterTypes.Length);
            
            // パイプラインをクリア
            _pipeline.ClearSteps();
            
            // カスタムフィルターを追加
            var filters = new List<IImageFilter>();
            foreach (var filterType in filterTypes)
            {
                try
                {
                    filters.Add(_filterFactory.CreateFilter(filterType));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "フィルター {FilterType} の作成中にエラーが発生しました。スキップします。", filterType);
                }
            }
            
            AddFiltersToPipeline(filters.ToArray());
            
            // グローバル設定を構成
            _pipeline.IntermediateResultMode = IntermediateResultMode.None;
            _pipeline.GlobalErrorHandlingStrategy = StepErrorHandlingStrategy.LogAndContinue;
            
            return _pipeline;
        }

        /// <inheritdoc/>
        public async Task<IImagePipeline> LoadPipelineFromProfileAsync(string profileName)
        {
            _logger.LogInformation("プロファイル '{ProfileName}' からOCRパイプラインを読み込んでいます", profileName);
            
            try
            {
                // 既存のプロファイルからパイプラインを読み込む
                var loadedPipeline = await _pipeline.LoadProfileAsync(profileName);
                
                if (loadedPipeline != null)
                {
                    _logger.LogInformation("プロファイル '{ProfileName}' からパイプラインを正常に読み込みました", profileName);
                    return loadedPipeline;
                }
                else
                {
                    _logger.LogWarning("プロファイル '{ProfileName}' が見つからないか読み込めませんでした。標準パイプラインを返します", profileName);
                    return BuildStandardPipeline();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "プロファイル '{ProfileName}' からのパイプライン読み込み中にエラーが発生しました。標準パイプラインを返します", profileName);
                return BuildStandardPipeline();
            }
        }

        /// <inheritdoc/>
        public async Task SavePipelineToProfileAsync(string profileName)
        {
            _logger.LogInformation("OCRパイプラインをプロファイル '{ProfileName}' として保存しています", profileName);
            
            try
            {
                // 現在のパイプラインをプロファイルとして保存
                await _pipeline.SaveProfileAsync(profileName);
                _logger.LogInformation("パイプラインをプロファイル '{ProfileName}' として正常に保存しました", profileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "パイプラインのプロファイル '{ProfileName}' への保存中にエラーが発生しました", profileName);
                throw;
            }
        }

        /// <summary>
        /// フィルターをパイプラインに追加します
        /// </summary>
        /// <param name="filters">追加するフィルター配列</param>
        private void AddFiltersToPipeline(Baketa.Core.Abstractions.Imaging.IImageFilter[] filters)
        {
            if (filters == null || filters.Length == 0)
            {
                _logger.LogWarning("追加するフィルターが指定されていません");
                return;
            }
            
            foreach (var filter in filters)
            {
                // フィルターをパイプラインステップに変換してパイプラインに追加
                var adapter = new FilterPipelineStepAdapter(filter, _logger);
                _pipeline.AddStep(adapter);
                _logger.LogDebug("パイプラインにフィルター '{FilterName}' を追加しました", filter.Name);
            }
            
            _logger.LogInformation("{Count}個のフィルターをパイプラインに追加しました", filters.Length);
        }
    }
}
