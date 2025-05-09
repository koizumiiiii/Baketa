using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Pipeline;
using Baketa.Core.Services.Imaging.Pipeline;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Baketa.Core.Tests.Imaging.Pipeline
{
#pragma warning disable CA1849 // 非同期メソッド内での同期メソッドの使用（テストコードのため抑制）
    public class ImagePipelineTests
    {
        private readonly Mock<ILogger<ImagePipeline>> _loggerMock;
        private readonly Mock<IAdvancedImage> _imageMock;

        public ImagePipelineTests()
        {
            _loggerMock = new Mock<ILogger<ImagePipeline>>();
            _imageMock = new Mock<IAdvancedImage>();
            _imageMock.Setup(i => i.Width).Returns(800);
            _imageMock.Setup(i => i.Height).Returns(600);
        }

        [Fact]
        public void AddStep_ShouldIncreaseStepCount()
        {
            // Arrange
            var pipeline = new ImagePipeline(_loggerMock.Object);
            var step = new Mock<IImagePipelineStep>().Object;

            // Act
            pipeline.AddStep(step);

            // Assert
            Assert.Equal(1, pipeline.StepCount);
            Assert.Contains(step, pipeline.Steps);
        }

        [Fact]
        public void RemoveStep_ByIndex_ShouldDecreaseStepCount()
        {
            // Arrange
            var pipeline = new ImagePipeline(_loggerMock.Object);
            var step = new Mock<IImagePipelineStep>().Object;
            pipeline.AddStep(step);

            // Act
            var result = pipeline.RemoveStep(0);

            // Assert
            Assert.True(result);
            Assert.Equal(0, pipeline.StepCount);
        }

        [Fact]
        public void RemoveStep_ByReference_ShouldDecreaseStepCount()
        {
            // Arrange
            var pipeline = new ImagePipeline(_loggerMock.Object);
            var step = new Mock<IImagePipelineStep>().Object;
            pipeline.AddStep(step);

            // Act
            var result = pipeline.RemoveStep(step);

            // Assert
            Assert.True(result);
            Assert.Equal(0, pipeline.StepCount);
        }

        [Fact]
        public void ClearSteps_ShouldRemoveAllSteps()
        {
            // Arrange
            var pipeline = new ImagePipeline(_loggerMock.Object);
            pipeline.AddStep(new Mock<IImagePipelineStep>().Object);
            pipeline.AddStep(new Mock<IImagePipelineStep>().Object);

            // Act
            pipeline.ClearSteps();

            // Assert
            Assert.Equal(0, pipeline.StepCount);
        }

        [Fact]
        public void GetStep_WithValidIndex_ShouldReturnStep()
        {
            // Arrange
            var pipeline = new ImagePipeline(_loggerMock.Object);
            var step = new Mock<IImagePipelineStep>().Object;
            pipeline.AddStep(step);

            // Act
            var result = pipeline.GetStep(0);

            // Assert
            Assert.Same(step, result);
        }

        [Fact]
        public void GetStep_WithInvalidIndex_ShouldThrowException()
        {
            // Arrange
            var pipeline = new ImagePipeline(_loggerMock.Object);

            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => pipeline.GetStep(0));
        }

        [Fact]
        public void GetStepByName_WithExistingName_ShouldReturnStep()
        {
            // Arrange
            var pipeline = new ImagePipeline(_loggerMock.Object);
            var stepMock = new Mock<IImagePipelineStep>();
            stepMock.Setup(s => s.Name).Returns("TestStep");
            pipeline.AddStep(stepMock.Object);

            // Act
            var result = pipeline.GetStepByName("TestStep");

            // Assert
            Assert.Same(stepMock.Object, result);
        }

        [Fact]
        public void GetStepByName_WithNonExistingName_ShouldReturnNull()
        {
            // Arrange
            var pipeline = new ImagePipeline(_loggerMock.Object);

            // Act
            var result = pipeline.GetStepByName("NonExistingStep");

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task ExecuteAsync_WithNoSteps_ShouldReturnOriginalImage()
        {
            // Arrange
            var pipeline = new ImagePipeline(_loggerMock.Object);

            // Act
            var result = await pipeline.ExecuteAsync(_imageMock.Object);

            // Assert
            Assert.Same(_imageMock.Object, result.Result);
            Assert.Empty(result.IntermediateResults);
        }

        [Fact]
        public async Task ExecuteAsync_WithOneStep_ShouldApplyStep()
        {
            // Arrange
            var pipeline = new ImagePipeline(_loggerMock.Object);
            var transformedImage = new Mock<IAdvancedImage>().Object;
            var stepMock = new Mock<IImagePipelineStep>();
            
            stepMock.Setup(s => s.Name).Returns("TestStep");
            stepMock.Setup(s => s.ExecuteAsync(
                    It.IsAny<IAdvancedImage>(), 
                    It.IsAny<PipelineContext>(), 
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(transformedImage);
                
            pipeline.AddStep(stepMock.Object);
            pipeline.IntermediateResultMode = IntermediateResultMode.All;

            // Act
            var result = await pipeline.ExecuteAsync(_imageMock.Object);

            // Assert
            Assert.Same(transformedImage, result.Result);
            Assert.Single(result.IntermediateResults);
            Assert.Contains("TestStep", result.IntermediateResults.Keys);
        }

        [Fact]
        public async Task ExecuteAsync_WithCancellation_ShouldRespectCancellation()
        {
            // Arrange
            var pipeline = new ImagePipeline(_loggerMock.Object);
            var stepMock = new Mock<IImagePipelineStep>();
            
            stepMock.Setup(s => s.ExecuteAsync(
                    It.IsAny<IAdvancedImage>(), 
                    It.IsAny<PipelineContext>(), 
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException());
                
            pipeline.AddStep(stepMock.Object);

            // キャンセル済みのトークンを作成
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => pipeline.ExecuteAsync(_imageMock.Object, cts.Token));
        }

        [Fact]
        public async Task ExecuteAsync_WithStepError_ShouldRespectErrorHandlingStrategy()
        {
            // Arrange
            var pipeline = new ImagePipeline(_loggerMock.Object);
            var stepMock = new Mock<IImagePipelineStep>();
            
            stepMock.Setup(s => s.Name).Returns("ErrorStep");
            stepMock.Setup(s => s.ErrorHandlingStrategy).Returns(StepErrorHandlingStrategy.SkipStep);
            stepMock.Setup(s => s.ExecuteAsync(
                    It.IsAny<IAdvancedImage>(), 
                    It.IsAny<PipelineContext>(), 
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("テストエラー"));
                
            pipeline.AddStep(stepMock.Object);

            // Act
            var result = await pipeline.ExecuteAsync(_imageMock.Object);

            // Assert
            Assert.Same(_imageMock.Object, result.Result);
            
            // ログ出力の検証 - ステップをスキップしたことがログに記録される
            _loggerMock.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((o, t) => o != null && o.ToString()!.Contains("スキップ")), 
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
        }
    }
#pragma warning restore CA1849
}
