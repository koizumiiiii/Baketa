using System;
using System.Diagnostics.CodeAnalysis;
using Baketa.Core.Abstractions.OCR;
using Baketa.Infrastructure.Platform.Windows.OpenCv.Extensions;
using OpenCvSharp;
using Xunit;

namespace Baketa.Infrastructure.Platform.Tests.Windows.OpenCv;

    /// <summary>
    /// OpenCvExtensionsクラスの単体テスト
    /// </summary>
    [SuppressMessage("Design", "CA1515:型を内部にする必要があります", Justification = "xUnitのテストクラスはpublicでなければなりません")]
    public class OpenCvExtensionsTests
    {
        #region ConvertThresholdType テスト

        [Theory]
        [InlineData(ThresholdType.Binary, ThresholdTypes.Binary)]
        [InlineData(ThresholdType.BinaryInv, ThresholdTypes.BinaryInv)]
        [InlineData(ThresholdType.Truncate, ThresholdTypes.Trunc)]
        [InlineData(ThresholdType.ToZero, ThresholdTypes.Tozero)]
        [InlineData(ThresholdType.ToZeroInv, ThresholdTypes.TozeroInv)]
        [InlineData(ThresholdType.Otsu, ThresholdTypes.Binary | ThresholdTypes.Otsu)]
        public void ConvertThresholdTypeValidTypesShouldConvertCorrectly(ThresholdType input, ThresholdTypes expected)
        {
            // Act
            var result = OpenCvExtensions.ConvertThresholdType(input);
            
            // Assert
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ConvertThresholdTypeAdaptiveTypeShouldThrowArgumentException()
        {
            // Act & Assert
            var exception = Assert.Throws<ArgumentException>(() => 
                OpenCvExtensions.ConvertThresholdType(ThresholdType.Adaptive));
            
            Assert.Contains("適応的閾値処理", exception.Message, StringComparison.Ordinal);
        }

        #endregion

        #region ConvertBinaryThresholdType テスト

        [Theory]
        [InlineData(ThresholdType.Binary, ThresholdTypes.Binary)]
        [InlineData(ThresholdType.BinaryInv, ThresholdTypes.BinaryInv)]
        [InlineData(ThresholdType.Truncate, ThresholdTypes.Binary)] // デフォルトに変換される
        [InlineData(ThresholdType.ToZero, ThresholdTypes.Binary)] // デフォルトに変換される
        public void ConvertBinaryThresholdTypeAnyTypeShouldReturnBinaryTypes(ThresholdType input, ThresholdTypes expected)
        {
            // Act
            var result = OpenCvExtensions.ConvertBinaryThresholdType(input);
            
            // Assert
            Assert.Equal(expected, result);
        }

        #endregion

        #region ConvertAdaptiveThresholdType テスト

        [Theory]
        [InlineData(AdaptiveThresholdType.Mean, AdaptiveThresholdTypes.MeanC)]
        [InlineData(AdaptiveThresholdType.Gaussian, AdaptiveThresholdTypes.GaussianC)]
        public void ConvertAdaptiveThresholdTypeValidTypesShouldConvertCorrectly(AdaptiveThresholdType input, AdaptiveThresholdTypes expected)
        {
            // Act
            var result = OpenCvExtensions.ConvertAdaptiveThresholdType(input);
            
            // Assert
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ConvertAdaptiveThresholdTypeInvalidValueShouldThrowArgumentException()
        {
            // Act & Assert
            // 未定義の列挙値を指定
            var invalidType = (AdaptiveThresholdType)99;
            var exception = Assert.Throws<ArgumentException>(() => 
                OpenCvExtensions.ConvertAdaptiveThresholdType(invalidType));
            
            Assert.Contains("未サポート", exception.Message, StringComparison.Ordinal);
        }

        #endregion

        #region ConvertMorphType テスト

        [Theory]
        [InlineData(MorphType.Erode, MorphTypes.Erode)]
        [InlineData(MorphType.Dilate, MorphTypes.Dilate)]
        [InlineData(MorphType.Open, MorphTypes.Open)]
        [InlineData(MorphType.Close, MorphTypes.Close)]
        [InlineData(MorphType.Gradient, MorphTypes.Gradient)]
        [InlineData(MorphType.TopHat, MorphTypes.TopHat)]
        [InlineData(MorphType.BlackHat, MorphTypes.BlackHat)]
        public void ConvertMorphTypeValidTypesShouldConvertCorrectly(MorphType input, MorphTypes expected)
        {
            // Act
            var result = OpenCvExtensions.ConvertMorphType(input);
            
            // Assert
            Assert.Equal(expected, result);
        }

        [Fact]
        public void ConvertMorphTypeInvalidValueShouldThrowArgumentException()
        {
            // Act & Assert
            // 未定義の列挙値を指定
            var invalidType = (MorphType)99;
            var exception = Assert.Throws<ArgumentException>(() => 
                OpenCvExtensions.ConvertMorphType(invalidType));
            
            Assert.Contains("未サポート", exception.Message, StringComparison.Ordinal);
        }

        #endregion
    }
