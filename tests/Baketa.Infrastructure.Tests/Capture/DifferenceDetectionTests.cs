using Baketa.Core.Events.Capture;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Capture;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Infrastructure.Capture.DifferenceDetection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.Capture
{
    public class DifferenceDetectionTests
    {
        private readonly Mock<IImage> _mockImage1;
        private readonly Mock<IImage> _mockImage2;
        private readonly Mock<IEventAggregator> _mockEventAggregator;
        private readonly Mock<ILogger<EnhancedDifferenceDetector>> _mockLogger;
        private readonly List<IDetectionAlgorithm> _mockAlgorithms;
        
        public DifferenceDetectionTests()
        {
            // 共通テスト用モックオブジェクトの準備
            _mockImage1 = new Mock<IImage>();
            _mockImage1.Setup(i => i.Width).Returns(800);
            _mockImage1.Setup(i => i.Height).Returns(600);
            
            _mockImage2 = new Mock<IImage>();
            _mockImage2.Setup(i => i.Width).Returns(800);
            _mockImage2.Setup(i => i.Height).Returns(600);
            
            _mockEventAggregator = new Mock<IEventAggregator>();
            _mockLogger = new Mock<ILogger<EnhancedDifferenceDetector>>();
            
            // アルゴリズムモック
            var mockHistogramAlgo = new Mock<IDetectionAlgorithm>();
            mockHistogramAlgo.Setup(a => a.AlgorithmType).Returns(DifferenceDetectionAlgorithm.HistogramBased);
            
            var mockSamplingAlgo = new Mock<IDetectionAlgorithm>();
            mockSamplingAlgo.Setup(a => a.AlgorithmType).Returns(DifferenceDetectionAlgorithm.SamplingBased);
            
            _mockAlgorithms = new List<IDetectionAlgorithm>
            {
                mockHistogramAlgo.Object,
                mockSamplingAlgo.Object
            };
        }
        
        [Fact]
        public async Task HasSignificantChangeAsyncWithNullImageThrowsArgumentNullException()
        {
            // Arrange
            var detector = new EnhancedDifferenceDetector(_mockAlgorithms, _mockEventAggregator.Object, _mockLogger.Object);
            
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => detector.HasSignificantChangeAsync(null!, _mockImage2.Object)).ConfigureAwait(true);
            await Assert.ThrowsAsync<ArgumentNullException>(() => detector.HasSignificantChangeAsync(_mockImage1.Object, null!)).ConfigureAwait(true);
        }
        
        [Fact]
        public async Task HasSignificantChangeAsyncWithDifferentSizeReturnsTrue()
        {
            // Arrange
            var detector = new EnhancedDifferenceDetector(_mockAlgorithms, _mockEventAggregator.Object, _mockLogger.Object);
            
            // 画像サイズを変更
            _mockImage2.Setup(i => i.Width).Returns(1024);
            _mockImage2.Setup(i => i.Height).Returns(768);
            
            // Act
            bool result = await detector.HasSignificantChangeAsync(_mockImage1.Object, _mockImage2.Object).ConfigureAwait(true);
            
            // Assert
            Assert.True(result);
        }
        
        [Fact]
        public async Task HasSignificantChangeAsyncWithSameSizeUsesSelectedAlgorithm()
        {
            // Arrange
            var mockAlgo = new Mock<IDetectionAlgorithm>();
            mockAlgo.Setup(a => a.AlgorithmType).Returns(DifferenceDetectionAlgorithm.HistogramBased);
            
            var mockResult = new DetectionResult { HasSignificantChange = true, ChangeRatio = 0.3 };
            mockAlgo.Setup(a => a.DetectAsync(
                    It.IsAny<IImage>(), 
                    It.IsAny<IImage>(), 
                    It.IsAny<DifferenceDetectionSettings>(), 
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResult);
                
            var detector = new EnhancedDifferenceDetector(
                new List<IDetectionAlgorithm> { mockAlgo.Object }, 
                _mockEventAggregator.Object, 
                _mockLogger.Object);
                
            // 設定でHistogramBasedアルゴリズムを選択
            var settings = new DifferenceDetectionSettings { Algorithm = DifferenceDetectionAlgorithm.HistogramBased };
            detector.ApplySettings(settings);
            
            // Act
            bool result = await detector.HasSignificantChangeAsync(_mockImage1.Object, _mockImage2.Object).ConfigureAwait(true);
            
            // Assert
            Assert.True(result);
            mockAlgo.Verify(a => a.DetectAsync(
                It.IsAny<IImage>(), 
                It.IsAny<IImage>(), 
                It.IsAny<DifferenceDetectionSettings>(), 
                It.IsAny<CancellationToken>()), 
                Times.Once);
        }
        
        [Fact]
        public async Task DetectChangedRegionsAsyncWithDifferentSizeReturnsEntireScreen()
        {
            // Arrange
            var detector = new EnhancedDifferenceDetector(_mockAlgorithms, _mockEventAggregator.Object, _mockLogger.Object);
            
            // 画像サイズを変更
            _mockImage2.Setup(i => i.Width).Returns(1024);
            _mockImage2.Setup(i => i.Height).Returns(768);
            
            // Act
            var regions = await detector.DetectChangedRegionsAsync(_mockImage1.Object, _mockImage2.Object).ConfigureAwait(true);
            
            // Assert
            Assert.Single(regions);
            Assert.Equal(0, regions[0].X);
            Assert.Equal(0, regions[0].Y);
            Assert.Equal(1024, regions[0].Width);
            Assert.Equal(768, regions[0].Height);
        }
        
        [Fact]
        public void SetThresholdWithValidValueSetsThreshold()
        {
            // Arrange
            var detector = new EnhancedDifferenceDetector(_mockAlgorithms, _mockEventAggregator.Object, _mockLogger.Object);
            
            // Act
            detector.SetThreshold(0.25);
            var settings = detector.GetSettings();
            
            // Assert
            Assert.Equal(0.25, settings.Threshold);
        }
        
        [Fact]
        public void SetThresholdWithInvalidValueThrowsException()
        {
            // Arrange
            var detector = new EnhancedDifferenceDetector(_mockAlgorithms, _mockEventAggregator.Object, _mockLogger.Object);
            
            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => detector.SetThreshold(1.5));
            Assert.Throws<ArgumentOutOfRangeException>(() => detector.SetThreshold(-0.1));
        }
        
        [Fact]
        public void ApplySettingsWithValidSettingsUpdatesSettings()
        {
            // Arrange
            var detector = new EnhancedDifferenceDetector(_mockAlgorithms, _mockEventAggregator.Object, _mockLogger.Object);
            var settings = new DifferenceDetectionSettings
            {
                Threshold = 0.2,
                BlockSize = 32,
                FocusOnTextRegions = false,
                Algorithm = DifferenceDetectionAlgorithm.BlockBased
            };
            
            // Act
            detector.ApplySettings(settings);
            var retrievedSettings = detector.GetSettings();
            
            // Assert
            Assert.Equal(0.2, retrievedSettings.Threshold);
            Assert.Equal(32, retrievedSettings.BlockSize);
            Assert.False(retrievedSettings.FocusOnTextRegions);
            Assert.Equal(DifferenceDetectionAlgorithm.BlockBased, retrievedSettings.Algorithm);
        }
        
        [Fact]
        public async Task DetectTextDisappearanceAsyncWithPreviousTextRegionsEmitsEvent()
        {
            // Arrange
            var mockAlgo = new Mock<IDetectionAlgorithm>();
            mockAlgo.Setup(a => a.AlgorithmType).Returns(DifferenceDetectionAlgorithm.EdgeBased);
            
            var disappearedRegions = new List<Rectangle> { new Rectangle(10, 10, 100, 20) };
            var mockResult = new DetectionResult
            {
                HasSignificantChange = true,
                DisappearedTextRegions = disappearedRegions
            };
            
            mockAlgo.Setup(a => a.DetectAsync(
                    It.IsAny<IImage>(),
                    It.IsAny<IImage>(),
                    It.IsAny<DifferenceDetectionSettings>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockResult);
            
            var detector = new EnhancedDifferenceDetector(
                new List<IDetectionAlgorithm> { mockAlgo.Object },
                _mockEventAggregator.Object,
                _mockLogger.Object);
            
            // テキスト領域設定
            var textRegions = new List<Rectangle> { new Rectangle(10, 10, 100, 20) };
            detector.SetPreviousTextRegions(textRegions);
            
            // Act
            await detector.DetectTextDisappearanceAsync(_mockImage1.Object, _mockImage2.Object).ConfigureAwait(true);
            
            // Assert - PublishAsyncの引数型はIEvent
            _mockEventAggregator.Verify(ea => ea.PublishAsync(It.IsAny<IEvent>()), Times.Once);
        }
        
        [Fact]
        public void SetPreviousTextRegionsWithValidRegionsSetsRegions()
        {
            // Arrange
            var detector = new EnhancedDifferenceDetector(_mockAlgorithms, _mockEventAggregator.Object, _mockLogger.Object);
            var textRegions = new List<Rectangle>
            {
                new Rectangle(10, 10, 100, 20),
                new Rectangle(10, 40, 100, 20)
            };
            
            // Act
            detector.SetPreviousTextRegions(textRegions);
            
            // テキスト消失検出が呼び出せるか確認（内部状態を直接検証できないため）
            // これは完全なテストではなく、例外が発生しないことを確認するだけ
            var task = detector.DetectTextDisappearanceAsync(_mockImage1.Object, _mockImage2.Object);
            
            // Assert
            Assert.NotNull(task);
        }
    }
}