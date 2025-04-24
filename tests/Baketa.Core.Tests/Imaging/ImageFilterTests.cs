using System;
using System.Collections.Generic;
using System.Linq;
using Baketa.Core.Abstractions.Imaging;
using Xunit;

namespace Baketa.Core.Tests.Imaging
{
    /// <summary>
    /// IImageFilterインターフェースの単体テスト
    /// </summary>
    public class ImageFilterTests
    {
        /// <summary>
        /// テスト用のフィルタークラス - 反転フィルター実装
        /// </summary>
        private class InvertFilter : IImageFilter
        {
            public IReadOnlyList<byte> Apply(IReadOnlyList<byte> imageData, int _1, int _2, int _3)
            {
                if (imageData == null)
                {
                    throw new ArgumentNullException(nameof(imageData));
                }
                
                // 全ピクセルを反転（255-値）
                return [.. imageData.Select(b => (byte)(255 - b))];
            }
        }

        /// <summary>
        /// テスト用のフィルタークラス - 恒等フィルター（何も変更しない）
        /// </summary>
        private class IdentityFilter : IImageFilter
        {
            public IReadOnlyList<byte> Apply(IReadOnlyList<byte> imageData, int _1, int _2, int _3)
            {
                if (imageData == null)
                {
                    throw new ArgumentNullException(nameof(imageData));
                }
                return [.. imageData];
            }
        }

        /// <summary>
        /// テスト用のフィルタークラス - 指定値を返すフィルター
        /// </summary>
        private class ConstantFilter(byte value) : IImageFilter
        {
        private readonly byte _value = value;

            public IReadOnlyList<byte> Apply(IReadOnlyList<byte> imageData, int _1, int _2, int _3)
            {
                if (imageData == null)
                {
                    throw new ArgumentNullException(nameof(imageData));
                }
                
                return [.. Enumerable.Repeat(_value, imageData.Count)];
            }
        }

        [Fact]
        public void Apply_InvertFilter_InvertsImageData()
        {
            // Arrange
            var filter = new InvertFilter();
            byte[] imageData = [0, 100, 200, 255];
            int width = 2;
            int height = 2;
            int stride = 2;

            // Act
            var result = filter.Apply(imageData, width, height, stride);

            // Assert
            Assert.Equal([255, 155, 55, 0], result);
        }

        [Fact]
        public void Apply_IdentityFilter_ReturnsCopy()
        {
            // Arrange
            var filter = new IdentityFilter();
            byte[] imageData = [10, 20, 30, 40];
            int width = 2;
            int height = 2;
            int stride = 2;

            // Act
            var result = filter.Apply(imageData, width, height, stride);

            // Assert
            Assert.Equal(imageData, result);
            Assert.NotSame(imageData, result); // 新しいインスタンスであることを確認
        }

        [Fact]
        public void Apply_ConstantFilter_ReturnsConstantValues()
        {
            // Arrange
            byte constantValue = 42;
            var filter = new ConstantFilter(constantValue);
            byte[] imageData = [10, 20, 30, 40, 50, 60];
            int width = 3;
            int height = 2;
            int stride = 3;

            // Act
            var result = filter.Apply(imageData, width, height, stride);

            // Assert
            Assert.Equal(6, result.Count);
            Assert.All(result, value => Assert.Equal(constantValue, value));
        }

        [Fact]
        public void Apply_EmptyImageData_ReturnsEmptyResult()
        {
            // Arrange
            var filter = new IdentityFilter();
            byte[] imageData = [];
            int width = 0;
            int height = 0;
            int stride = 0;

            // Act
            var result = filter.Apply(imageData, width, height, stride);

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public void Apply_NullImageData_ThrowsException()
        {
            // Arrange
            var filter = new IdentityFilter();
            IReadOnlyList<byte>? imageData = null;
            int width = 1;
            int height = 1;
            int stride = 1;

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => filter.Apply(imageData!, width, height, stride));
        }
    }
}