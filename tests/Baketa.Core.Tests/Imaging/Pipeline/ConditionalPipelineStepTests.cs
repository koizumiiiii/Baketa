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

namespace Baketa.Core.Tests.Imaging.Pipeline;

#pragma warning disable CA1849 // 非同期メソッド内での同期メソッドの使用（テストコードのため抑制）
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
            _conditionMock.Object,
            _thenStepMock.Object,
            _elseStepMock.Object);

        // Assert
        Assert.Equal("条件ステップ", step.Name);
        // Description はコンストラクターで自動生成される
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
            _conditionMock.Object,
            _thenStepMock.Object,
            _elseStepMock.Object);

        var transformedImage = new Mock<IAdvancedImage>().Object;
        _conditionMock.Setup(c => c.EvaluateAsync(_imageMock.Object, It.IsAny<PipelineContext>()))
            .ReturnsAsync(true);

        _thenStepMock.Setup(s => s.ExecuteAsync(
                _imageMock.Object,
                It.IsAny<PipelineContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(transformedImage);

        var context = new PipelineContext(
            _loggerMock.Object,
            IntermediateResultMode.All,
            StepErrorHandlingStrategy.StopExecution,
            null,
            CancellationToken.None);

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
            _conditionMock.Object,
            _thenStepMock.Object,
            _elseStepMock.Object);

        var transformedImage = new Mock<IAdvancedImage>().Object;
        _conditionMock.Setup(c => c.EvaluateAsync(_imageMock.Object, It.IsAny<PipelineContext>()))
            .ReturnsAsync(false);

        _elseStepMock.Setup(s => s.ExecuteAsync(
                _imageMock.Object,
                It.IsAny<PipelineContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(transformedImage);

        var context = new PipelineContext(
            _loggerMock.Object,
            IntermediateResultMode.All,
            StepErrorHandlingStrategy.StopExecution,
            null,
            CancellationToken.None);

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
            _conditionMock.Object,
            _thenStepMock.Object,
            null); // ElseStepなし

        _conditionMock.Setup(c => c.EvaluateAsync(_imageMock.Object, It.IsAny<PipelineContext>()))
            .ReturnsAsync(false);

        var context = new PipelineContext(
            _loggerMock.Object,
            IntermediateResultMode.All,
            StepErrorHandlingStrategy.StopExecution,
            null,
            CancellationToken.None);

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
            _conditionMock.Object,
            _thenStepMock.Object,
            _elseStepMock.Object);

        _conditionMock.Setup(c => c.EvaluateAsync(_imageMock.Object, It.IsAny<PipelineContext>()))
            .ThrowsAsync(new OperationCanceledException());

        // キャンセル済みのトークンを作成
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var context = new PipelineContext(
            _loggerMock.Object,
            IntermediateResultMode.All,
            StepErrorHandlingStrategy.StopExecution,
            null,
            cts.Token);

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => step.ExecuteAsync(_imageMock.Object, context, cts.Token));
    }

    [Fact]
    public void GetParameter_ShouldThrowNotSupportedException()
    {
        // Arrange
        var step = new ConditionalPipelineStep(
            "条件ステップ",
            _conditionMock.Object,
            _thenStepMock.Object,
            _elseStepMock.Object);

        // Act & Assert
        Assert.Throws<NotSupportedException>(() => step.GetParameter("anyParam"));
    }

    [Fact]
    public void SetParameter_ShouldThrowNotSupportedException()
    {
        // Arrange
        var step = new ConditionalPipelineStep(
            "条件ステップ",
            _conditionMock.Object,
            _thenStepMock.Object,
            _elseStepMock.Object);

        // Act & Assert
        Assert.Throws<NotSupportedException>(() => step.SetParameter("anyParam", "value"));
    }

    [Fact]
    public async Task AndCondition_ShouldCombineConditions()
    {
        // Arrange
        var condition1 = new Mock<IPipelineCondition>();
        var condition2 = new Mock<IPipelineCondition>();

        condition1.Setup(c => c.EvaluateAsync(_imageMock.Object, It.IsAny<PipelineContext>()))
            .ReturnsAsync(true);

        condition2.Setup(c => c.EvaluateAsync(_imageMock.Object, It.IsAny<PipelineContext>()))
            .ReturnsAsync(true);

        var andCondition = new AndCondition(condition1.Object, condition2.Object);

        // Act
        var mockContext = new PipelineContext(_loggerMock.Object);
        var result = await andCondition.EvaluateAsync(_imageMock.Object, mockContext);

        // Assert
        Assert.True(result);

        // 2つ目のテスト - 片方がfalseの場合
        condition2.Setup(c => c.EvaluateAsync(_imageMock.Object, It.IsAny<PipelineContext>()))
            .ReturnsAsync(false);

        // Act
        result = await andCondition.EvaluateAsync(_imageMock.Object, mockContext);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task OrCondition_ShouldCombineConditions()
    {
        // Arrange
        var condition1 = new Mock<IPipelineCondition>();
        var condition2 = new Mock<IPipelineCondition>();

        condition1.Setup(c => c.EvaluateAsync(_imageMock.Object, It.IsAny<PipelineContext>()))
            .ReturnsAsync(true);

        condition2.Setup(c => c.EvaluateAsync(_imageMock.Object, It.IsAny<PipelineContext>()))
            .ReturnsAsync(false);

        var orCondition = new OrCondition(condition1.Object, condition2.Object);

        // Act
        var mockContext = new PipelineContext(_loggerMock.Object);
        var result = await orCondition.EvaluateAsync(_imageMock.Object, mockContext);

        // Assert
        Assert.True(result);

        // 2つ目のテスト - 両方ともfalseの場合
        condition1.Setup(c => c.EvaluateAsync(_imageMock.Object, It.IsAny<PipelineContext>()))
            .ReturnsAsync(false);

        // Act
        result = await orCondition.EvaluateAsync(_imageMock.Object, mockContext);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task NotCondition_ShouldInvertCondition()
    {
        // Arrange
        var condition = new Mock<IPipelineCondition>();
        condition.Setup(c => c.EvaluateAsync(_imageMock.Object, It.IsAny<PipelineContext>()))
            .ReturnsAsync(true);

        var notCondition = new NotCondition(condition.Object);

        // Act
        var mockContext = new PipelineContext(_loggerMock.Object);
        var result = await notCondition.EvaluateAsync(_imageMock.Object, mockContext);

        // Assert
        Assert.False(result);

        // 2つ目のテスト - falseの場合
        condition.Setup(c => c.EvaluateAsync(_imageMock.Object, It.IsAny<PipelineContext>()))
            .ReturnsAsync(false);

        // Act
        result = await notCondition.EvaluateAsync(_imageMock.Object, mockContext);

        // Assert
        Assert.True(result);
    }
}
#pragma warning restore CA1849

