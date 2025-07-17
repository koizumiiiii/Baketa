using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using OCRTextRegion = Baketa.Core.Abstractions.OCR.TextDetection.TextRegion;

namespace Baketa.Core.Abstractions.OCR;

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
/// <remarks>
/// コンストラクタ
/// </remarks>
/// <param name="isCancelled">処理がキャンセルされたかどうか</param>
/// <param name="error">エラーが発生した場合の例外</param>
/// <param name="processedImage">前処理後の画像</param>
/// <param name="detectedRegions">検出されたテキスト領域</param>
public class OcrPreprocessingResult(
        bool isCancelled,
        Exception? error,
        IAdvancedImage processedImage,
        IReadOnlyList<OCRTextRegion> detectedRegions)
{
    /// <summary>
    /// 処理がキャンセルされたかどうか
    /// </summary>
    public bool IsCancelled { get; } = isCancelled;

    /// <summary>
    /// エラーが発生した場合の例外
    /// </summary>
    public Exception? Error { get; } = error;

    /// <summary>
    /// 前処理後の画像
    /// </summary>
    public IAdvancedImage ProcessedImage { get; } = processedImage ?? throw new ArgumentNullException(nameof(processedImage));

    /// <summary>
    /// 検出されたテキスト領域
    /// </summary>
    public IReadOnlyList<OCRTextRegion> DetectedRegions { get; } = detectedRegions ?? Array.Empty<OCRTextRegion>();
}