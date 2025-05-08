using System;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Pipeline;
using Baketa.Core.Services.Imaging.Pipeline;
using Baketa.Core.Services.Imaging.Pipeline.Conditions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Baketa.Core.Tests.Imaging.Pipeline
{
    public class ConditionalPipelineStepTests
    {
        private readonly Mock<ILogger<ConditionalPipelineStep>> _loggerMock;
        private readonly Mock<IImagePipelineStep> _thenStepMock;
        private readonly Mock<IImagePipelineStep> _elseStepMock;
        private readonly Mock<IPipelineCondition> _conditionMock;
        private readonly Mock<IAdvancedImage> _imageMock;

        public ConditionalPipelineStepTests()
        {
            _loggerMock = new Mock<ILogger<ConditionalPipelineStep>>();
            
            _thenStepMock = new Mock<IImagePipelineStep>();
            _thenStepMock.Setup(s => s.Name).Returns("ThenStep");
            
            _elseStepMock = new Mock<IImagePipelineStep>();
            _elseStepMock.Setup(s => s.Name).Returns("ElseStep");
            
            _conditionMock = new Mock<IPipelineCondition>();
            
            _imageMock = new Mock<IAdvancedImage>();
            _imageMock.Setup(i => i.Width).Returns(800);
            _imageMock.Setup(i => i.Height).Returns(600);
        }

        [Fact]
        public void Constructor_ShouldInitializePropertiesCorrectly()
        {
            // Arrange & Act
            var step = new ConditionalPipelineStep(
                "条件ステップ",
                "条件に基づく分岐ステップ",
                _conditionMock.Object,
                _thenStepMock.Object,
                _elseStepMock.Object,
                _loggerMock.Object);

            // Assert
            Assert.Equal("条件ステップ", step.Name);
            Assert.Equal("条件に基づく分岐ステップ", step.Description);
            Assert.Equal(StepErrorHandlingStrategy.StopExecution, step.ErrorHandlingStrategy);
            Assert.NotNull(step.Parameters);
            Assert.Empty(step.Parameters); // 条件ステップには独自のパラメータはない
        }

        [Fact]
        public async Task ExecuteAsync_WhenConditionIsTrue_ShouldExecuteThenStep()
        {
            // Arrange
            var step = new ConditionalPipelineStep(
                "条件ステップ",
                "条件に基づく分岐ステップ",
                _conditionMock.Object,
                _thenStepMock.Object,
                _elseStepMock.Object,
                _loggerMock.Object);
                
            var transformedImage = new Mock<IAdvancedImage>().Object;
            _conditionMock.Setup(c => c.EvaluateAsync(_imageMock.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
                
            _thenStepMock.Setup(s => s.ExecuteAsync(
                    _imageMock.Object, 
                    It.IsAny<PipelineContext>(), 
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(transformedImage);
                
            var context = new PipelineContext
            {
                IntermediateResultMode = IntermediateResultMode.All
            };

            // Act
            var result = await step.ExecuteAsync(_imageMock.Object, context, CancellationToken.None);

            // Assert
            Assert.Same(transformedImage, result);
            _thenStepMock.Verify(s => s.ExecuteAsync(
                _imageMock.Object, 
                It.IsAny<PipelineContext>(), 
                It.IsAny<CancellationToken>()), 
                Times.Once);
                
            _elseStepMock.Verify(s => s.ExecuteAsync(
                It.IsAny<IAdvancedImage>(), 
                It.IsAny<PipelineContext>(), 
                It.IsAny<CancellationToken>()), 
                Times.Never);
        }

        [Fact]
        public async Task ExecuteAsync_WhenConditionIsFalse_ShouldExecuteElseStep()
        {
            // Arrange
            var step = new ConditionalPipelineStep(
                "条件ステップ",
                "条件に基づく分岐ステップ",
                _conditionMock.Object,
                _thenStepMock.Object,
                _elseStepMock.Object,
                _loggerMock.Object);
                
            var transformedImage = new Mock<IAdvancedImage>().Object;
            _conditionMock.Setup(c => c.EvaluateAsync(_imageMock.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
                
            _elseStepMock.Setup(s => s.ExecuteAsync(
                    _imageMock.Object, 
                    It.IsAny<PipelineContext>(), 
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(transformedImage);
                
            var context = new PipelineContext
            {
                IntermediateResultMode = IntermediateResultMode.All
            };

            // Act
            var result = await step.ExecuteAsync(_imageMock.Object, context, CancellationToken.None);

            // Assert
            Assert.Same(transformedImage, result);
            _elseStepMock.Verify(s => s.ExecuteAsync(
                _imageMock.Object, 
                It.IsAny<PipelineContext>(), 
                It.IsAny<CancellationToken>()), 
                Times.Once);
                
            _thenStepMock.Verify(s => s.ExecuteAsync(
                It.IsAny<IAdvancedImage>(), 
                It.IsAny<PipelineContext>(), 
                It.IsAny<CancellationToken>()), 
                Times.Never);
        }

        [Fact]
        public async Task ExecuteAsync_WhenNoElseStepProvided_AndConditionIsFalse_ShouldReturnOriginalImage()
        {
            // Arrange
            var step = new ConditionalPipelineStep(
                "条件ステップ",
                "条件に基づく分岐ステップ",
                _conditionMock.Object,
                _thenStepMock.Object,
                null, // ElseStepなし
                _loggerMock.Object);
                
            _conditionMock.Setup(c => c.EvaluateAsync(_imageMock.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
                
            var context = new PipelineContext
            {
                IntermediateResultMode = IntermediateResultMode.All
            };

            // Act
            var result = await step.ExecuteAsync(_imageMock.Object, context, CancellationToken.None);

            // Assert
            Assert.Same(_imageMock.Object, result);
            _thenStepMock.Verify(s => s.ExecuteAsync(
                It.IsAny<IAdvancedImage>(), 
                It.IsAny<PipelineContext>(), 
                It.IsAny<CancellationToken>()), 
                Times.Never);
        }

        [Fact]
        public async Task ExecuteAsync_WithCancellation_ShouldRespectCancellation()
        {
            // Arrange
            var step = new ConditionalPipelineStep(
                "条件ステップ",
                "条件に基づく分岐ステップ",
                _conditionMock.Object,
                _thenStepMock.Object,
                _elseStepMock.Object,
                _loggerMock.Object);
                
            _conditionMock.Setup(c => c.EvaluateAsync(_imageMock.Object, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException());
                
            var context = new PipelineContext
            {
                IntermediateResultMode = IntermediateResultMode.All
            };
            
            // キャンセル済みのトークンを作成
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await step.ExecuteAsync(
                    _imageMock.Object, context, cts.Token));
        }

        [Fact]
        public void GetParameter_ShouldThrowNotSupportedException()
        {
            // Arrange
            var step = new ConditionalPipelineStep(
                "条件ステップ",
                "条件に基づく分岐ステップ",
                _conditionMock.Object,
                _thenStepMock.Object,
                _elseStepMock.Object,
                _loggerMock.Object);

            // Act & Assert
            Assert.Throws<NotSupportedException>(() => step.GetParameter("anyParam"));
        }

        [Fact]
        public void SetParameter_ShouldThrowNotSupportedException()
        {
            // Arrange
            var step = new ConditionalPipelineStep(
                "条件ステップ",
                "条件に基づく分岐ステップ",
                _conditionMock.Object,
                _thenStepMock.Object,
                _elseStepMock.Object,
                _loggerMock.Object);

            // Act & Assert
            Assert.Throws<NotSupportedException>(() => step.SetParameter("anyParam", "value"));
        }

        [Fact]
        public void AndCondition_ShouldCombineConditions()
        {
            // Arrange
            var condition1 = new Mock<IPipelineCondition>();
            var condition2 = new Mock<IPipelineCondition>();
            
            condition1.Setup(c => c.EvaluateAsync(_imageMock.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
                
            condition2.Setup(c => c.EvaluateAsync(_imageMock.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
                
            var andCondition = new AndCondition(condition1.Object, condition2.Object);

            // Act
            var result = andCondition.EvaluateAsync(_imageMock.Object, CancellationToken.None).Result;

            // Assert
            Assert.True(result);
            
            // 2つ目のテスト - 片方がfalseの場合
            condition2.Setup(c => c.EvaluateAsync(_imageMock.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
                
            // Act
            result = andCondition.EvaluateAsync(_imageMock.Object, CancellationToken.None).Result;
            
            // Assert
            Assert.False(result);
        }

        [Fact]
        public void OrCondition_ShouldCombineConditions()
        {
            // Arrange
            var condition1 = new Mock<IPipelineCondition>();
            var condition2 = new Mock<IPipelineCondition>();
            
            condition1.Setup(c => c.EvaluateAsync(_imageMock.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
                
            condition2.Setup(c => c.EvaluateAsync(_imageMock.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
                
            var orCondition = new OrCondition(condition1.Object, condition2.Object);

            // Act
            var result = orCondition.EvaluateAsync(_imageMock.Object, CancellationToken.None).Result;

            // Assert
            Assert.True(result);
            
            // 2つ目のテスト - 両方ともfalseの場合
            condition1.Setup(c => c.EvaluateAsync(_imageMock.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
                
            // Act
            result = orCondition.EvaluateAsync(_imageMock.Object, CancellationToken.None).Result;
            
            // Assert
            Assert.False(result);
        }

        [Fact]
        public void NotCondition_ShouldInvertCondition()
        {
            // Arrange
            var condition = new Mock<IPipelineCondition>();
            condition.Setup(c => c.EvaluateAsync(_imageMock.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
                
            var notCondition = new NotCondition(condition.Object);

            // Act
            var result = notCondition.EvaluateAsync(_imageMock.Object, CancellationToken.None).Result;

            // Assert
            Assert.False(result);
            
            // 2つ目のテスト - falseの場合
            condition.Setup(c => c.EvaluateAsync(_imageMock.Object, It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
                
            // Act
            result = notCondition.EvaluateAsync(_imageMock.Object, CancellationToken.None).Result;
            
            // Assert
            Assert.True(result);
        }
    }
}
