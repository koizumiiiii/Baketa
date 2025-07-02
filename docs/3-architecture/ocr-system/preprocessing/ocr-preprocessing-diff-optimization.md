# OCR前処理システム - 差分検出と最適化

*最終更新: 2025年4月24日*

## 1. 差分検出システム

### 1.1 差分検出の目的と重要性

差分検出システムは、OCR処理の効率化のために画面の変化を検出し、変更があった部分のみを処理する機能を提供します。これにより、以下の利点が得られます：

- CPU/GPU使用率の低減
- 処理レイテンシの短縮
- バッテリー消費の抑制
- 全体的なパフォーマンスの向上

### 1.2 差分検出パラメータ

```csharp
namespace Baketa.Core.Models
{
    /// <summary>
    /// 差分検出パラメータ
    /// </summary>
    public class DifferenceDetectionParameters
    {
        /// <summary>
        /// ヒストグラム比較の閾値 (0.0～1.0)
        /// </summary>
        public float HistogramThreshold { get; set; } = 0.15f;
        
        /// <summary>
        /// サンプリング戦略
        /// </summary>
        public SamplingStrategy SamplingStrategy { get; set; } = SamplingStrategy.Grid;
        
        /// <summary>
        /// サンプリング密度
        /// </summary>
        public int SamplingDensity { get; set; } = 20;
        
        /// <summary>
        /// 色差の閾値 (0～255)
        /// </summary>
        public float ColorThreshold { get; set; } = 30.0f;
        
        /// <summary>
        /// 早期終了閾値 (0.0～1.0)
        /// </summary>
        public float EarlyTerminationThreshold { get; set; } = 0.1f;
        
        /// <summary>
        /// 差分ピクセル比率の閾値 (0.0～1.0)
        /// </summary>
        public float DifferenceThreshold { get; set; } = 0.05f;
        
        /// <summary>
        /// テキスト領域に焦点を当てるかどうか
        /// </summary>
        public bool FocusOnTextRegions { get; set; } = true;
        
        /// <summary>
        /// 検出されたテキスト領域
        /// </summary>
        public List<Rectangle> TextRegions { get; set; } = new List<Rectangle>();
    }
    
    /// <summary>
    /// サンプリング戦略
    /// </summary>
    public enum SamplingStrategy
    {
        /// <summary>
        /// グリッドパターンでサンプリング
        /// </summary>
        Grid = 0,
        
        /// <summary>
        /// ランダムサンプリング
        /// </summary>
        Random = 1,
        
        /// <summary>
        /// テキスト領域重点サンプリング
        /// </summary>
        TextRegionFocus = 2
    }
}
```

### 1.3 ヒストグラム比較による差分検出

```csharp
namespace Baketa.Infrastructure.OpenCV.DifferenceDetection
{
    /// <summary>
    /// ヒストグラム比較による差分検出
    /// </summary>
    public class HistogramDifferenceDetector : IDifferenceDetector
    {
        private readonly ILogger<HistogramDifferenceDetector> _logger;
        
        public HistogramDifferenceDetector(ILogger<HistogramDifferenceDetector> logger)
        {
            _logger = logger;
        }
        
        public async Task<bool> HasSignificantChangesAsync(IImage previous, IImage current, DifferenceDetectionParameters parameters)
        {
            if (previous == null || current == null)
                throw new ArgumentNullException(previous == null ? nameof(previous) : nameof(current));
                
            try
            {
                using var prevMat = await ImageToMatAsync(previous);
                using var currMat = await ImageToMatAsync(current);
                using var prevHist = new Mat();
                using var currHist = new Mat();
                
                // グレースケール変換
                using var prevGray = new Mat();
                using var currGray = new Mat();
                Cv2.CvtColor(prevMat, prevGray, ColorConversionCodes.BGR2GRAY);
                Cv2.CvtColor(currMat, currGray, ColorConversionCodes.BGR2GRAY);
                
                // ヒストグラム計算
                int[] histSize = { 256 };
                Rangef[] ranges = { new Rangef(0, 256) };
                int[] channels = { 0 };
                
                Cv2.CalcHist(
                    new[] { prevGray },
                    channels,
                    null,
                    prevHist,
                    1,
                    histSize,
                    ranges);
                    
                Cv2.CalcHist(
                    new[] { currGray },
                    channels,
                    null,
                    currHist,
                    1,
                    histSize,
                    ranges);
                
                // ヒストグラム正規化
                Cv2.Normalize(prevHist, prevHist, 0, 1, NormTypes.MinMax);
                Cv2.Normalize(currHist, currHist, 0, 1, NormTypes.MinMax);
                
                // ヒストグラム比較
                double correlation = Cv2.CompareHist(prevHist, currHist, HistCompMethods.Correl);
                double difference = 1.0 - correlation;
                
                _logger.LogDebug("ヒストグラム差分: {Difference}", difference);
                
                // 閾値との比較
                bool hasChanges = difference > parameters.HistogramThreshold;
                
                return hasChanges;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ヒストグラム比較中にエラーが発生しました");
                return true; // エラー時は安全のため変更ありと判断
            }
        }
    }
}
```

### 1.4 サンプリングベースの差分検出

```csharp
namespace Baketa.Infrastructure.OpenCV.DifferenceDetection
{
    /// <summary>
    /// サンプリングベースの差分検出
    /// </summary>
    public class SamplingDifferenceDetector : IDifferenceDetector
    {
        private readonly ILogger<SamplingDifferenceDetector> _logger;
        
        public SamplingDifferenceDetector(ILogger<SamplingDifferenceDetector> logger)
        {
            _logger = logger;
        }
        
        public async Task<bool> HasSignificantChangesAsync(IImage previous, IImage current, DifferenceDetectionParameters parameters)
        {
            if (previous == null || current == null)
                throw new ArgumentNullException(previous == null ? nameof(previous) : nameof(current));
                
            try
            {
                using var prevMat = await ImageToMatAsync(previous);
                using var currMat = await ImageToMatAsync(current);
                
                int width = prevMat.Width;
                int height = prevMat.Height;
                
                // サンプリングパターンの選択
                var samplingPoints = GetSamplingPoints(
                    width, 
                    height, 
                    parameters.SamplingStrategy, 
                    parameters.SamplingDensity,
                    parameters.FocusOnTextRegions ? parameters.TextRegions : null);
                
                int differentPixels = 0;
                
                // サンプリングポイントでのピクセル比較
                foreach (var point in samplingPoints)
                {
                    if (point.X >= width || point.Y >= height)
                        continue;
                        
                    var prevColor = prevMat.Get<Vec3b>(point.Y, point.X);
                    var currColor = currMat.Get<Vec3b>(point.Y, point.X);
                    
                    // 色差の計算
                    double colorDifference = CalculateColorDifference(prevColor, currColor);
                    
                    if (colorDifference > parameters.ColorThreshold)
                    {
                        differentPixels++;
                        
                        // 早期終了判定
                        if ((double)differentPixels / samplingPoints.Count > parameters.EarlyTerminationThreshold)
                        {
                            _logger.LogDebug("早期終了: 十分な差分を検出 ({DiffPixels}/{TotalSamples})", 
                                differentPixels, samplingPoints.Count);
                            return true;
                        }
                    }
                }
                
                // 差分ピクセルの割合が閾値を超えるか判定
                double diffRatio = (double)differentPixels / samplingPoints.Count;
                bool hasChanges = diffRatio > parameters.DifferenceThreshold;
                
                _logger.LogDebug("サンプリング差分: {DiffRatio} ({DiffPixels}/{TotalSamples})", 
                    diffRatio, differentPixels, samplingPoints.Count);
                
                return hasChanges;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "サンプリング差分検出中にエラーが発生しました");
                return true; // エラー時は安全のため変更ありと判断
            }
        }
        
        private List<Point> GetSamplingPoints(
            int width, 
            int height, 
            SamplingStrategy strategy, 
            int density,
            List<Rectangle> textRegions = null)
        {
            var points = new List<Point>();
            var random = new Random();
            
            switch (strategy)
            {
                case SamplingStrategy.Grid:
                    // グリッドパターンのサンプリング
                    int stepX = Math.Max(1, width / density);
                    int stepY = Math.Max(1, height / density);
                    
                    for (int y = 0; y < height; y += stepY)
                    {
                        for (int x = 0; x < width; x += stepX)
                        {
                            points.Add(new Point(x, y));
                        }
                    }
                    break;
                    
                case SamplingStrategy.Random:
                    // ランダムサンプリング
                    int sampleCount = (width * height) / (density * density);
                    
                    for (int i = 0; i < sampleCount; i++)
                    {
                        int x = random.Next(width);
                        int y = random.Next(height);
                        points.Add(new Point(x, y));
                    }
                    break;
                    
                case SamplingStrategy.TextRegionFocus:
                    // テキスト領域重点サンプリング
                    if (textRegions != null && textRegions.Count > 0)
                    {
                        // テキスト領域内のサンプリング（高密度）
                        foreach (var region in textRegions)
                        {
                            int regionSampleCount = (region.Width * region.Height) / ((density / 2) * (density / 2));
                            
                            for (int i = 0; i < regionSampleCount; i++)
                            {
                                int x = region.X + random.Next(region.Width);
                                int y = region.Y + random.Next(region.Height);
                                points.Add(new Point(x, y));
                            }
                        }
                        
                        // 残りの領域のサンプリング（低密度）
                        int remainingSampleCount = (width * height) / (density * density * 2);
                        
                        for (int i = 0; i < remainingSampleCount; i++)
                        {
                            int x = random.Next(width);
                            int y = random.Next(height);
                            
                            // テキスト領域外のポイントを追加
                            if (!IsInAnyTextRegion(x, y, textRegions))
                            {
                                points.Add(new Point(x, y));
                            }
                        }
                    }
                    else
                    {
                        // テキスト領域がない場合はグリッドサンプリングにフォールバック
                        int stepX = Math.Max(1, width / density);
                        int stepY = Math.Max(1, height / density);
                        
                        for (int y = 0; y < height; y += stepY)
                        {
                            for (int x = 0; x < width; x += stepX)
                            {
                                points.Add(new Point(x, y));
                            }
                        }
                    }
                    break;
            }
            
            return points;
        }
        
        private bool IsInAnyTextRegion(int x, int y, List<Rectangle> textRegions)
        {
            foreach (var region in textRegions)
            {
                if (x >= region.X && x < region.X + region.Width &&
                    y >= region.Y && y < region.Y + region.Height)
                {
                    return true;
                }
            }
            
            return false;
        }
        
        private double CalculateColorDifference(Vec3b color1, Vec3b color2)
        {
            // ユークリッド距離による色差計算
            double sumSquares = 
                Math.Pow(color1[0] - color2[0], 2) +
                Math.Pow(color1[1] - color2[1], 2) +
                Math.Pow(color1[2] - color2[2], 2);
                
            return Math.Sqrt(sumSquares);
        }
    }
}
```

### 1.5 複合差分検出

```csharp
namespace Baketa.Infrastructure.OpenCV.DifferenceDetection
{
    /// <summary>
    /// 複合差分検出（ヒストグラムとサンプリングの組み合わせ）
    /// </summary>
    public class CompositeDifferenceDetector : IDifferenceDetector
    {
        private readonly HistogramDifferenceDetector _histogramDetector;
        private readonly SamplingDifferenceDetector _samplingDetector;
        private readonly ILogger<CompositeDifferenceDetector> _logger;
        
        public CompositeDifferenceDetector(
            HistogramDifferenceDetector histogramDetector,
            SamplingDifferenceDetector samplingDetector,
            ILogger<CompositeDifferenceDetector> logger)
        {
            _histogramDetector = histogramDetector;
            _samplingDetector = samplingDetector;
            _logger = logger;
        }
        
        public async Task<bool> HasSignificantChangesAsync(IImage previous, IImage current, DifferenceDetectionParameters parameters)
        {
            if (previous == null || current == null)
                throw new ArgumentNullException(previous == null ? nameof(previous) : nameof(current));
                
            try
            {
                // ヒストグラム比較（高速な全体比較）
                bool histogramDifference = await _histogramDetector.HasSignificantChangesAsync(
                    previous, current, parameters);
                
                if (histogramDifference)
                {
                    _logger.LogDebug("ヒストグラム差分検出: 変更あり");
                    
                    // サンプリング比較（詳細な差分検出）
                    bool samplingDifference = await _samplingDetector.HasSignificantChangesAsync(
                        previous, current, parameters);
                    
                    _logger.LogDebug("サンプリング差分検出: {HasChanges}", 
                        samplingDifference ? "変更あり" : "変更なし");
                    
                    return samplingDifference;
                }
                
                _logger.LogDebug("ヒストグラム差分検出: 変更なし");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "複合差分検出中にエラーが発生しました");
                return true; // エラー時は安全のため変更ありと判断
            }
        }
    }
}
```

## 2. パラメータ最適化システム

### 2.1 最適化設計の目的

パラメータ最適化システムは、OCR結果のフィードバックに基づいて前処理パラメータを自動調整することで、継続的に精度を向上させる機能を提供します。その主な目的は：

- OCR精度の継続的な向上
- ゲーム固有の特性への自動適応
- ユーザー手動設定の必要性の低減
- リソース使用量と精度のバランス最適化

### 2.2 OCR最適化パラメータ

```csharp
namespace Baketa.Core.Models
{
    /// <summary>
    /// OCR最適化パラメータ
    /// </summary>
    public class OcrOptimizationParameters
    {
        /// <summary>
        /// 最適化の種類
        /// </summary>
        public OptimizationType Type { get; set; } = OptimizationType.Accuracy;
        
        /// <summary>
        /// 最適化強度 (0.0～1.0)
        /// </summary>
        public float OptimizationStrength { get; set; } = 0.5f;
        
        /// <summary>
        /// 学習率 (0.0～1.0)
        /// </summary>
        public float LearningRate { get; set; } = 0.1f;
        
        /// <summary>
        /// 最適化間隔（フレーム数）
        /// </summary>
        public int OptimizationInterval { get; set; } = 20;
        
        /// <summary>
        /// 最適化のためのOCR結果
        /// </summary>
        public List<OcrResult> OcrResults { get; set; } = new List<OcrResult>();
        
        /// <summary>
        /// 最適化対象のパラメータ
        /// </summary>
        public HashSet<string> TargetParameters { get; set; } = new HashSet<string>();
    }
    
    /// <summary>
    /// 最適化の種類
    /// </summary>
    public enum OptimizationType
    {
        /// <summary>
        /// 精度重視
        /// </summary>
        Accuracy = 0,
        
        /// <summary>
        /// パフォーマンス重視
        /// </summary>
        Performance = 1,
        
        /// <summary>
        /// バランス型
        /// </summary>
        Balanced = 2
    }
}
```

### 2.3 フィードバックベースの最適化

```csharp
namespace Baketa.Application.Services.Ocr
{
    /// <summary>
    /// フィードバックベースのOCR最適化サービス
    /// </summary>
    public class OcrParameterOptimizer : IOcrParameterOptimizer
    {
        private readonly ILogger<OcrParameterOptimizer> _logger;
        private readonly IGameProfileManager _profileManager;
        
        public OcrParameterOptimizer(
            ILogger<OcrParameterOptimizer> logger,
            IGameProfileManager profileManager)
        {
            _logger = logger;
            _profileManager = profileManager;
        }
        
        public async Task<ImageProcessingParameters> OptimizeParametersAsync(
            GameProfile gameProfile,
            List<OcrResult> ocrResults,
            ImageProcessingParameters currentParameters,
            OcrOptimizationParameters optimizationParameters)
        {
            if (gameProfile == null)
                throw new ArgumentNullException(nameof(gameProfile));
                
            if (currentParameters == null)
                throw new ArgumentNullException(nameof(currentParameters));
                
            if (ocrResults == null || ocrResults.Count == 0)
                return currentParameters;
                
            try
            {
                // 現在のパラメータのコピーを作成
                var optimizedParameters = currentParameters.Clone();
                
                // OCR結果の分析
                var averageConfidence = ocrResults.SelectMany(r => r.TextRegions)
                    .Average(t => t.Confidence);
                var textRegionCount = ocrResults.Average(r => r.TextRegions.Count);
                
                _logger.LogDebug(
                    "OCR結果分析: 平均信頼度={AvgConfidence}, テキスト領域数={RegionCount}", 
                    averageConfidence, textRegionCount);
                
                // 最適化タイプに基づく調整
                switch (optimizationParameters.Type)
                {
                    case OptimizationType.Accuracy:
                        OptimizeForAccuracy(
                            optimizedParameters, 
                            averageConfidence, 
                            textRegionCount, 
                            optimizationParameters);
                        break;
                        
                    case OptimizationType.Performance:
                        OptimizeForPerformance(
                            optimizedParameters, 
                            averageConfidence, 
                            textRegionCount, 
                            optimizationParameters);
                        break;
                        
                    case OptimizationType.Balanced:
                        OptimizeForBalance(
                            optimizedParameters, 
                            averageConfidence, 
                            textRegionCount, 
                            optimizationParameters);
                        break;
                }
                
                // 最適化結果の検証
                ValidateParameters(optimizedParameters);
                
                // ゲームプロファイルの更新
                await UpdateGameProfileAsync(gameProfile, optimizedParameters);
                
                _logger.LogInformation("OCRパラメータを最適化しました: ゲームID={GameId}", gameProfile.GameId);
                
                return optimizedParameters;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OCRパラメータの最適化中にエラーが発生しました");
                return currentParameters;
            }
        }
        
        private void OptimizeForAccuracy(
            ImageProcessingParameters parameters,
            float averageConfidence,
            float textRegionCount,
            OcrOptimizationParameters optimizationParameters)
        {
            float strength = optimizationParameters.OptimizationStrength;
            float learningRate = optimizationParameters.LearningRate;
            
            // 信頼度に基づく調整
            if (averageConfidence < 0.6f)
            {
                // 低信頼度の場合、コントラストと明るさを調整
                if (optimizationParameters.TargetParameters.Contains("Contrast"))
                {
                    parameters.Contrast += (1.2f - parameters.Contrast) * learningRate * strength;
                }
                
                if (optimizationParameters.TargetParameters.Contains("Brightness"))
                {
                    if (parameters.Brightness < 0)
                    {
                        parameters.Brightness += Math.Abs(parameters.Brightness) * learningRate * strength;
                    }
                    else
                    {
                        parameters.Brightness -= parameters.Brightness * learningRate * strength;
                    }
                }
                
                // 二値化の適応的閾値を有効化
                if (optimizationParameters.TargetParameters.Contains("UseAdaptiveThreshold"))
                {
                    parameters.UseAdaptiveThreshold = true;
                }
                
                // ノイズ除去を強化
                if (optimizationParameters.TargetParameters.Contains("NoiseReduction"))
                {
                    parameters.NoiseReduction += (1.0f - parameters.NoiseReduction) * learningRate * strength;
                }
            }
            else if (averageConfidence > 0.9f)
            {
                // 高信頼度の場合、現在の設定を維持または微調整
                if (optimizationParameters.TargetParameters.Contains("NoiseReduction"))
                {
                    parameters.NoiseReduction -= parameters.NoiseReduction * 0.05f * learningRate * strength;
                }
            }
            
            // テキスト領域数に基づく調整
            if (textRegionCount < 3)
            {
                // テキスト領域が少ない場合、検出感度を上げる
                if (optimizationParameters.TargetParameters.Contains("MorphologyKernelSize"))
                {
                    parameters.MorphologyKernelSize = Math.Max(1, parameters.MorphologyKernelSize - 1);
                }
            }
        }
        
        private void OptimizeForPerformance(
            ImageProcessingParameters parameters,
            float averageConfidence,
            float textRegionCount,
            OcrOptimizationParameters optimizationParameters)
        {
            float strength = optimizationParameters.OptimizationStrength;
            float learningRate = optimizationParameters.LearningRate;
            
            // パフォーマンス最適化：必要最小限の処理のみを有効化
            if (averageConfidence > 0.7f)
            {
                // 十分な精度がある場合、処理を軽減
                if (optimizationParameters.TargetParameters.Contains("NoiseReduction"))
                {
                    parameters.NoiseReduction *= (1.0f - 0.1f * learningRate * strength);
                }
                
                if (optimizationParameters.TargetParameters.Contains("UseAdaptiveThreshold"))
                {
                    parameters.UseAdaptiveThreshold = false;
                }
                
                if (optimizationParameters.TargetParameters.Contains("ApplyMorphology"))
                {
                    parameters.ApplyMorphology = false;
                }
            }
            else
            {
                // 精度が低い場合、最小限の処理を維持
                if (optimizationParameters.TargetParameters.Contains("Contrast"))
                {
                    parameters.Contrast = Math.Max(1.0f, parameters.Contrast);
                }
                
                if (optimizationParameters.TargetParameters.Contains("UseAdaptiveThreshold"))
                {
                    parameters.UseAdaptiveThreshold = true;
                }
            }
        }
        
        private void OptimizeForBalance(
            ImageProcessingParameters parameters,
            float averageConfidence,
            float textRegionCount,
            OcrOptimizationParameters optimizationParameters)
        {
            float strength = optimizationParameters.OptimizationStrength;
            float learningRate = optimizationParameters.LearningRate;
            
            // バランス型：精度とパフォーマンスのバランスを取る
            if (averageConfidence < 0.6f)
            {
                // 精度を優先
                OptimizeForAccuracy(parameters, averageConfidence, textRegionCount, 
                    new OcrOptimizationParameters 
                    { 
                        OptimizationStrength = strength * 0.7f,
                        LearningRate = learningRate,
                        TargetParameters = optimizationParameters.TargetParameters
                    });
            }
            else if (averageConfidence > 0.85f)
            {
                // パフォーマンスを優先
                OptimizeForPerformance(parameters, averageConfidence, textRegionCount, 
                    new OcrOptimizationParameters 
                    { 
                        OptimizationStrength = strength * 0.7f,
                        LearningRate = learningRate,
                        TargetParameters = optimizationParameters.TargetParameters
                    });
            }
            else
            {
                // 中間領域：わずかに精度寄りの調整
                OptimizeForAccuracy(parameters, averageConfidence, textRegionCount, 
                    new OcrOptimizationParameters 
                    { 
                        OptimizationStrength = strength * 0.3f,
                        LearningRate = learningRate,
                        TargetParameters = optimizationParameters.TargetParameters
                    });
            }
        }
        
        private void ValidateParameters(ImageProcessingParameters parameters)
        {
            // パラメータの範囲チェックと修正
            parameters.Contrast = Math.Clamp(parameters.Contrast, 0.5f, 2.0f);
            parameters.Brightness = Math.Clamp(parameters.Brightness, -50f, 50f);
            parameters.NoiseReduction = Math.Clamp(parameters.NoiseReduction, 0f, 1.0f);
            parameters.MorphologyKernelSize = Math.Clamp(parameters.MorphologyKernelSize, 1, 7);
        }
        
        private async Task UpdateGameProfileAsync(GameProfile gameProfile, ImageProcessingParameters parameters)
        {
            gameProfile.ImageProcessingParameters = parameters;
            gameProfile.LastModified = DateTime.UtcNow;
            await _profileManager.SaveGameProfileAsync(gameProfile);
        }
    }
}
```