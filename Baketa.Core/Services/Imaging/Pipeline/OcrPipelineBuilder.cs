using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Filters;
using Baketa.Core.Abstractions.Imaging.Pipeline;
using Baketa.Core.Services.Imaging.Filters.OCR;
using Microsoft.Extensions.Logging;

namespace Baketa.Core.Services.Imaging.Pipeline
{
    /// <summary>
    /// OCR最適化パイプラインを構築するビルダークラス
    /// </summary>
#pragma warning disable CA1062 // パラメータの引数チェックはメソッド内で行っているため
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
            ArgumentNullException.ThrowIfNull(_filterFactory);
            
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
            _logger.LogInformation("カスタムOCRパイプラインを構築しています (フィルター数: {FilterCount})", filterTypes.Length);
            
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
#pragma warning restore CA1031
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(ex, "フィルター {FilterType} の作成中にエラーが発生しました。スキップします。", filterType);
                }
                catch (ArgumentException ex)
                {
                    _logger.LogWarning(ex, "フィルタータイプ {FilterType} が無効です。スキップします。", filterType);
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
                var loadedPipeline = await _pipeline.LoadProfileAsync(profileName).ConfigureAwait(false);
                
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
            catch (FileNotFoundException ex)
            {
                _logger.LogError(ex, "プロファイル '{ProfileName}' のファイルが見つかりません。標準パイプラインを返します", profileName);
                return BuildStandardPipeline();
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "プロファイル '{ProfileName}' のJSONフォーマットが不正です。標準パイプラインを返します", profileName);
                return BuildStandardPipeline();
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "プロファイル '{ProfileName}' からのパイプライン読み込み中にアクセス権限エラーが発生しました。標準パイプラインを返します", profileName);
                return BuildStandardPipeline();
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "プロファイル '{ProfileName}' からのパイプライン読み込み中にIOエラーが発生しました。標準パイプラインを返します", profileName);
                return BuildStandardPipeline();
            }
#pragma warning disable CA1031 // プロファイルロード中の例外はすべてキャッチし、標準パイプラインを返す必要があるため
            catch (Exception ex) // すべての例外をキャッチ
            {
                _logger.LogError(ex, "プロファイル '{ProfileName}' からのパイプライン読み込み中にエラーが発生しました。標準パイプラインを返します", profileName);
                return BuildStandardPipeline(); // 例外をスローせず、標準パイプラインを返す
            }
#pragma warning restore CA1031
        }

        /// <inheritdoc/>
        public async Task SavePipelineToProfileAsync(string profileName)
        {
            _logger.LogInformation("OCRパイプラインをプロファイル '{ProfileName}' として保存しています", profileName);
            
            try
            {
                // 現在のパイプラインをプロファイルとして保存
                await _pipeline.SaveProfileAsync(profileName).ConfigureAwait(false);
                _logger.LogInformation("パイプラインをプロファイル '{ProfileName}' として正常に保存しました", profileName);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "パイプラインのプロファイル '{ProfileName}' への保存中にアクセス権限エラーが発生しました", profileName);
                throw new UnauthorizedAccessException($"プロファイル '{profileName}' へのアクセスが拒否されました", ex);
            }
            catch (DirectoryNotFoundException ex)
            {
                _logger.LogError(ex, "プロファイルディレクトリが見つかりません");
                throw new IOException($"プロファイルの保存先ディレクトリが見つかりません。プロファイル '{profileName}' を保存できません。", ex);
            }
            catch (System.Text.Json.JsonException ex)
            {
                _logger.LogError(ex, "パイプラインデータのJSONシリアライズ中にエラーが発生しました");
                throw new InvalidOperationException($"プロファイル '{profileName}' への保存中にデータ変換エラーが発生しました", ex);
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "パイプラインのプロファイル '{ProfileName}' への保存中にIOエラーが発生しました", profileName);
                throw new IOException($"プロファイル '{profileName}' への保存に失敗しました", ex);
            }
        }

        /// <summary>
        /// フィルターをパイプラインに追加します
        /// </summary>
        /// <param name="filters">追加するフィルター配列</param>
#pragma warning disable CA1062 // privateメソッドで引数は内部的に制御されるため
        private void AddFiltersToPipeline(Baketa.Core.Abstractions.Imaging.IImageFilter[] filters)
        {
            ArgumentNullException.ThrowIfNull(filters);
            ArgumentNullException.ThrowIfNull(_pipeline);
            ArgumentNullException.ThrowIfNull(_logger);
            
            if (filters.Length == 0)
            {
                _logger.LogWarning("追加するフィルターが指定されていません");
                return;
            }
            
            foreach (var filter in filters)
            {
                ArgumentNullException.ThrowIfNull(filter);
                // フィルターをパイプラインステップに変換してパイプラインに追加
                var adapter = new FilterPipelineStepAdapter(filter, _logger);
                _pipeline.AddStep(adapter);
                _logger.LogDebug("パイプラインにフィルター '{FilterName}' を追加しました", filter.Name);
            }
            
            _logger.LogInformation("{FilterCount}個のフィルターをパイプラインに追加しました", filters.Length);
        }
#pragma warning restore CA1062
    }
#pragma warning restore CA1062
}
