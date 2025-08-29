using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Factories;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace Baketa.Infrastructure.OCR.StickyRoi;

/// <summary>
/// IOcrEngineからISimpleOcrEngineへのアダプター
/// StickyROI統合のために必要な軽量インターフェース変換
/// Sprint 1: 完全版実装 - 実際のPaddleOCR統合
/// </summary>
public sealed class SimpleOcrEngineAdapter : ISimpleOcrEngine
{
    private readonly Baketa.Core.Abstractions.OCR.IOcrEngine _baseOcrEngine;
    private readonly Baketa.Core.Abstractions.Factories.IImageFactory _imageFactory;
    private readonly ILogger<SimpleOcrEngineAdapter> _logger;
    private bool _disposed;

    public SimpleOcrEngineAdapter(
        Baketa.Core.Abstractions.OCR.IOcrEngine baseOcrEngine, 
        Baketa.Core.Abstractions.Factories.IImageFactory imageFactory,
        ILogger<SimpleOcrEngineAdapter> logger)
    {
        _baseOcrEngine = baseOcrEngine ?? throw new ArgumentNullException(nameof(baseOcrEngine));
        _imageFactory = imageFactory ?? throw new ArgumentNullException(nameof(imageFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _logger.LogInformation("🔗 SimpleOcrEngineAdapter完全版初期化完了: BaseEngine={BaseEngineType}, ImageFactory={ImageFactoryType}", 
            _baseOcrEngine.GetType().Name, _imageFactory.GetType().Name);
    }

    /// <summary>
    /// テキスト認識実行（実際のPaddleOCR処理に委譲）
    /// Sprint 1: 完全版実装
    /// </summary>
    public async Task<OcrResult> RecognizeTextAsync(byte[] imageData, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            _logger.LogDebug("🔄 SimpleOcrEngineAdapter: 実際のOCR処理開始 - ImageSize: {Size}bytes", imageData.Length);
            
            // Step 1: byte[] → IImage変換
            using var image = await _imageFactory.CreateFromBytesAsync(imageData);
            _logger.LogDebug("✅ IImage作成完了: {Width}x{Height}", image.Width, image.Height);
            
            // Step 2: IOcrEngineでOCR実行
            var ocrResults = await _baseOcrEngine.RecognizeAsync(image, cancellationToken: cancellationToken);
            
            // Step 3: OcrResults → OcrResult変換
            var convertedResult = ConvertOcrResults(ocrResults, stopwatch.Elapsed);
            
            _logger.LogInformation("🎯 SimpleOcrEngineAdapter: OCR完了 - 検出テキスト数: {Count}, 処理時間: {Time}ms, 全体信頼度: {Confidence:F3}", 
                convertedResult.TextCount, stopwatch.ElapsedMilliseconds, convertedResult.OverallConfidence);
            
            return convertedResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ SimpleOcrEngineAdapter: テキスト認識エラー - 処理時間: {Time}ms", stopwatch.ElapsedMilliseconds);
            
            return new OcrResult
            {
                DetectedTexts = [],
                IsSuccessful = false,
                ProcessingTime = stopwatch.Elapsed,
                ErrorMessage = ex.Message,
                Metadata = new Dictionary<string, object>
                {
                    ["Exception"] = ex.GetType().Name,
                    ["ImageSizeBytes"] = imageData.Length
                }
            };
        }
        finally
        {
            stopwatch.Stop();
        }
    }
    
    /// <summary>
    /// OcrResults を OcrResult に変換
    /// </summary>
    private OcrResult ConvertOcrResults(OcrResults ocrResults, TimeSpan processingTime)
    {
        var detectedTexts = ocrResults.TextRegions.Select(region => new DetectedText
        {
            Text = region.Text,
            Confidence = region.Confidence,
            BoundingBox = region.Bounds,
            Language = ocrResults.LanguageCode,
            ProcessingTechnique = OptimizationTechnique.None, // CPU First
            ProcessingTime = processingTime,
            DetailedRegion = region.Contour?.Select(p => new PointF(p.X, p.Y)).ToArray(),
            Angle = 0f, // 将来拡張用
            EstimatedFont = null, // 将来拡張用
            Metadata = new Dictionary<string, object>
            {
                ["Direction"] = region.Direction.ToString(),
                ["SourceEngine"] = _baseOcrEngine.EngineName
            }
        }).ToList();
        
        return new OcrResult
        {
            DetectedTexts = detectedTexts,
            IsSuccessful = ocrResults.HasText,
            ProcessingTime = processingTime,
            ErrorMessage = null,
            Metadata = new Dictionary<string, object>
            {
                ["SourceImageWidth"] = ocrResults.SourceImage.Width,
                ["SourceImageHeight"] = ocrResults.SourceImage.Height,
                ["LanguageCode"] = ocrResults.LanguageCode,
                ["TotalRegions"] = ocrResults.TextRegions.Count,
                ["MergedText"] = ocrResults.Text,
                ["EngineVersion"] = _baseOcrEngine.EngineVersion,
                ["RegionOfInterest"] = ocrResults.RegionOfInterest?.ToString() ?? "None"
            }
        };
    }

    /// <summary>
    /// エンジンの利用可能性確認（完全版実装）
    /// </summary>
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return false;
            
        try
        {
            // IOcrEngineとIImageFactoryの両方が利用可能かチェック
            var engineAvailable = _baseOcrEngine?.IsInitialized == true;
            var factoryAvailable = _imageFactory != null;
            
            if (engineAvailable && factoryAvailable)
            {
                _logger.LogDebug("✅ SimpleOcrEngineAdapter: 利用可能 - Engine={EngineName}, Version={Version}", 
                    _baseOcrEngine.EngineName, _baseOcrEngine.EngineVersion);
                return true;
            }
            
            _logger.LogWarning("⚠️ SimpleOcrEngineAdapter: 利用不可 - Engine初期化済み={EngineReady}, Factory利用可能={FactoryReady}", 
                engineAvailable, factoryAvailable);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ SimpleOcrEngineAdapter: 利用可能性確認エラー");
            return false;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _logger.LogDebug("🔄 SimpleOcrEngineAdapter: リソース解放開始");
            
            try
            {
                // IOcrEngineがIDisposableの場合は解放
                if (_baseOcrEngine is IDisposable disposableEngine)
                {
                    disposableEngine.Dispose();
                    _logger.LogDebug("✅ BaseOcrEngine解放完了");
                }
                
                // IImageFactoryもIDisposableの場合は解放
                if (_imageFactory is IDisposable disposableFactory)
                {
                    disposableFactory.Dispose();
                    _logger.LogDebug("✅ ImageFactory解放完了");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ SimpleOcrEngineAdapter: リソース解放中にエラー");
            }
            finally
            {
                _disposed = true;
                _logger.LogInformation("✅ SimpleOcrEngineAdapter: リソース解放完了");
            }
        }
    }
}