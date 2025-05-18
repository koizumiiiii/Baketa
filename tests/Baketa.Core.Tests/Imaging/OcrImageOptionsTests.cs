using System;
using Baketa.Core.Abstractions.Imaging;
using Xunit;

namespace Baketa.Core.Tests.Imaging;

    /// <summary>
    /// OcrImageOptionsクラスの単体テスト
    /// </summary>
    public class OcrImageOptionsTests
    {
        [Fact]
        public void DefaultConstructor_InitializesWithDefaultValues()
        {
            // Act
            var options = new OcrImageOptions();

            // Assert
            Assert.Equal(0, options.BinarizationThreshold);
            Assert.True(options.UseAdaptiveThreshold);
            Assert.Equal(11, options.AdaptiveBlockSize);
            Assert.Equal(0.3f, options.NoiseReduction);
            Assert.Equal(1.2f, options.ContrastEnhancement);
            Assert.Equal(0.3f, options.SharpnessEnhancement);
            Assert.Equal(0, options.DilationPixels);
            Assert.False(options.DetectAndCorrectOrientation);
        }

        [Fact]
        public void CustomValues_SetCorrectly()
        {
            // Arrange
            var options = new OcrImageOptions
            {
                BinarizationThreshold = 150,
                UseAdaptiveThreshold = false,
                AdaptiveBlockSize = 15,
                NoiseReduction = 0.5f,
                ContrastEnhancement = 1.5f,
                SharpnessEnhancement = 0.6f,
                DilationPixels = 2,
                DetectAndCorrectOrientation = true
            };

            // Act & Assert
            Assert.Equal(150, options.BinarizationThreshold);
            Assert.False(options.UseAdaptiveThreshold);
            Assert.Equal(15, options.AdaptiveBlockSize);
            Assert.Equal(0.5f, options.NoiseReduction);
            Assert.Equal(1.5f, options.ContrastEnhancement);
            Assert.Equal(0.6f, options.SharpnessEnhancement);
            Assert.Equal(2, options.DilationPixels);
            Assert.True(options.DetectAndCorrectOrientation);
        }

        [Fact]
        public void CreatePreset_Default_ReturnsDefaultOptions()
        {
            // Act
            var options = OcrImageOptions.CreatePreset(OcrPreset.Default);

            // Assert
            Assert.Equal(0, options.BinarizationThreshold);
            Assert.True(options.UseAdaptiveThreshold);
            Assert.Equal(11, options.AdaptiveBlockSize);
            Assert.Equal(0.3f, options.NoiseReduction);
            Assert.Equal(1.2f, options.ContrastEnhancement);
        }

        [Fact]
        public void CreatePreset_HighContrast_ReturnsHighContrastOptions()
        {
            // Act
            var options = OcrImageOptions.CreatePreset(OcrPreset.HighContrast);

            // Assert
            Assert.True(options.UseAdaptiveThreshold);
            Assert.Equal(1.4f, options.ContrastEnhancement);
            Assert.Equal(0.4f, options.NoiseReduction);
        }

        [Fact]
        public void CreatePreset_SmallText_ReturnsSmallTextOptions()
        {
            // Act
            var options = OcrImageOptions.CreatePreset(OcrPreset.SmallText);

            // Assert
            Assert.Equal(7, options.AdaptiveBlockSize);
            Assert.Equal(0.5f, options.SharpnessEnhancement);
            Assert.Equal(0.5f, options.NoiseReduction);
        }

        [Fact]
        public void CreatePreset_LightText_ReturnsLightTextOptions()
        {
            // Act
            var options = OcrImageOptions.CreatePreset(OcrPreset.LightText);

            // Assert
            Assert.True(options.UseAdaptiveThreshold);
            Assert.Equal(1.6f, options.ContrastEnhancement);
            Assert.Equal(0.4f, options.NoiseReduction);
            Assert.Equal(1, options.DilationPixels);
        }

        [Fact]
        public void CreatePreset_InvalidPreset_ThrowsArgumentOutOfRangeException()
        {
            // Act & Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => 
                OcrImageOptions.CreatePreset((OcrPreset)999));
        }
    }
