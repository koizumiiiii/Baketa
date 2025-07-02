using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Filters;
using Baketa.Core.Abstractions.Imaging.Pipeline;
using Baketa.Core.Services.Imaging.Pipeline;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Baketa.Core.Tests.Imaging.Pipeline;

#pragma warning disable CA1849 // 非同期メソッド内での同期メソッドの使用（テストコードのため抑制）
    public class FilterPipelineStepAdapterTests
    {
        private readonly Mock<ILogger<FilterPipelineStepAdapter>> _loggerMock;
        private readonly Mock<IImageFilter> _filterMock;
        private readonly Mock<IAdvancedImage> _imageMock;
        private readonly Dictionary<string, object> _filterParams;

        public FilterPipelineStepAdapterTests()
        {
            _loggerMock = new Mock<ILogger<FilterPipelineStepAdapter>>();
            
            _filterMock = new Mock<IImageFilter>();
            _filterMock.Setup(f => f.Name).Returns("テストフィルター");
            _filterMock.Setup(f => f.Description).Returns("テスト用フィルターの説明");
            _filterMock.Setup(f => f.Category).Returns(FilterCategory.ColorAdjustment);
            
            _filterParams = new Dictionary<string, object>
            {
                { "Param1", 100 },
                { "Param2", "値" }
            };
            
            _filterMock.Setup(f => f.GetParameters()).Returns(_filterParams);
            
            _imageMock = new Mock<IAdvancedImage>();
            _imageMock.Setup(i => i.Width).Returns(800);
            _imageMock.Setup(i => i.Height).Returns(600);
        }

        [Fact]
        public void Constructor_ShouldInitializePropertiesCorrectly()
        {
            // Arrange & Act
            var adapter = new FilterPipelineStepAdapter(
                _filterMock.Object, _loggerMock.Object);

            // Assert
            Assert.Equal("テストフィルター", adapter.Name);
            Assert.Equal("テスト用フィルターの説明", adapter.Description);
            Assert.Equal(StepErrorHandlingStrategy.StopExecution, adapter.ErrorHandlingStrategy);
            
            // パラメータの検証
            Assert.Equal(2, adapter.Parameters.Count);
            
            // パラメータの値が正しく設定されているか確認
            Assert.Equal(100, adapter.GetParameter("Param1"));
            Assert.Equal("値", adapter.GetParameter("Param2"));
        }

        [Fact]
        public void SetParameter_ShouldUpdateFilterParameter()
        {
            // Arrange
            var adapter = new FilterPipelineStepAdapter(
                _filterMock.Object, _loggerMock.Object);

            // Act
            adapter.SetParameter("Param1", 200);

            // Assert
            _filterMock.Verify(f => f.SetParameter("Param1", 200), Times.Once);
        }

        [Fact]
        public void GetParameter_WithGenericType_ShouldReturnTypedValue()
        {
            // Arrange
            var adapter = new FilterPipelineStepAdapter(
                _filterMock.Object, _loggerMock.Object);

            // Act
            int value = adapter.GetParameter<int>("Param1");

            // Assert
            Assert.Equal(100, value);
        }

        [Fact]
        public async Task ExecuteAsync_ShouldApplyFilterToImage()
        {
            // Arrange
            var adapter = new FilterPipelineStepAdapter(
                _filterMock.Object, _loggerMock.Object);
            
            var transformedImage = new Mock<IAdvancedImage>().Object;
            _filterMock.Setup(f => f.ApplyAsync(_imageMock.Object))
                .ReturnsAsync(transformedImage);
                
            var context = new PipelineContext(
                _loggerMock.Object,
                IntermediateResultMode.All,
                StepErrorHandlingStrategy.StopExecution,
                null,
                CancellationToken.None);

            // Act
            var result = await adapter.ExecuteAsync(
                _imageMock.Object, context, CancellationToken.None);

            // Assert
            Assert.Same(transformedImage, result);
            _filterMock.Verify(f => f.ApplyAsync(_imageMock.Object), Times.Once);
        }

        [Fact]
        public async Task ExecuteAsync_WhenFilterThrowsException_ShouldPropagateException()
        {
            // Arrange
            var adapter = new FilterPipelineStepAdapter(
                _filterMock.Object, _loggerMock.Object);
            
            _filterMock.Setup(f => f.ApplyAsync(_imageMock.Object))
                .ThrowsAsync(new InvalidOperationException("フィルターエラー"));
                
            var context = new PipelineContext(
                _loggerMock.Object,
                IntermediateResultMode.All,
                StepErrorHandlingStrategy.StopExecution,
                null,
                CancellationToken.None);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await adapter.ExecuteAsync(
                    _imageMock.Object, context, CancellationToken.None));
                    
            Assert.Equal("フィルターエラー", exception.Message);
        }

        [Fact]
        public async Task ExecuteAsync_WithCancellation_ShouldRespectCancellation()
        {
            // Arrange
            var adapter = new FilterPipelineStepAdapter(
                _filterMock.Object, _loggerMock.Object);
            
            var context = new PipelineContext(
                _loggerMock.Object,
                IntermediateResultMode.All,
                StepErrorHandlingStrategy.StopExecution,
                null,
                CancellationToken.None);
            
            // キャンセル済みのトークンを作成
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => adapter.ExecuteAsync(
                    _imageMock.Object, context, cts.Token));
        }

        [Fact]
        public void GetOutputImageInfo_ShouldDelegateToFilter()
        {
            // Arrange
            var adapter = new FilterPipelineStepAdapter(
                _filterMock.Object, _loggerMock.Object);
            
            var imageInfo = new ImageInfo
            {
                Width = 400,
                Height = 300,
                Format = ImageFormat.Grayscale8,
                Channels = 1
            };
            
            _filterMock.Setup(f => f.GetOutputImageInfo(_imageMock.Object))
                .Returns(imageInfo);

            // Act
            var result = adapter.GetOutputImageInfo(_imageMock.Object);

            // Assert
            // PipelineImageInfoと対応する値を確認
            Assert.Equal(imageInfo.Width, result.Width);
            Assert.Equal(imageInfo.Height, result.Height);
            Assert.Equal(imageInfo.Format, result.Format);
            Assert.Equal(imageInfo.Channels, result.Channels);
            // Assert.Same(imageInfo, result); - 型が異なるので直接比較ではなく値比較を行う
        }
    }
#pragma warning restore CA1849
