using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Filters;
using Baketa.Core.Abstractions.Memory;
using Xunit;
using Rectangle = Baketa.Core.Abstractions.Memory.Rectangle;

namespace Baketa.Core.Tests.Imaging;

/// <summary>
/// IImageFilterインターフェースの単体テスト
/// </summary>
public class ImageFilterTests
{
    /// <summary>
    /// テスト用のフィルタークラス - 反転フィルター実装
    /// </summary>
    private sealed class InvertFilter : ImageFilterBase
    {
        public override string Name => "反転フィルター";

        public override string Description => "画像の色を反転（255-値）します";

        public override FilterCategory Category => FilterCategory.ColorAdjustment;

        protected override void InitializeDefaultParameters()
        {
            // パラメータなし
        }

        public override async Task<IAdvancedImage> ApplyAsync(IAdvancedImage inputImage)
        {
            ArgumentNullException.ThrowIfNull(inputImage);

            // テスト用のシンプル実装 - バイトデータを取得して反転して返す
            var imageData = await inputImage.ToByteArrayAsync().ConfigureAwait(false);
            byte[] result = [.. imageData.Select(static b => (byte)(255 - b))];

            // モック実装なので実際には元の画像をそのまま返す
            return inputImage;
        }

        // 互換性のためのレガシーメソッド
        public byte[] Apply(byte[] imageData, int _1, int _2, int _3)
        {
            ArgumentNullException.ThrowIfNull(imageData);

            // 全ピクセルを反転（255-値）
            return [.. imageData.Select(static b => (byte)(255 - b))];
        }
    }

    /// <summary>
    /// テスト用のフィルタークラス - 恒等フィルター（何も変更しない）
    /// </summary>
    private sealed class IdentityFilter : ImageFilterBase
    {
        public override string Name => "恒等フィルター";

        public override string Description => "画像を変更せずにそのまま返します";

        public override FilterCategory Category => FilterCategory.Effect;

        protected override void InitializeDefaultParameters()
        {
            // パラメータなし
        }

        public override Task<IAdvancedImage> ApplyAsync(IAdvancedImage inputImage)
        {
            ArgumentNullException.ThrowIfNull(inputImage);

            // 画像をそのまま返す
            return Task.FromResult(inputImage);
        }

        // 互換性のためのレガシーメソッド
        public byte[] Apply(byte[] imageData, int _1, int _2, int _3)
        {
            ArgumentNullException.ThrowIfNull(imageData);

            // Lengthプロパティを使用
            byte[] result = new byte[imageData.Length];
            Array.Copy(imageData, result, imageData.Length);
            return result;
        }
    }

    /// <summary>
    /// テスト用のフィルタークラス - 指定値を返すフィルター
    /// </summary>
    private sealed class ConstantFilter : ImageFilterBase
    {
        private readonly byte _value;

        public ConstantFilter(byte value)
        {
            _value = value;
            InitializeDefaultParameters();
        }

        public override string Name => "定数フィルター";

        public override string Description => $"すべてのピクセルを値 {_value} に設定します";

        public override FilterCategory Category => FilterCategory.Effect;

        protected override void InitializeDefaultParameters()
        {
            RegisterParameter("Value", _value);
        }

        public override Task<IAdvancedImage> ApplyAsync(IAdvancedImage inputImage)
        {
            ArgumentNullException.ThrowIfNull(inputImage);

            // 実際のテスト実装では値を置き換えるが、テストなので元の画像を返す
            return Task.FromResult(inputImage);
        }

        // 互換性のためのレガシーメソッド
        public byte[] Apply(byte[] imageData, int _1, int _2, int _3)
        {
            ArgumentNullException.ThrowIfNull(imageData);

            // カウントを明示的に指定
            return [.. Enumerable.Repeat(_value, imageData.Length)];
        }
    }

    /// <summary>
    /// モックAdvancedImageクラス
    /// </summary>
    private sealed class MockAdvancedImage(byte[] data, int width = 2, int height = 2) : IAdvancedImage
    {
        private readonly byte[] _data = data;

        public int Width { get; } = width;
        public int Height { get; } = height;
        public ImageFormat Format => ImageFormat.Rgb24;
        public bool IsGrayscale => false;
        public int BitsPerPixel => 24;
        public int ChannelCount => 3;

        /// <summary>
        /// Phase 2.5: ROI座標変換対応 - モック画像なのでnull
        /// </summary>
        public System.Drawing.Rectangle? CaptureRegion => null;

        public IAdvancedImage ToGrayscale() => new MockAdvancedImage(_data, Width, Height);

        public Task<byte[]> ToByteArrayAsync() => Task.FromResult(_data);
        public IImage Clone() => new MockAdvancedImage(_data, Width, Height);
        public Task<IImage> ResizeAsync(int _width, int _height) => Task.FromResult<IImage>(this);
        public Task SaveAsync(string _path, ImageFormat? _format = null) => Task.CompletedTask;
        public Task<IImage> CropAsync(Rectangle _rectangle) => Task.FromResult<IImage>(this);
        public Task<byte[]> GetPixelsAsync(int _x, int _y, int _width, int _height) => Task.FromResult(_data);

        public Color GetPixel(int _x, int _y) => Color.FromArgb(255, 255, 255);
        public void SetPixel(int _x, int _y, Color _color) { }

        public Task<IAdvancedImage> ApplyFilterAsync(IImageFilter _filter) => Task.FromResult<IAdvancedImage>(this);
        public Task<IAdvancedImage> ApplyFiltersAsync(IEnumerable<IImageFilter> _filters) => Task.FromResult<IAdvancedImage>(this);
        public Task<int[]> ComputeHistogramAsync(ColorChannel _channel = ColorChannel.Luminance) => Task.FromResult(new int[256]);
        public Task<IAdvancedImage> ToGrayscaleAsync() => Task.FromResult<IAdvancedImage>(this);
        public Task<IAdvancedImage> ToBinaryAsync(byte _threshold) => Task.FromResult<IAdvancedImage>(this);
        public Task<IAdvancedImage> ExtractRegionAsync(Rectangle _rectangle) => Task.FromResult<IAdvancedImage>(this);
        public Task<IAdvancedImage> OptimizeForOcrAsync() => Task.FromResult<IAdvancedImage>(this);
        public Task<IAdvancedImage> OptimizeForOcrAsync(OcrImageOptions _options) => Task.FromResult<IAdvancedImage>(this);
        public Task<float> CalculateSimilarityAsync(IImage _other) => Task.FromResult(1.0f);
        public Task<float> EvaluateTextProbabilityAsync(Rectangle _rectangle) => Task.FromResult(0.5f);
        public Task<IAdvancedImage> RotateAsync(float _degrees) => Task.FromResult<IAdvancedImage>(this);
        public Task<IAdvancedImage> EnhanceAsync(ImageEnhancementOptions _options) => Task.FromResult<IAdvancedImage>(this);
        public Task<List<Rectangle>> DetectTextRegionsAsync() => Task.FromResult(new List<Rectangle>());

        // IImageの追加メンバー
        public ReadOnlyMemory<byte> GetImageMemory() => new ReadOnlyMemory<byte>(_data);
        public ImagePixelFormat PixelFormat => ImagePixelFormat.Rgb24;

        /// <summary>
        /// 🔥 [PHASE5.2G-A] LockPixelData (MockAdvancedImage is test-only, not supported)
        /// </summary>
        public PixelDataLock LockPixelData() => throw new NotSupportedException("MockAdvancedImage does not support LockPixelData");

        public void Dispose() { }
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
        byte[] expected = [255, 155, 55, 0];
        Assert.Equal(expected, result);
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
        Assert.Equal(6, result.Length);
        var allMatched = result.All(value => value == constantValue);
        Assert.True(allMatched);
    }

    [Fact]
    public void Apply_EmptyImageData_ReturnsEmptyResult()
    {
        // Arrange
        var filter = new IdentityFilter();
        byte[] empty = [];
        int width = 0;
        int height = 0;
        int stride = 0;

        // Act
        var result = filter.Apply(empty, width, height, stride);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void Apply_NullImageData_ThrowsException()
    {
        // Arrange
        var filter = new IdentityFilter();
        byte[]? imageData = null;
        int width = 1;
        int height = 1;
        int stride = 1;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => filter.Apply(imageData!, width, height, stride));
    }

    [Fact]
    public async Task ApplyAsync_InvertFilter_Works()
    {
        // Arrange
        var filter = new InvertFilter();
        var mockImage = new MockAdvancedImage([0, 100, 200, 255]);

        // Act
        var result = await filter.ApplyAsync(mockImage);

        // Assert
        Assert.NotNull(result);
        // モック実装では元の画像を返すので、同一のインスタンスであることを確認
        Assert.Same(mockImage, result);
    }

    [Fact]
    public async Task ApplyAsync_IdentityFilter_ReturnsSameImage()
    {
        // Arrange
        var filter = new IdentityFilter();
        var mockImage = new MockAdvancedImage([10, 20, 30, 40]);

        // Act
        var result = await filter.ApplyAsync(mockImage);

        // Assert
        Assert.NotNull(result);
        Assert.Same(mockImage, result);
    }

    [Fact]
    public async Task ApplyAsync_ConstantFilter_Works()
    {
        // Arrange
        byte constantValue = 42;
        var filter = new ConstantFilter(constantValue);
        var mockImage = new MockAdvancedImage([10, 20, 30, 40]);

        // Act
        var result = await filter.ApplyAsync(mockImage);

        // Assert
        Assert.NotNull(result);
        Assert.Same(mockImage, result);

        // パラメータ取得のテスト
        var parameters = (IDictionary<string, object>)filter.GetParameters();
        Assert.Contains("Value", parameters.Keys);
        Assert.Equal(constantValue, parameters["Value"]);
    }

    [Fact]
    public async Task ApplyAsync_NullInput_ThrowsException()
    {
        // Arrange
        var filter = new IdentityFilter();
        IAdvancedImage? nullImage = null;

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => filter.ApplyAsync(nullImage!));
    }
}
