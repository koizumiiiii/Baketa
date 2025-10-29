using System;
using System.Drawing;
using System.IO;
using System.Text;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Models.OCR;
using Baketa.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.OCR.StickyRoi;

/// <summary>
/// StickyRoiEnhancedOcrEngineをIOcrEngineインターフェースでラップするアダプター
/// DI統合と既存のOCRパイプラインとの互換性を提供
/// </summary>
public sealed class StickyRoiOcrEngineWrapper : IOcrEngine
{
    private readonly StickyRoiEnhancedOcrEngine _enhancedEngine;
    private readonly IOcrEngine _fallbackEngine;
    private readonly ILogger<StickyRoiOcrEngineWrapper> _logger;
    private bool _disposed;

    public StickyRoiOcrEngineWrapper(
        StickyRoiEnhancedOcrEngine enhancedEngine,
        IOcrEngine fallbackEngine,
        ILogger<StickyRoiOcrEngineWrapper> logger)
    {
        _enhancedEngine = enhancedEngine ?? throw new ArgumentNullException(nameof(enhancedEngine));
        _fallbackEngine = fallbackEngine ?? throw new ArgumentNullException(nameof(fallbackEngine));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string EngineName => $"StickyRoi-{_fallbackEngine.EngineName}";
    public string EngineVersion => _fallbackEngine.EngineVersion;
    public bool IsInitialized => _fallbackEngine.IsInitialized;
    public string? CurrentLanguage => _fallbackEngine.CurrentLanguage;

    public async Task<bool> InitializeAsync(OcrEngineSettings? settings = null, CancellationToken cancellationToken = default)
    {
        return await _fallbackEngine.InitializeAsync(settings, cancellationToken);
    }

    public async Task<bool> WarmupAsync(CancellationToken cancellationToken = default)
    {
        return await _fallbackEngine.WarmupAsync(cancellationToken);
    }

    public async Task<OcrResults> RecognizeAsync(
        IImage image,
        IProgress<OcrProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        return await RecognizeAsync(image, null, progressCallback, cancellationToken);
    }

    public async Task<OcrResults> RecognizeAsync(
        IImage image,
        Rectangle? regionOfInterest,
        IProgress<OcrProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        // 🚨 [CRITICAL_WRAPPER_DEBUG] 最優先ログ - RecognizeAsync呼び出し確認
        SafeFileWriter.WriteDebugLog("🚨 [WRAPPER_DEBUG] StickyRoiOcrEngineWrapper.RecognizeAsync: ROI={0}", regionOfInterest);
        // メインログは SafeFileWriter で記録済み
        
        try
        {
            _logger.LogInformation("🎯🎯🎯 [WRAPPER_DEBUG] StickyRoiOcrEngineWrapper.RecognizeAsync呼び出し開始");
            // 重複ログ削除
            
            _logger.LogInformation("🎯🎯🎯 StickyROI OCR認識開始: ROI={Roi}", regionOfInterest);
            
            // StickyRoiEnhancedOcrEngineを使用してOCR実行
            // IImageをbyte[]に変換
            SafeFileWriter.WriteDebugLog("🔍 [WRAPPER_DEBUG] IImageをbyte[]に変換開始");
            
            byte[] imageData = await ConvertImageToBytesAsync(image);
            SafeFileWriter.WriteDebugLog("🎯 [WRAPPER_DEBUG] 画像データ変換完了: {0}B", imageData.Length);
            
            SafeFileWriter.WriteDebugLog("🚀 [WRAPPER_DEBUG] StickyRoiEnhancedOcrEngine.RecognizeTextAsync呼び出し開始");
            
            var stickyResult = await _enhancedEngine.RecognizeTextAsync(imageData, cancellationToken);
            
            SafeFileWriter.WriteDebugLog("✅ [WRAPPER_DEBUG] StickyRoiEnhancedOcrEngine.RecognizeTextAsync完了");
            
            // OcrResultをOcrResultsに変換
            var ocrResults = ConvertToOcrResults(stickyResult, image, regionOfInterest);
            
            _logger.LogInformation("✅ StickyROI OCR完了: テキスト領域数={RegionCount}, 処理時間={ProcessingTime}ms", 
                ocrResults.TextRegions.Count, ocrResults.ProcessingTime.TotalMilliseconds);
            
            Console.WriteLine($"✅ [WRAPPER_DEBUG] StickyROI OCR完了: テキスト領域数={ocrResults.TextRegions.Count}, 処理時間={ocrResults.ProcessingTime.TotalMilliseconds}ms");
            
            return ocrResults;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ StickyROI OCR認識エラー - フォールバックエンジンに切り替え");
            SafeFileWriter.WriteDebugLog("❌ [WRAPPER_ERROR] StickyROI OCR認識エラー: {0} - フォールバックエンジンに切り替え", ex.Message);
            
            // エラー時はフォールバックエンジンを使用
            SafeFileWriter.WriteDebugLog("🔄 [WRAPPER_DEBUG] フォールバックエンジンRecognizeAsync呼び出し開始");
            
            var fallbackResult = await _fallbackEngine.RecognizeAsync(image, regionOfInterest, progressCallback, cancellationToken);
            
            SafeFileWriter.WriteDebugLog("✅ [WRAPPER_DEBUG] フォールバックエンジンRecognizeAsync完了: テキスト領域数={0}", fallbackResult.TextRegions.Count);
            
            return fallbackResult;
        }
    }

    /// <summary>
    /// [Option B] OcrContextを使用してテキストを認識します（座標問題恒久対応）
    /// </summary>
    public async Task<OcrResults> RecognizeAsync(OcrContext context, IProgress<OcrProgress>? progressCallback = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        _logger.LogInformation("🎯 [OPTION_B] StickyRoiOcrEngineWrapper - OcrContext使用のRecognizeAsync呼び出し");

        // 既存メソッドに委譲
        return await RecognizeAsync(
            context.Image,
            context.CaptureRegion,
            progressCallback,
            context.CancellationToken).ConfigureAwait(false);
    }

    public OcrEngineSettings GetSettings()
    {
        return _fallbackEngine.GetSettings();
    }

    public async Task ApplySettingsAsync(OcrEngineSettings settings, CancellationToken cancellationToken = default)
    {
        await _fallbackEngine.ApplySettingsAsync(settings, cancellationToken);
    }

    public IReadOnlyList<string> GetAvailableLanguages()
    {
        return _fallbackEngine.GetAvailableLanguages();
    }

    public IReadOnlyList<string> GetAvailableModels()
    {
        return _fallbackEngine.GetAvailableModels();
    }

    public async Task<bool> IsLanguageAvailableAsync(string languageCode, CancellationToken cancellationToken = default)
    {
        return await _fallbackEngine.IsLanguageAvailableAsync(languageCode, cancellationToken);
    }

    public OcrPerformanceStats GetPerformanceStats()
    {
        return _fallbackEngine.GetPerformanceStats();
    }

    public void CancelCurrentOcrTimeout()
    {
        _fallbackEngine.CancelCurrentOcrTimeout();
    }

    /// <summary>
    /// 連続失敗回数を取得（診断・フォールバック判定用）
    /// </summary>
    /// <returns>連続失敗回数</returns>
    public int GetConsecutiveFailureCount()
    {
        // フォールバックエンジンに委譲
        return _fallbackEngine.GetConsecutiveFailureCount();
    }

    /// <summary>
    /// 失敗カウンタをリセット（緊急時復旧用）
    /// </summary>
    public void ResetFailureCounter()
    {
        // フォールバックエンジンに委譲
        _fallbackEngine.ResetFailureCounter();
    }

    public async Task<OcrResults> DetectTextRegionsAsync(IImage image, CancellationToken cancellationToken = default)
    {
        SafeFileWriter.WriteDebugLog("🚨 [WRAPPER_DEBUG] DetectTextRegionsAsync開始");
        
        var result = await _fallbackEngine.DetectTextRegionsAsync(image, cancellationToken);
        
        SafeFileWriter.WriteDebugLog("✅ [WRAPPER_DEBUG] StickyRoiOcrEngineWrapper.DetectTextRegionsAsync完了: テキスト領域数={0}", result.TextRegions.Count);
        
        return result;
    }

    /// <summary>
    /// IImageをbyte[]に変換
    /// </summary>
    private async Task<byte[]> ConvertImageToBytesAsync(IImage image)
    {
        try
        {
            return await image.ToByteArrayAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ IImage変換エラー - フォールバックエンジンに委任");
            throw;
        }
    }

    /// <summary>
    /// OcrResultをOcrResultsに変換
    /// </summary>
    private static OcrResults ConvertToOcrResults(
        Baketa.Core.Abstractions.OCR.OcrResult stickyResult, 
        IImage sourceImage, 
        Rectangle? regionOfInterest)
    {
        var textRegions = new List<OcrTextRegion>();
        
        // DetectedTextsから個々のテキスト領域を変換
        foreach (var detectedText in stickyResult.DetectedTexts)
        {
            if (!string.IsNullOrWhiteSpace(detectedText.Text))
            {
                var textRegion = new OcrTextRegion(
                    text: detectedText.Text,
                    bounds: detectedText.BoundingBox,
                    confidence: detectedText.Confidence,
                    direction: TextDirection.Horizontal
                );
                textRegions.Add(textRegion);
            }
        }
        
        // DetectedTextsが空で、かつCombinedTextがある場合の代替処理
        if (textRegions.Count == 0 && !string.IsNullOrWhiteSpace(stickyResult.CombinedText))
        {
            var bounds = regionOfInterest ?? new Rectangle(0, 0, sourceImage.Width, sourceImage.Height);
            var textRegion = new OcrTextRegion(
                text: stickyResult.CombinedText,
                bounds: bounds,
                confidence: stickyResult.OverallConfidence,
                direction: TextDirection.Horizontal
            );
            textRegions.Add(textRegion);
        }

        // 使用された言語を推定（DetectedTextsから）
        var language = stickyResult.DetectedTexts.FirstOrDefault()?.Language ?? "jpn";

        return new OcrResults(
            textRegions: textRegions,
            sourceImage: sourceImage,
            processingTime: stickyResult.ProcessingTime,
            languageCode: language,
            regionOfInterest: regionOfInterest,
            mergedText: stickyResult.CombinedText
        );
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _enhancedEngine?.Dispose();
        _fallbackEngine?.Dispose();
        
        _disposed = true;
    }
}