# 適応的キャプチャシステム詳細設計書

## 1. システム概要

### 1.1 目的
GPU環境に応じて最適なキャプチャ手法を自動選択し、どのハードウェア構成でも確実に動作する適応的キャプチャシステムを実装する。

### 1.2 主要コンポーネント
1. **GPU環境検出システム** - ハードウェア環境の自動識別
2. **戦略パターンシステム** - 環境別最適キャプチャ戦略
3. **フォールバック機構** - 多段階の確実動作保証
4. **テキスト領域検出** - 効率的なROI処理

## 2. アーキテクチャ設計

### 2.1 レイヤー構成

```
┌─────────────────────────────────────────────────────────┐
│                    Baketa.UI Layer                      │
│              (AdaptiveCaptureViewModel)                 │
└────────────────────────┬────────────────────────────────┘
                         │
┌────────────────────────┴────────────────────────────────┐
│               Baketa.Application Layer                  │
│         (AdaptiveCaptureService, Orchestrator)          │
└────────────────────────┬────────────────────────────────┘
                         │
┌────────────────────────┴────────────────────────────────┐
│           Baketa.Infrastructure.Platform Layer          │
│  (GPUEnvironmentDetector, CaptureStrategyFactory)       │
└────────────────────────┬────────────────────────────────┘
                         │
┌────────────────────────┴────────────────────────────────┐
│                  Baketa.Core Layer                      │
│       (IAdaptiveCaptureService, ICaptureStrategy)      │
└─────────────────────────────────────────────────────────┘
```

### 2.2 クラス設計

#### 2.2.1 Core層インターフェース

```csharp
namespace Baketa.Core.Abstractions.Capture
{
    /// <summary>
    /// 適応的キャプチャサービスの抽象インターフェース
    /// </summary>
    public interface IAdaptiveCaptureService
    {
        /// <summary>
        /// 環境に応じた最適手法でキャプチャを実行
        /// </summary>
        Task<AdaptiveCaptureResult> CaptureAsync(IntPtr hwnd, CaptureOptions options);
        
        /// <summary>
        /// GPU環境を検出
        /// </summary>
        Task<GPUEnvironmentInfo> DetectGPUEnvironmentAsync();
        
        /// <summary>
        /// 最適な戦略を選択
        /// </summary>
        Task<ICaptureStrategy> SelectOptimalStrategyAsync(GPUEnvironmentInfo environment);
    }

    /// <summary>
    /// キャプチャ戦略の抽象インターフェース
    /// </summary>
    public interface ICaptureStrategy
    {
        /// <summary>
        /// 戦略名
        /// </summary>
        string StrategyName { get; }
        
        /// <summary>
        /// この戦略が適用可能かチェック
        /// </summary>
        bool CanApply(GPUEnvironmentInfo environment, IntPtr hwnd);
        
        /// <summary>
        /// キャプチャを実行
        /// </summary>
        Task<CaptureStrategyResult> ExecuteCaptureAsync(IntPtr hwnd, CaptureOptions options);
    }

    /// <summary>
    /// テキスト領域検出器のインターフェース
    /// </summary>
    public interface ITextRegionDetector
    {
        /// <summary>
        /// 画像からテキスト領域を検出
        /// </summary>
        Task<IList<Rectangle>> DetectTextRegionsAsync(IWindowsImage image);
        
        /// <summary>
        /// 検出パラメータを調整
        /// </summary>
        void ConfigureDetection(TextDetectionConfig config);
    }
}
```

#### 2.2.2 データモデル

```csharp
namespace Baketa.Core.Models.Capture
{
    /// <summary>
    /// GPU環境情報
    /// </summary>
    public class GPUEnvironmentInfo
    {
        public bool IsIntegratedGPU { get; set; }
        public bool IsDedicatedGPU { get; set; }
        public bool HasDirectX11Support { get; set; }
        public string GPUName { get; set; }
        public long AvailableMemoryMB { get; set; }
        public uint MaximumTexture2DDimension { get; set; }
        public bool IsMultiGPUEnvironment { get; set; }
        public bool HasHDRSupport { get; set; }
        public string ColorSpaceSupport { get; set; }
        public DirectXFeatureLevel FeatureLevel { get; set; }
        public IList<GPUAdapter> AvailableAdapters { get; set; }
    }

    /// <summary>
    /// キャプチャ結果
    /// </summary>
    public class AdaptiveCaptureResult
    {
        public bool Success { get; set; }
        public IList<IWindowsImage> CapturedImages { get; set; }
        public CaptureStrategyUsed StrategyUsed { get; set; }
        public GPUEnvironmentInfo GPUEnvironment { get; set; }
        public TimeSpan ProcessingTime { get; set; }
        public IList<string> FallbacksAttempted { get; set; }
        public IList<Rectangle> DetectedTextRegions { get; set; }
        public CaptureMetrics Metrics { get; set; }
    }

    /// <summary>
    /// キャプチャオプション
    /// </summary>
    public class CaptureOptions
    {
        public bool AllowDirectFullScreen { get; set; } = true;
        public bool AllowROIProcessing { get; set; } = true;
        public bool AllowSoftwareFallback { get; set; } = true;
        public float ROIScaleFactor { get; set; } = 0.25f;
        public int MaxRetryAttempts { get; set; } = 3;
        public bool EnableHDRProcessing { get; set; } = true;
        public int TDRTimeoutMs { get; set; } = 2000;
    }
}
```

### 2.3 実装クラス設計

#### 2.3.1 GPU環境検出器

```csharp
namespace Baketa.Infrastructure.Platform.Windows.GPU
{
    /// <summary>
    /// GPU環境を検出する実装クラス
    /// </summary>
    public class GPUEnvironmentDetector : IGPUEnvironmentDetector
    {
        private readonly ILogger<GPUEnvironmentDetector> _logger;
        
        public async Task<GPUEnvironmentInfo> DetectEnvironmentAsync()
        {
            var info = new GPUEnvironmentInfo();
            
            // 1. DirectXサポートレベル確認
            await CheckDirectXSupportAsync(info);
            
            // 2. 利用可能なGPUアダプター列挙
            await EnumerateAdaptersAsync(info);
            
            // 3. GPU種別判定（統合/専用）
            DetermineGPUType(info);
            
            // 4. テクスチャサイズ制限確認
            await CheckTextureLimitsAsync(info);
            
            // 5. HDR・色空間サポート確認
            await CheckDisplayCapabilitiesAsync(info);
            
            return info;
        }
        
        private async Task CheckDirectXSupportAsync(GPUEnvironmentInfo info)
        {
            // D3D11CreateDevice を使用してFeature Level確認
            // D3D11_FEATURE_DATA_D3D10_X_HARDWARE_OPTIONS取得
        }
        
        private async Task EnumerateAdaptersAsync(GPUEnvironmentInfo info)
        {
            // IDXGIFactory1::EnumAdapters1 使用
            // 各アダプターのDESC情報取得
        }
    }
}
```

#### 2.3.2 キャプチャ戦略実装

```csharp
namespace Baketa.Infrastructure.Platform.Windows.Capture.Strategies
{
    /// <summary>
    /// 統合GPU向け直接キャプチャ戦略
    /// </summary>
    public class DirectFullScreenCaptureStrategy : ICaptureStrategy
    {
        public string StrategyName => "DirectFullScreen";
        
        public bool CanApply(GPUEnvironmentInfo environment, IntPtr hwnd)
        {
            // 統合GPUかつ十分なテクスチャサイズサポート
            return environment.IsIntegratedGPU && 
                   environment.MaximumTexture2DDimension >= 4096;
        }
        
        public async Task<CaptureStrategyResult> ExecuteCaptureAsync(
            IntPtr hwnd, CaptureOptions options)
        {
            // Windows Graphics Capture APIで直接キャプチャ
            // 既存のNativeWindowsCaptureWrapperを使用
        }
    }
    
    /// <summary>
    /// 専用GPU向けROIベースキャプチャ戦略
    /// </summary>
    public class ROIBasedCaptureStrategy : ICaptureStrategy
    {
        private readonly ITextRegionDetector _textDetector;
        
        public string StrategyName => "ROIBased";
        
        public bool CanApply(GPUEnvironmentInfo environment, IntPtr hwnd)
        {
            // 専用GPUまたは大画面での制約回避が必要
            return environment.IsDedicatedGPU || 
                   environment.MaximumTexture2DDimension < 4096;
        }
        
        public async Task<CaptureStrategyResult> ExecuteCaptureAsync(
            IntPtr hwnd, CaptureOptions options)
        {
            // 1. 低解像度スキャン
            var lowResImage = await CaptureLowResolutionAsync(hwnd, options.ROIScaleFactor);
            
            // 2. テキスト領域検出
            var textRegions = await _textDetector.DetectTextRegionsAsync(lowResImage);
            
            // 3. 高解像度部分キャプチャ
            var results = await CaptureHighResRegionsAsync(hwnd, textRegions);
            
            return new CaptureStrategyResult
            {
                Success = true,
                Images = results,
                TextRegions = textRegions
            };
        }
    }
    
    /// <summary>
    /// ソフトウェアフォールバック戦略
    /// </summary>
    public class PrintWindowFallbackStrategy : ICaptureStrategy
    {
        public string StrategyName => "PrintWindowFallback";
        
        public bool CanApply(GPUEnvironmentInfo environment, IntPtr hwnd)
        {
            // 常に適用可能（最終手段）
            return true;
        }
        
        public async Task<CaptureStrategyResult> ExecuteCaptureAsync(
            IntPtr hwnd, CaptureOptions options)
        {
            // PrintWindow APIを使用した確実なキャプチャ
            // 既存のPrintWindowCaptureServiceを活用
        }
    }
}
```

#### 2.3.3 適応的キャプチャサービス実装

```csharp
namespace Baketa.Application.Services.Capture
{
    /// <summary>
    /// 適応的キャプチャサービスの実装
    /// </summary>
    public class AdaptiveCaptureService : IAdaptiveCaptureService
    {
        private readonly IGPUEnvironmentDetector _gpuDetector;
        private readonly ICaptureStrategyFactory _strategyFactory;
        private readonly ILogger<AdaptiveCaptureService> _logger;
        private readonly IEventAggregator _eventAggregator;
        
        // GPUEnvironmentInfoのキャッシュ（起動時に1回だけ検出）
        private GPUEnvironmentInfo _cachedEnvironment;
        
        public async Task<AdaptiveCaptureResult> CaptureAsync(
            IntPtr hwnd, CaptureOptions options)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new AdaptiveCaptureResult
            {
                FallbacksAttempted = new List<string>()
            };
            
            try
            {
                // 1. GPU環境取得（キャッシュ利用）
                result.GPUEnvironment = _cachedEnvironment ??= 
                    await DetectGPUEnvironmentAsync();
                
                // 2. 戦略選択
                var strategy = await SelectOptimalStrategyAsync(result.GPUEnvironment);
                
                // 3. キャプチャ実行（フォールバック付き）
                var captureResult = await ExecuteWithFallbackAsync(
                    hwnd, options, strategy, result.FallbacksAttempted);
                
                // 4. 結果構築
                result.Success = captureResult.Success;
                result.CapturedImages = captureResult.Images;
                result.StrategyUsed = ParseStrategyUsed(captureResult.StrategyName);
                result.DetectedTextRegions = captureResult.TextRegions;
                result.ProcessingTime = stopwatch.Elapsed;
                
                // 5. メトリクス記録
                RecordMetrics(result);
                
                // 6. イベント発行
                await _eventAggregator.PublishAsync(
                    new CaptureCompletedEvent(result));
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "適応的キャプチャ失敗");
                result.Success = false;
                result.ProcessingTime = stopwatch.Elapsed;
                return result;
            }
        }
        
        private async Task<CaptureStrategyResult> ExecuteWithFallbackAsync(
            IntPtr hwnd, 
            CaptureOptions options, 
            ICaptureStrategy primaryStrategy,
            IList<string> fallbacksAttempted)
        {
            var strategies = _strategyFactory.GetStrategiesInOrder(primaryStrategy);
            
            foreach (var strategy in strategies)
            {
                if (!ShouldTryStrategy(strategy, options))
                    continue;
                    
                try
                {
                    _logger.LogDebug($"戦略実行中: {strategy.StrategyName}");
                    fallbacksAttempted.Add(strategy.StrategyName);
                    
                    var result = await strategy.ExecuteCaptureAsync(hwnd, options);
                    if (result.Success)
                    {
                        _logger.LogInformation($"戦略成功: {strategy.StrategyName}");
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"戦略失敗: {strategy.StrategyName}");
                    
                    // TDR検出
                    if (IsTDRException(ex))
                    {
                        await HandleTDRAsync();
                    }
                }
            }
            
            throw new InvalidOperationException("すべての戦略が失敗しました");
        }
        
        private bool IsTDRException(Exception ex)
        {
            // HRESULTやエラーメッセージからTDRを検出
            return ex.Message.Contains("0x887A0005") || // DXGI_ERROR_DEVICE_REMOVED
                   ex.Message.Contains("0x887A0006");   // DXGI_ERROR_DEVICE_HUNG
        }
        
        private async Task HandleTDRAsync()
        {
            _logger.LogWarning("TDR検出 - GPU回復待機中");
            await Task.Delay(3000); // GPU回復待機
            _cachedEnvironment = null; // 環境情報リセット
        }
    }
}
```

### 2.4 テキスト領域検出システム

```csharp
namespace Baketa.Infrastructure.Imaging
{
    /// <summary>
    /// OpenCVベースのテキスト領域検出器
    /// </summary>
    public class OpenCVTextRegionDetector : ITextRegionDetector
    {
        private readonly IOpenCvWrapper _openCv;
        private TextDetectionConfig _config;
        
        public async Task<IList<Rectangle>> DetectTextRegionsAsync(IWindowsImage image)
        {
            return await Task.Run(() =>
            {
                using var mat = ConvertToMat(image);
                
                // 1. 前処理
                var processed = PreprocessImage(mat);
                
                // 2. エッジ検出
                var edges = DetectEdges(processed);
                
                // 3. 輪郭抽出
                var contours = FindContours(edges);
                
                // 4. テキスト候補フィルタリング
                var textRegions = FilterTextRegions(contours);
                
                // 5. 領域統合
                var mergedRegions = MergeNearbyRegions(textRegions);
                
                // 6. スケール変換（低解像度→高解像度）
                var scaledRegions = ScaleRegions(mergedRegions);
                
                return scaledRegions;
            });
        }
        
        private Mat PreprocessImage(Mat image)
        {
            // グレースケール変換
            var gray = new Mat();
            Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
            
            // ノイズ除去
            var denoised = new Mat();
            Cv2.FastNlMeansDenoising(gray, denoised);
            
            return denoised;
        }
        
        private IList<Rectangle> FilterTextRegions(IList<Contour> contours)
        {
            var regions = new List<Rectangle>();
            
            foreach (var contour in contours)
            {
                var rect = Cv2.BoundingRect(contour);
                
                // テキスト領域の条件
                if (IsLikelyTextRegion(rect))
                {
                    regions.Add(rect);
                }
            }
            
            return regions;
        }
        
        private bool IsLikelyTextRegion(Rectangle rect)
        {
            // アスペクト比チェック
            var aspectRatio = (float)rect.Width / rect.Height;
            if (aspectRatio < 0.1f || aspectRatio > 20f)
                return false;
                
            // サイズチェック
            if (rect.Width < _config.MinTextWidth || 
                rect.Height < _config.MinTextHeight)
                return false;
                
            // 面積チェック
            var area = rect.Width * rect.Height;
            if (area < _config.MinTextArea)
                return false;
                
            return true;
        }
    }
}
```

## 3. エラーハンドリング設計

### 3.1 例外階層

```csharp
namespace Baketa.Core.Exceptions.Capture
{
    /// <summary>
    /// キャプチャ関連の基底例外
    /// </summary>
    public abstract class CaptureException : BaketaException
    {
        protected CaptureException(string message, Exception innerException = null)
            : base(message, innerException) { }
    }
    
    /// <summary>
    /// GPU制約による例外
    /// </summary>
    public class GPUConstraintException : CaptureException
    {
        public uint RequestedSize { get; }
        public uint MaximumSize { get; }
        
        public GPUConstraintException(uint requestedSize, uint maximumSize)
            : base($"要求サイズ {requestedSize} が最大サイズ {maximumSize} を超えています")
        {
            RequestedSize = requestedSize;
            MaximumSize = maximumSize;
        }
    }
    
    /// <summary>
    /// TDR（Timeout Detection and Recovery）例外
    /// </summary>
    public class TDRException : CaptureException
    {
        public int HResult { get; }
        
        public TDRException(int hResult)
            : base($"GPU タイムアウトが検出されました (HRESULT: 0x{hResult:X8})")
        {
            HResult = hResult;
        }
    }
}
```

### 3.2 リトライ・フォールバック戦略

```csharp
namespace Baketa.Application.Services.Capture
{
    /// <summary>
    /// リトライとフォールバックを管理
    /// </summary>
    public class CaptureResiliencyManager
    {
        private readonly ILogger<CaptureResiliencyManager> _logger;
        
        public async Task<T> ExecuteWithResiliencyAsync<T>(
            Func<Task<T>> operation,
            ResiliencyOptions options)
        {
            var attempts = 0;
            var delays = GenerateExponentialBackoff(options);
            
            while (attempts < options.MaxRetries)
            {
                try
                {
                    return await operation();
                }
                catch (TDRException ex)
                {
                    _logger.LogWarning($"TDR検出 (試行 {attempts + 1}/{options.MaxRetries})");
                    
                    if (attempts < options.MaxRetries - 1)
                    {
                        await Task.Delay(delays[attempts]);
                        attempts++;
                        continue;
                    }
                    throw;
                }
                catch (GPUConstraintException ex)
                {
                    _logger.LogWarning($"GPU制約エラー: {ex.Message}");
                    throw; // リトライ不可
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"予期しないエラー (試行 {attempts + 1})");
                    
                    if (attempts < options.MaxRetries - 1 && IsRetriable(ex))
                    {
                        await Task.Delay(delays[attempts]);
                        attempts++;
                        continue;
                    }
                    throw;
                }
            }
            
            throw new InvalidOperationException("最大リトライ回数に達しました");
        }
        
        private IList<TimeSpan> GenerateExponentialBackoff(ResiliencyOptions options)
        {
            var delays = new List<TimeSpan>();
            for (int i = 0; i < options.MaxRetries; i++)
            {
                var delay = TimeSpan.FromMilliseconds(
                    options.BaseDelayMs * Math.Pow(2, i));
                delays.Add(delay);
            }
            return delays;
        }
    }
}
```

## 4. 実装優先順位

### Phase 1: 基盤実装（最優先）
1. **GPU環境検出システム**
   - DirectX機能レベル確認
   - GPU種別判定
   - テクスチャサイズ制限取得

2. **基本戦略パターン実装**
   - DirectFullScreenCaptureStrategy（統合GPU向け）
   - PrintWindowFallbackStrategy（確実動作保証）

3. **適応的キャプチャサービス基本実装**
   - 戦略選択ロジック
   - 基本的なフォールバック機能

### Phase 2: 高度機能実装
1. **ROIベースキャプチャ戦略**
   - 低解像度スキャン機能
   - テキスト領域検出（OpenCV）
   - 部分高解像度キャプチャ

2. **HDR・色空間対応**
   - HDR検出
   - トーンマッピング実装

3. **TDR対応**
   - TDR検出機能
   - 自動回復メカニズム

### Phase 3: 最適化・分析
1. **パフォーマンス最適化**
   - キャッシュ機構
   - 並列処理強化

2. **分析・学習機能**
   - メトリクス収集
   - 戦略選択の学習

## 5. テスト計画

### 5.1 単体テスト
- GPU環境検出の各種パターン
- 各戦略の個別動作確認
- エラーハンドリングとフォールバック

### 5.2 統合テスト
- 異なるGPU環境でのE2Eテスト
- フォールバックシナリオの確認
- パフォーマンス測定

### 5.3 受け入れテスト
- 実際のゲーム環境での動作確認
- 様々なハードウェア構成での検証

---

この設計により、GPU環境に関係なく確実に動作し、かつ各環境で最適なパフォーマンスを発揮する適応的キャプチャシステムを実現します。