using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Pipeline;
using Baketa.Core.Abstractions.Imaging.Pipeline.Settings;
using Baketa.Infrastructure.Imaging.Extensions;
using Baketa.Infrastructure.Imaging.Pipeline;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.Imaging.Pipeline;

/// <summary>
/// テスト用のパイプラインフィルターインターフェース実装
/// </summary>
public class TestIPipelineImageFilter(string name, string description, string category) : IImagePipelineFilter
{
    public string Name { get; set; } = name ?? throw new ArgumentNullException(nameof(name));
    public string Description { get; set; } = description ?? throw new ArgumentNullException(nameof(description));
    public string Category { get; set; } = category ?? throw new ArgumentNullException(nameof(category));
    public StepErrorHandlingStrategy ErrorHandlingStrategy { get; set; } = StepErrorHandlingStrategy.StopExecution;

    private readonly Dictionary<string, object> _parameters = [];

    public IReadOnlyCollection<PipelineStepParameter> Parameters =>
        [
            new PipelineStepParameter("param1", "Parameter 1", typeof(string), "default")
        ];

    public Task<IAdvancedImage> ExecuteAsync(IAdvancedImage input, PipelineContext context, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(input);
    }

    // オーバーライド可能なメソッドに変更
    public virtual Task<IAdvancedImage> ApplyAsync(IAdvancedImage inputImage)
    {
        return Task.FromResult(inputImage);
    }

    // GetParameters メソッドの実装
    public IDictionary<string, object> GetParameters()
    {
        return new Dictionary<string, object>(_parameters);
    }

    public void SetParameter(string parameterName, object value)
    {
        _parameters[parameterName] = value;
    }

    public object GetParameter(string parameterName)
    {
        return _parameters.TryGetValue(parameterName, out var value) ? value : null!;
    }

    public T GetParameter<T>(string parameterName)
    {
        var value = GetParameter(parameterName);
        if (value is T typedValue)
            return typedValue;

        return default!;
    }

    public PipelineImageInfo GetOutputImageInfo(IAdvancedImage input)
    {
        ArgumentNullException.ThrowIfNull(input, nameof(input));

        return new PipelineImageInfo(
            input.Width,
            input.Height,
            input.ChannelCount,
            input.Format,
            PipelineStage.Output
        );
    }
}

/// <summary>
/// テスト用のモックフィルター
/// </summary>
public class TestIPipelineImageFilterMock(IAdvancedImage inputImage, IAdvancedImage outputImage) : TestIPipelineImageFilter("MockFilter", "Mock Filter", "Test")
{
    private readonly IAdvancedImage _inputImage = inputImage;

    public override Task<IAdvancedImage> ApplyAsync(IAdvancedImage inputImage)
    {
        // 入力画像が期待通りなら出力画像を返す
        if (inputImage == _inputImage)
        {
            return Task.FromResult(outputImage);
        }
        return Task.FromResult(inputImage);
    }
}

public class ImagePipelineTests
{
    private readonly Mock<ILogger<ImagePipeline>> _loggerMock;
    private readonly TestIPipelineImageFilter _filter1;
    private readonly TestIPipelineImageFilter _filter2;
    private readonly Mock<IAdvancedImage> _imageMock;

    public ImagePipelineTests()
    {
        _loggerMock = new Mock<ILogger<ImagePipeline>>();

        _filter1 = new TestIPipelineImageFilter("TestFilter1", "テスト用フィルター1", "Composite");
        _filter2 = new TestIPipelineImageFilter("TestFilter2", "テスト用フィルター2", "Composite");

        _imageMock = new Mock<IAdvancedImage>();
        // ImageInfoの代わりにプロパティ純正を設定
        _imageMock.Setup(i => i.Width).Returns(100);
        _imageMock.Setup(i => i.Height).Returns(100);
        _imageMock.Setup(i => i.Format).Returns(ImageFormat.Rgb24);
        _imageMock.Setup(i => i.IsGrayscale).Returns(false);
        _imageMock.Setup(i => i.BitsPerPixel).Returns(24);
        _imageMock.Setup(i => i.ChannelCount).Returns(3);
        _imageMock.Setup(i => i.ToGrayscale()).Returns(_imageMock.Object);
        _imageMock.Setup(i => i.Clone()).Returns(_imageMock.Object);
    }

    [Fact]
    public void ConstructorSetsNameAndDescription()
    {
        // Arrange & Act
        var pipeline = new ImagePipeline("TestPipeline", "テスト用パイプライン");

        // Assert
        Assert.Equal("TestPipeline", pipeline.Name);
        Assert.Equal("テスト用パイプライン", pipeline.Description);
    }

    [Fact]
    public void AddStepAddsFilterToPipeline()
    {
        // Arrange
        var pipeline = new ImagePipeline("TestPipeline", "テスト用パイプライン");

        // Act
        pipeline.AddStep(_filter1);

        // Assert
        Assert.Equal(1, pipeline.StepCount);
        var step = pipeline.GetStep(0);
        Assert.NotNull(step);
        Assert.Equal(_filter1.Name, step.Name);
        Assert.Equal(_filter1.Description, step.Description);
        Assert.Equal(_filter1.Category, ((IImagePipelineFilter)step).Category);
    }

    [Fact]
    public void RemoveStepByIndexRemovesFilterFromPipeline()
    {
        // Arrange
        var pipeline = new ImagePipeline("TestPipeline", "テスト用パイプライン");
        pipeline.AddStep(_filter1);
        pipeline.AddStep(_filter2);

        // Act
        var result = pipeline.RemoveStep(0);

        // Assert
        Assert.True(result);
        Assert.Equal(1, pipeline.StepCount);
        var step = pipeline.GetStep(0);
        Assert.NotNull(step);
        Assert.Equal(_filter2.Name, step.Name);
        Assert.Equal(_filter2.Description, step.Description);
        Assert.Equal(_filter2.Category, ((IImagePipelineFilter)step).Category);
    }

    [Fact]
    public void RemoveStepByReferenceRemovesFilterFromPipeline()
    {
        // Arrange
        var pipeline = new ImagePipeline("TestPipeline", "テスト用パイプライン");
        pipeline.AddStep(_filter1);
        pipeline.AddStep(_filter2);

        // ここで重要なのは型の一致ではなく、関数としての動作を測定すること

        // Act
        // 普通のRemoveStepメソッドを使用してテスト
        // 名前を取得して、名前でフィルターを見つけて削除する
        var stepToRemove = pipeline.GetStepByName(_filter1.Name);
        Assert.NotNull(stepToRemove); // 先に確認

        // ステップを見つけたら別のインターフェースで削除
        var result = pipeline.RemoveStep(stepToRemove);

        // Assert
        Assert.True(result);
        Assert.Equal(1, pipeline.StepCount);
        var step = pipeline.GetStep(0);
        Assert.NotNull(step);
        Assert.Equal(_filter2.Name, step.Name);
        Assert.Equal(_filter2.Description, step.Description);
        Assert.Equal(_filter2.Category, ((IImagePipelineFilter)step).Category);
    }

    [Fact]
    public void ClearStepsRemovesAllFiltersFromPipeline()
    {
        // Arrange
        var pipeline = new ImagePipeline("TestPipeline", "テスト用パイプライン");
        pipeline.AddStep(_filter1);
        pipeline.AddStep(_filter2);

        // Act
        pipeline.ClearSteps();

        // Assert
        Assert.Equal(0, pipeline.StepCount);
    }

    [Fact]
    public void GetStepByNameReturnsCorrectFilter()
    {
        // Arrange
        var pipeline = new ImagePipeline("TestPipeline", "テスト用パイプライン");
        pipeline.AddStep(_filter1);
        pipeline.AddStep(_filter2);

        // Act
        var result = pipeline.GetStepByName("TestFilter2");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(_filter2.Name, result.Name);
        Assert.Equal(_filter2.Description, result.Description);
        Assert.Equal(_filter2.Category, ((IImagePipelineFilter)result).Category);
    }

    [Fact]
    public async Task ExecuteAsyncAppliesAllFilters()
    {
        // Arrange
        var pipeline = new ImagePipeline("TestPipeline", "テスト用パイプライン", _loggerMock.Object);

        // 中間・最終処理結果用の画像モック
        var processedImage1 = new Mock<IAdvancedImage>();
        var processedImage2 = new Mock<IAdvancedImage>();

        // 画像プロパティの設定
        SetupImageMock(processedImage1);
        SetupImageMock(processedImage2);

        // カスタムフィルタークラスを作成
        var filter1 = new TestCustomFilter("Filter1", inputImage => processedImage1.Object);
        var filter2 = new TestCustomFilter("Filter2", inputImage => processedImage2.Object);

        pipeline.AddStep(filter1);
        pipeline.AddStep(filter2);

        // Act
        var result = await pipeline.ExecuteAsync(_imageMock.Object).ConfigureAwait(true);

        // Assert
        Assert.NotNull(result.Result);
        Assert.Same(processedImage2.Object, result.Result);
    }

    // 補助メソッド：画像モックのセットアップ
    private void SetupImageMock(Mock<IAdvancedImage> imageMock)
    {
        imageMock.Setup(i => i.Width).Returns(100);
        imageMock.Setup(i => i.Height).Returns(100);
        imageMock.Setup(i => i.Format).Returns(ImageFormat.Rgb24);
        imageMock.Setup(i => i.IsGrayscale).Returns(false);
        imageMock.Setup(i => i.BitsPerPixel).Returns(24);
        imageMock.Setup(i => i.ChannelCount).Returns(3);
        imageMock.Setup(i => i.ToGrayscale()).Returns(imageMock.Object);
        imageMock.Setup(i => i.Clone()).Returns(imageMock.Object);
    }

    // テスト用の拡張されたフィルタークラス
    private sealed class TestCustomFilter(string name, Func<IAdvancedImage, IAdvancedImage> transform) : IImagePipelineFilter
    {
        public string Name { get; init; } = name;
        public string Description => $"{Name} Description";
        public string Category => "Test";
        public StepErrorHandlingStrategy ErrorHandlingStrategy { get; set; } = StepErrorHandlingStrategy.StopExecution;

        public IReadOnlyCollection<PipelineStepParameter> Parameters => [];

        public Task<IAdvancedImage> ExecuteAsync(IAdvancedImage input, PipelineContext context, CancellationToken cancellationToken = default)
        {
            return ApplyAsync(input);
        }

        public Task<IAdvancedImage> ApplyAsync(IAdvancedImage inputImage)
        {
            return Task.FromResult(transform(inputImage));
        }

        public IDictionary<string, object> GetParameters()
        {
            return new Dictionary<string, object>();
        }

        public void SetParameter(string parameterName, object value) { }

        public object GetParameter(string parameterName) => null!;

        public T GetParameter<T>(string parameterName) => default!;

        public PipelineImageInfo GetOutputImageInfo(IAdvancedImage input)
        {
            return new PipelineImageInfo(
                input.Width,
                input.Height,
                input.ChannelCount,
                input.Format,
                PipelineStage.Output
            );
        }
    }

    [Fact]
    public void GetSettingsReturnsCorrectSettings()
    {
        // Arrange
        var pipeline = new ImagePipeline("TestPipeline", "テスト用パイプライン");
        pipeline.AddStep(_filter1);
        pipeline.AddStep(_filter2);
        pipeline.IntermediateResultMode = IntermediateResultMode.All;
        pipeline.GlobalErrorHandlingStrategy = StepErrorHandlingStrategy.LogAndContinue;

        // Act
        var settings = pipeline.GetSettings();

        // Assert
        Assert.Equal("TestPipeline", settings.Name);
        Assert.Equal("テスト用パイプライン", settings.Description);
        Assert.Equal(IntermediateResultMode.All, settings.IntermediateResultMode);
        Assert.Equal(StepErrorHandlingStrategy.LogAndContinue, settings.ErrorHandlingStrategy);
        Assert.Equal(2, settings.Filters.Count);
        // 型名の確認→型名がMockの型になっているか確認
        Assert.NotEmpty(settings.Filters[0].TypeName);
        Assert.NotEmpty(settings.Filters[1].TypeName);
    }

    [Fact]
    public void ApplySettingsUpdatesPipelineProperties()
    {
        // Arrange
        var pipeline = new ImagePipeline("OldName", "古い説明");
        var settings = new ImagePipelineSettings
        {
            Name = "NewName",
            Description = "新しい説明",
            IntermediateResultMode = IntermediateResultMode.All,
            ErrorHandlingStrategy = StepErrorHandlingStrategy.LogAndContinue
        };

        // Act
        pipeline.ApplySettings(settings);

        // Assert
        Assert.Equal("NewName", pipeline.Name);
        Assert.Equal("新しい説明", pipeline.Description);
        Assert.Equal(IntermediateResultMode.All, pipeline.IntermediateResultMode);
        Assert.Equal(StepErrorHandlingStrategy.LogAndContinue, pipeline.GlobalErrorHandlingStrategy);
    }
}
