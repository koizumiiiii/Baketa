using System;
using System.Collections.Generic;
using Baketa.Core.Abstractions.Imaging.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.Core.Services.Imaging.Filters.OCR;

/// <summary>
/// OCR最適化フィルターのファクトリークラス
/// </summary>
/// <remarks>
/// 新しいOcrFilterFactoryを作成します
/// </remarks>
/// <param name="serviceProvider">サービスプロバイダー</param>
/// <param name="logger">ロガー</param>
public class OcrFilterFactory(
        IServiceProvider serviceProvider,
        ILogger<OcrFilterFactory> logger) : IOcrFilterFactory
{
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    private readonly ILogger<OcrFilterFactory> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc/>
    public Baketa.Core.Abstractions.Imaging.IImageFilter CreateFilter(OcrFilterType filterType)
    {
        try
        {
            return filterType switch
            {
                OcrFilterType.Grayscale => _serviceProvider.GetRequiredService<OcrGrayscaleFilter>(),
                OcrFilterType.ContrastEnhancement => _serviceProvider.GetRequiredService<OcrContrastEnhancementFilter>(),
                OcrFilterType.NoiseReduction => _serviceProvider.GetRequiredService<OcrNoiseReductionFilter>(),
                OcrFilterType.Threshold => _serviceProvider.GetRequiredService<OcrThresholdFilter>(),
                OcrFilterType.Morphology => _serviceProvider.GetRequiredService<OcrMorphologyFilter>(),
                OcrFilterType.EdgeDetection => _serviceProvider.GetRequiredService<OcrEdgeDetectionFilter>(),
                _ => throw new ArgumentException($"未サポートのOCRフィルタータイプ: {filterType}", nameof(filterType))
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OCRフィルターの作成中にエラーが発生しました: {FilterType}", filterType);
            throw new OcrFilterCreationException($"OCRフィルターの作成中にエラーが発生しました: {filterType}", ex);
        }
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, OcrFilterType> GetAvailableFilters()
    {
        return new Dictionary<string, OcrFilterType>
        {
            ["グレースケール変換"] = OcrFilterType.Grayscale,
            ["コントラスト強調"] = OcrFilterType.ContrastEnhancement,
            ["ノイズ除去"] = OcrFilterType.NoiseReduction,
            ["二値化"] = OcrFilterType.Threshold,
            ["モルフォロジー処理"] = OcrFilterType.Morphology,
            ["エッジ検出"] = OcrFilterType.EdgeDetection
        };
    }

    /// <inheritdoc/>
    public Baketa.Core.Abstractions.Imaging.IImageFilter[] CreateStandardOcrPipeline()
    {
        _logger.LogInformation("標準OCR前処理パイプラインを作成しています");

        // 標準的なOCR前処理パイプライン
        // 1. グレースケール変換
        // 2. ノイズ除去
        // 3. コントラスト強調
        // 4. 二値化
        // 5. モルフォロジー処理（必要に応じて）
        return [
            CreateFilter(OcrFilterType.Grayscale),
                CreateFilter(OcrFilterType.NoiseReduction),
                CreateFilter(OcrFilterType.ContrastEnhancement),
                CreateFilter(OcrFilterType.Threshold),
                CreateFilter(OcrFilterType.Morphology)
        ];
    }

    /// <inheritdoc/>
    public Baketa.Core.Abstractions.Imaging.IImageFilter[] CreateMinimalOcrPipeline()
    {
        _logger.LogInformation("最小限のOCR前処理パイプラインを作成しています");

        // 最小限のOCR前処理パイプライン
        // 1. グレースケール変換
        // 2. コントラスト強調
        // 3. 二値化
        return [
            CreateFilter(OcrFilterType.Grayscale),
                CreateFilter(OcrFilterType.ContrastEnhancement),
                CreateFilter(OcrFilterType.Threshold)
        ];
    }

    /// <inheritdoc/>
    public Baketa.Core.Abstractions.Imaging.IImageFilter[] CreateEdgeBasedOcrPipeline()
    {
        _logger.LogInformation("エッジベースのOCR前処理パイプラインを作成しています");

        // エッジ検出に基づくOCR前処理パイプライン
        // 1. グレースケール変換
        // 2. ノイズ除去
        // 3. エッジ検出
        // 4. 二値化
        return [
            CreateFilter(OcrFilterType.Grayscale),
                CreateFilter(OcrFilterType.NoiseReduction),
                CreateFilter(OcrFilterType.EdgeDetection),
                CreateFilter(OcrFilterType.Threshold)
        ];
    }
}

/// <summary>
/// OCRフィルター作成時の例外
/// </summary>
public class OcrFilterCreationException : Exception
{
    /// <summary>
    /// 新しいOcrFilterCreationExceptionを作成します
    /// </summary>
    public OcrFilterCreationException()
        : base()
    {
    }

    /// <summary>
    /// 新しいOcrFilterCreationExceptionを作成します
    /// </summary>
    /// <param name="message">エラーメッセージ</param>
    public OcrFilterCreationException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// 新しいOcrFilterCreationExceptionを作成します
    /// </summary>
    /// <param name="message">エラーメッセージ</param>
    /// <param name="innerException">内部例外</param>
    public OcrFilterCreationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
