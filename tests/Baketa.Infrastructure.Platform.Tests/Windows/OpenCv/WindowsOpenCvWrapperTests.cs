using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Imaging;
// 名前空間の衝突を避けるためのエイリアス
using OCRThresholdType = Baketa.Core.Abstractions.OCR.ThresholdType;
using OCRAdaptiveThresholdType = Baketa.Core.Abstractions.OCR.AdaptiveThresholdType;
using OCRMorphType = Baketa.Core.Abstractions.OCR.MorphType;
using DrawingPoint = System.Drawing.Point;
using OpenCvPoint = OpenCvSharp.Point;
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

namespace Baketa.Infrastructure.Platform.Tests.Windows.OpenCv;

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
        private readonly Mock<IWindowsOpenCvLibrary> _mockOpenCvLib;

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

            // IWindowsOpenCvLibraryのモックを作成
            _mockOpenCvLib = new Mock<IWindowsOpenCvLibrary>();
            
            // 各メソッドのスタブを設定
            _mockOpenCvLib.Setup(lib => lib.ThresholdAsync(It.IsAny<IAdvancedImage>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<int>()))
                .Returns<IAdvancedImage, double, double, int>((image, _, __, ___) => Task.FromResult(image));
                
            _mockOpenCvLib.Setup(lib => lib.AdaptiveThresholdAsync(It.IsAny<IAdvancedImage>(), It.IsAny<double>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<double>()))
                .Returns<IAdvancedImage, double, int, int, int, double>((image, _, __, ___, ____, _____) => Task.FromResult(image));
                
            _mockOpenCvLib.Setup(lib => lib.MorphologyAsync(It.IsAny<IAdvancedImage>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns<IAdvancedImage, int, int>((image, _, __) => Task.FromResult(image));
                
            // 型変換問題を解決するため、DetectedRegion型を返すよう変更
            _mockOpenCvLib.Setup(lib => lib.FindConnectedComponents(It.IsAny<IAdvancedImage>(), It.IsAny<int>(), It.IsAny<int>()))
                // 接続可能なPoint配列の空リストを返す
                .Returns(new List<DrawingPoint[]>([]));
                
            _mockOpenCvLib.Setup(lib => lib.DetectMserRegions(It.IsAny<IAdvancedImage>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns([
                    new DetectedRegion
                    { 
                        Bounds = new Rectangle(10, 10, 50, 50),
                        Points = [new DrawingPoint(10, 10), new DrawingPoint(60, 60)],
                        Confidence = 0.8f
                    }
                ]);
                
            _mockOpenCvLib.Setup(lib => lib.DetectSwtRegions(It.IsAny<IAdvancedImage>(), It.IsAny<bool>(), It.IsAny<float>()))
                .Returns([
                    new DetectedRegion
                    { 
                        Bounds = new Rectangle(10, 10, 50, 50),
                        Points = [new DrawingPoint(10, 10), new DrawingPoint(60, 60)],
                        Confidence = 0.8f,
                        StrokeWidth = 2.5f
                    }
                ]);

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
            mockImage.Setup(i => i.IsGrayscale).Returns(format == ImageFormat.Grayscale8);
            mockImage.Setup(i => i.BitsPerPixel).Returns(format == ImageFormat.Grayscale8 ? 8 : 24);
            mockImage.Setup(i => i.ChannelCount).Returns(format == ImageFormat.Grayscale8 ? 1 : 3);
            mockImage.Setup(i => i.ToGrayscale()).Returns(() => {
                if (format == ImageFormat.Grayscale8) return mockImage.Object;
                var grayMock = new Mock<IAdvancedImage>();
                grayMock.Setup(i => i.Width).Returns(mat.Width);
                grayMock.Setup(i => i.Height).Returns(mat.Height);
                grayMock.Setup(i => i.Format).Returns(ImageFormat.Grayscale8);
                grayMock.Setup(i => i.IsGrayscale).Returns(true);
                grayMock.Setup(i => i.BitsPerPixel).Returns(8);
                grayMock.Setup(i => i.ChannelCount).Returns(1);
                grayMock.Setup(i => i.ToByteArrayAsync()).ReturnsAsync(bytes);
                return grayMock.Object;
            });
            
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
            Cv2.Rectangle(mat, new OpenCvPoint(50, 50), new OpenCvPoint(250, 150), new Scalar(0, 0, 0), -1);
            Cv2.PutText(mat, "Test", new OpenCvPoint(100, 100), HersheyFonts.HersheyPlain, 2, new Scalar(255, 255, 255), 2);
            
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
            return new WindowsOpenCvWrapper(_mockOpenCvLib.Object);
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
        public void ConstructorWithNullOpenCvLibShouldThrowArgumentNullException()
        {
            // Arrange, Act & Assert
            var exception = Assert.Throws<ArgumentNullException>(() => new WindowsOpenCvWrapper(null!));
                
            Assert.Equal("openCvLib", exception.ParamName);
        }

        // 他のコンストラクタパラメータのテストは削除（単一パラメータになったため）

        #endregion

        #region グレースケール変換テスト

        [Fact]
        public async Task ConvertToGrayscaleAsyncWithValidImageShouldReturnGrayscaleImage()
        {
            // Arrange
            using var wrapper = CreateWrapper();
            var sourceImage = await LoadTestImageAsync().ConfigureAwait(true);
            
            // Act
            var result = await WindowsOpenCvWrapper.ConvertToGrayscaleAsync(sourceImage).ConfigureAwait(true);
            
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
                () => WindowsOpenCvWrapper.ConvertToGrayscaleAsync(null!)).ConfigureAwait(true);
                
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
            var result = await wrapper.ApplyOcrThresholdAsync(sourceImage, 128, 255, OCRThresholdType.Binary).ConfigureAwait(true);
            
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
                wrapper.ApplyOcrThresholdAsync(null!, 128, 255, OCRThresholdType.Binary)).ConfigureAwait(true);
                
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
            var result = await wrapper.ApplyOcrAdaptiveThresholdAsync(
                sourceImage, 255, OCRAdaptiveThresholdType.Gaussian, OCRThresholdType.Binary, 11, 2).ConfigureAwait(true);
            
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
                wrapper.ApplyOcrAdaptiveThresholdAsync(null!, 255, OCRAdaptiveThresholdType.Gaussian, OCRThresholdType.Binary, 11, 2))
                .ConfigureAwait(true);
                
            Assert.Equal("source", exception.ParamName);
        }

        #endregion

        #region ガウシアンブラーテスト

        [Fact]
        public async Task ApplyGaussianBlurAsyncWithValidParametersShouldReturnBlurredImage()
        {
            // Arrange
            var sourceImage = await LoadTestImageAsync().ConfigureAwait(true);
            
            // Act
            var result = await WindowsOpenCvWrapper.ApplyGaussianBlurAsync(sourceImage, new Size(5, 5), 1.5).ConfigureAwait(true);
            
            // Assert
            Assert.NotNull(result);
            // 詳細な結果検証
        }

        [Fact]
        public async Task ApplyGaussianBlurAsyncWithNullImageShouldThrowArgumentNullException()
        {
            // Arrange & Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => 
                WindowsOpenCvWrapper.ApplyGaussianBlurAsync(null!, new Size(5, 5), 1.5)).ConfigureAwait(true);
                
            Assert.Equal("source", exception.ParamName);
        }

        #endregion

        #region Cannyエッジ検出テスト

        [Fact]
        public async Task ApplyCannyEdgeAsyncWithValidParametersShouldReturnEdgeImage()
        {
            // Arrange
            var sourceImage = await LoadTestImageAsync().ConfigureAwait(true);
            
            // Act
            var result = await WindowsOpenCvWrapper.ApplyCannyEdgeAsync(sourceImage, 50, 150).ConfigureAwait(true);
            
            // Assert
            Assert.NotNull(result);
            // 詳細な結果検証
        }

        [Fact]
        public async Task ApplyCannyEdgeAsyncWithNullImageShouldThrowArgumentNullException()
        {
            // Arrange & Act & Assert
            var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => 
                WindowsOpenCvWrapper.ApplyCannyEdgeAsync(null!, 50, 150)).ConfigureAwait(true);
                
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
                sourceImage, Baketa.Core.Abstractions.Imaging.MorphType.Dilate, new Size(3, 3), 1).ConfigureAwait(true);
            
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
                wrapper.ApplyMorphologyAsync(null!, Baketa.Core.Abstractions.Imaging.MorphType.Dilate, new Size(3, 3), 1)).ConfigureAwait(true);
                
            Assert.Equal("source", exception.ParamName);
        }

        #endregion

        #region テキスト領域検出テスト

        [Fact]
        public Task DetectTextRegionsAsyncWithValidParametersShouldReturnRectangles()
        {
            // Arrange
            using var wrapper = CreateWrapper();
            
            // テスト画像を同期的に作成
            var sourceImage = CreateEmptyMockAdvancedImage();
            
            // Act
            var result = wrapper.DetectTextRegionsWithMser(
                sourceImage, 5, 60, 14400);
            
            // Assert
            Assert.NotNull(result);
            
            return Task.CompletedTask;
        }

        [Fact]
        public Task DetectTextRegionsAsyncWithNullImageShouldThrowArgumentNullException()
        {
            // Arrange
            using var wrapper = CreateWrapper();
            
            // Act & Assert
            var exception = Assert.Throws<ArgumentNullException>(() => 
                wrapper.DetectTextRegionsWithMser(null!, 5, 60, 14400));
                
            Assert.Equal("image", exception.ParamName);
            
            return Task.CompletedTask;
        }

        #endregion

        #region リソース解放テスト

        [Fact]
        public Task DisposeShouldReleaseResources()
        {
            // Arrange - 後で明示的に破棄するため using は使わない
            var wrapper = CreateWrapper();
            
            // Act
            wrapper.Dispose();
            
            // Assert
            // 二重解放が例外を投げないことを確認
            wrapper.Dispose();

            // 解放後のメソッド呼び出しが適切な例外をスローすることを確認
            var exception = Assert.Throws<ObjectDisposedException>(() => 
                wrapper.DetectTextRegionsWithMser(CreateEmptyMockAdvancedImage(), 5, 60, 14400));
                
            Assert.Contains(nameof(WindowsOpenCvWrapper), exception.ObjectName, StringComparison.Ordinal);
            
            return Task.CompletedTask;
        }

        #endregion

        #region 例外処理テスト

        [Fact]
        public async Task ErrorHandlingShouldWrapExceptionsCorrectly()
        {
            // Arrange
            // 意図的にエラーを発生させるためのモック設定
            var mockErrorImage = new Mock<IAdvancedImage>();
            mockErrorImage.Setup(i => i.ToByteArrayAsync())
                .ThrowsAsync(new InvalidOperationException("テスト用のエラー"));

            // OpenCvLibに例外をスローさせる
            _mockOpenCvLib.Setup(lib => lib.ThresholdAsync(It.IsAny<IAdvancedImage>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<int>()))
                .ThrowsAsync(new InvalidOperationException("テスト用のエラー"));

            // テストするラッパーを作成
            using var wrapper = CreateWrapper();
            var sourceImage = await LoadTestImageAsync().ConfigureAwait(true);

            // Act & Assert - OcrProcessingExceptionがスローされることを確認
            // エラーがOcrProcessingExceptionかInvalidOperationExceptionのどちらかであることを確認
            var exception = await Assert.ThrowsAnyAsync<Exception>(() => 
                wrapper.ApplyOcrThresholdAsync(sourceImage, 128, 255, OCRThresholdType.Binary)).ConfigureAwait(true);
            
            // 例外チェック - 実装で投げる例外型に応じて検証
            Assert.True(exception is InvalidOperationException || exception is Baketa.Infrastructure.Platform.Windows.OpenCv.Exceptions.OcrProcessingException,
                      $"期待される例外型ではありません: {exception.GetType().Name}");
        }

        #endregion
    }
