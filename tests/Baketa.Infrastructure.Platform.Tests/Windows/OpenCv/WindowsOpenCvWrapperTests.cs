using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.OCR;
using Baketa.Infrastructure.Platform.Windows.OpenCv;
using Baketa.Infrastructure.Platform.Windows.OpenCv.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OpenCvSharp;
using Xunit;
using FactoryImageFactory = Baketa.Core.Abstractions.Factories.IImageFactory;
using IImage = Baketa.Core.Abstractions.Imaging.IImage;
using IAdvancedImage = Baketa.Core.Abstractions.Imaging.IAdvancedImage;
using ImageFormat = Baketa.Core.Abstractions.Imaging.ImageFormat;
using Size = System.Drawing.Size;

namespace Baketa.Infrastructure.Platform.Tests.Windows.OpenCv
{
    /// <summary>
    /// WindowsOpenCvWrapperの単体テスト
    /// </summary>
    [SuppressMessage("Design", "CA1515:型を内部にする必要があります", Justification = "xUnitのテストクラスはpublicでなければなりません")]
    public class WindowsOpenCvWrapperTests
    {
        private readonly Mock<ILogger<WindowsOpenCvWrapper>> _mockLogger;
        private readonly Mock<FactoryImageFactory> _mockImageFactory;
        private readonly Mock<IOptions<OpenCvOptions>> _mockOptions;
        private readonly OpenCvOptions _options;

        public WindowsOpenCvWrapperTests()
        {
            // テスト用のモックを初期化
            _mockLogger = new Mock<ILogger<WindowsOpenCvWrapper>>();
            _mockImageFactory = new Mock<FactoryImageFactory>();
            _options = new OpenCvOptions
            {
                DefaultThreadCount = 1,
                DefaultMserParameters = TextDetectionParams.CreateForMethod(TextDetectionMethod.Mser),
                DefaultConnectedComponentsParameters = TextDetectionParams.CreateForMethod(TextDetectionMethod.ConnectedComponents),
                DefaultContoursParameters = TextDetectionParams.CreateForMethod(TextDetectionMethod.Contours),
                DefaultEdgeBasedParameters = TextDetectionParams.CreateForMethod(TextDetectionMethod.EdgeBased)
            };
            _mockOptions = new Mock<IOptions<OpenCvOptions>>();
            _mockOptions.Setup(o => o.Value).Returns(_options);

            // IImageFactory設定 - CreateFromBytesAsyncをモック化
            _mockImageFactory
                .Setup(factory => factory.CreateFromBytesAsync(It.IsAny<byte[]>()))
                .Returns<byte[]>(bytes => 
                {
                    var mockImage = CreateMockAdvancedImage(bytes);
                    return Task.FromResult<IImage>(mockImage);
                });
        }

        #region ヘルパーメソッド

        /// <summary>
        /// テスト用の空のAdvancedImageモックを作成します
        /// </summary>
        private static IAdvancedImage CreateEmptyMockAdvancedImage()
        {
            var mockImage = new Mock<IAdvancedImage>();
            mockImage.Setup(i => i.Width).Returns(100);
            mockImage.Setup(i => i.Height).Returns(100);
            mockImage.Setup(i => i.Format).Returns(ImageFormat.Rgb24);
            
            // バイト配列への変換をモック
            mockImage
                .Setup(i => i.ToByteArrayAsync())
                .ReturnsAsync(() => {
                    // 空のMatからバイト配列を作成（テスト用）
                    using var mat = new Mat(100, 100, MatType.CV_8UC3, new Scalar(0, 0, 0));
                    return mat.ToBytes(".png");
                });
            
            return mockImage.Object;
        }

        /// <summary>
        /// バイト配列から変換したAdvancedImageモックを作成します
        /// </summary>
        private static IAdvancedImage CreateMockAdvancedImage(byte[] bytes)
        {
            var mockImage = new Mock<IAdvancedImage>();
            
            // Matからサイズと形式を取得
            using var mat = Mat.FromImageData(bytes);
            
            mockImage.Setup(i => i.Width).Returns(mat.Width);
            mockImage.Setup(i => i.Height).Returns(mat.Height);
            
            // チャンネル数に応じてフォーマットを設定
            var format = mat.Channels() switch
            {
                1 => ImageFormat.Grayscale8,
                3 => ImageFormat.Rgb24,
                4 => ImageFormat.Rgba32,
                _ => ImageFormat.Unknown
            };
            mockImage.Setup(i => i.Format).Returns(format);
            
            // ToByteArrayAsyncは元のバイト配列を返す
            mockImage.Setup(i => i.ToByteArrayAsync()).ReturnsAsync(bytes);
            
            return mockImage.Object;
        }

        /// <summary>
        /// テスト用の実際の画像を読み込みます
        /// </summary>
        private static async Task<IAdvancedImage> LoadTestImageAsync()
        {
            // テスト用のサンプル画像生成
            using var mat = new Mat(200, 300, MatType.CV_8UC3, new Scalar(255, 255, 255));
            
            // テキスト領域をシミュレートするために簡単な矩形を描画
            Cv2.Rectangle(mat, new OpenCvSharp.Point(50, 50), new OpenCvSharp.Point(250, 150), new Scalar(0, 0, 0), -1);
            Cv2.PutText(mat, "Test", new OpenCvSharp.Point(100, 100), HersheyFonts.HersheyPlain, 2, new Scalar(255, 255, 255), 2);
            
            // バイト配列に変換
            var bytes = mat.ToBytes(".png");
            
            // モック画像を作成 - 非同期呼び出しを待機する
            return await Task.FromResult(CreateMockAdvancedImage(bytes)).ConfigureAwait(true);
        }

        /// <summary>
        /// シンプルなWindowsOpenCvWrapperインスタンスを作成します
        /// </summary>
        private WindowsOpenCvWrapper CreateWrapper()
        {
            return new WindowsOpenCvWrapper(
                _mockLogger.Object,
                _mockImageFactory.Object,
                _mockOptions.Object);
        }

        #endregion

        #region 基本初期化テスト

        [Fact]
        public void ConstructorWithValidParametersShouldInitializeCorrectly()
        {
            // Arrange & Act
            using var wrapper = CreateWrapper();
            
            // Assert
            Assert.NotNull(wrapper);
            // 追加の検証が必要な場合はここに
        }

        [Fact]
        public void ConstructorWithNullLoggerShouldThrowArgumentNullException()
        {
            // Arrange, Act & Assert
            var exception = Assert.Throws<ArgumentNullException>(() => new WindowsOpenCvWrapper(
                null!,
                _mockImageFactory.Object,
                _mockOptions.Object));
                
            Assert.Equal("logger", exception.ParamName);
        }

        [Fact]
        public void ConstructorWithNullImageFactoryShouldThrowArgumentNullException()
        {
            // Arrange, Act & Assert
            var exception = Assert.Throws<ArgumentNullException>(() => new WindowsOpenCvWrapper(
                _mockLogger.Object,
                null!,
                _mockOptions.Object));
                
            Assert.Equal("imageFactory", exception.ParamName);
        }

        #endregion

        #region グレースケール変換テスト

        [Fact]
        public async Task ConvertToGrayscaleAsyncWithValidImageShouldReturnGrayscaleImage()
        {
            // Arrange
            using var wrapper = CreateWrapper();
            var sourceImage = await LoadTestImageAsync().ConfigureAwait(true);
            
            // Act
            var result = await wrapper.ConvertToGrayscaleAsync(sourceImage).ConfigureAwait(true);
            
            // Assert
            Assert.NotNull(result);
            // フォーマットまたはバイナリデータを検証
            // 注: 詳細な結果検証にはモック応答を調整する必要があります
        }

        [Fact]
        public async Task ConvertToGrayscaleAsyncWithNullImageShouldThrowArgumentNullException()
        {
            // Arrange
            using var wrapper = CreateWrapper();
            
            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentNullException>(
                () => wrapper.ConvertToGrayscaleAsync(null!)).ConfigureAwait(true);
                
            Assert.Equal("source", exception.ParamName);
        }

        #endregion

        #region 閾値処理テスト

        [Fact]
        public async Task ApplyThresholdAsyncWithValidParametersShouldReturnThresholdedImage()
        {
            // Arrange
            using var wrapper = CreateWrapper();
            var sourceImage = await LoadTestImageAsync().ConfigureAwait(true);
            
            // Act
            var result = await wrapper.ApplyThresholdAsync(sourceImage, 128, 255, ThresholdType.Binary).ConfigureAwait(true);
            
            // Assert
            Assert.NotNull(result);
            // 詳細な結果検証
        }

        [Fact]
        public async Task ApplyThresholdAsyncWithNullImageShouldThrowArgumentNullException()
        {
            // Arrange
            using var wrapper = CreateWrapper();
            
            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => 
                wrapper.ApplyThresholdAsync(null!, 128, 255, ThresholdType.Binary)).ConfigureAwait(true);
                
            Assert.Equal("source", exception.ParamName);
        }

        #endregion

        #region 適応的閾値処理テスト

        [Fact]
        public async Task ApplyAdaptiveThresholdAsyncWithValidParametersShouldReturnThresholdedImage()
        {
            // Arrange
            using var wrapper = CreateWrapper();
            var sourceImage = await LoadTestImageAsync().ConfigureAwait(true);
            
            // Act
            var result = await wrapper.ApplyAdaptiveThresholdAsync(
                sourceImage, 255, AdaptiveThresholdType.Gaussian, ThresholdType.Binary, 11, 2).ConfigureAwait(true);
            
            // Assert
            Assert.NotNull(result);
            // 詳細な結果検証
        }

        [Fact]
        public async Task ApplyAdaptiveThresholdAsyncWithNullImageShouldThrowArgumentNullException()
        {
            // Arrange
            using var wrapper = CreateWrapper();
            
            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => 
                wrapper.ApplyAdaptiveThresholdAsync(null!, 255, AdaptiveThresholdType.Gaussian, ThresholdType.Binary, 11, 2))
                .ConfigureAwait(true);
                
            Assert.Equal("source", exception.ParamName);
        }

        #endregion

        #region ガウシアンブラーテスト

        [Fact]
        public async Task ApplyGaussianBlurAsyncWithValidParametersShouldReturnBlurredImage()
        {
            // Arrange
            using var wrapper = CreateWrapper();
            var sourceImage = await LoadTestImageAsync().ConfigureAwait(true);
            
            // Act
            var result = await wrapper.ApplyGaussianBlurAsync(sourceImage, new Size(5, 5), 1.5).ConfigureAwait(true);
            
            // Assert
            Assert.NotNull(result);
            // 詳細な結果検証
        }

        [Fact]
        public async Task ApplyGaussianBlurAsyncWithNullImageShouldThrowArgumentNullException()
        {
            // Arrange
            using var wrapper = CreateWrapper();
            
            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => 
                wrapper.ApplyGaussianBlurAsync(null!, new Size(5, 5), 1.5)).ConfigureAwait(true);
                
            Assert.Equal("source", exception.ParamName);
        }

        #endregion

        #region Cannyエッジ検出テスト

        [Fact]
        public async Task ApplyCannyEdgeAsyncWithValidParametersShouldReturnEdgeImage()
        {
            // Arrange
            using var wrapper = CreateWrapper();
            var sourceImage = await LoadTestImageAsync().ConfigureAwait(true);
            
            // Act
            var result = await wrapper.ApplyCannyEdgeAsync(sourceImage, 50, 150).ConfigureAwait(true);
            
            // Assert
            Assert.NotNull(result);
            // 詳細な結果検証
        }

        [Fact]
        public async Task ApplyCannyEdgeAsyncWithNullImageShouldThrowArgumentNullException()
        {
            // Arrange
            using var wrapper = CreateWrapper();
            
            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => 
                wrapper.ApplyCannyEdgeAsync(null!, 50, 150)).ConfigureAwait(true);
                
            Assert.Equal("source", exception.ParamName);
        }

        #endregion

        #region モルフォロジー演算テスト

        [Fact]
        public async Task ApplyMorphologyAsyncWithValidParametersShouldReturnProcessedImage()
        {
            // Arrange
            using var wrapper = CreateWrapper();
            var sourceImage = await LoadTestImageAsync().ConfigureAwait(true);
            
            // Act
            var result = await wrapper.ApplyMorphologyAsync(
                sourceImage, MorphType.Dilate, new Size(3, 3), 1).ConfigureAwait(true);
            
            // Assert
            Assert.NotNull(result);
            // 詳細な結果検証
        }

        [Fact]
        public async Task ApplyMorphologyAsyncWithNullImageShouldThrowArgumentNullException()
        {
            // Arrange
            using var wrapper = CreateWrapper();
            
            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => 
                wrapper.ApplyMorphologyAsync(null!, MorphType.Dilate, new Size(3, 3), 1)).ConfigureAwait(true);
                
            Assert.Equal("source", exception.ParamName);
        }

        #endregion

        #region テキスト領域検出テスト

        [Fact]
        public async Task DetectTextRegionsAsyncWithValidParametersShouldReturnRectangles()
        {
            // Arrange
            using var wrapper = CreateWrapper();
            var sourceImage = await LoadTestImageAsync().ConfigureAwait(true);
            
            // Act
            var result = await wrapper.DetectTextRegionsAsync(
                sourceImage, TextDetectionMethod.Mser).ConfigureAwait(true);
            
            // Assert
            Assert.NotNull(result);
            // 詳細な結果検証は実装に応じて
        }

        [Fact]
        public async Task DetectTextRegionsAsyncWithNullImageShouldThrowArgumentNullException()
        {
            // Arrange
            using var wrapper = CreateWrapper();
            
            // Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => 
                wrapper.DetectTextRegionsAsync(null!, TextDetectionMethod.Mser)).ConfigureAwait(true);
                
            Assert.Equal("source", exception.ParamName);
        }

        #endregion

        #region リソース解放テスト

        [Fact]
        public async Task DisposeShouldReleaseResources()
        {
            // Arrange - 後で明示的に破棄するため using は使わない
            var wrapper = CreateWrapper();
            
            // Act
            wrapper.Dispose();
            
            // Assert
            // 二重解放が例外を投げないことを確認
            wrapper.Dispose();

            // 解放後のメソッド呼び出しが適切な例外をスローすることを確認
            var exception = await Assert.ThrowsAsync<ObjectDisposedException>(() => 
                wrapper.ConvertToGrayscaleAsync(CreateEmptyMockAdvancedImage())).ConfigureAwait(true);
                
            Assert.Contains(nameof(WindowsOpenCvWrapper), exception.ObjectName, StringComparison.Ordinal);
            
            // テスト終了時に確実に破棄 - コード上としては必要ないが
            // CA1816警告を回避するために明示的な Dispose の呼び出しで置き換え
            // 実際はすでに Dispose されているのでファイナライザの実行は必要ない
            // GC.SuppressFinalize(wrapper);
        }

        #endregion

        #region 例外処理テスト

        [Fact]
        public async Task ErrorHandlingShouldWrapExceptionsCorrectly()
        {
            // Arrange
            using var wrapper = CreateWrapper();
            
            // 意図的にエラーを発生させるためのモック設定
            var mockErrorImage = new Mock<IAdvancedImage>();
            mockErrorImage.Setup(i => i.ToByteArrayAsync())
                .ThrowsAsync(new InvalidOperationException("テスト用のエラー"));
            
            // Act & Assert
            var exception = await Assert.ThrowsAsync<OcrProcessingException>(() => 
                wrapper.ConvertToGrayscaleAsync(mockErrorImage.Object)).ConfigureAwait(true);
            
            Assert.NotNull(exception.InnerException);
            Assert.IsType<InvalidOperationException>(exception.InnerException);
            Assert.Contains("テスト用のエラー", exception.InnerException.Message, StringComparison.Ordinal);
        }

        #endregion
    }
}
