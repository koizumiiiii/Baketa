using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Models.Capture;
using Baketa.Core.Events.EventTypes;
using Baketa.Core.Events.Diagnostics;
using Baketa.Infrastructure.OCR.PaddleOCR.Models;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models.Local;
using Sdcb.PaddleInference;
using System.Diagnostics;

namespace Baketa.Infrastructure.OCR.PaddleOCR.Services;

/// <summary>
/// PaddleOCR v3/v5 ハイブリッド戦略実装
/// 高速検出(v3) → 高精度認識(v5) のパイプラインを提供
/// </summary>
public sealed class HybridPaddleOcrService : IDisposable
{
    private readonly ILogger<HybridPaddleOcrService> _logger;
    private readonly IEventAggregator _eventAggregator;
    private readonly HybridOcrSettings _settings;
    
    private PaddleOcrAll? _v3Engine;
    private PaddleOcrAll? _v5Engine;
    private bool _disposed;

    public HybridPaddleOcrService(
        ILogger<HybridPaddleOcrService> logger,
        IEventAggregator eventAggregator,
        HybridOcrSettings settings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>
    /// ハイブリッドOCRエンジンを初期化
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🚀 ハイブリッドPaddleOCR初期化開始 - V3(高速) + V5(高精度)");
        
        try
        {
            // V3エンジン初期化（高速検出用）
            await InitializeV3EngineAsync(cancellationToken);
            
            // V5エンジン初期化（高精度認識用）  
            await InitializeV5EngineAsync(cancellationToken);
            
            _logger.LogInformation("✅ ハイブリッドPaddleOCR初期化完了");
            
            await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
            {
                Stage = "OCR",
                IsSuccess = true,
                ProcessingTimeMs = 0,
                Message = "ハイブリッドPaddleOCR初期化完了",
                Severity = DiagnosticSeverity.Information,
                Metrics = new Dictionary<string, object>
                {
                    { "V3Initialized", _v3Engine != null },
                    { "V5Initialized", _v5Engine != null },
                    { "HybridMode", "V3Detection_V5Recognition" }
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ハイブリッドPaddleOCR初期化失敗");
            throw;
        }
    }

    /// <summary>
    /// ハイブリッド戦略でOCR実行
    /// </summary>
    public async Task<IReadOnlyList<OcrTextRegion>> ExecuteHybridOcrAsync(
        Mat image,
        OcrProcessingMode mode,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        var stopwatch = Stopwatch.StartNew();
        var sessionId = Guid.NewGuid().ToString("N")[..8];
        
        _logger.LogDebug("🔄 ハイブリッドOCR実行開始 - Mode: {Mode}, SessionId: {SessionId}", mode, sessionId);
        
        try
        {
            return mode switch
            {
                OcrProcessingMode.FastDetection => await ExecuteFastDetectionAsync(image, sessionId, cancellationToken),
                OcrProcessingMode.HighQuality => await ExecuteHighQualityAsync(image, sessionId, cancellationToken),
                OcrProcessingMode.Hybrid => await ExecuteHybridPipelineAsync(image, sessionId, cancellationToken),
                OcrProcessingMode.Adaptive => await ExecuteAdaptiveAsync(image, sessionId, cancellationToken),
                _ => throw new ArgumentException($"サポートされていない処理モード: {mode}", nameof(mode))
            };
        }
        finally
        {
            stopwatch.Stop();
            _logger.LogDebug("⏱️ ハイブリッドOCR実行完了 - 処理時間: {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// V3エンジン初期化（高速検出用）
    /// </summary>
    private async Task InitializeV3EngineAsync(CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            _logger.LogDebug("⚡ V3エンジン初期化中（高速検出用）");
            _v3Engine = new PaddleOcrAll(LocalFullModels.ChineseV3, PaddleDevice.Mkldnn())
            {
                AllowRotateDetection = true,
                Enable180Classification = false
            };
            _logger.LogDebug("✅ V3エンジン初期化完了");
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// V5エンジン初期化（高精度認識用）
    /// </summary>
    private async Task InitializeV5EngineAsync(CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            _logger.LogDebug("🎯 V5エンジン初期化中（高精度認識用）");
            _v5Engine = new PaddleOcrAll(LocalFullModels.ChineseV5, PaddleDevice.Mkldnn())
            {
                AllowRotateDetection = true,
                Enable180Classification = true
            };
            _logger.LogDebug("✅ V5エンジン初期化完了");
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 高速検出実行（V3）
    /// </summary>
    private async Task<IReadOnlyList<OcrTextRegion>> ExecuteFastDetectionAsync(
        Mat image, 
        string sessionId, 
        CancellationToken cancellationToken)
    {
        if (_v3Engine == null)
            throw new InvalidOperationException("V3エンジンが初期化されていません");

        _logger.LogDebug("⚡ V3高速検出実行 - SessionId: {SessionId}", sessionId);
        
        var result = await Task.Run(() =>
        {
            using var timeoutCts = new CancellationTokenSource(_settings.FastDetectionTimeoutMs);
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            
            return _v3Engine.Run(image);
        }, cancellationToken).ConfigureAwait(false);

        return ConvertPaddleOcrResult(result, "V3_Fast");
    }

    /// <summary>
    /// 高精度認識実行（V5）
    /// </summary>
    private async Task<IReadOnlyList<OcrTextRegion>> ExecuteHighQualityAsync(
        Mat image, 
        string sessionId, 
        CancellationToken cancellationToken)
    {
        if (_v5Engine == null)
            throw new InvalidOperationException("V5エンジンが初期化されていません");

        _logger.LogDebug("🎯 V5高精度認識実行 - SessionId: {SessionId}", sessionId);
        
        var result = await Task.Run(() =>
        {
            using var timeoutCts = new CancellationTokenSource(_settings.HighQualityTimeoutMs);
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            
            return _v5Engine.Run(image);
        }, cancellationToken).ConfigureAwait(false);

        return ConvertPaddleOcrResult(result, "V5_Quality");
    }

    /// <summary>
    /// ハイブリッドパイプライン実行（V3検出 → V5認識）
    /// </summary>
    private async Task<IReadOnlyList<OcrTextRegion>> ExecuteHybridPipelineAsync(
        Mat image, 
        string sessionId, 
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("🔄 ハイブリッドパイプライン実行 - SessionId: {SessionId}", sessionId);
        
        // Phase 1: V3で高速検出
        var fastRegions = await ExecuteFastDetectionAsync(image, sessionId, cancellationToken);
        
        if (fastRegions.Count == 0)
        {
            _logger.LogWarning("⚠️ V3高速検出で領域が見つからない - V5にフォールバック");
            return await ExecuteHighQualityAsync(image, sessionId, cancellationToken);
        }

        if (fastRegions.Count < _settings.RegionCountThreshold)
        {
            _logger.LogDebug("📏 検出領域数が閾値未満({Count} < {Threshold}) - V5全画面処理", 
                fastRegions.Count, _settings.RegionCountThreshold);
            return await ExecuteHighQualityAsync(image, sessionId, cancellationToken);
        }

        // Phase 2: 検出されたROIでV5高精度認識
        _logger.LogDebug("🎯 V5高精度認識をROI({Count}領域)で実行", fastRegions.Count);
        var qualityRegions = new List<OcrTextRegion>();

        foreach (var region in fastRegions)
        {
            try
            {
                var roi = ExtractROI(image, region.Bounds);
                if (roi.Empty()) continue;

                var roiResults = await ExecuteHighQualityAsync(roi, sessionId, cancellationToken);
                
                // 座標をオリジナル画像に合わせて調整
                foreach (var roiResult in roiResults)
                {
                    var adjustedBounds = new System.Drawing.Rectangle(
                        roiResult.Bounds.X + region.Bounds.X,
                        roiResult.Bounds.Y + region.Bounds.Y,
                        roiResult.Bounds.Width,
                        roiResult.Bounds.Height
                    );
                    
                    qualityRegions.Add(new OcrTextRegion(
                        roiResult.Text,
                        adjustedBounds,
                        roiResult.Confidence
                    ));
                }
                
                roi.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ ROI処理でエラー発生 - 領域をスキップ: {Bounds}", region.Bounds);
            }
        }

        _logger.LogInformation("✅ ハイブリッドパイプライン完了 - 最終結果: {Count}領域", qualityRegions.Count);
        return qualityRegions;
    }

    /// <summary>
    /// 適応的実行（画像品質に基づく自動選択）
    /// </summary>
    private async Task<IReadOnlyList<OcrTextRegion>> ExecuteAdaptiveAsync(
        Mat image, 
        string sessionId, 
        CancellationToken cancellationToken)
    {
        var imageQuality = CalculateImageQuality(image);
        _logger.LogDebug("📊 画像品質評価: {Quality:F3} (閾値: {Threshold:F3})", imageQuality, _settings.ImageQualityThreshold);
        
        if (imageQuality < _settings.ImageQualityThreshold)
        {
            _logger.LogDebug("📉 低品質画像検出 - V5高精度モード実行");
            return await ExecuteHighQualityAsync(image, sessionId, cancellationToken);
        }
        else
        {
            _logger.LogDebug("📈 高品質画像検出 - ハイブリッドパイプライン実行");
            return await ExecuteHybridPipelineAsync(image, sessionId, cancellationToken);
        }
    }

    /// <summary>
    /// ROI領域を抽出
    /// </summary>
    private static Mat ExtractROI(Mat image, System.Drawing.Rectangle bounds)
    {
        var rect = new OpenCvSharp.Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        
        // 画像境界内に調整
        rect.X = Math.Max(0, Math.Min(rect.X, image.Cols - 1));
        rect.Y = Math.Max(0, Math.Min(rect.Y, image.Rows - 1));
        rect.Width = Math.Min(rect.Width, image.Cols - rect.X);
        rect.Height = Math.Min(rect.Height, image.Rows - rect.Y);
        
        return new Mat(image, rect);
    }

    /// <summary>
    /// 画像品質を計算
    /// </summary>
    private static double CalculateImageQuality(Mat image)
    {
        // 簡単な画像品質評価：ラプラシアン分散
        using var gray = new Mat();
        Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
        
        using var laplacian = new Mat();
        Cv2.Laplacian(gray, laplacian, MatType.CV_64F);
        
        var mean = new Scalar();
        var stddev = new Scalar();
        Cv2.MeanStdDev(laplacian, out mean, out stddev);
        
        return stddev.Val0 * stddev.Val0; // 分散
    }

    /// <summary>
    /// PaddleOCR結果を変換（PaddleOcrEngineと同様のロジック）
    /// </summary>
    private IReadOnlyList<OcrTextRegion> ConvertPaddleOcrResult(object result, string modelVersion)
    {
        var regions = new List<OcrTextRegion>();
        
        try
        {
            if (result != null)
            {
                if (result is PaddleOcrResult[] paddleResults)
                {
                    _logger.LogDebug("🔄 {ModelVersion} - PaddleOcrResult配列として処理: {Count}個", modelVersion, paddleResults.Length);
                    
                    foreach (var paddleResult in paddleResults)
                    {
                        var textRegion = ProcessSinglePaddleResult(paddleResult);
                        if (textRegion != null)
                        {
                            regions.Add(textRegion);
                        }
                    }
                }
                else if (result is PaddleOcrResult singleResult)
                {
                    _logger.LogDebug("🔄 {ModelVersion} - 単一PaddleOcrResultとして処理", modelVersion);
                    var textRegion = ProcessSinglePaddleResult(singleResult);
                    if (textRegion != null)
                    {
                        regions.Add(textRegion);
                    }
                }
                else
                {
                    _logger.LogWarning("🔄 {ModelVersion} - 予期しない結果タイプ: {Type}", modelVersion, result.GetType().FullName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔄 {ModelVersion} - PaddleOCR結果変換でエラー", modelVersion);
        }
        
        _logger.LogDebug("🔄 {ModelVersion}結果変換完了: {Count}領域", modelVersion, regions.Count);
        return regions;
    }

    /// <summary>
    /// 単一のPaddleOcrResultを処理
    /// </summary>
    private OcrTextRegion? ProcessSinglePaddleResult(object result)
    {
        try
        {
            // リフレクションを使用してPaddleOcrResultのプロパティにアクセス
            var resultType = result.GetType();
            
            // テキストを取得
            var textProperty = resultType.GetProperty("Text");
            var text = textProperty?.GetValue(result)?.ToString() ?? "";
            
            // 信頼度を取得
            var scoreProperty = resultType.GetProperty("Score");
            var score = scoreProperty?.GetValue(result) is double s ? s : 0.0;
            
            // 境界を取得
            var bounds = GetBoundsFromResult(result);
            
            return new OcrTextRegion(text, bounds, (float)score);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "単一PaddleOcrResult処理でエラー");
            return null;
        }
    }

    /// <summary>
    /// PaddleOcrResultから境界矩形を取得
    /// </summary>
    private System.Drawing.Rectangle GetBoundsFromResult(object result)
    {
        try
        {
            var resultType = result.GetType();
            var regionProperty = resultType.GetProperty("Region");
            
            if (regionProperty?.GetValue(result) is object region)
            {
                var regionType = region.GetType();
                var pointsProperty = regionType.GetProperty("Points");
                
                if (pointsProperty?.GetValue(region) is Array pointsArray)
                {
                    var points = new List<System.Drawing.PointF>();
                    
                    foreach (var point in pointsArray)
                    {
                        var pointType = point.GetType();
                        var xProperty = pointType.GetProperty("X");
                        var yProperty = pointType.GetProperty("Y");
                        
                        if (xProperty?.GetValue(point) is float x && yProperty?.GetValue(point) is float y)
                        {
                            points.Add(new System.Drawing.PointF(x, y));
                        }
                    }
                    
                    if (points.Count > 0)
                    {
                        var minX = (int)points.Min(p => p.X);
                        var minY = (int)points.Min(p => p.Y);
                        var maxX = (int)points.Max(p => p.X);
                        var maxY = (int)points.Max(p => p.Y);
                        
                        return new System.Drawing.Rectangle(minX, minY, maxX - minX, maxY - minY);
                    }
                }
            }
            
            // フォールバック：デフォルト境界
            return new System.Drawing.Rectangle(0, 0, 100, 30);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "境界矩形取得でエラー - デフォルト値を使用");
            return new System.Drawing.Rectangle(0, 0, 100, 30);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(HybridPaddleOcrService));
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _v3Engine?.Dispose();
            _v5Engine?.Dispose();
            _disposed = true;
            
            _logger.LogInformation("🧹 ハイブリッドPaddleOCRサービスが破棄されました");
        }
    }
}