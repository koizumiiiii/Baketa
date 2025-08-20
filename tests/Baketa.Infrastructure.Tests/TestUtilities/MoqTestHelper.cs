using System.Drawing;
using Moq;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Imaging;

namespace Baketa.Infrastructure.Tests.TestUtilities;

/// <summary>
/// Moqテスト用ユーティリティ
/// CS8620警告の根本解決：null許容性対応
/// </summary>
public static class MoqTestHelper
{
    /// <summary>
    /// IOcrEngine用のnull許容性対応Mockセットアップ（DetectTextRegionsAsync）
    /// CS8620警告解決：明示的な型キャストでnull許容性一致
    /// </summary>
    /// <param name="mockOcrEngine">MockのIOcrEngine</param>
    /// <param name="result">返却するOcrResults</param>
    public static void SetupOcrEngineDetectTextRegionsAsync(Mock<IOcrEngine> mockOcrEngine, OcrResults result)
    {
        // null許容性問題を明示的キャストで解決
        mockOcrEngine
            .Setup(x => x.DetectTextRegionsAsync(It.IsAny<IImage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
    }

    /// <summary>
    /// IOcrEngine用のnull許容性対応Mockセットアップ（RecognizeAsync）
    /// </summary>
    /// <param name="mockOcrEngine">MockのIOcrEngine</param>
    /// <param name="result">返却するOcrResults</param>
    public static void SetupOcrEngineRecognizeAsync(Mock<IOcrEngine> mockOcrEngine, OcrResults result)
    {
        mockOcrEngine
            .Setup(x => x.RecognizeAsync(It.IsAny<IImage>(), It.IsAny<IProgress<OcrProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        
        mockOcrEngine
            .Setup(x => x.RecognizeAsync(It.IsAny<IImage>(), It.IsAny<Rectangle?>(), It.IsAny<IProgress<OcrProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
    }

    /// <summary>
    /// 例外をスローするOcrEngineのセットアップ（DetectTextRegionsAsync）
    /// </summary>
    /// <param name="mockOcrEngine">MockのIOcrEngine</param>
    /// <param name="exception">スローする例外</param>
    public static void SetupOcrEngineDetectException(Mock<IOcrEngine> mockOcrEngine, Exception exception)
    {
        mockOcrEngine
            .Setup(x => x.DetectTextRegionsAsync(It.IsAny<IImage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);
    }

    /// <summary>
    /// 例外をスローするOcrEngineのセットアップ（RecognizeAsync）
    /// </summary>
    /// <param name="mockOcrEngine">MockのIOcrEngine</param>
    /// <param name="exception">スローする例外</param>
    public static void SetupOcrEngineRecognizeException(Mock<IOcrEngine> mockOcrEngine, Exception exception)
    {
        mockOcrEngine
            .Setup(x => x.RecognizeAsync(It.IsAny<IImage>(), It.IsAny<IProgress<OcrProgress>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);
            
        mockOcrEngine
            .Setup(x => x.RecognizeAsync(It.IsAny<IImage>(), It.IsAny<Rectangle?>(), It.IsAny<IProgress<OcrProgress>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);
    }

    /// <summary>
    /// 条件付きでOcrResultsを返すセットアップ（DetectTextRegionsAsync）
    /// </summary>
    /// <param name="mockOcrEngine">MockのIOcrEngine</param>
    /// <param name="predicate">条件判定</param>
    /// <param name="successResult">成功時の結果</param>
    /// <param name="failureResult">失敗時の結果</param>
    public static void SetupConditionalOcrEngineDetect(
        Mock<IOcrEngine> mockOcrEngine, 
        Func<IImage, bool> predicate,
        OcrResults successResult,
        OcrResults failureResult)
    {
        mockOcrEngine
            .Setup(x => x.DetectTextRegionsAsync(It.IsAny<IImage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IImage frame, CancellationToken _) => 
                predicate(frame) ? successResult : failureResult);
    }

    /// <summary>
    /// テスト用のダミーOcrResults作成
    /// </summary>
    /// <param name="text">OCR結果テキスト</param>
    /// <param name="confidence">信頼度（0.0-1.0）</param>
    /// <returns>テスト用OcrResults</returns>
    public static OcrResults CreateTestOcrResults(string text = "Test OCR Result", double confidence = 0.95)
    {
        var mockImage = new Mock<IImage>();
        mockImage.Setup(x => x.Width).Returns(100);
        mockImage.Setup(x => x.Height).Returns(50);
        
        var textRegions = string.IsNullOrEmpty(text) ? 
            new List<OcrTextRegion>() :
            new List<OcrTextRegion>
            {
                new(text, new System.Drawing.Rectangle(0, 0, 100, 20), confidence)
            };
        
        return new OcrResults(
            textRegions,
            mockImage.Object,
            TimeSpan.FromMilliseconds(100),
            "ja"
        );
    }
}
