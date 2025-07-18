using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using System.Diagnostics;
using System.Drawing;

namespace Baketa.Infrastructure.OCR.AdaptivePreprocessing;

/// <summary>
/// 適応的前処理機能を統合したOCRエンジンラッパー
/// </summary>
public class AdaptiveOcrEngine : IOcrEngine
{
    private readonly IOcrEngine _baseOcrEngine;
    private readonly IAdaptivePreprocessingParameterOptimizer _parameterOptimizer;
    private readonly ILogger<AdaptiveOcrEngine> _logger;
    private OcrEngineSettings? _currentSettings;

    // IOcrEngineプロパティの実装
    public string EngineName => $"Adaptive-{_baseOcrEngine.EngineName}";
    public string EngineVersion => _baseOcrEngine.EngineVersion;
    public bool IsInitialized => _baseOcrEngine.IsInitialized;
    public string? CurrentLanguage => _baseOcrEngine.CurrentLanguage;

    public AdaptiveOcrEngine(
        IOcrEngine baseOcrEngine,
        IAdaptivePreprocessingParameterOptimizer parameterOptimizer,
        ILogger<AdaptiveOcrEngine> logger)
    {
        _baseOcrEngine = baseOcrEngine;
        _parameterOptimizer = parameterOptimizer;
        _logger = logger;
    }

    /// <summary>
    /// 初期化
    /// </summary>
    public async Task<bool> InitializeAsync(OcrEngineSettings? settings = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("適応的OCRエンジン初期化開始");
        _currentSettings = settings;
        
        var result = await _baseOcrEngine.InitializeAsync(settings, cancellationToken);
        
        if (result)
        {
            _logger.LogInformation("適応的OCRエンジン初期化完了");
        }
        else
        {
            _logger.LogError("ベースOCRエンジンの初期化に失敗しました");
        }
        
        return result;
    }

    /// <summary>
    /// 適応的前処理を適用してOCR認識を実行
    /// </summary>
    public async Task<OcrResults> RecognizeAsync(IImage image, IProgress<OcrProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        // IAdvancedImageに変換が必要な場合の処理
        if (image is not IAdvancedImage advancedImage)
        {
            // 簡易変換（実際のプロジェクトではより適切な変換が必要）
            var imageBytes = await image.ToByteArrayAsync();
            advancedImage = new Core.Services.Imaging.AdvancedImage(imageBytes, image.Width, image.Height, 
                image.Format == Core.Abstractions.Imaging.ImageFormat.Png 
                    ? Core.Abstractions.Imaging.ImageFormat.Png 
                    : Core.Abstractions.Imaging.ImageFormat.Rgb24);
        }
        
        return await RecognizeAdvancedAsync(advancedImage, progress, cancellationToken);
    }
    
    /// <summary>
    /// 領域指定での認識
    /// </summary>
    public async Task<OcrResults> RecognizeAsync(IImage image, Rectangle? region = null, IProgress<OcrProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        // 簡易実装：領域指定は無視してフル画像で処理
        return await RecognizeAsync(image, progress, cancellationToken);
    }

    /// <summary>
    /// IAdvancedImage用の内部認識メソッド
    /// </summary>
    private async Task<OcrResults> RecognizeAdvancedAsync(IAdvancedImage image, IProgress<OcrProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("適応的OCR認識開始: {Width}x{Height}", image.Width, image.Height);

        try
        {
            // Step 1: 適応的前処理パラメータを最適化
            var optimizationResult = await _parameterOptimizer.OptimizeWithDetailsAsync(image);
            
            _logger.LogInformation(
                "前処理パラメータ最適化完了: 戦略={Strategy}, 改善予想={Improvement:F2} ({OptimizationMs}ms)",
                optimizationResult.OptimizationStrategy,
                optimizationResult.ExpectedImprovement,
                optimizationResult.OptimizationTimeMs);

            // Step 2: 最適化されたパラメータでOCR設定を調整
            var optimizedSettings = CreateOptimizedSettings(optimizationResult.Parameters);
            
            // Step 3: 前処理された画像でOCR実行
            var preprocessedImage = await ApplyPreprocessingAsync(image, optimizationResult.Parameters);
            
            // Step 4: 最適化された設定でOCR認識
            var ocrResults = await RecognizeWithOptimizedSettingsAsync(preprocessedImage, optimizedSettings);
            
            // Step 5: 結果に最適化情報を付加
            var enhancedResults = EnhanceResultsWithOptimizationInfo(ocrResults, optimizationResult);

            _logger.LogInformation(
                "適応的OCR認識完了: {Regions}リージョン検出, 総時間={TotalMs}ms (最適化={OptMs}ms, OCR={OcrMs}ms)",
                enhancedResults.TextRegions.Count,
                sw.ElapsedMilliseconds,
                optimizationResult.OptimizationTimeMs,
                sw.ElapsedMilliseconds - optimizationResult.OptimizationTimeMs);

            return enhancedResults;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "適応的OCR認識中にエラーが発生しました");
            
            // フォールバック: 通常のOCR処理
            _logger.LogInformation("フォールバック: 通常のOCR処理を実行");
            return await _baseOcrEngine.RecognizeAsync(image, progress, cancellationToken);
        }
    }

    /// <summary>
    /// 設定取得
    /// </summary>
    public OcrEngineSettings GetSettings()
    {
        return _baseOcrEngine.GetSettings() ?? new OcrEngineSettings();
    }

    /// <summary>
    /// 設定適用
    /// </summary>
    public async Task ApplySettingsAsync(OcrEngineSettings settings, CancellationToken cancellationToken = default)
    {
        _currentSettings = settings;
        await _baseOcrEngine.ApplySettingsAsync(settings, cancellationToken);
    }

    /// <summary>
    /// 利用可能言語取得
    /// </summary>
    public IReadOnlyList<string> GetAvailableLanguages()
    {
        return _baseOcrEngine.GetAvailableLanguages().ToList();
    }

    /// <summary>
    /// 利用可能モデル取得
    /// </summary>
    public IReadOnlyList<string> GetAvailableModels()
    {
        return _baseOcrEngine.GetAvailableModels().ToList();
    }

    /// <summary>
    /// 言語利用可能性チェック
    /// </summary>
    public async Task<bool> IsLanguageAvailableAsync(string language, CancellationToken cancellationToken = default)
    {
        return await _baseOcrEngine.IsLanguageAvailableAsync(language, cancellationToken);
    }

    /// <summary>
    /// パフォーマンス統計取得
    /// </summary>
    public OcrPerformanceStats GetPerformanceStats()
    {
        return _baseOcrEngine.GetPerformanceStats() ?? new OcrPerformanceStats();
    }

    /// <summary>
    /// Dispose実装
    /// </summary>
    public void Dispose()
    {
        _baseOcrEngine?.Dispose();
    }

    /// <summary>
    /// リソース解放
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("適応的OCRエンジンのリソースを解放します");
        
        if (_baseOcrEngine is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else if (_baseOcrEngine is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    /// <summary>
    /// 最適化されたOCR設定を作成
    /// </summary>
    private OcrEngineSettings CreateOptimizedSettings(AdaptivePreprocessingParameters parameters)
    {
        if (_currentSettings == null)
        {
            return new OcrEngineSettings
            {
                DetectionThreshold = parameters.DetectionThreshold,
                RecognitionThreshold = parameters.RecognitionThreshold
            };
        }

        return new OcrEngineSettings
        {
            Language = _currentSettings.Language,
            DetectionThreshold = parameters.DetectionThreshold,
            RecognitionThreshold = parameters.RecognitionThreshold
        };
    }

    /// <summary>
    /// 前処理を適用した画像を作成
    /// </summary>
    private async Task<IAdvancedImage> ApplyPreprocessingAsync(
        IAdvancedImage originalImage, 
        AdaptivePreprocessingParameters parameters)
    {
        return await Task.Run(() =>
        {
            try
            {
                // 実際の画像前処理はここで実装
                // 現在は簡易実装として元画像をそのまま返す
                _logger.LogDebug("前処理適用: γ={Gamma:F2}, C={Contrast:F2}, B={Brightness:F2}",
                    parameters.Gamma, parameters.Contrast, parameters.Brightness);

                // TODO: 実際の前処理実装
                // - ガンマ補正
                // - コントラスト調整  
                // - 明度調整
                // - ノイズ除去
                // - シャープニング
                // - 二値化
                // - モルフォロジー処理

                return originalImage; // 暫定的に元画像を返す
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "前処理適用中にエラーが発生しました。元画像を使用します");
                return originalImage;
            }
        });
    }

    /// <summary>
    /// 最適化された設定でOCR認識を実行
    /// </summary>
    private async Task<OcrResults> RecognizeWithOptimizedSettingsAsync(
        IAdvancedImage image, 
        OcrEngineSettings optimizedSettings)
    {
        // 設定を一時的に変更してOCR実行
        var originalSettings = _currentSettings;
        
        try
        {
            // 最適化された設定で再初期化（必要に応じて）
            if (ShouldReinitialize(originalSettings, optimizedSettings))
            {
                await _baseOcrEngine.InitializeAsync(optimizedSettings);
                _currentSettings = optimizedSettings;
            }

            return await _baseOcrEngine.RecognizeAsync(image);
        }
        finally
        {
            // 必要に応じて元の設定に戻す
            if (originalSettings != null && ShouldReinitialize(optimizedSettings, originalSettings))
            {
                await _baseOcrEngine.InitializeAsync(originalSettings);
                _currentSettings = originalSettings;
            }
        }
    }

    /// <summary>
    /// 設定変更のために再初期化が必要かチェック
    /// </summary>
    private bool ShouldReinitialize(OcrEngineSettings? current, OcrEngineSettings? target)
    {
        if (current == null || target == null) return true;
        
        // 閾値の差が大きい場合のみ再初期化
        var detectionDiff = Math.Abs(current.DetectionThreshold - target.DetectionThreshold);
        var recognitionDiff = Math.Abs(current.RecognitionThreshold - target.RecognitionThreshold);
        
        return detectionDiff > 0.1 || recognitionDiff > 0.1;
    }

    /// <summary>
    /// OCR結果に最適化情報を付加
    /// </summary>
    private OcrResults EnhanceResultsWithOptimizationInfo(
        OcrResults originalResults, 
        AdaptivePreprocessingResult optimizationResult)
    {
        // 最適化情報をメタデータとして追加（簡易実装）
        // 注：OcrResultsには現在Metadataプロパティがないため、元の結果をそのまま返す
        // 実際の実装では、OcrResultsに拡張メタデータ機能を追加する必要があります
        
        _logger.LogInformation(
            "適応的前処理メタデータ: 戦略={Strategy}, 理由={Reason}, 改善予想={Improvement:F2}, 信頼度={Confidence:F2}",
            optimizationResult.OptimizationStrategy,
            optimizationResult.OptimizationReason,
            optimizationResult.ExpectedImprovement,
            optimizationResult.ParameterConfidence);

        return originalResults;
    }
}