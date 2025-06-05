using System;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Filters;
using Baketa.Core.Abstractions.Imaging.Pipeline;
using Baketa.Core.Services.Imaging.Pipeline;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Baketa.Core.Tests.Imaging.Pipeline;

    public class OcrPipelineBuilderTests
    {
        private readonly Mock<ILogger<OcrPipelineBuilder>> _loggerMock;
        private readonly Mock<IImagePipeline> _pipelineMock;
        private readonly Mock<IOcrFilterFactory> _filterFactoryMock;
        private readonly Mock<IImageFilter> _filterMock1;
        private readonly Mock<IImageFilter> _filterMock2;
        private readonly Mock<IImageFilter> _filterMock3;

        public OcrPipelineBuilderTests()
        {
            _loggerMock = new Mock<ILogger<OcrPipelineBuilder>>();
            _pipelineMock = new Mock<IImagePipeline>();
            _filterFactoryMock = new Mock<IOcrFilterFactory>();
            
            _filterMock1 = new Mock<IImageFilter>();
            _filterMock1.Setup(f => f.Name).Returns("テストフィルター1");
            
            _filterMock2 = new Mock<IImageFilter>();
            _filterMock2.Setup(f => f.Name).Returns("テストフィルター2");
            
            _filterMock3 = new Mock<IImageFilter>();
            _filterMock3.Setup(f => f.Name).Returns("テストフィルター3");
        }

        [Fact]
        public void BuildStandardPipeline_ShouldClearAndAddFilters()
        {
            // Arrange
            var builder = new OcrPipelineBuilder(
                _pipelineMock.Object,
                _filterFactoryMock.Object,
                _loggerMock.Object);
                
            var standardFilters = new[] 
            { 
                _filterMock1.Object, 
                _filterMock2.Object, 
                _filterMock3.Object 
            };
            
            _filterFactoryMock.Setup(f => f.CreateStandardOcrPipeline())
                .Returns(standardFilters);

            // Act
            var result = builder.BuildStandardPipeline();

            // Assert
            Assert.Same(_pipelineMock.Object, result);
            _pipelineMock.Verify(p => p.ClearSteps(), Times.Once);
            _pipelineMock.Verify(p => p.AddStep(It.IsAny<IImagePipelineStep>()), Times.Exactly(3));
            _pipelineMock.VerifySet(p => p.IntermediateResultMode = IntermediateResultMode.None);
            _pipelineMock.VerifySet(p => p.GlobalErrorHandlingStrategy = StepErrorHandlingStrategy.LogAndContinue);
        }

        [Fact]
        public void BuildMinimalPipeline_ShouldClearAndAddFilters()
        {
            // Arrange
            var builder = new OcrPipelineBuilder(
                _pipelineMock.Object,
                _filterFactoryMock.Object,
                _loggerMock.Object);
                
            var minimalFilters = new[] 
            { 
                _filterMock1.Object, 
                _filterMock2.Object
            };
            
            _filterFactoryMock.Setup(f => f.CreateMinimalOcrPipeline())
                .Returns(minimalFilters);

            // Act
            var result = builder.BuildMinimalPipeline();

            // Assert
            Assert.Same(_pipelineMock.Object, result);
            _pipelineMock.Verify(p => p.ClearSteps(), Times.Once);
            _pipelineMock.Verify(p => p.AddStep(It.IsAny<IImagePipelineStep>()), Times.Exactly(2));
        }

        [Fact]
        public void BuildEdgeBasedPipeline_ShouldClearAndAddFilters()
        {
            // Arrange
            var builder = new OcrPipelineBuilder(
                _pipelineMock.Object,
                _filterFactoryMock.Object,
                _loggerMock.Object);
                
            var edgeFilters = new[] 
            { 
                _filterMock1.Object, 
                _filterMock3.Object
            };
            
            _filterFactoryMock.Setup(f => f.CreateEdgeBasedOcrPipeline())
                .Returns(edgeFilters);

            // Act
            var result = builder.BuildEdgeBasedPipeline();

            // Assert
            Assert.Same(_pipelineMock.Object, result);
            _pipelineMock.Verify(p => p.ClearSteps(), Times.Once);
            _pipelineMock.Verify(p => p.AddStep(It.IsAny<IImagePipelineStep>()), Times.Exactly(2));
        }

        [Fact]
        public void BuildCustomPipeline_ShouldCreateFiltersByType()
        {
            // Arrange
            var builder = new OcrPipelineBuilder(
                _pipelineMock.Object,
                _filterFactoryMock.Object,
                _loggerMock.Object);
                
            _filterFactoryMock.Setup(f => f.CreateFilter(OcrFilterType.Grayscale))
                .Returns(_filterMock1.Object);
                
            _filterFactoryMock.Setup(f => f.CreateFilter(OcrFilterType.Threshold))
                .Returns(_filterMock2.Object);

            // Act
            var result = builder.BuildCustomPipeline(OcrFilterType.Grayscale, OcrFilterType.Threshold);

            // Assert
            Assert.Same(_pipelineMock.Object, result);
            _pipelineMock.Verify(p => p.ClearSteps(), Times.Once);
            _pipelineMock.Verify(p => p.AddStep(It.IsAny<IImagePipelineStep>()), Times.Exactly(2));
            _filterFactoryMock.Verify(f => f.CreateFilter(OcrFilterType.Grayscale), Times.Once);
            _filterFactoryMock.Verify(f => f.CreateFilter(OcrFilterType.Threshold), Times.Once);
        }

        [Fact]
        public void BuildCustomPipeline_ShouldHandleFilterCreationErrors()
        {
            // Arrange
            var builder = new OcrPipelineBuilder(
                _pipelineMock.Object,
                _filterFactoryMock.Object,
                _loggerMock.Object);
                
            _filterFactoryMock.Setup(f => f.CreateFilter(OcrFilterType.Grayscale))
                .Returns(_filterMock1.Object);
                
            _filterFactoryMock.Setup(f => f.CreateFilter(OcrFilterType.Threshold))
                .Throws(new InvalidOperationException("テストエラー"));
                
            _filterFactoryMock.Setup(f => f.CreateFilter(OcrFilterType.NoiseReduction))
                .Returns(_filterMock3.Object);

            // Act
            var result = builder.BuildCustomPipeline(
                OcrFilterType.Grayscale, 
                OcrFilterType.Threshold, 
                OcrFilterType.NoiseReduction);

            // Assert
            Assert.Same(_pipelineMock.Object, result);
            _pipelineMock.Verify(p => p.ClearSteps(), Times.Once);
            _pipelineMock.Verify(p => p.AddStep(It.IsAny<IImagePipelineStep>()), Times.Exactly(2));
            _filterFactoryMock.Verify(f => f.CreateFilter(OcrFilterType.Grayscale), Times.Once);
            _filterFactoryMock.Verify(f => f.CreateFilter(OcrFilterType.Threshold), Times.Once);
            _filterFactoryMock.Verify(f => f.CreateFilter(OcrFilterType.NoiseReduction), Times.Once);
        }

        [Fact]
        public async Task LoadPipelineFromProfileAsync_ShouldDelegateToImagePipeline()
        {
            // Arrange
            var builder = new OcrPipelineBuilder(
                _pipelineMock.Object,
                _filterFactoryMock.Object,
                _loggerMock.Object);
                
            var profileName = "テストプロファイル";
            
            _pipelineMock.Setup(p => p.LoadProfileAsync(profileName))
                .ReturnsAsync(_pipelineMock.Object);

            // Act
            var result = await builder.LoadPipelineFromProfileAsync(profileName);

            // Assert
            Assert.Same(_pipelineMock.Object, result);
            _pipelineMock.Verify(p => p.LoadProfileAsync(profileName), Times.Once);
        }

        [Fact]
        public async Task LoadPipelineFromProfileAsync_WhenFails_ShouldReturnStandardPipeline()
        {
            // Arrange
            var builder = new OcrPipelineBuilder(
                _pipelineMock.Object,
                _filterFactoryMock.Object,
                _loggerMock.Object);
                
            var profileName = "不正なプロファイル";
            
            _pipelineMock.Setup(p => p.LoadProfileAsync(profileName))
                .ThrowsAsync(new InvalidOperationException("プロファイル読み込みエラー"));
                
            var standardFilters = new[] { _filterMock1.Object };
            _filterFactoryMock.Setup(f => f.CreateStandardOcrPipeline())
                .Returns(standardFilters);

            // Act
            var result = await builder.LoadPipelineFromProfileAsync(profileName);

            // Assert
            Assert.Same(_pipelineMock.Object, result);
            _pipelineMock.Verify(p => p.LoadProfileAsync(profileName), Times.Once);
            _pipelineMock.Verify(p => p.ClearSteps(), Times.Once);
            _filterFactoryMock.Verify(f => f.CreateStandardOcrPipeline(), Times.Once);
        }

        [Fact]
        public async Task SavePipelineToProfileAsync_ShouldDelegateToImagePipeline()
        {
            // Arrange
            var builder = new OcrPipelineBuilder(
                _pipelineMock.Object,
                _filterFactoryMock.Object,
                _loggerMock.Object);
                
            var profileName = "テストプロファイル";

            // Act
            await builder.SavePipelineToProfileAsync(profileName);

            // Assert
            _pipelineMock.Verify(p => p.SaveProfileAsync(profileName), Times.Once);
        }

        [Fact]
        public async Task SavePipelineToProfileAsync_WhenFails_ShouldPropagateException()
        {
            // Arrange
            var builder = new OcrPipelineBuilder(
                _pipelineMock.Object,
                _filterFactoryMock.Object,
                _loggerMock.Object);
                
            var profileName = "不正なプロファイル";
            
            _pipelineMock.Setup(p => p.SaveProfileAsync(profileName))
                .ThrowsAsync(new InvalidOperationException("プロファイル保存エラー"));

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await builder.SavePipelineToProfileAsync(profileName));
                
            _pipelineMock.Verify(p => p.SaveProfileAsync(profileName), Times.Once);
        }
    }
