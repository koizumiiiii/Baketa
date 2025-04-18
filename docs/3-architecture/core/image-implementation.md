# イメージ実装ガイド

*最終更新: 2025年4月18日*

## 1. 概要

このドキュメントでは、Baketaプロジェクトのイメージ抽象化レイヤーの具体的な実装方法について説明します。イメージ抽象化インターフェースを実装するクラスの設計、Windowsプラットフォーム固有の実装、および一般的なパフォーマンス最適化テクニックについて詳述します。

## 2. 実装アプローチ

イメージ抽象化レイヤーの実装には、以下の2つの主要なアプローチがあります：

1. **プラットフォーム中立実装**: `Baketa.Core.Services.Imaging` 名前空間内の基本実装
2. **Windows最適化実装**: `Baketa.Infrastructure.Platform.Windows.Imaging` 名前空間内のWindows最適化実装

### 2.1 実装選択の指針

以下の指針に従って実装アプローチを選択します：

- **パフォーマンスが重要な場合**: Windows最適化実装を使用
- **クロスプラットフォーム互換性が必要な場合**: プラットフォーム中立実装を使用（将来的な拡張用）
- **テスト容易性が重要な場合**: プラットフォーム中立実装またはモック実装を使用

## 3. プラットフォーム中立実装

### 3.1 CoreImage クラス

`CoreImage` クラスは、`IImage` インターフェースの基本実装です：

```csharp
namespace Baketa.Core.Services.Imaging
{
    /// <summary>
    /// IImageの基本実装
    /// </summary>
    public class CoreImage : IImage
    {
        private readonly byte[] _pixelData;
        private readonly int _stride;
        private readonly int _width;
        private readonly int _height;
        private readonly ImageFormat _format;
        private bool _disposed;
        
        public CoreImage(byte[] pixelData, int width, int height, int stride, ImageFormat format)
        {
            _pixelData = pixelData ?? throw new ArgumentNullException(nameof(pixelData));
            _width = width > 0 ? width : throw new ArgumentOutOfRangeException(nameof(width));
            _height = height > 0 ? height : throw new ArgumentOutOfRangeException(nameof(height));
            _stride = stride > 0 ? stride : width * GetBytesPerPixel(format);
            _format = format;
        }
        
        public int Width => _width;
        
        public int Height => _height;
        
        public ImageFormat Format => _format;
        
        public Task<byte[]> ToByteArrayAsync()
        {
            ThrowIfDisposed();
            return Task.FromResult(_pixelData.ToArray());
        }
        
        public IImage Clone()
        {
            ThrowIfDisposed();
            var clonedData = new byte[_pixelData.Length];
            Array.Copy(_pixelData, clonedData, _pixelData.Length);
            return new CoreImage(clonedData, _width, _height, _stride, _format);
        }
        
        public async Task<IImage> ResizeAsync(int width, int height)
        {
            ThrowIfDisposed();
            
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
                
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height));
                
            // リサイズの実装
            // 簡易版: バイリニア補間
            int bytesPerPixel = GetBytesPerPixel(_format);
            int newStride = width * bytesPerPixel;
            byte[] newData = new byte[height * newStride];
            
            await Task.Run(() =>
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        // 元画像の対応座標を計算
                        float srcX = x * _width / (float)width;
                        float srcY = y * _height / (float)height;
                        
                        // バイリニア補間
                        int x1 = (int)srcX;
                        int y1 = (int)srcY;
                        int x2 = Math.Min(x1 + 1, _width - 1);
                        int y2 = Math.Min(y1 + 1, _height - 1);
                        
                        float xFraction = srcX - x1;
                        float yFraction = srcY - y1;
                        
                        for (int c = 0; c < bytesPerPixel; c++)
                        {
                            // 4つの近傍ピクセルを取得
                            byte p11 = _pixelData[y1 * _stride + x1 * bytesPerPixel + c];
                            byte p12 = _pixelData[y1 * _stride + x2 * bytesPerPixel + c];
                            byte p21 = _pixelData[y2 * _stride + x1 * bytesPerPixel + c];
                            byte p22 = _pixelData[y2 * _stride + x2 * bytesPerPixel + c];
                            
                            // バイリニア補間
                            float top = p11 * (1 - xFraction) + p12 * xFraction;
                            float bottom = p21 * (1 - xFraction) + p22 * xFraction;
                            byte result = (byte)(top * (1 - yFraction) + bottom * yFraction);
                            
                            // 結果を書き込み
                            newData[y * newStride + x * bytesPerPixel + c] = result;
                        }
                    }
                }
            });
            
            return new CoreImage(newData, width, height, newStride, _format);
        }
        
        public async Task SaveAsync(string path, ImageFormat? format = null)
        {
            ThrowIfDisposed();
            
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("パスが無効です", nameof(path));
                
            // 実際の実装では、保存先フォーマットに基づいてエンコードが必要
            // この実装は簡略化されています
            await File.WriteAllBytesAsync(path, _pixelData);
        }
        
        public async Task<IImage> CropAsync(Rectangle rectangle)
        {
            ThrowIfDisposed();
            
            if (rectangle.X < 0 || rectangle.Y < 0 || 
                rectangle.X + rectangle.Width > _width || 
                rectangle.Y + rectangle.Height > _height ||
                rectangle.Width <= 0 || rectangle.Height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(rectangle), "無効な切り取り領域です");
            }
            
            int bytesPerPixel = GetBytesPerPixel(_format);
            int newStride = rectangle.Width * bytesPerPixel;
            byte[] newData = new byte[rectangle.Height * newStride];
            
            await Task.Run(() =>
            {
                for (int y = 0; y < rectangle.Height; y++)
                {
                    for (int x = 0; x < rectangle.Width; x++)
                    {
                        int srcX = rectangle.X + x;
                        int srcY = rectangle.Y + y;
                        
                        for (int c = 0; c < bytesPerPixel; c++)
                        {
                            newData[y * newStride + x * bytesPerPixel + c] = 
                                _pixelData[srcY * _stride + srcX * bytesPerPixel + c];
                        }
                    }
                }
            });
            
            return new CoreImage(newData, rectangle.Width, rectangle.Height, newStride, _format);
        }
        
        public async Task<byte[]> GetPixelsAsync(int x, int y, int width, int height)
        {
            ThrowIfDisposed();
            
            if (x < 0 || y < 0 || 
                x + width > _width || 
                y + height > _height ||
                width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    $"無効な領域 ({x}, {y}, {width}, {height})");
            }
            
            int bytesPerPixel = GetBytesPerPixel(_format);
            int newStride = width * bytesPerPixel;
            byte[] pixelData = new byte[height * newStride];
            
            await Task.Run(() =>
            {
                for (int row = 0; row < height; row++)
                {
                    int srcOffset = (y + row) * _stride + x * bytesPerPixel;
                    int destOffset = row * newStride;
                    int rowBytes = width * bytesPerPixel;
                    
                    Array.Copy(_pixelData, srcOffset, pixelData, destOffset, rowBytes);
                }
            });
            
            return pixelData;
        }
        
        protected void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CoreImage));
        }
        
        protected int GetBytesPerPixel(ImageFormat format)
        {
            return format switch
            {
                ImageFormat.Rgb24 => 3,
                ImageFormat.Rgba32 => 4,
                ImageFormat.Grayscale8 => 1,
                _ => throw new NotSupportedException($"サポートされていないフォーマットです: {format}")
            };
        }
        
        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}
```

### 3.2 CoreAdvancedImage クラス

`CoreAdvancedImage` クラスは、`IAdvancedImage` インターフェースを実装し、高度な画像処理機能を提供します：

```csharp
namespace Baketa.Core.Services.Imaging
{
    /// <summary>
    /// IAdvancedImageの基本実装
    /// </summary>
    public class CoreAdvancedImage : CoreImage, IAdvancedImage
    {
        public CoreAdvancedImage(byte[] pixelData, int width, int height, int stride, ImageFormat format)
            : base(pixelData, width, height, stride, format)
        {
        }
        
        public async Task<IImage> ApplyFilterAsync(IImageFilter filter)
        {
            ThrowIfDisposed();
            
            if (filter == null)
                throw new ArgumentNullException(nameof(filter));
                
            // 画像データを取得
            byte[] pixelData = await GetPixelsAsync(0, 0, Width, Height);
            int bytesPerPixel = GetBytesPerPixel(Format);
            int stride = Width * bytesPerPixel;
            
            // フィルターを適用
            byte[] resultData = filter.Apply(pixelData, Width, Height, stride);
            
            // 新しい画像を作成して返す
            return new CoreAdvancedImage(resultData, Width, Height, stride, Format);
        }
        
        public async Task<float> CalculateSimilarityAsync(IImage other)
        {
            ThrowIfDisposed();
            
            if (other == null)
                throw new ArgumentNullException(nameof(other));
                
            // 両方の画像が同じサイズでない場合はリサイズ
            IImage normalizedOther = other;
            if (other.Width != Width || other.Height != Height)
            {
                normalizedOther = await other.ResizeAsync(Width, Height);
            }
            
            try
            {
                // 両方の画像のピクセルデータを取得
                byte[] thisData = await GetPixelsAsync(0, 0, Width, Height);
                byte[] otherData = await normalizedOther.GetPixelsAsync(0, 0, Width, Height);
                
                int bytesPerPixel = GetBytesPerPixel(Format);
                int pixelCount = Width * Height;
                
                // ピクセル単位で比較
                double totalDifference = 0;
                
                await Task.Run(() =>
                {
                    for (int i = 0; i < thisData.Length; i++)
                    {
                        int diff = thisData[i] - otherData[i];
                        totalDifference += diff * diff;
                    }
                    
                    // 正規化（0.0〜1.0の範囲に変換）
                    totalDifference = Math.Sqrt(totalDifference / (pixelCount * bytesPerPixel * 255.0 * 255.0));
                });
                
                // 差の逆数を類似度として返す（1.0が完全一致）
                return (float)(1.0 - totalDifference);
            }
            finally
            {
                // リサイズした場合はリソースを解放
                if (!ReferenceEquals(other, normalizedOther))
                {
                    normalizedOther.Dispose();
                }
            }
        }
        
        public async Task<float> EvaluateTextProbabilityAsync(Rectangle rectangle)
        {
            ThrowIfDisposed();
            
            if (rectangle.X < 0 || rectangle.Y < 0 || 
                rectangle.X + rectangle.Width > Width || 
                rectangle.Y + rectangle.Height > Height ||
                rectangle.Width <= 0 || rectangle.Height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(rectangle));
            }
            
            // 指定領域を切り出し
            using IImage regionImage = await CropAsync(rectangle);
            
            // グレースケール変換
            using IImage grayImage = await ConvertToGrayscaleAsync(regionImage);
            
            // エッジ検出と変動分析
            byte[] pixelData = await grayImage.GetPixelsAsync(0, 0, grayImage.Width, grayImage.Height);
            
            float textProbability = 0.0f;
            
            await Task.Run(() =>
            {
                // エッジ検出（簡易Sobelフィルタ）
                int[] edgeMap = DetectEdges(pixelData, grayImage.Width, grayImage.Height);
                
                // テキスト特徴量の計算
                textProbability = CalculateTextFeatures(edgeMap, grayImage.Width, grayImage.Height);
            });
            
            return textProbability;
        }
        
        public async Task<IImage> EnhanceAsync(ImageEnhancementOptions options)
        {
            ThrowIfDisposed();
            
            if (options == null)
                throw new ArgumentNullException(nameof(options));
                
            // 画像データを取得
            byte[] pixelData = await GetPixelsAsync(0, 0, Width, Height);
            int bytesPerPixel = GetBytesPerPixel(Format);
            int stride = Width * Height * bytesPerPixel;
            
            // 強調処理の適用
            byte[] resultData = await Task.Run(() => ApplyEnhancements(pixelData, Width, Height, bytesPerPixel, stride, options));
            
            // 新しい画像を作成して返す
            return new CoreAdvancedImage(resultData, Width, Height, stride, Format);
        }
        
        public async Task<List<Rectangle>> DetectTextRegionsAsync()
        {
            ThrowIfDisposed();
            
            // テキスト領域検出（MSER+フィルタリングなど複雑なアルゴリズムが必要）
            // この実装は簡略化されています
            
            List<Rectangle> textRegions = new List<Rectangle>();
            
            // 画像をグレースケールに変換
            using IImage grayImage = await ConvertToGrayscaleAsync(this);
            
            // テキスト領域検出ロジック
            // ...
            
            // ダミーの結果を返す
            textRegions.Add(new Rectangle(10, 10, 100, 30));
            textRegions.Add(new Rectangle(10, 50, 150, 30));
            
            return textRegions;
        }
        
        // ヘルパーメソッド
        
        private async Task<IImage> ConvertToGrayscaleAsync(IImage image)
        {
            int width = image.Width;
            int height = image.Height;
            
            byte[] sourceData = await image.GetPixelsAsync(0, 0, width, height);
            int sourceBytesPerPixel = GetBytesPerPixel(image.Format);
            
            byte[] grayData = new byte[width * height];
            
            await Task.Run(() =>
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int sourceOffset = (y * width + x) * sourceBytesPerPixel;
                        int grayOffset = y * width + x;
                        
                        // RGB→グレースケール変換（輝度変換）
                        byte r = sourceData[sourceOffset];
                        byte g = sourceData[sourceOffset + 1];
                        byte b = sourceData[sourceOffset + 2];
                        
                        // 変換式: Y = 0.299*R + 0.587*G + 0.114*B
                        grayData[grayOffset] = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
                    }
                }
            });
            
            return new CoreImage(grayData, width, height, width, ImageFormat.Grayscale8);
        }
        
        private int[] DetectEdges(byte[] grayData, int width, int height)
        {
            // 簡易Sobelフィルタによるエッジ検出
            int[] edges = new int[width * height];
            
            // 各ピクセルについてSobelフィルタを適用
            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    // 横方向勾配（簡易版）
                    int gx = 
                        -grayData[(y - 1) * width + (x - 1)] +
                        grayData[(y - 1) * width + (x + 1)] +
                        -2 * grayData[y * width + (x - 1)] +
                        2 * grayData[y * width + (x + 1)] +
                        -grayData[(y + 1) * width + (x - 1)] +
                        grayData[(y + 1) * width + (x + 1)];
                    
                    // 縦方向勾配（簡易版）
                    int gy = 
                        -grayData[(y - 1) * width + (x - 1)] +
                        -2 * grayData[(y - 1) * width + x] +
                        -grayData[(y - 1) * width + (x + 1)] +
                        grayData[(y + 1) * width + (x - 1)] +
                        2 * grayData[(y + 1) * width + x] +
                        grayData[(y + 1) * width + (x + 1)];
                    
                    // 勾配の大きさ
                    edges[y * width + x] = (int)Math.Sqrt(gx * gx + gy * gy);
                }
            }
            
            return edges;
        }
        
        private float CalculateTextFeatures(int[] edgeMap, int width, int height)
        {
            // エッジの統計情報からテキスト確率を計算
            int edgeCount = 0;
            int strongEdgeCount = 0;
            int totalEdge = 0;
            
            // エッジを分析
            for (int i = 0; i < edgeMap.Length; i++)
            {
                int edge = edgeMap[i];
                totalEdge += edge;
                
                if (edge > 30) edgeCount++;
                if (edge > 100) strongEdgeCount++;
            }
            
            // 画像全体に対するエッジの割合
            float edgeRatio = (float)edgeCount / edgeMap.Length;
            float strongEdgeRatio = (float)strongEdgeCount / edgeMap.Length;
            float avgEdge = (float)totalEdge / edgeMap.Length;
            
            // 水平/垂直変化分析
            // ...
            
            // テキスト領域は中程度のエッジ密度を持つ傾向がある
            float textLikelihood = 0.0f;
            
            // エッジ割合が一定範囲内（テキストらしさ）
            if (edgeRatio > 0.05f && edgeRatio < 0.4f)
            {
                textLikelihood = 0.5f + edgeRatio;
            }
            
            // 強いエッジも適度にある場合加点
            if (strongEdgeRatio > 0.01f && strongEdgeRatio < 0.1f)
            {
                textLikelihood += 0.3f;
            }
            
            // 正規化（0.0〜1.0の範囲に収める）
            return Math.Min(1.0f, textLikelihood);
        }
        
        private byte[] ApplyEnhancements(byte[] sourceData, int width, int height, int bytesPerPixel, int stride, ImageEnhancementOptions options)
        {
            byte[] resultData = new byte[sourceData.Length];
            
            // 明るさとコントラスト調整
            for (int i = 0; i < sourceData.Length; i++)
            {
                // -1.0〜1.0の明るさを適用
                float value = sourceData[i] / 255.0f;
                
                // 明るさ調整
                if (options.Brightness != 0.0f)
                {
                    value += options.Brightness;
                }
                
                // コントラスト調整
                if (Math.Abs(options.Contrast - 1.0f) > 0.001f)
                {
                    value = (value - 0.5f) * options.Contrast + 0.5f;
                }
                
                // 0.0〜1.0の範囲にクリップ
                value = Math.Clamp(value, 0.0f, 1.0f);
                
                // バイト値に戻す
                resultData[i] = (byte)(value * 255);
            }
            
            // その他の強調処理
            
            // シャープネス強調
            if (options.Sharpness > 0.0f)
            {
                ApplySharpen(resultData, width, height, bytesPerPixel, stride, options.Sharpness);
            }
            
            // 二値化処理
            if (options.BinarizationThreshold > 0)
            {
                ApplyBinarization(resultData, width, height, bytesPerPixel, options.BinarizationThreshold);
            }
            
            return resultData;
        }
        
        private void ApplySharpen(byte[] data, int width, int height, int bytesPerPixel, int stride, float amount)
        {
            // シャープニングフィルタ（簡易版）
            // ...
        }
        
        private void ApplyBinarization(byte[] data, int width, int height, int bytesPerPixel, int threshold)
        {
            // 二値化処理（簡易版）
            // ...
        }
    }
}
```

### 3.3 イメージファクトリの実装

`CoreImageFactory` クラスは、プラットフォーム中立の方法で画像を作成します：

```csharp
namespace Baketa.Core.Services.Imaging
{
    /// <summary>
    /// プラットフォーム中立のイメージファクトリ実装
    /// </summary>
    public class CoreImageFactory : IImageFactory
    {
        private readonly IOcrImageProcessor _ocrImageProcessor;
        
        public CoreImageFactory(IOcrImageProcessor ocrImageProcessor = null)
        {
            _ocrImageProcessor = ocrImageProcessor;
        }
        
        public async Task<IImage> CreateFromFileAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("ファイルパスが空です", nameof(filePath));
                
            if (!File.Exists(filePath))
                throw new FileNotFoundException("ファイルが見つかりません", filePath);
                
            // ファイルからの読み込み（プラットフォームに依存するため、実際はアダプターが必要）
            try
            {
                byte[] fileData = await File.ReadAllBytesAsync(filePath);
                return await CreateFromBytesAsync(fileData);
            }
            catch (Exception ex)
            {
                throw new ImageProcessingException($"画像ファイルの読み込みに失敗しました: {filePath}", ex);
            }
        }
        
        public Task<IImage> CreateFromBytesAsync(byte[] data)
        {
            if (data == null || data.Length == 0)
                throw new ArgumentException("無効な画像データです", nameof(data));
                
            // この単純な実装では、RGB24形式で固定サイズの画像を仮作成
            // 実際の実装では、画像フォーマットのデコードが必要
            
            try
            {
                // このダミー実装では、固定サイズの画像を作成
                int width = 100;
                int height = 100;
                int bytesPerPixel = 3; // RGB24
                int stride = width * bytesPerPixel;
                
                // このデータは実際にはデコードされたピクセルデータに置き換え
                byte[] pixelData = new byte[height * stride];
                
                return Task.FromResult<IImage>(
                    new CoreAdvancedImage(pixelData, width, height, stride, ImageFormat.Rgb24));
            }
            catch (Exception ex)
            {
                throw new ImageProcessingException("画像データの処理に失敗しました", ex);
            }
        }
        
        public Task<IImage> CreateEmptyAsync(int width, int height, ImageFormat format = ImageFormat.Rgb24)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "幅は正の値である必要があります");
                
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height), "高さは正の値である必要があります");
                
            int bytesPerPixel = GetBytesPerPixel(format);
            int stride = width * bytesPerPixel;
            byte[] pixelData = new byte[height * stride];
            
            return Task.FromResult<IImage>(
                new CoreAdvancedImage(pixelData, width, height, stride, format));
        }
        
        public IImage CreateFromPlatformImage(object platformImage)
        {
            if (platformImage == null)
                throw new ArgumentNullException(nameof(platformImage));
                
            throw new NotImplementedException("この基本実装ではサポートされていません。プラットフォーム固有の実装を使用してください。");
        }
        
        private int GetBytesPerPixel(ImageFormat format)
        {
            return format switch
            {
                ImageFormat.Rgb24 => 3,
                ImageFormat.Rgba32 => 4,
                ImageFormat.Grayscale8 => 1,
                _ => throw new NotSupportedException($"サポートされていないフォーマットです: {format}")
            };
        }
    }
}
```

## 4. Windows最適化実装

### 4.1 Windows固有のイメージクラス

`WindowsImage` クラスは、Windows GDI+の `Bitmap` クラスをラップして最適化されたパフォーマンスを提供します：

```csharp
namespace Baketa.Infrastructure.Platform.Windows.Imaging
{
    /// <summary>
    /// Windows固有の画像実装
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class WindowsImage : IWindowsImage
    {
        private readonly Bitmap _bitmap;
        private bool _disposed;
        
        public WindowsImage(Bitmap bitmap)
        {
            _bitmap = bitmap ?? throw new ArgumentNullException(nameof(bitmap));
        }
        
        public int Width => _bitmap.Width;
        
        public int Height => _bitmap.Height;
        
        public Bitmap GetNativeImage() => _bitmap;
        
        public Task SaveAsync(string path)
        {
            ThrowIfDisposed();
            
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("無効なパスです", nameof(path));
                
            try
            {
                // 拡張子に基づいてフォーマットを決定
                var extension = Path.GetExtension(path).ToLowerInvariant();
                var format = extension switch
                {
                    ".png" => System.Drawing.Imaging.ImageFormat.Png,
                    ".jpg" or ".jpeg" => System.Drawing.Imaging.ImageFormat.Jpeg,
                    ".bmp" => System.Drawing.Imaging.ImageFormat.Bmp,
                    ".gif" => System.Drawing.Imaging.ImageFormat.Gif,
                    _ => System.Drawing.Imaging.ImageFormat.Png
                };
                
                // 保存
                _bitmap.Save(path, format);
                
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                throw new IOException($"画像の保存に失敗しました: {path}", ex);
            }
        }
        
        protected void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(WindowsImage));
        }
        
        public void Dispose()
        {
            if (!_disposed)
            {
                _bitmap.Dispose();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}
```

### 4.2 Windows固有のイメージファクトリ

`WindowsImageFactory` クラスは、Windows GDI+を使用して効率的に画像を作成します：

```csharp
namespace Baketa.Infrastructure.Platform.Windows.Imaging
{
    /// <summary>
    /// Windows固有のイメージファクトリ
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class WindowsImageFactory : IWindowsImageFactory
    {
        public Task<IWindowsImage> CreateFromFileAsync(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("パスが無効です", nameof(path));
                
            if (!File.Exists(path))
                throw new FileNotFoundException("ファイルが見つかりません", path);
                
            try
            {
                var bitmap = new Bitmap(path);
                return Task.FromResult<IWindowsImage>(new WindowsImage(bitmap));
            }
            catch (Exception ex)
            {
                throw new ImageProcessingException($"画像ファイルの読み込みに失敗しました: {path}", ex);
            }
        }
        
        public Task<IWindowsImage> CreateFromBytesAsync(byte[] data)
        {
            if (data == null || data.Length == 0)
                throw new ArgumentException("無効な画像データです", nameof(data));
                
            try
            {
                using var stream = new MemoryStream(data);
                var bitmap = new Bitmap(stream);
                return Task.FromResult<IWindowsImage>(new WindowsImage(bitmap));
            }
            catch (Exception ex)
            {
                throw new ImageProcessingException("画像データの処理に失敗しました", ex);
            }
        }
        
        public Task<IWindowsImage> CreateEmptyAsync(int width, int height)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
                
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height));
                
            try
            {
                var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.Transparent);
                }
                
                return Task.FromResult<IWindowsImage>(new WindowsImage(bitmap));
            }
            catch (Exception ex)
            {
                throw new ImageProcessingException("空の画像の作成に失敗しました", ex);
            }
        }
        
        public IWindowsImage CreateFromNativeBitmap(Bitmap bitmap)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));
                
            return new WindowsImage(bitmap);
        }
    }
}
```

### 4.3 Windows高度な画像処理

Windows固有の高度な画像処理クラスの実装例：

```csharp
namespace Baketa.Infrastructure.Platform.Windows.Imaging
{
    /// <summary>
    /// Windows固有の高度な画像処理実装
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class WindowsAdvancedImage : IWindowsAdvancedImage
    {
        private readonly WindowsImage _windowsImage;
        
        public WindowsAdvancedImage(WindowsImage windowsImage)
        {
            _windowsImage = windowsImage ?? throw new ArgumentNullException(nameof(windowsImage));
        }
        
        public int Width => _windowsImage.Width;
        
        public int Height => _windowsImage.Height;
        
        public Bitmap GetNativeImage() => _windowsImage.GetNativeImage();
        
        public Task SaveAsync(string path) => _windowsImage.SaveAsync(path);
        
        public async Task<IWindowsImage> ApplyFilterAsync(IImageFilter filter)
        {
            if (filter == null)
                throw new ArgumentNullException(nameof(filter));
                
            var bitmap = GetNativeImage();
            var data = GetBitmapData(bitmap);
            
            try
            {
                // フィルターの適用
                byte[] resultData = await Task.Run(() => 
                    filter.Apply(data, bitmap.Width, bitmap.Height, GetStride(bitmap)));
                
                // 新しいビットマップを作成
                Bitmap resultBitmap = CreateBitmapFromData(resultData, bitmap.Width, bitmap.Height);
                
                return new WindowsImage(resultBitmap);
            }
            catch (Exception ex)
            {
                throw new ImageProcessingException("フィルター適用に失敗しました", ex);
            }
        }
        
        public async Task<IWindowsImage> EnhanceAsync(ImageEnhancementOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));
                
            var bitmap = GetNativeImage();
            
            try
            {
                // OpenCVを使用した高速画像処理の例
                // 実際の実装ではWindowsの最適なAPIを使用
                
                // 例: シャープネス強調
                if (options.Sharpness > 0.0f)
                {
                    bitmap = await ApplySharpenFilterAsync(bitmap, options.Sharpness);
                }
                
                // 例: コントラスト調整
                if (Math.Abs(options.Contrast - 1.0f) > 0.001f)
                {
                    bitmap = await AdjustContrastAsync(bitmap, options.Contrast);
                }
                
                // 例: 二値化処理
                if (options.BinarizationThreshold > 0)
                {
                    bitmap = await ApplyThresholdAsync(bitmap, options.BinarizationThreshold);
                }
                
                return new WindowsImage(bitmap);
            }
            catch (Exception ex)
            {
                throw new ImageProcessingException("画像強調に失敗しました", ex);
            }
        }
        
        public void Dispose()
        {
            _windowsImage.Dispose();
        }
        
        // ヘルパーメソッド
        
        private byte[] GetBitmapData(Bitmap bitmap)
        {
            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var bmpData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, bitmap.PixelFormat);
            
            try
            {
                int stride = bmpData.Stride;
                int dataSize = stride * bitmap.Height;
                byte[] data = new byte[dataSize];
                
                Marshal.Copy(bmpData.Scan0, data, 0, dataSize);
                
                return data;
            }
            finally
            {
                bitmap.UnlockBits(bmpData);
            }
        }
        
        private int GetStride(Bitmap bitmap)
        {
            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var bmpData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, bitmap.PixelFormat);
            
            try
            {
                return bmpData.Stride;
            }
            finally
            {
                bitmap.UnlockBits(bmpData);
            }
        }
        
        private Bitmap CreateBitmapFromData(byte[] data, int width, int height)
        {
            Bitmap bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var rect = new Rectangle(0, 0, width, height);
            var bmpData = bitmap.LockBits(rect, ImageLockMode.WriteOnly, bitmap.PixelFormat);
            
            try
            {
                Marshal.Copy(data, 0, bmpData.Scan0, data.Length);
                
                return bitmap;
            }
            finally
            {
                bitmap.UnlockBits(bmpData);
            }
        }
        
        private Task<Bitmap> ApplySharpenFilterAsync(Bitmap bitmap, float amount)
        {
            // シャープニングフィルタの実装
            // ...
            return Task.FromResult(bitmap);
        }
        
        private Task<Bitmap> AdjustContrastAsync(Bitmap bitmap, float contrast)
        {
            // コントラスト調整の実装
            // ...
            return Task.FromResult(bitmap);
        }
        
        private Task<Bitmap> ApplyThresholdAsync(Bitmap bitmap, int threshold)
        {
            // 二値化処理の実装
            // ...
            return Task.FromResult(bitmap);
        }
    }
}
```

## 5. アダプターの実装

### 5.1 WindowsImageAdapter

`WindowsImageAdapter` クラスは、Windows画像をコアの `IImage` インターフェースに適応させます：

```csharp
namespace Baketa.Infrastructure.Platform.Adapters
{
    /// <summary>
    /// Windows画像をIImageインターフェースに適応させるアダプター
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class WindowsImageAdapter : IImage
    {
        private readonly IWindowsImage _windowsImage;
        private bool _disposed;
        
        public WindowsImageAdapter(IWindowsImage windowsImage)
        {
            _windowsImage = windowsImage ?? throw new ArgumentNullException(nameof(windowsImage));
        }
        
        public int Width => _windowsImage.Width;
        
        public int Height => _windowsImage.Height;
        
        public ImageFormat Format => ImageFormat.Rgba32; // WindowsのBitmapはRGBA32がデフォルト
        
        public Task<byte[]> ToByteArrayAsync()
        {
            ThrowIfDisposed();
            
            var bitmap = _windowsImage.GetNativeImage();
            using var stream = new MemoryStream();
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            return Task.FromResult(stream.ToArray());
        }
        
        public IImage Clone()
        {
            ThrowIfDisposed();
            
            var bitmap = _windowsImage.GetNativeImage();
            var clonedBitmap = new Bitmap(bitmap);
            var clonedWindowsImage = new WindowsImage(clonedBitmap);
            
            return new WindowsImageAdapter(clonedWindowsImage);
        }
        
        public async Task<IImage> ResizeAsync(int width, int height)
        {
            ThrowIfDisposed();
            
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
                
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height));
                
            var bitmap = _windowsImage.GetNativeImage();
            var resized = new Bitmap(width, height);
            
            using (var g = Graphics.FromImage(resized))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(bitmap, 0, 0, width, height);
            }
            
            var resizedWindowsImage = new WindowsImage(resized);
            return new WindowsImageAdapter(resizedWindowsImage);
        }
        
        public Task SaveAsync(string path, ImageFormat? format = null)
        {
            ThrowIfDisposed();
            
            // Windowsネイティブの保存メソッドに委譲
            return _windowsImage.SaveAsync(path);
        }
        
        public async Task<IImage> CropAsync(Rectangle rectangle)
        {
            ThrowIfDisposed();
            
            if (rectangle.X < 0 || rectangle.Y < 0 || 
                rectangle.X + rectangle.Width > Width || 
                rectangle.Y + rectangle.Height > Height ||
                rectangle.Width <= 0 || rectangle.Height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(rectangle));
            }
            
            var bitmap = _windowsImage.GetNativeImage();
            var cropped = new Bitmap(rectangle.Width, rectangle.Height);
            
            using (var g = Graphics.FromImage(cropped))
            {
                var srcRect = new System.Drawing.Rectangle(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
                g.DrawImage(bitmap, 0, 0, srcRect, GraphicsUnit.Pixel);
            }
            
            var croppedWindowsImage = new WindowsImage(cropped);
            return new WindowsImageAdapter(croppedWindowsImage);
        }
        
        public async Task<byte[]> GetPixelsAsync(int x, int y, int width, int height)
        {
            ThrowIfDisposed();
            
            if (x < 0 || y < 0 || 
                x + width > Width || 
                y + height > Height ||
                width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    $"無効な領域 ({x}, {y}, {width}, {height})");
            }
            
            var bitmap = _windowsImage.GetNativeImage();
            var rect = new System.Drawing.Rectangle(x, y, width, height);
            
            // 指定領域のデータを取得
            var bmpData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, bitmap.PixelFormat);
            
            try
            {
                int stride = bmpData.Stride;
                int bytes = stride * height;
                byte[] pixelData = new byte[bytes];
                
                Marshal.Copy(bmpData.Scan0, pixelData, 0, bytes);
                
                return pixelData;
            }
            finally
            {
                bitmap.UnlockBits(bmpData);
            }
        }
        
        protected void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(WindowsImageAdapter));
        }
        
        public void Dispose()
        {
            if (!_disposed)
            {
                _windowsImage.Dispose();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}
```

### 5.2 WindowsAdvancedImageAdapter

`WindowsAdvancedImageAdapter` クラスは、Windows画像をコアの `IAdvancedImage` インターフェースに適応させます：

```csharp
namespace Baketa.Infrastructure.Platform.Adapters
{
    /// <summary>
    /// Windows画像をIAdvancedImageインターフェースに適応させるアダプター
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class WindowsAdvancedImageAdapter : WindowsImageAdapter, IAdvancedImage
    {
        private readonly IWindowsAdvancedImage _windowsAdvancedImage;
        
        public WindowsAdvancedImageAdapter(IWindowsAdvancedImage windowsAdvancedImage)
            : base(windowsAdvancedImage)
        {
            _windowsAdvancedImage = windowsAdvancedImage;
        }
        
        public async Task<IImage> ApplyFilterAsync(IImageFilter filter)
        {
            ThrowIfDisposed();
            
            if (filter == null)
                throw new ArgumentNullException(nameof(filter));
                
            var result = await _windowsAdvancedImage.ApplyFilterAsync(filter);
            return new WindowsImageAdapter(result);
        }
        
        public async Task<float> CalculateSimilarityAsync(IImage other)
        {
            ThrowIfDisposed();
            
            if (other == null)
                throw new ArgumentNullException(nameof(other));
                
            // 他の実装クラスからWindowsImageを取得
            IWindowsImage otherWindowsImage;
            
            if (other is WindowsImageAdapter adapter)
            {
                otherWindowsImage = adapter.GetWindowsImage();
            }
            else
            {
                // 異なる実装の場合、変換が必要
                byte[] otherData = await other.ToByteArrayAsync();
                
                using var stream = new MemoryStream(otherData);
                var bitmap = new Bitmap(stream);
                otherWindowsImage = new WindowsImage(bitmap);
            }
            
            try
            {
                return await CalculateSimilarityInternalAsync(_windowsAdvancedImage, otherWindowsImage);
            }
            finally
            {
                // 変換した場合はリソースを解放
                if (!(other is WindowsImageAdapter))
                {
                    otherWindowsImage.Dispose();
                }
            }
        }
        
        public async Task<float> EvaluateTextProbabilityAsync(Rectangle rectangle)
        {
            ThrowIfDisposed();
            
            if (rectangle.X < 0 || rectangle.Y < 0 || 
                rectangle.X + rectangle.Width > Width || 
                rectangle.Y + rectangle.Height > Height ||
                rectangle.Width <= 0 || rectangle.Height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(rectangle));
            }
            
            // Windows固有の最適な実装
            // ...
            
            // 仮実装
            return 0.5f;
        }
        
        public async Task<IImage> EnhanceAsync(ImageEnhancementOptions options)
        {
            ThrowIfDisposed();
            
            if (options == null)
                throw new ArgumentNullException(nameof(options));
                
            var result = await _windowsAdvancedImage.EnhanceAsync(options);
            return new WindowsImageAdapter(result);
        }
        
        public async Task<List<Rectangle>> DetectTextRegionsAsync()
        {
            ThrowIfDisposed();
            
            // Windows固有のテキスト領域検出（OpenCV連携など）
            // ...
            
            // 仮実装
            return new List<Rectangle>
            {
                new Rectangle(10, 10, 100, 30),
                new Rectangle(10, 50, 150, 30)
            };
        }
        
        public IWindowsImage GetWindowsImage()
        {
            ThrowIfDisposed();
            return _windowsAdvancedImage;
        }
        
        // ヘルパーメソッド
        
        private async Task<float> CalculateSimilarityInternalAsync(IWindowsImage image1, IWindowsImage image2)
        {
            // Windows最適化された類似度計算
            // 実際の実装ではより効率的なアルゴリズムを使用
            
            // 仮実装
            return 0.95f;
        }
    }
}
```

### 5.3 WindowsImageAdapterFactory

`WindowsImageAdapterFactory` クラスは、Windows画像ファクトリをコアの `IImageFactory` インターフェースに適応させます：

```csharp
namespace Baketa.Infrastructure.Platform.Adapters
{
    /// <summary>
    /// WindowsのイメージファクトリをIImageFactoryに適応させるアダプター
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class WindowsImageAdapterFactory : IImageFactory
    {
        private readonly IWindowsImageFactory _windowsImageFactory;
        
        public WindowsImageAdapterFactory(IWindowsImageFactory windowsImageFactory)
        {
            _windowsImageFactory = windowsImageFactory ?? throw new ArgumentNullException(nameof(windowsImageFactory));
        }
        
        public async Task<IImage> CreateFromFileAsync(string filePath)
        {
            var windowsImage = await _windowsImageFactory.CreateFromFileAsync(filePath);
            return new WindowsImageAdapter(windowsImage);
        }
        
        public async Task<IImage> CreateFromBytesAsync(byte[] data)
        {
            var windowsImage = await _windowsImageFactory.CreateFromBytesAsync(data);
            return new WindowsImageAdapter(windowsImage);
        }
        
        public async Task<IImage> CreateEmptyAsync(int width, int height, ImageFormat format = ImageFormat.Rgb24)
        {
            var windowsImage = await _windowsImageFactory.CreateEmptyAsync(width, height);
            return new WindowsImageAdapter(windowsImage);
        }
        
        public IImage CreateFromPlatformImage(object platformImage)
        {
            if (platformImage == null)
                throw new ArgumentNullException(nameof(platformImage));
                
            if (platformImage is Bitmap bitmap)
            {
                var windowsImage = _windowsImageFactory.CreateFromNativeBitmap(bitmap);
                return new WindowsImageAdapter(windowsImage);
            }
            
            throw new ArgumentException($"サポートされていないプラットフォーム画像タイプです: {platformImage.GetType().Name}");
        }
    }
}
```

## 6. OCR関連の画像前処理実装

### 6.1 OpenCVベースのOCR前処理

OCR処理のための画像前処理機能を提供する実装例：

```csharp
namespace Baketa.Infrastructure.OCR.OpenCV
{
    /// <summary>
    /// OpenCVを使用したOCR画像前処理実装
    /// </summary>
    public class OpenCvOcrImageProcessor : IOcrImageProcessor
    {
        private readonly ILogger<OpenCvOcrImageProcessor> _logger;
        
        public OpenCvOcrImageProcessor(ILogger<OpenCvOcrImageProcessor> logger)
        {
            _logger = logger;
        }
        
        public async Task<IImage> OptimizeForOcrAsync(IImage image, OcrImageOptions options)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));
                
            if (options == null)
                throw new ArgumentNullException(nameof(options));
                
            // Windows固有の実装を取得（最適化のため）
            WindowsImage windowsImage = GetWindowsImage(image);
            
            try
            {
                // OpenCVを使用した画像最適化
                using var src = OpenCvSharp.Windows.BitmapConverter.ToMat(windowsImage.GetNativeImage());
                using var dest = new Mat();
                
                // グレースケール変換
                Cv2.CvtColor(src, dest, ColorConversionCodes.BGR2GRAY);
                
                // ノイズ除去
                if (options.NoiseReduction > 0.0f)
                {
                    int kernelSize = (int)(options.NoiseReduction * 10) * 2 + 1;
                    Cv2.GaussianBlur(dest, dest, new Size(kernelSize, kernelSize), 0);
                }
                
                // コントラスト強調
                if (Math.Abs(options.ContrastEnhancement - 1.0f) > 0.001f)
                {
                    dest.ConvertTo(dest, -1, options.ContrastEnhancement, 0);
                }
                
                // 二値化処理
                if (options.UseAdaptiveThreshold)
                {
                    Cv2.AdaptiveThreshold(
                        dest,
                        dest,
                        255,
                        AdaptiveThresholdType.GaussianC,
                        ThresholdType.Binary,
                        options.AdaptiveBlockSize,
                        5);
                }
                else if (options.BinarizationThreshold > 0)
                {
                    Cv2.Threshold(
                        dest,
                        dest,
                        options.BinarizationThreshold,
                        255,
                        ThresholdType.Binary);
                }
                
                // 膨張処理（テキスト強調）
                if (options.DilationPixels > 0)
                {
                    var element = Cv2.GetStructuringElement(
                        MorphShapes.Rect,
                        new Size(options.DilationPixels * 2 + 1, options.DilationPixels * 2 + 1));
                        
                    Cv2.Dilate(dest, dest, element);
                }
                
                // シャープネス強調
                if (options.SharpnessEnhancement > 0.0f)
                {
                    using var blurred = new Mat();
                    Cv2.GaussianBlur(dest, blurred, new Size(0, 0), 3);
                    Cv2.AddWeighted(
                        dest,
                        1.0 + options.SharpnessEnhancement,
                        blurred,
                        -options.SharpnessEnhancement,
                        0,
                        dest);
                }
                
                // OpenCV Matを新しいImageオブジェクトに変換
                var resultBitmap = OpenCvSharp.Windows.BitmapConverter.ToBitmap(dest);
                var resultWindowsImage = new WindowsImage(resultBitmap);
                
                return new WindowsImageAdapter(resultWindowsImage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OCR画像最適化に失敗しました");
                throw new ImageProcessingException("OCR画像最適化に失敗しました", ex);
            }
        }
        
        public async Task<List<TextRegionCandidate>> DetectTextRegionsAsync(IImage image)
        {
            if (image == null)
                throw new ArgumentNullException(nameof(image));
                
            // Windows固有の実装を取得（最適化のため）
            WindowsImage windowsImage = GetWindowsImage(image);
            
            try
            {
                var candidates = new List<TextRegionCandidate>();
                
                // OpenCVを使用したテキスト領域検出（MSERベース）
                using var src = OpenCvSharp.Windows.BitmapConverter.ToMat(windowsImage.GetNativeImage());
                using var gray = new Mat();
                
                // グレースケール変換
                Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
                
                // MSERを使用してテキスト領域候補を検出
                using var mser = MSER.Create();
                mser.Delta = 5;
                mser.MinArea = 60;
                mser.MaxArea = 14400;
                
                mser.DetectRegions(
                    gray,
                    out Point[][] contours,
                    out Rect[] boundingBoxes);
                
                // 各領域を評価
                for (int i = 0; i < boundingBoxes.Length; i++)
                {
                    var box = boundingBoxes[i];
                    
                    // サイズフィルタリング
                    if (box.Width < 10 || box.Height < 5 || box.Width > 400 || box.Height > 100)
                        continue;
                        
                    // アスペクト比フィルタリング
                    float aspectRatio = (float)box.Width / box.Height;
                    if (aspectRatio < 0.1f || aspectRatio > 10.0f)
                        continue;
                        
                    // 信頼度評価
                    float confidence = EvaluateConfidence(gray, box);
                    
                    if (confidence > 0.3f)
                    {
                        candidates.Add(new TextRegionCandidate
                        {
                            Bounds = new Rectangle(box.X, box.Y, box.Width, box.Height),
                            Confidence = confidence,
                            Orientation = TextOrientation.Horizontal
                        });
                    }
                }
                
                // 信頼度でソート
                candidates.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));
                
                return candidates;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "テキスト領域検出に失敗しました");
                throw new ImageProcessingException("テキスト領域検出に失敗しました", ex);
            }
        }
        
        public async Task<List<Rectangle>> DetectTextChangesAsync(IImage previous, IImage current)
        {
            if (previous == null)
                throw new ArgumentNullException(nameof(previous));
                
            if (current == null)
                throw new ArgumentNullException(nameof(current));
                
            // Windows固有の実装を取得
            WindowsImage prevWindowsImage = GetWindowsImage(previous);
            WindowsImage currWindowsImage = GetWindowsImage(current);
            
            try
            {
                // 差分検出のロジック
                using var prevMat = OpenCvSharp.Windows.BitmapConverter.ToMat(prevWindowsImage.GetNativeImage());
                using var currMat = OpenCvSharp.Windows.BitmapConverter.ToMat(currWindowsImage.GetNativeImage());
                
                // グレースケール変換
                using var prevGray = new Mat();
                using var currGray = new Mat();
                Cv2.CvtColor(prevMat, prevGray, ColorConversionCodes.BGR2GRAY);
                Cv2.CvtColor(currMat, currGray, ColorConversionCodes.BGR2GRAY);
                
                // 差分検出
                using var diff = new Mat();
                Cv2.Absdiff(prevGray, currGray, diff);
                
                // 閾値処理で変化領域を二値化
                using var thresholded = new Mat();
                Cv2.Threshold(diff, thresholded, 30, 255, ThresholdType.Binary);
                
                // ノイズ除去のためのモルフォロジー演算
                var element = Cv2.GetStructuringElement(
                    MorphShapes.Rect,
                    new Size(3, 3));
                    
                Cv2.MorphologyEx(
                    thresholded,
                    thresholded,
                    MorphTypes.Close,
                    element);
                
                // 連結成分の検出
                using var labels = new Mat();
                using var stats = new Mat();
                using var centroids = new Mat();
                
                int numLabels = Cv2.ConnectedComponentsWithStats(
                    thresholded,
                    labels,
                    stats,
                    centroids);
                
                // 変化領域を矩形として取得
                var changedRegions = new List<Rectangle>();
                
                for (int i = 1; i < numLabels; i++)  // 0はバックグラウンド
                {
                    int left = stats.At<int>(i, (int)ConnectedComponentsTypes.Left);
                    int top = stats.At<int>(i, (int)ConnectedComponentsTypes.Top);
                    int width = stats.At<int>(i, (int)ConnectedComponentsTypes.Width);
                    int height = stats.At<int>(i, (int)ConnectedComponentsTypes.Height);
                    int area = stats.At<int>(i, (int)ConnectedComponentsTypes.Area);
                    
                    // 小さすぎる領域はノイズとして除外
                    if (area < 50 || width < 10 || height < 5)
                        continue;
                        
                    changedRegions.Add(new Rectangle(left, top, width, height));
                }
                
                return changedRegions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "テキスト変更検出に失敗しました");
                throw new ImageProcessingException("テキスト変更検出に失敗しました", ex);
            }
        }
        
        // ヘルパーメソッド
        
        private WindowsImage GetWindowsImage(IImage image)
        {
            // アダプターからWindowsImageを取得
            if (image is WindowsImageAdapter adapter)
            {
                return (WindowsImage)adapter.GetWindowsImage();
            }
            
            // 異なる実装の場合、変換が必要
            throw new ArgumentException("WindowsImageAdapter型のみサポートされています", nameof(image));
        }
        
        private float EvaluateConfidence(Mat grayImage, Rect box)
        {
            // 領域の特徴から信頼度を計算（簡略版）
            // 実際の実装ではより複雑な判定ロジックを使用
            
            return 0.7f; // ダミー値
        }
    }
}
```

## 7. 依存性注入の設定

### 7.1 サービス登録

イメージ関連サービスの依存性注入設定例：

```csharp
namespace Baketa.Application.DI.Modules
{
    /// <summary>
    /// イメージサービスモジュール
    /// </summary>
    public class ImageServicesModule : IServiceModule
    {
        public void RegisterServices(IServiceCollection services)
        {
            // Windows固有の実装
            services.AddSingleton<IWindowsImageFactory, WindowsImageFactory>();
            
            // アダプター
            services.AddSingleton<IImageFactory>(provider =>
            {
                var windowsFactory = provider.GetRequiredService<IWindowsImageFactory>();
                return new WindowsImageAdapterFactory(windowsFactory);
            });
            
            // OCR画像前処理
            services.AddSingleton<IOcrImageProcessor, OpenCvOcrImageProcessor>();
            
            // フィルター
            services.AddTransient<IImageFilter, ContrastFilter>();
            services.AddTransient<IImageFilter, BinarizationFilter>();
            services.AddTransient<IImageFilter, GaussianBlurFilter>();
        }
    }
}
```

### 7.2 アプリケーションでの使用例

アプリケーションコードでのイメージサービスの使用例：

```csharp
public class OcrService : IOcrService
{
    private readonly IImageFactory _imageFactory;
    private readonly IOcrImageProcessor _ocrImageProcessor;
    private readonly ILogger<OcrService> _logger;
    
    public OcrService(
        IImageFactory imageFactory,
        IOcrImageProcessor ocrImageProcessor,
        ILogger<OcrService> logger)
    {
        _imageFactory = imageFactory;
        _ocrImageProcessor = ocrImageProcessor;
        _logger = logger;
    }
    
    public async Task<OcrResult> RecognizeTextAsync(IImage image, OcrOptions options)
    {
        if (image == null)
            throw new ArgumentNullException(nameof(image));
            
        if (options == null)
            throw new ArgumentNullException(nameof(options));
            
        try
        {
            // OCR前の画像前処理
            using var optimizedImage = await _ocrImageProcessor.OptimizeForOcrAsync(
                image, options.ImageOptions);
                
            // OCR処理
            // ...
            
            return new OcrResult
            {
                // 結果設定
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OCR処理に失敗しました");
            throw new OcrException("OCR処理に失敗しました", ex);
        }
    }
}
```

## 8. パフォーマンス最適化

### 8.1 Windows GDI+の効率的な使用

Windows GDI+の機能を効率的に使用するためのガイドライン：

1. **LockBits/UnlockBits**: ピクセルデータへの直接アクセスには `LockBits` と `UnlockBits` を使用
2. **適切なPixelFormat**: 目的に適した `PixelFormat` を選択
3. **Dispose**: 不要になったリソースを迅速に解放
4. **Graphics設定**: `Graphics` オブジェクトの適切な設定（補間モードなど）

### 8.2 メモリ管理

メモリ管理の最適化ガイドライン：

1. **IDisposableの適切な実装**: リソースの確実な解放
2. **静的メンバーの適切な利用**: 複数のインスタンス間でリソースを共有
3. **ピクセルデータのコピー回数最小化**: 不要なコピーを避ける
4. **部分的な処理**: 可能な場合は画像全体ではなく部分的に処理

### 8.3 マルチスレッド処理

並列処理の最適化ガイドライン：

1. **大きな画像の並列処理**: `Parallel.For` による並列処理
2. **Task.Runの適切な使用**: UI応答性を確保するためのバックグラウンド処理
3. **スレッドプール**: リソース効率の良いスレッド管理
4. **同期コンテキスト**: UI更新時の適切な同期コンテキスト確保

## 9. トラブルシューティング

### 9.1 一般的な問題と解決策

| 問題 | 考えられる原因 | 解決策 |
|------|--------------|-------|
| メモリリーク | リソースが適切に解放されていない | `Dispose`パターンの正しい実装を確認 |
| パフォーマンス低下 | 不適切な画像処理方法 | LockBits/UnlockBits方式の採用 |
| OutOfMemoryException | 大きな画像の処理 | 処理前にサイズ確認とダウンサンプリングの検討 |
| NullReferenceException | アダプターの不適切な使用 | null値の処理を徹底し、防御的プログラミングを採用 |

### 9.2 問題分析のためのロギング

効果的なロギング戦略：

```csharp
// 画像処理操作のログ記録例
public async Task<IImage> ResizeAsync(int width, int height)
{
    ThrowIfDisposed();
    
    try
    {
        _logger.LogDebug(
            "画像リサイズを開始: 元サイズ={OrigWidth}x{OrigHeight}、新サイズ={NewWidth}x{NewHeight}",
            Width, Height, width, height);
            
        var stopwatch = Stopwatch.StartNew();
        
        // リサイズ処理
        var result = /* 実際の処理 */;
        
        stopwatch.Stop();
        
        _logger.LogDebug(
            "画像リサイズ完了: 所要時間={ElapsedMs}ms",
            stopwatch.ElapsedMilliseconds);
            
        return result;
    }
    catch (Exception ex)
    {
        _logger.LogError(
            ex,
            "画像リサイズに失敗: 元サイズ={OrigWidth}x{OrigHeight}、新サイズ={NewWidth}x{NewHeight}",
            Width, Height, width, height);
            
        throw;
    }
}
```

## 10. まとめ

イメージ抽象化レイヤーの実装は、Baketaアプリケーションの画像処理機能の中核を担います。適切な設計とパフォーマンス最適化により、効率的で拡張性の高い実装を実現できます。

### 10.1 実装のポイント

- **プラットフォーム最適化**: Windows環境での最高のパフォーマンスを実現するためにGDI+を活用
- **適切な抽象化**: インターフェースを通じた柔軟な拡張性を確保
- **メモリ管理**: 大きな画像データの効率的な管理と解放
- **アダプターパターン**: 異なる実装間のシームレスな連携
- **並列処理**: マルチコアCPUを活用した処理速度の向上

### 10.2 ベストプラクティス

1. リソースの適切な解放を徹底する（`IDisposable`パターン）
2. パフォーマンスが重要な場面ではWindows最適化実装を使用する
3. モジュール性と再利用性を高めるためにインターフェースベースの設計を維持する
4. 大きな画像データの処理には分割処理や並列処理を検討する
5. OCR処理など特殊な処理には専用の最適化機能を活用する

このガイドラインに従うことで、効率的で信頼性の高いイメージ処理機能を実装し、Baketaアプリケーションの中核機能であるOCRと翻訳オーバーレイの基盤を提供することができます。