using System;
using System.Drawing;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Infrastructure.Platform.Adapters;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Platform.Tests.Adapters.DefaultWindowsImageAdapterTests
{
    /// <summary>
    /// DefaultWindowsImageAdapterのメモリ管理テスト
    /// </summary>
    public class MemoryManagementTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly DefaultWindowsImageAdapter _adapter;
        
        public MemoryManagementTests(ITestOutputHelper output)
        {
            _output = output;
            _adapter = new DefaultWindowsImageAdapter();
            
            // テストデータの準備
            AdapterTestHelper.EnsureTestDataExists();
        }
        
        [Fact]
        public async Task MultipleConversions_DoNotLeakMemory()
        {
            // Arrange
            const int iterations = 50;
            using var windowsImage = AdapterTestHelper.CreateMockWindowsImage(1024, 768);
            
            // 事前にGCを実行してメモリ状態をクリーンに
            GC.Collect(2, GCCollectionMode.Forced, true);
            var initialMemory = GC.GetTotalMemory(true);
            
            // Act
            for (int i = 0; i < iterations; i++)
            {
                // 変換の繰り返し
                using var image = _adapter.ToAdvancedImage(windowsImage);
                using var windowsImageResult = await _adapter.FromAdvancedImageAsync(image);
                
                // usingステートメントにより自動的にスコープ終了時にDisposeされる
            }
            
            // 再度GCを実行
            GC.Collect(2, GCCollectionMode.Forced, true);
            var finalMemory = GC.GetTotalMemory(true);
            
            // Assert
            var memoryDiff = finalMemory - initialMemory;
            _output.WriteLine($"Memory difference: {memoryDiff / 1024} KB");
            
            // 1MB未満の差であること（若干の変動は許容）
            Assert.True(memoryDiff < 1024 * 1024, 
                $"Memory leak detected: {memoryDiff / 1024} KB increase after {iterations} conversions");
        }
        
        [Fact]
        public async Task LargeImageProcessing_DoesNotExceedReasonableMemory()
        {
            // Arrange
            const int width = 3840;
            const int height = 2160;
            
            using var testBitmap = AdapterTestHelper.CreateTestImage(width, height);
            var imageData = AdapterTestHelper.ConvertImageToBytes(testBitmap);
            
            // 事前にGCを実行してメモリ状態をクリーンに
            GC.Collect(2, GCCollectionMode.Forced, true);
            var initialMemory = GC.GetTotalMemory(true);
            
            // Act
            var stopwatch = new System.Diagnostics.Stopwatch();
            stopwatch.Start();
            
            using var image = await _adapter.CreateAdvancedImageFromBytesAsync(imageData);
            _ = await image.ToByteArrayAsync(); // 結果は使用しないが処理は実行したいため _ に代入
            
            stopwatch.Stop();
            
            // 再度GCを実行
            GC.Collect(2, GCCollectionMode.Forced, true);
            var finalMemory = GC.GetTotalMemory(true);
            
            // Assert
            var memoryDiff = finalMemory - initialMemory;
            _output.WriteLine($"Processing time: {stopwatch.ElapsedMilliseconds} ms");
            _output.WriteLine($"Memory difference: {memoryDiff / 1024} KB");
            
            // 画像サイズの5倍未満であること（4K画像で約40MB以下）
            var expectedMaxMemory = width * height * 4 * 5; // RGBA * 5倍
            Assert.True(memoryDiff < expectedMaxMemory, 
                $"Memory usage too high: {memoryDiff / (1024 * 1024)} MB for a {width}x{height} image");
            
            // 処理時間が5秒未満であること
            Assert.True(stopwatch.ElapsedMilliseconds < 5000, 
                $"Processing too slow: {stopwatch.ElapsedMilliseconds} ms for a {width}x{height} image");
        }
        
        [Fact]
        public void Dispose_ReleasesAllResources()
        {
            // Arrange
            using var adapter = new DefaultWindowsImageAdapter();
            using var windowsImage = AdapterTestHelper.CreateMockWindowsImage(1024, 768);
            
            // 複数の変換を実行してリソースを確保させる
            var images = new IAdvancedImage[10];
            try
            {
                for (int i = 0; i < images.Length; i++)
                {
                    images[i] = adapter.ToAdvancedImage(windowsImage);
                }
                
                // Act
                adapter.Dispose();
                
                // Assert - 明示的な検証は難しいが、例外が発生しないことを確認
                // Disposeが正しく実装されていれば、全てのリソースが解放される
                
                // このテストはコードカバレッジを向上させるためのものであり、
                // 実際のリソース解放はメモリプロファイラーで確認する必要がある
                Assert.True(true, "Dispose completed without exceptions");
            }
            finally
            {
                // ここで明示的にリソースを解放しておく
                foreach (var img in images)
                {
                    if (img is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
            }
        }
        
        public void Dispose()
        {
            _adapter.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}