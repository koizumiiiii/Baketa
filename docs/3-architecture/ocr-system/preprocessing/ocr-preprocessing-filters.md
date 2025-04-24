# OCR前処理システム - フィルターとテキスト検出

*最終更新: 2025年4月24日*

## 1. フィルターコンポーネント詳細設計

### 1.1 IImageFilter インターフェース

画像フィルターの基本インターフェースは以下のように定義します：

```csharp
namespace Baketa.Core.Services.ImageProcessing.Filters
{
    /// <summary>
    /// 画像フィルターの基本インターフェース
    /// </summary>
    public interface IImageFilter
    {
        /// <summary>
        /// フィルターの名前
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// フィルターのパラメータ
        /// </summary>
        IReadOnlyDictionary<string, object> Parameters { get; }
        
        /// <summary>
        /// フィルターを適用
        /// </summary>
        Task<IImage> ApplyAsync(IImage inputImage);
    }
}
```

### 1.2 基本フィルターの実装

#### 1.2.1 グレースケールフィルター

```csharp
namespace Baketa.Infrastructure.OpenCV.Filters
{
    /// <summary>
    /// グレースケール変換フィルター
    /// </summary>
    public class GrayscaleFilter : IImageFilter
    {
        public string Name => "Grayscale";
        
        public IReadOnlyDictionary<string, object> Parameters => 
            new Dictionary<string, object>();
        
        public async Task<IImage> ApplyAsync(IImage inputImage)
        {
            // OpenCVを使用した実装
            using var mat = await ImageToMatAsync(inputImage);
            using var grayMat = new Mat();
            
            if (mat.Channels() > 1)
            {
                Cv2.CvtColor(mat, grayMat, ColorConversionCodes.BGR2GRAY);
                return await MatToImageAsync(grayMat);
            }
            
            return inputImage.Clone();
        }
    }
}
```

#### 1.2.2 ノイズ除去フィルター

```csharp
namespace Baketa.Infrastructure.OpenCV.Filters
{
    /// <summary>
    /// ノイズ除去フィルター
    /// </summary>
    public class NoiseReductionFilter : IImageFilter
    {
        private readonly float _strength;
        
        public NoiseReductionFilter(float strength = 0.5f)
        {
            _strength = Math.Clamp(strength, 0f, 1f);
        }
        
        public string Name => "NoiseReduction";
        
        public IReadOnlyDictionary<string, object> Parameters => 
            new Dictionary<string, object> { ["Strength"] = _strength };
        
        public async Task<IImage> ApplyAsync(IImage inputImage)
        {
            // OpenCVを使用した実装
            using var mat = await ImageToMatAsync(inputImage);
            using var resultMat = new Mat();
            
            // 強度に基づいてカーネルサイズを決定
            int kernelSize = 2 * Math.Max(1, (int)(_strength * 5)) + 1;
            
            // ガウシアンフィルタでノイズ除去
            Cv2.GaussianBlur(mat, resultMat, new Size(kernelSize, kernelSize), 0);
            
            return await MatToImageAsync(resultMat);
        }
    }
}
```

#### 1.2.3 コントラスト強調フィルター

```csharp
namespace Baketa.Infrastructure.OpenCV.Filters
{
    /// <summary>
    /// コントラスト強調フィルター
    /// </summary>
    public class ContrastEnhancementFilter : IImageFilter
    {
        private readonly float _contrast;
        private readonly float _brightness;
        
        public ContrastEnhancementFilter(float contrast = 1.2f, float brightness = 0f)
        {
            _contrast = Math.Max(0.1f, contrast);
            _brightness = Math.Clamp(brightness, -100f, 100f);
        }
        
        public string Name => "ContrastEnhancement";
        
        public IReadOnlyDictionary<string, object> Parameters => 
            new Dictionary<string, object> 
            { 
                ["Contrast"] = _contrast,
                ["Brightness"] = _brightness
            };
        
        public async Task<IImage> ApplyAsync(IImage inputImage)
        {
            // OpenCVを使用した実装
            using var mat = await ImageToMatAsync(inputImage);
            using var resultMat = new Mat();
            
            // コントラストと明るさを調整
            mat.ConvertTo(resultMat, -1, _contrast, _brightness);
            
            return await MatToImageAsync(resultMat);
        }
    }
}
```

#### 1.2.4 二値化フィルター

```csharp
namespace Baketa.Infrastructure.OpenCV.Filters
{
    /// <summary>
    /// 二値化フィルター
    /// </summary>
    public class BinarizationFilter : IImageFilter
    {
        private readonly bool _useAdaptiveThreshold;
        private readonly int _threshold;
        private readonly int _adaptiveBlockSize;
        private readonly double _adaptiveConstant;
        
        public BinarizationFilter(
            bool useAdaptiveThreshold = true,
            int threshold = 127,
            int adaptiveBlockSize = 11,
            double adaptiveConstant = 2)
        {
            _useAdaptiveThreshold = useAdaptiveThreshold;
            _threshold = Math.Clamp(threshold, 0, 255);
            _adaptiveBlockSize = Math.Max(3, adaptiveBlockSize | 1); // 奇数にする
            _adaptiveConstant = adaptiveConstant;
        }
        
        public string Name => "Binarization";
        
        public IReadOnlyDictionary<string, object> Parameters => 
            new Dictionary<string, object> 
            { 
                ["UseAdaptiveThreshold"] = _useAdaptiveThreshold,
                ["Threshold"] = _threshold,
                ["AdaptiveBlockSize"] = _adaptiveBlockSize,
                ["AdaptiveConstant"] = _adaptiveConstant
            };
        
        public async Task<IImage> ApplyAsync(IImage inputImage)
        {
            // OpenCVを使用した実装
            using var mat = await ImageToMatAsync(inputImage);
            using var grayMat = new Mat();
            using var resultMat = new Mat();
            
            // グレースケール変換
            if (mat.Channels() > 1)
            {
                Cv2.CvtColor(mat, grayMat, ColorConversionCodes.BGR2GRAY);
            }
            else
            {
                mat.CopyTo(grayMat);
            }
            
            // 二値化処理
            if (_useAdaptiveThreshold)
            {
                Cv2.AdaptiveThreshold(
                    grayMat,
                    resultMat,
                    255,
                    AdaptiveThresholdType.GaussianC,
                    ThresholdType.Binary,
                    _adaptiveBlockSize,
                    _adaptiveConstant);
            }
            else
            {
                Cv2.Threshold(
                    grayMat,
                    resultMat,
                    _threshold,
                    255,
                    ThresholdType.Binary);
            }
            
            return await MatToImageAsync(resultMat);
        }
    }
}
```

#### 1.2.5 モルフォロジーフィルター

```csharp
namespace Baketa.Infrastructure.OpenCV.Filters
{
    /// <summary>
    /// モルフォロジー演算フィルター
    /// </summary>
    public class MorphologyFilter : IImageFilter
    {
        private readonly MorphologyOperationType _operation;
        private readonly int _kernelSize;
        
        public MorphologyFilter(
            MorphologyOperationType operation = MorphologyOperationType.Dilate,
            int kernelSize = 3)
        {
            _operation = operation;
            _kernelSize = Math.Max(1, kernelSize);
        }
        
        public string Name => "Morphology";
        
        public IReadOnlyDictionary<string, object> Parameters => 
            new Dictionary<string, object> 
            { 
                ["Operation"] = _operation,
                ["KernelSize"] = _kernelSize
            };
        
        public async Task<IImage> ApplyAsync(IImage inputImage)
        {
            // OpenCVを使用した実装
            using var mat = await ImageToMatAsync(inputImage);
            using var resultMat = new Mat();
            
            // 構造要素の作成
            var element = Cv2.GetStructuringElement(
                MorphShapes.Rect,
                new Size(_kernelSize, _kernelSize));
            
            // モルフォロジー演算
            switch (_operation)
            {
                case MorphologyOperationType.Dilate:
                    Cv2.Dilate(mat, resultMat, element);
                    break;
                case MorphologyOperationType.Erode:
                    Cv2.Erode(mat, resultMat, element);
                    break;
                case MorphologyOperationType.Open:
                    Cv2.MorphologyEx(mat, resultMat, MorphTypes.Open, element);
                    break;
                case MorphologyOperationType.Close:
                    Cv2.MorphologyEx(mat, resultMat, MorphTypes.Close, element);
                    break;
                default:
                    mat.CopyTo(resultMat);
                    break;
            }
            
            return await MatToImageAsync(resultMat);
        }
    }
    
    /// <summary>
    /// モルフォロジー演算の種類
    /// </summary>
    public enum MorphologyOperationType
    {
        /// <summary>
        /// 膨張（Dilation）
        /// </summary>
        Dilate = 0,
        
        /// <summary>
        /// 収縮（Erosion）
        /// </summary>
        Erode = 1,
        
        /// <summary>
        /// オープニング（収縮→膨張）
        /// </summary>
        Open = 2,
        
        /// <summary>
        /// クロージング（膨張→収縮）
        /// </summary>
        Close = 3
    }
}
```

### 1.3 カスタムフィルター拡張ポイント

```csharp
namespace Baketa.Core.Services.ImageProcessing
{
    /// <summary>
    /// カスタムフィルター拡張マネージャー
    /// </summary>
    public class FilterExtensionManager : IFilterExtensionPoint
    {
        private readonly Dictionary<string, IImageFilter> _customFilters = 
            new Dictionary<string, IImageFilter>();
        private readonly ILogger<FilterExtensionManager> _logger;
        
        public FilterExtensionManager(ILogger<FilterExtensionManager> logger)
        {
            _logger = logger;
        }
        
        public void RegisterCustomFilter(IImageFilter filter)
        {
            if (filter == null)
                throw new ArgumentNullException(nameof(filter));
                
            if (string.IsNullOrWhiteSpace(filter.Name))
                throw new ArgumentException("フィルター名が無効です", nameof(filter));
                
            _customFilters[filter.Name] = filter;
            _logger.LogInformation("カスタムフィルターを登録しました: {FilterName}", filter.Name);
        }
        
        public IImageFilter GetCustomFilter(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("フィルター名が無効です", nameof(name));
                
            if (_customFilters.TryGetValue(name, out var filter))
                return filter;
                
            throw new KeyNotFoundException($"指定された名前のカスタムフィルターが見つかりません: {name}");
        }
        
        public IEnumerable<string> GetRegisteredFilterNames()
        {
            return _customFilters.Keys;
        }
    }
}
```

## 2. テキスト領域検出詳細設計

### 2.1 テキスト領域表現

```csharp
namespace Baketa.Core.Models
{
    /// <summary>
    /// テキスト領域を表すモデル
    /// </summary>
    public class TextRegion
    {
        /// <summary>
        /// 領域の位置と大きさ
        /// </summary>
        public Rectangle Bounds { get; set; }
        
        /// <summary>
        /// テキスト存在の確信度 (0.0〜1.0)
        /// </summary>
        public float Confidence { get; set; }
        
        /// <summary>
        /// テキスト方向
        /// </summary>
        public TextOrientation Orientation { get; set; }
        
        /// <summary>
        /// 領域識別子
        /// </summary>
        public Guid Id { get; set; } = Guid.NewGuid();
        
        /// <summary>
        /// 認識されたテキスト（OCR後に設定）
        /// </summary>
        public string RecognizedText { get; set; }
    }
    
    /// <summary>
    /// テキスト方向
    /// </summary>
    public enum TextOrientation
    {
        /// <summary>
        /// 水平方向 (左から右)
        /// </summary>
        Horizontal = 0,
        
        /// <summary>
        /// 垂直方向 (上から下)
        /// </summary>
        Vertical = 1,
        
        /// <summary>
        /// 不明または検出不可
        /// </summary>
        Unknown = 2
    }
}
```

### 2.2 テキスト領域検出パラメータ

```csharp
namespace Baketa.Core.Models
{
    /// <summary>
    /// テキスト領域検出パラメータ
    /// </summary>
    public class TextDetectionParameters
    {
        // MSERパラメータ
        public int MserDelta { get; set; } = 5;
        public int MserMinArea { get; set; } = 60;
        public int MserMaxArea { get; set; } = 14400;
        
        // フィルタリングパラメータ
        public int MinWidth { get; set; } = 10;
        public int MinHeight { get; set; } = 10;
        public float MinAspectRatio { get; set; } = 0.1f;
        public float MaxAspectRatio { get; set; } = 10.0f;
        
        // 統合パラメータ
        public float OverlapThreshold { get; set; } = 0.5f;
        
        // 検出方法
        public TextDetectionMethod Method { get; set; } = TextDetectionMethod.Mser;
        
        // エッジベース検出パラメータ
        public double CannyThreshold1 { get; set; } = 50;
        public double CannyThreshold2 { get; set; } = 150;
        
        // 共通パラメータ
        public float ConfidenceThreshold { get; set; } = 0.5f;
        
        /// <summary>
        /// ゲームプロファイルに基づくパラメータの最適化
        /// </summary>
        public void OptimizeForGameProfile(GameProfile profile)
        {
            // プロファイルに基づくパラメータ調整
            // 例: フォントサイズ、スタイルに基づく調整
        }
    }
    
    /// <summary>
    /// テキスト検出方法
    /// </summary>
    public enum TextDetectionMethod
    {
        /// <summary>
        /// MSERアルゴリズム
        /// </summary>
        Mser = 0,
        
        /// <summary>
        /// エッジベース検出
        /// </summary>
        EdgeBased = 1,
        
        /// <summary>
        /// 複合手法
        /// </summary>
        Combined = 2
    }
}
```

### 2.3 エッジベースのテキスト検出

```csharp
namespace Baketa.Infrastructure.OpenCV.TextDetection
{
    /// <summary>
    /// エッジベースのテキスト検出実装
    /// </summary>
    public class EdgeBasedTextDetector : ITextRegionDetector
    {
        private readonly ILogger<EdgeBasedTextDetector> _logger;
        private TextDetectionParameters _parameters;
        
        public EdgeBasedTextDetector(ILogger<EdgeBasedTextDetector> logger)
        {
            _logger = logger;
            _parameters = new TextDetectionParameters
            {
                Method = TextDetectionMethod.EdgeBased
            };
        }
        
        public async Task<List<TextRegion>> DetectTextRegionsAsync(IImage image)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));
                
            try
            {
                using var mat = await ImageToMatAsync(image);
                using var grayMat = new Mat();
                using var edgesMat = new Mat();
                
                // グレースケール変換
                if (mat.Channels() > 1)
                {
                    Cv2.CvtColor(mat, grayMat, ColorConversionCodes.BGR2GRAY);
                }
                else
                {
                    mat.CopyTo(grayMat);
                }
                
                // エッジ検出（Canny）
                Cv2.Canny(
                    grayMat,
                    edgesMat,
                    _parameters.CannyThreshold1,
                    _parameters.CannyThreshold2);
                
                // 膨張処理でエッジを強調
                var kernel = Cv2.GetStructuringElement(
                    MorphShapes.Rect,
                    new Size(3, 3));
                Cv2.Dilate(edgesMat, edgesMat, kernel);
                
                // 輪郭検出
                Cv2.FindContours(
                    edgesMat,
                    out Point[][] contours,
                    out _,
                    RetrievalModes.List,
                    ContourApproximationModes.ApproxSimple);
                
                // 検出された輪郭からテキスト領域候補を作成
                var textRegions = new List<TextRegion>();
                
                foreach (var contour in contours)
                {
                    var rect = Cv2.BoundingRect(contour);
                    
                    // フィルタリング条件をチェック
                    if (rect.Width < _parameters.MinWidth || rect.Height < _parameters.MinHeight)
                        continue;
                        
                    float aspectRatio = rect.Width / (float)rect.Height;
                    if (aspectRatio < _parameters.MinAspectRatio || aspectRatio > _parameters.MaxAspectRatio)
                        continue;
                    
                    // テキスト領域として追加
                    textRegions.Add(new TextRegion
                    {
                        Bounds = new Rectangle(rect.X, rect.Y, rect.Width, rect.Height),
                        Confidence = CalculateConfidence(grayMat, rect),
                        Orientation = TextOrientation.Horizontal // 簡易実装
                    });
                }
                
                // 重複領域の統合
                MergeOverlappingRegions(textRegions, _parameters.OverlapThreshold);
                
                _logger.LogInformation("エッジベースのテキスト領域検出: {Count}個の領域を検出", textRegions.Count);
                
                return textRegions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "エッジベースのテキスト領域検出中にエラーが発生しました");
                throw new TextDetectionException("テキスト領域の検出に失敗しました", ex);
            }
        }
        
        private float CalculateConfidence(Mat grayMat, Rect region)
        {
            // テキスト存在の信頼度を計算
            // 実装例：エッジ密度、コントラスト変動などから算出
            return 0.7f; // 簡略化した例
        }
        
        private void MergeOverlappingRegions(List<TextRegion> regions, float overlapThreshold)
        {
            // 重複領域の統合アルゴリズム
            // 実装略
        }
        
        public void Configure(TextDetectionParameters parameters)
        {
            _parameters = parameters ?? new TextDetectionParameters();
            _parameters.Method = TextDetectionMethod.EdgeBased;
        }
    }
}
```

### 2.4 複合テキスト検出アルゴリズム

```csharp
namespace Baketa.Infrastructure.OpenCV.TextDetection
{
    /// <summary>
    /// 複合テキスト検出アルゴリズム
    /// </summary>
    public class CombinedTextDetector : ITextRegionDetector
    {
        private readonly ITextRegionDetector _mserDetector;
        private readonly ITextRegionDetector _edgeDetector;
        private readonly ILogger<CombinedTextDetector> _logger;
        private TextDetectionParameters _parameters;
        
        public CombinedTextDetector(
            MserTextDetector mserDetector,
            EdgeBasedTextDetector edgeDetector,
            ILogger<CombinedTextDetector> logger)
        {
            _mserDetector = mserDetector;
            _edgeDetector = edgeDetector;
            _logger = logger;
            _parameters = new TextDetectionParameters
            {
                Method = TextDetectionMethod.Combined
            };
        }
        
        public async Task<List<TextRegion>> DetectTextRegionsAsync(IImage image)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));
                
            try
            {
                // MSERによる検出
                _mserDetector.Configure(_parameters);
                var mserRegions = await _mserDetector.DetectTextRegionsAsync(image);
                
                // エッジベースの検出
                _edgeDetector.Configure(_parameters);
                var edgeRegions = await _edgeDetector.DetectTextRegionsAsync(image);
                
                // 両方の結果を統合
                var combinedRegions = new List<TextRegion>();
                combinedRegions.AddRange(mserRegions);
                combinedRegions.AddRange(edgeRegions);
                
                // 重複排除と信頼度の統合
                var mergedRegions = MergeDetectionResults(combinedRegions);
                
                _logger.LogInformation(
                    "複合テキスト検出: MSER={MserCount}, Edge={EdgeCount}, 統合後={MergedCount}",
                    mserRegions.Count, edgeRegions.Count, mergedRegions.Count);
                
                return mergedRegions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "複合テキスト検出中にエラーが発生しました");
                throw new TextDetectionException("テキスト領域の検出に失敗しました", ex);
            }
        }
        
        private List<TextRegion> MergeDetectionResults(List<TextRegion> regions)
        {
            // 検出結果の統合アルゴリズム
            // 重複する領域を検出し、信頼度の高い方を優先または統合
            
            // 実装略
            
            return regions;
        }
        
        public void Configure(TextDetectionParameters parameters)
        {
            _parameters = parameters ?? new TextDetectionParameters();
            _parameters.Method = TextDetectionMethod.Combined;
        }
    }
}
```

## 3. テキスト検出ファクトリ

```csharp
namespace Baketa.Infrastructure.OpenCV.TextDetection
{
    /// <summary>
    /// テキスト検出アルゴリズムのファクトリ
    /// </summary>
    public class TextDetectorFactory
    {
        private readonly IServiceProvider _serviceProvider;
        
        public TextDetectorFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }
        
        /// <summary>
        /// 指定された方法に基づいてテキスト検出器を作成
        /// </summary>
        public ITextRegionDetector CreateDetector(TextDetectionMethod method)
        {
            return method switch
            {
                TextDetectionMethod.Mser => 
                    _serviceProvider.GetRequiredService<MserTextDetector>(),
                    
                TextDetectionMethod.EdgeBased => 
                    _serviceProvider.GetRequiredService<EdgeBasedTextDetector>(),
                    
                TextDetectionMethod.Combined => 
                    _serviceProvider.GetRequiredService<CombinedTextDetector>(),
                    
                _ => throw new ArgumentException($"不明なテキスト検出方法: {method}")
            };
        }
    }
}
```

## 4. 依存関係登録

```csharp
namespace Baketa.Infrastructure.OpenCV.DI
{
    /// <summary>
    /// OpenCV関連の依存関係登録拡張メソッド
    /// </summary>
    public static class OpenCvServiceExtensions
    {
        /// <summary>
        /// OpenCV関連のサービスを登録
        /// </summary>
        public static IServiceCollection AddOpenCvServices(this IServiceCollection services)
        {
            // OpenCVラッパー
            services.AddSingleton<IOpenCvWrapper, OpenCvWrapper>();
            
            // 画像処理
            services.AddSingleton<IImageProcessor, OpenCvImageProcessor>();
            services.AddSingleton<IImageProcessingPipeline, ImageProcessingPipeline>();
            
            // フィルター
            services.AddTransient<GrayscaleFilter>();
            services.AddTransient<NoiseReductionFilter>();
            services.AddTransient<ContrastEnhancementFilter>();
            services.AddTransient<BinarizationFilter>();
            services.AddTransient<MorphologyFilter>();
            
            // フィルター拡張
            services.AddSingleton<IFilterExtensionPoint, FilterExtensionManager>();
            
            // テキスト検出
            services.AddSingleton<MserTextDetector>();
            services.AddSingleton<EdgeBasedTextDetector>();
            services.AddSingleton<CombinedTextDetector>();
            services.AddSingleton<TextDetectorFactory>();
            
            // フィルターパイプライン初期化
            services.AddSingleton<IStartupTask, OpenCvPipelineInitializer>();
            
            return services;
        }
    }
    
    /// <summary>
    /// OpenCVパイプライン初期化タスク
    /// </summary>
    public class OpenCvPipelineInitializer : IStartupTask
    {
        private readonly IImageProcessingPipeline _pipeline;
        private readonly GrayscaleFilter _grayscaleFilter;
        private readonly NoiseReductionFilter _noiseReductionFilter;
        private readonly ContrastEnhancementFilter _contrastEnhancementFilter;
        private readonly BinarizationFilter _binarizationFilter;
        private readonly MorphologyFilter _morphologyFilter;
        private readonly ILogger<OpenCvPipelineInitializer> _logger;
        
        public OpenCvPipelineInitializer(
            IImageProcessingPipeline pipeline,
            GrayscaleFilter grayscaleFilter,
            NoiseReductionFilter noiseReductionFilter,
            ContrastEnhancementFilter contrastEnhancementFilter,
            BinarizationFilter binarizationFilter,
            MorphologyFilter morphologyFilter,
            ILogger<OpenCvPipelineInitializer> logger)
        {
            _pipeline = pipeline;
            _grayscaleFilter = grayscaleFilter;
            _noiseReductionFilter = noiseReductionFilter;
            _contrastEnhancementFilter = contrastEnhancementFilter;
            _binarizationFilter = binarizationFilter;
            _morphologyFilter = morphologyFilter;
            _logger = logger;
        }
        
        public Task ExecuteAsync()
        {
            try
            {
                // デフォルトパイプラインを構成
                _pipeline.AddFilter(_grayscaleFilter);
                _pipeline.AddFilter(_noiseReductionFilter);
                _pipeline.AddFilter(_contrastEnhancementFilter);
                _pipeline.AddFilter(_binarizationFilter);
                _pipeline.AddFilter(_morphologyFilter);
                
                // 初期設定
                _pipeline.Configure(new ImageProcessingPipelineOptions
                {
                    EnabledFilters = new HashSet<string>
                    {
                        _grayscaleFilter.Name,
                        _noiseReductionFilter.Name,
                        _contrastEnhancementFilter.Name,
                        _binarizationFilter.Name
                    }
                });
                
                _logger.LogInformation("OpenCVパイプラインを初期化しました");
                
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OpenCVパイプラインの初期化に失敗しました");
                throw;
            }
        }
    }
}
```