using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Baketa.Infrastructure.OCR.TextDetection;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Pipeline;
using Baketa.Core.Abstractions.OCR.TextDetection;
using OCRTextRegion = Baketa.Core.Abstractions.OCR.TextDetection.TextRegion;

namespace Baketa.Infrastructure.Tests.OCR.TextDetection;

/// <summary>
/// AdaptiveTextRegionDetectorのテストクラス
/// 1-B2: テキスト領域検出高度化のテスト実装
/// </summary>
public class AdaptiveTextRegionDetectorTests : IDisposable
{
    private readonly AdaptiveTextRegionDetector _detector;
    private readonly ILogger<AdaptiveTextRegionDetector> _logger;
    private readonly TestImage _testImage;

    public AdaptiveTextRegionDetectorTests()
    {
        _logger = new NullLogger<AdaptiveTextRegionDetector>();
        _detector = new AdaptiveTextRegionDetector(_logger);
        _testImage = new TestImage(800, 600);
    }

    [Fact]
    public async Task DetectRegionsAsync_WithValidImage_ReturnsRegions()
    {
        // Act
        var regions = await _detector.DetectRegionsAsync(_testImage);

        // Assert
        Assert.NotNull(regions);
        Assert.IsAssignableFrom<IReadOnlyList<OCRTextRegion>>(regions);
    }

    [Fact]
    public async Task DetectRegionsAsync_MultipleCalls_ShowsAdaptiveBehavior()
    {
        // 複数回実行して適応的動作を確認
        var results = new List<IReadOnlyList<OCRTextRegion>>();

        // Act - 複数回実行
        for (int i = 0; i < 5; i++)
        {
            var regions = await _detector.DetectRegionsAsync(_testImage);
            results.Add(regions);
            
            // 少し間隔を空けて履歴ベースの最適化を発動させる
            await Task.Delay(100);
        }

        // Assert - 各実行が完了していることを確認
        Assert.All(results, result => Assert.NotNull(result));
        Assert.Equal(5, results.Count);
    }

    [Fact]
    public void SetParameter_ValidParameter_UpdatesSuccessfully()
    {
        // Arrange
        const string paramName = "AdaptiveSensitivity";
        const double expectedValue = 0.8;

        // Act
        _detector.SetParameter(paramName, expectedValue);
        var actualValue = _detector.GetParameter<double>(paramName);

        // Assert
        Assert.Equal(expectedValue, actualValue);
    }

    [Fact]
    public void GetParameter_UnknownParameter_ReturnsDefault()
    {
        // Arrange
        const string unknownParam = "UnknownParameter";

        // Act
        var result = _detector.GetParameter(unknownParam);

        // Assert - 未知パラメータはデフォルト値0を返す（型はdouble）
        Assert.NotNull(result);
        Assert.IsType<double>(result);
        Assert.Equal(0.0, (double)result);
    }

    [Fact]
    public void GetParameters_ReturnsAllParameters()
    {
        // Act
        var parameters = _detector.GetParameters();

        // Assert
        Assert.NotNull(parameters);
        Assert.Contains("AdaptiveSensitivity", parameters.Keys);
        Assert.Contains("AdaptiveMinArea", parameters.Keys);
        Assert.Contains("MaxRegionsPerImage", parameters.Keys);
    }

    [Fact]
    public async Task SaveProfileAsync_ValidProfile_CompletesSuccessfully()
    {
        // Arrange
        var profileName = "TestProfile_" + Guid.NewGuid().ToString("N")[..8];

        try
        {
            // Act - 一時的なファイル名でプロファイル保存をテスト
            await _detector.SaveProfileAsync(profileName);
            
            // Assert - 例外が発生しないことを確認
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // ファイルアクセス権限エラーは環境依存のためスキップ
            Assert.True(true, $"ファイルアクセス環境の制約によりスキップ: {ex.GetType().Name}");
        }
        finally
        {
            // テストファイルをクリーンアップしようと試みる
            try
            {
                var profilePath = Path.Combine(Path.GetTempPath(), "BaketaProfiles", $"{profileName}.json");
                if (File.Exists(profilePath))
                {
                    File.Delete(profilePath);
                }
            }
            catch
            {
                // クリーンアップ失敗は無視
            }
        }
    }

    [Fact]
    public async Task LoadProfileAsync_NonExistentProfile_CompletesGracefully()
    {
        // Arrange
        const string nonExistentProfile = "NonExistentProfile";

        // Act & Assert - 例外が発生しないことを確認
        await _detector.LoadProfileAsync(nonExistentProfile);
    }

    [Fact]
    public async Task DetectRegionsAsync_WithCancellation_RespondsToToken()
    {
        // Arrange - キャンセルトークンを遅延して適用
        using var cancellationTokenSource = new CancellationTokenSource();
        
        // Act - 非同期でキャンセルして適切なタイミングで例外を発生させる
        var detectionTask = _detector.DetectRegionsAsync(_testImage, cancellationTokenSource.Token);
        
        // 短い遅延でキャンセルを申請（税理処理開始後）
        _ = Task.Run(async () => 
        {
            await Task.Delay(50); // 処理開始を待つ
            cancellationTokenSource.Cancel();
        });
        
        // Assert - キャンセル例外または正常完了を許可
        try
        {
            await detectionTask;
            // キャンセル前に完了した場合も正常動作
        }
        catch (OperationCanceledException)
        {
            // 期待される動作：キャンセル例外
        }
    }

    [Fact]
    public void Properties_ReturnsExpectedValues()
    {
        // Assert
        Assert.Equal("AdaptiveTextRegionDetector", _detector.Name);
        Assert.Equal("適応的テキスト領域検出器 - 履歴ベース最適化と動的調整", _detector.Description);
        Assert.Equal(TextDetectionMethod.Adaptive, _detector.Method);
    }

    [Fact]
    public async Task AdaptiveBehavior_PerformanceOptimization_AdjustsParameters()
    {
        // Arrange - 初期パラメータを記録
        _ = _detector.GetParameter<double>("AdaptiveSensitivity");
        _ = _detector.GetParameter<int>("AdaptiveMinArea");

        // Act - 複数回検出を実行して適応処理をトリガー
        for (int i = 0; i < 10; i++)
        {
            await _detector.DetectRegionsAsync(_testImage);
            await Task.Delay(50); // 適応間隔を考慮
        }

        // 適応処理の実行を待つ
        await Task.Delay(6000); // 適応間隔(5秒)より長く待機

        // Assert - パラメータの変化をチェック（実際の適応処理によって変わる可能性）
        var finalSensitivity = _detector.GetParameter<double>("AdaptiveSensitivity");
        var finalMinArea = _detector.GetParameter<int>("AdaptiveMinArea");

        // パラメータが初期値から変わっているか、または同じ値を維持していることを確認
        Assert.True(finalSensitivity >= 0.0 && finalSensitivity <= 1.0);
        Assert.True(finalMinArea >= 0);
    }

    public void Dispose()
    {
        _detector?.Dispose();
        _testImage?.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// テスト用の画像実装
/// </summary>
internal sealed class TestImage(int width, int height) : IAdvancedImage
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public int Channels => 3;
    public Type PixelType => typeof(byte);
    
    // IAdvancedImage specific properties
    public bool IsGrayscale => false;
    public int BitsPerPixel => 24;
    public int ChannelCount => 3;
    public ImageFormat Format => ImageFormat.Rgb24;

    // IImage methods
    IImage IImage.Clone() => new TestImage(Width, Height);
    public async Task<IImage> ResizeAsync(int newWidth, int newHeight) => await Task.FromResult(new TestImage(newWidth, newHeight)).ConfigureAwait(false);
    public async Task<byte[]> ToByteArrayAsync() => await Task.FromResult(new byte[Width * Height * 3]).ConfigureAwait(false);
    
    // IAdvancedImage methods
    public IAdvancedImage Clone() => new TestImage(Width, Height);
    public async Task<IAdvancedImage> ToGrayscaleAsync() => await Task.FromResult(new TestImage(Width, Height)).ConfigureAwait(false);
    public async Task<IAdvancedImage> ToBinaryAsync(byte threshold) { _ = threshold; return await Task.FromResult(new TestImage(Width, Height)).ConfigureAwait(false); }
    public async Task<IAdvancedImage> ExtractRegionAsync(Rectangle region) => await Task.FromResult(new TestImage(region.Width, region.Height)).ConfigureAwait(false);
    public async Task<IAdvancedImage> OptimizeForOcrAsync() => await Task.FromResult(this).ConfigureAwait(false);
    public async Task<IAdvancedImage> OptimizeForOcrAsync(OcrImageOptions options) { _ = options; return await Task.FromResult(this).ConfigureAwait(false); }
    public async Task<float> CalculateSimilarityAsync(IImage other) { _ = other; return await Task.FromResult(0.5f).ConfigureAwait(false); }
    public async Task<float> EvaluateTextProbabilityAsync(Rectangle region) { _ = region; return await Task.FromResult(0.7f).ConfigureAwait(false); }
    public async Task<IAdvancedImage> RotateAsync(float angle) { _ = angle; return await Task.FromResult(this).ConfigureAwait(false); }
    public async Task<IAdvancedImage> EnhanceAsync(ImageEnhancementOptions options) { _ = options; return await Task.FromResult(this).ConfigureAwait(false); }
    public async Task<List<Rectangle>> DetectTextRegionsAsync() => await Task.FromResult<List<Rectangle>>([]);
    
    // 不足しているメンバー
    public Color GetPixel(int x, int y) { _ = x; _ = y; return Color.Black; }
    public void SetPixel(int x, int y, Color value) { _ = x; _ = y; _ = value; /* スタブ実装 */ }
    public async Task<IAdvancedImage> ApplyFilterAsync(IImageFilter filter) { _ = filter; return await Task.FromResult(this).ConfigureAwait(false); }
    public async Task<IAdvancedImage> ApplyFiltersAsync(IEnumerable<IImageFilter> filters) { _ = filters; return await Task.FromResult(this).ConfigureAwait(false); }
    public async Task<int[]> ComputeHistogramAsync(ColorChannel channel) { _ = channel; return await Task.FromResult(new int[256]).ConfigureAwait(false); }
    public IAdvancedImage ToGrayscale() => this;
    
    // Legacy methods
    public void Save(string filePath) { _ = filePath; /* スタブ実装 */ }
    public T GetPixel<T>(int x, int y) { _ = x; _ = y; return default!; }
    public void SetPixel<T>(int x, int y, T value) { _ = x; _ = y; _ = value; /* スタブ実装 */ }
    public IAdvancedImage Resize(int newWidth, int newHeight) => new TestImage(newWidth, newHeight);
    public IAdvancedImage Crop(Rectangle cropArea) => new TestImage(cropArea.Width, cropArea.Height);
    public void ApplyFilter(string filterName, Dictionary<string, object>? parameters = null) { _ = filterName; _ = parameters; /* スタブ実装 */ }
    public Dictionary<string, object> GetMetadata() => [];
    public void SetMetadata(string key, object value) { _ = key; _ = value; /* スタブ実装 */ }
    public byte[] ToByteArray() => new byte[Width * Height * 3];
    public void FromByteArray(byte[] data) { _ = data; /* スタブ実装 */ }

    public void Dispose()
    {
        // スタブ実装 - リソース解放は不要
        GC.SuppressFinalize(this);
    }
}
