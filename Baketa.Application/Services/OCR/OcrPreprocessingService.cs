using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Pipeline;
using Baketa.Core.Abstractions.Imaging.Pipeline.Settings;
using OCRTextRegion = Baketa.Core.Abstractions.OCR.TextDetection.TextRegion;
using Baketa.Core.Abstractions.OCR.TextDetection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Baketa.Infrastructure.Imaging.Filters;
using Baketa.Infrastructure.Imaging.Extensions;

namespace Baketa.Application.Services.OCR;

    /// <summary>
    /// OCR前処理サービス
    /// </summary>
    public class OcrPreprocessingService : IOcrPreprocessingService
    {
        private readonly IImagePipelineBuilder _pipelineBuilder;
        private readonly IFilterFactory _filterFactory;
        private readonly ITextRegionAggregator _regionAggregator;
        private readonly Func<string, ITextRegionDetector> _detectorFactory;
        private readonly ILogger<OcrPreprocessingService> _logger;
        private readonly IServiceProvider _serviceProvider;
        
        private readonly Dictionary<string, IImagePipeline> _pipelineCache = new Dictionary<string, IImagePipeline>();
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="pipelineBuilder">パイプラインビルダー</param>
        /// <param name="filterFactory">フィルターファクトリー</param>
        /// <param name="regionAggregator">テキスト領域集約器</param>
        /// <param name="detectorFactory">テキスト検出器ファクトリー</param>
        /// <param name="logger">ロガー</param>
        public OcrPreprocessingService(
            IImagePipelineBuilder pipelineBuilder,
            IFilterFactory filterFactory,
            ITextRegionAggregator regionAggregator,
            Func<string, ITextRegionDetector> detectorFactory,
            ILogger<OcrPreprocessingService> logger,
            IServiceProvider serviceProvider)
        {
            _pipelineBuilder = pipelineBuilder ?? throw new ArgumentNullException(nameof(pipelineBuilder));
            _filterFactory = filterFactory ?? throw new ArgumentNullException(nameof(filterFactory));
            _regionAggregator = regionAggregator ?? throw new ArgumentNullException(nameof(regionAggregator));
            _detectorFactory = detectorFactory ?? throw new ArgumentNullException(nameof(detectorFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }
        
        /// <summary>
        /// 画像を処理し、OCRのためのテキスト領域を検出します
        /// </summary>
        /// <param name="image">入力画像</param>
        /// <param name="profileName">使用するプロファイル名（null=デフォルト）</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>前処理結果（検出されたテキスト領域を含む）</returns>
        public async Task<OcrPreprocessingResult> ProcessImageAsync(
            IAdvancedImage image, 
            string? profileName = null, 
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(image);
                
            try
            {
                _logger.LogDebug("OCR前処理を開始 (プロファイル: {ProfileName})", 
                    profileName ?? "デフォルト");
                
                // プロファイルに基づいてパイプラインを取得または作成
                var pipeline = GetPipelineForProfile(profileName);
                
                // パイプラインを実行
                var pipelineResult = await pipeline.ExecuteAsync(image, cancellationToken).ConfigureAwait(false);
                
                // 現在のPipelineResultにはStatus属性がないため、条件判定を変更
                // 完了したかどうかはErrorが無いことで判断する
                if (pipelineResult.Result == null)
                {
                    _logger.LogWarning("パイプライン実行が完了しませんでした");
                    return new OcrPreprocessingResult(
                        false, // キャンセルかどうかを確認できないのでfalseとする
                        new InvalidOperationException("パイプライン実行が完了しませんでした"),
                        image,
                        Array.Empty<OCRTextRegion>());
                }
                
                // 処理済み画像からテキスト領域を取得
                var processedImage = pipelineResult.Result;
                
                // HasMetadataメソッドが存在しないため、TryGetMetadataを使用して判定
if (!processedImage.TryGetMetadata("TextRegions", out object _))
                {
                    _logger.LogWarning("パイプライン出力にテキスト領域メタデータがありません");
                    return new OcrPreprocessingResult(
                        false,
                        null,
                        processedImage,
                        Array.Empty<OCRTextRegion>());
                }
                
                // GetMetadataメソッドが存在しないため、TryGetMetadataを使用
IReadOnlyList<OCRTextRegion>? detectedRegions = null;
processedImage.TryGetMetadata("TextRegions", out object metadataObj);
if (metadataObj is IReadOnlyList<OCRTextRegion> regions)
{
    detectedRegions = regions;
}
                
                _logger.LogDebug("OCR前処理が完了 (検出テキスト領域: {RegionCount}個)", 
                    detectedRegions?.Count ?? 0);
                    
                return new OcrPreprocessingResult(
                    false,
                    null,
                    processedImage,
                    detectedRegions ?? Array.Empty<OCRTextRegion>());
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("OCR前処理がキャンセルされました");
                return new OcrPreprocessingResult(
                    true,
                    null,
                    image,
                    Array.Empty<OCRTextRegion>());
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "OCR前処理中に操作エラーが発生しました");
                return new OcrPreprocessingResult(
                    false,
                    ex,
                    image,
                    Array.Empty<OCRTextRegion>());
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "OCR前処理中に引数エラーが発生しました");
                return new OcrPreprocessingResult(
                    false,
                    ex,
                    image,
                    Array.Empty<OCRTextRegion>());
            }
            catch (Exception ex) when (ex is not OperationCanceledException && 
                                      ex is not InvalidOperationException &&
                                      ex is not ArgumentException)
            {
                _logger.LogError(ex, "OCR前処理中にエラーが発生しました");
                return new OcrPreprocessingResult(
                    false,
                    ex,
                    image,
                    Array.Empty<OCRTextRegion>());
            }
        }
        
        /// <summary>
        /// 複数の検出器を使用してテキスト領域を検出し、結果を集約します
        /// </summary>
        /// <param name="image">入力画像</param>
        /// <param name="detectorTypes">使用する検出器タイプ</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>集約された検出結果</returns>
        public async Task<IReadOnlyList<OCRTextRegion>> DetectTextRegionsAsync(
            IAdvancedImage image,
            IEnumerable<string> detectorTypes,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(image);
                
            if (detectorTypes == null || !detectorTypes.Any())
            {
                detectorTypes = new[] { "mser" }; // デフォルトはMSER
            }
            
            _logger.LogDebug("テキスト領域検出を開始 (検出器: {DetectorTypes})", 
                string.Join(", ", detectorTypes));
                
            try
            {
                // 指定された検出器を使用してテキスト領域を検出
                var detectionResults = new List<IReadOnlyList<OCRTextRegion>>();
                
                foreach (var detectorType in detectorTypes)
                {
                    try
                    {
                        var detector = _detectorFactory(detectorType);
                        var regions = await detector.DetectRegionsAsync(image, cancellationToken).ConfigureAwait(false);
                        detectionResults.Add(regions);
                        
                        _logger.LogDebug("検出器 {DetectorType} の結果: {RegionCount}個の領域", 
                            detectorType, regions.Count);
                    }
                    catch (ArgumentException ex)
                    {
                        _logger.LogError(ex, "検出器 {DetectorType} の実行中に引数エラーが発生しました", detectorType);
                        // エラーが発生しても他の検出器は続行
                    }
                    catch (InvalidOperationException ex)
                    {
                        _logger.LogError(ex, "検出器 {DetectorType} の実行中に操作エラーが発生しました", detectorType);
                        // エラーが発生しても他の検出器は続行
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException &&
                                           ex is not ArgumentException &&
                                           ex is not InvalidOperationException)
                    {
                        _logger.LogError(ex, "検出器 {DetectorType} の実行中にエラーが発生しました", detectorType);
                        // エラーが発生しても他の検出器は続行
                    }
                }
                
                // 検出結果を集約
                var aggregatedResults = await _regionAggregator.AggregateResultsAsync(
                    detectionResults, cancellationToken).ConfigureAwait(false);
                
                _logger.LogDebug("テキスト領域検出が完了 (集約後: {RegionCount}個の領域)", 
                    aggregatedResults.Count);
                    
                return aggregatedResults;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("テキスト領域検出がキャンセルされました");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "テキスト領域検出中にエラーが発生しました");
                throw;
            }
        }
        
        /// <summary>
        /// プロファイルに基づいてパイプラインを作成します
        /// </summary>
        /// <param name="profileName">プロファイル名（null=デフォルト）</param>
        /// <returns>パイプライン</returns>
        private IImagePipeline GetPipelineForProfile(string? profileName)
        {
            // プロファイルが指定されていない場合はデフォルトを使用
            if (string.IsNullOrEmpty(profileName))
            {
                profileName = "default";
            }
            
            // キャッシュからパイプラインを取得
            if (_pipelineCache.TryGetValue(profileName, out var cachedPipeline))
            {
                return cachedPipeline;
            }
            
            // 指定されたプロファイルに基づいてパイプラインを作成
            
            // 大文字への標準化を使用（小文字化では国際化に問題がある可能性がある）
            string normalizedProfile = profileName.ToUpperInvariant();
            
            var pipeline = normalizedProfile switch
            {
                "GAMEUI" => CreateGameUiPipeline(),
                "DARKTEXT" => CreateDarkTextPipeline(),
                "LIGHTTEXT" => CreateLightTextPipeline(),
                "MINIMAL" => CreateMinimalPipeline(),
                "DEFAULT" => CreateStandardPipeline(),
                _ => CreateStandardPipeline()
            };
            
            // パイプラインをキャッシュ
            _pipelineCache[profileName] = pipeline;
            
            return pipeline;
        }
        
        /// <summary>
        /// 標準的なOCRパイプラインを作成します
        /// </summary>
        /// <returns>パイプライン</returns>
        private IImagePipeline CreateStandardPipeline()
        {
            _logger.LogDebug("標準OCRパイプラインを作成");
            
            return _pipelineBuilder
                .WithName("標準OCRパイプライン")
                .WithDescription("一般的なテキスト認識向けの前処理パイプライン")
                .AddFilter(_filterFactory.CreateFilter("GrayscaleFilter") 
                    ?? throw new InvalidOperationException("GrayscaleFilterが見つかりません"))
                .AddFilter(_filterFactory.CreateFilter("GaussianBlurFilter")
                    ?? throw new InvalidOperationException("GaussianBlurFilterが見つかりません"))
                .AddFilter(_filterFactory.CreateFilter("ContrastEnhancementFilter")
                    ?? throw new InvalidOperationException("ContrastEnhancementFilterが見つかりません"))
                .AddFilter(_filterFactory.CreateFilter("AdaptiveThresholdFilter")
                    ?? throw new InvalidOperationException("AdaptiveThresholdFilterが見つかりません"))
                .AddFilter(_serviceProvider.GetRequiredService<TextRegionDetectionFilter>())
                .WithIntermediateResultMode(IntermediateResultMode.All)
                .WithErrorHandlingStrategy(StepErrorHandlingStrategy.LogAndContinue)
                .Build();
        }
        
        /// <summary>
        /// ゲームUI向けのOCRパイプラインを作成します
        /// </summary>
        /// <returns>パイプライン</returns>
        private IImagePipeline CreateGameUiPipeline()
        {
            _logger.LogDebug("ゲームUI向けOCRパイプラインを作成");
            
            return _pipelineBuilder
                .WithName("ゲームUIテキスト検出パイプライン")
                .WithDescription("ゲームUI内のテキスト検出に特化したパイプライン")
                .AddFilter(_filterFactory.CreateFilter("GrayscaleFilter")
                    ?? throw new InvalidOperationException("GrayscaleFilterが見つかりません"))
                .AddFilter(_filterFactory.CreateFilter("BilateralFilter")
                    ?? throw new InvalidOperationException("BilateralFilterが見つかりません"))
                .AddFilter(_filterFactory.CreateFilter("SharpenFilter")
                    ?? throw new InvalidOperationException("SharpenFilterが見つかりません"))
                .AddFilter(_filterFactory.CreateFilter("OtsuThresholdFilter")
                    ?? throw new InvalidOperationException("OtsuThresholdFilterが見つかりません"))
                .AddFilter(_serviceProvider.GetRequiredService<TextRegionDetectionFilter>())
                .WithIntermediateResultMode(IntermediateResultMode.All)
                .WithErrorHandlingStrategy(StepErrorHandlingStrategy.LogAndContinue)
                .Build();
        }
        
        /// <summary>
        /// 暗いテキスト向けのOCRパイプラインを作成します
        /// </summary>
        /// <returns>パイプライン</returns>
        private IImagePipeline CreateDarkTextPipeline()
        {
            _logger.LogDebug("暗いテキスト向けOCRパイプラインを作成");
            
            // 実装はダミー（使用できるフィルターがまだ不足しているため）
            return CreateStandardPipeline();
        }
        
        /// <summary>
        /// 明るいテキスト向けのOCRパイプラインを作成します
        /// </summary>
        /// <returns>パイプライン</returns>
        private IImagePipeline CreateLightTextPipeline()
        {
            _logger.LogDebug("明るいテキスト向けOCRパイプラインを作成");
            
            // 実装はダミー（使用できるフィルターがまだ不足しているため）
            return CreateStandardPipeline();
        }
        
        /// <summary>
        /// 最小限のOCRパイプラインを作成します
        /// </summary>
        /// <returns>パイプライン</returns>
        private IImagePipeline CreateMinimalPipeline()
        {
            _logger.LogDebug("最小限OCRパイプラインを作成");
            
            return _pipelineBuilder
                .WithName("最小限OCRパイプライン")
                .WithDescription("最小限の処理だけを行うパイプライン")
                .AddFilter(_filterFactory.CreateFilter("GrayscaleFilter")
                    ?? throw new InvalidOperationException("GrayscaleFilterが見つかりません"))
                .AddFilter(_serviceProvider.GetRequiredService<TextRegionDetectionFilter>())
                .WithIntermediateResultMode(IntermediateResultMode.All)
                .WithErrorHandlingStrategy(StepErrorHandlingStrategy.LogAndContinue)
                .Build();
        }
    }
    
    /// <summary>
    /// OCR前処理サービスインターフェース
    /// </summary>
    public interface IOcrPreprocessingService
    {
        /// <summary>
        /// 画像を処理し、OCRのためのテキスト領域を検出します
        /// </summary>
        /// <param name="image">入力画像</param>
        /// <param name="profileName">使用するプロファイル名（null=デフォルト）</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>前処理結果（検出されたテキスト領域を含む）</returns>
        Task<OcrPreprocessingResult> ProcessImageAsync(
            IAdvancedImage image, 
            string? profileName = null, 
            CancellationToken cancellationToken = default);
            
        /// <summary>
        /// 複数の検出器を使用してテキスト領域を検出し、結果を集約します
        /// </summary>
        /// <param name="image">入力画像</param>
        /// <param name="detectorTypes">使用する検出器タイプ</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>集約された検出結果</returns>
        Task<IReadOnlyList<OCRTextRegion>> DetectTextRegionsAsync(
            IAdvancedImage image,
            IEnumerable<string> detectorTypes,
            CancellationToken cancellationToken = default);
    }
    
    /// <summary>
    /// OCR前処理結果
    /// </summary>
    public class OcrPreprocessingResult
    {
        /// <summary>
        /// 処理がキャンセルされたかどうか
        /// </summary>
        public bool IsCancelled { get; }
        
        /// <summary>
        /// エラーが発生した場合の例外
        /// </summary>
        public Exception? Error { get; }
        
        /// <summary>
        /// 前処理後の画像
        /// </summary>
        public IAdvancedImage ProcessedImage { get; }
        
        /// <summary>
        /// 検出されたテキスト領域
        /// </summary>
        public IReadOnlyList<OCRTextRegion> DetectedRegions { get; }
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="isCancelled">処理がキャンセルされたかどうか</param>
        /// <param name="error">エラーが発生した場合の例外</param>
        /// <param name="processedImage">前処理後の画像</param>
        /// <param name="detectedRegions">検出されたテキスト領域</param>
        public OcrPreprocessingResult(
            bool isCancelled,
            Exception? error,
            IAdvancedImage processedImage,
            IReadOnlyList<OCRTextRegion> detectedRegions)
        {
            IsCancelled = isCancelled;
            Error = error;
            ProcessedImage = processedImage ?? throw new ArgumentNullException(nameof(processedImage));
            DetectedRegions = detectedRegions ?? Array.Empty<OCRTextRegion>();
        }
    }
