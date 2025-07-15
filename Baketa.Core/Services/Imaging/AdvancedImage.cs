using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Extensions;

namespace Baketa.Core.Services.Imaging;

    /// <summary>
    /// IAdvancedImageの実装
    /// </summary>
    public class AdvancedImage : CoreImage, IAdvancedImage
    {
        private readonly byte[] _rawPixelData;
        private readonly int _stride;
        
        /// <inheritdoc/>
        public bool IsGrayscale => Format == ImageFormat.Grayscale8;
        
        /// <inheritdoc/>
        public int BitsPerPixel => Format switch
        {
            ImageFormat.Grayscale8 => 8,
            ImageFormat.Rgb24 => 24,
            ImageFormat.Rgba32 => 32,
            _ => throw new NotSupportedException($"未サポートのフォーマット: {Format}")
        };
        
        /// <inheritdoc/>
        public int ChannelCount => Format switch
        {
            ImageFormat.Grayscale8 => 1,
            ImageFormat.Rgb24 => 3,
            ImageFormat.Rgba32 => 4,
            _ => throw new NotSupportedException($"未サポートのフォーマット: {Format}")
        };
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="pixelData">ピクセルデータ</param>
        /// <param name="width">幅</param>
        /// <param name="height">高さ</param>
        /// <param name="format">フォーマット</param>
        /// <param name="stride">ストライド（オプション）</param>
        public AdvancedImage(byte[] pixelData, int width, int height, ImageFormat format, int stride = 0)
            : base(pixelData, width, height, format)
        {
            _rawPixelData = pixelData ?? throw new ArgumentNullException(nameof(pixelData));
            _stride = stride > 0 ? stride : width * BytesPerPixel;
        }
        
        /// <inheritdoc/>
        public Color GetPixel(int x, int y)
        {
            ThrowIfDisposed();
            
            if (x < 0) throw new ArgumentOutOfRangeException(nameof(x), "X座標が0より小さいです");
            if (x >= Width) throw new ArgumentOutOfRangeException(nameof(x), "X座標が画像の幅以上です");
            if (y < 0) throw new ArgumentOutOfRangeException(nameof(y), "Y座標が0より小さいです");
            if (y >= Height) throw new ArgumentOutOfRangeException(nameof(y), "Y座標が画像の高さ以上です");
                
            int bytesPerPixel = BytesPerPixel;
            int offset = y * _stride + x * bytesPerPixel;
            
            return Format switch
            {
                ImageFormat.Rgb24 => Color.FromArgb(
                    _rawPixelData[offset],     // R
                    _rawPixelData[offset + 1], // G
                    _rawPixelData[offset + 2]  // B
                ),
                ImageFormat.Rgba32 => Color.FromArgb(
                    _rawPixelData[offset + 3], // A
                    _rawPixelData[offset],     // R
                    _rawPixelData[offset + 1], // G
                    _rawPixelData[offset + 2]  // B
                ),
                ImageFormat.Grayscale8 => Color.FromArgb(
                    _rawPixelData[offset],     // グレースケール値を全チャンネルに適用
                    _rawPixelData[offset],
                    _rawPixelData[offset]
                ),
                _ => throw new NotSupportedException($"未サポートのフォーマット: {Format}")
            };
        }
        
        /// <inheritdoc/>
        public void SetPixel(int x, int y, Color color)
        {
            ThrowIfDisposed();
            
            if (x < 0) throw new ArgumentOutOfRangeException(nameof(x), "X座標が0より小さいです");
            if (x >= Width) throw new ArgumentOutOfRangeException(nameof(x), "X座標が画像の幅以上です");
            if (y < 0) throw new ArgumentOutOfRangeException(nameof(y), "Y座標が0より小さいです");
            if (y >= Height) throw new ArgumentOutOfRangeException(nameof(y), "Y座標が画像の高さ以上です");
                
            int bytesPerPixel = BytesPerPixel;
            int offset = y * _stride + x * bytesPerPixel;
            
            switch (Format)
            {
                case ImageFormat.Rgb24:
                    _rawPixelData[offset] = color.R;
                    _rawPixelData[offset + 1] = color.G;
                    _rawPixelData[offset + 2] = color.B;
                    break;
                    
                case ImageFormat.Rgba32:
                    _rawPixelData[offset] = color.R;
                    _rawPixelData[offset + 1] = color.G;
                    _rawPixelData[offset + 2] = color.B;
                    _rawPixelData[offset + 3] = color.A;
                    break;
                    
                case ImageFormat.Grayscale8:
                    byte gray = (byte)((0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B));
                    _rawPixelData[offset] = gray;
                    break;
                    
                default:
                    throw new NotSupportedException($"未サポートのフォーマット: {Format}");
            }
        }
        
        /// <inheritdoc/>
        public async Task<IAdvancedImage> ApplyFilterAsync(IImageFilter filter)
        {
            ThrowIfDisposed();
            
            ArgumentNullException.ThrowIfNull(filter);
                
            // 新しいIImageFilterインターフェースのApplyAsyncメソッドを使用
            return await filter.ApplyAsync(this).ConfigureAwait(false);
        }
        
        /// <inheritdoc/>
        public async Task<IAdvancedImage> ApplyFiltersAsync(IEnumerable<IImageFilter> filters)
        {
            ThrowIfDisposed();
            
            ArgumentNullException.ThrowIfNull(filters);
            
            // 複数回の列挙を避けるために列挙を参照型に固定
            var filtersList = filters.AsList();
                
            if (!filtersList.SafeAny())
                return this.Clone() as IAdvancedImage;
                
            AdvancedImage result = this;
            foreach (var filter in filtersList)
            {
                result = (AdvancedImage)await result.ApplyFilterAsync(filter).ConfigureAwait(false);
            }
            
            return result;
        }
        
        /// <inheritdoc/>
        public Task<int[]> ComputeHistogramAsync(ColorChannel channel = ColorChannel.Luminance)
        {
            ThrowIfDisposed();
            
            // ヒストグラム配列（0-255の度数分布）
            int[] histogram = new int[256];
            
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    Color pixel = GetPixel(x, y);
                    
                    byte value = channel switch
                    {
                        ColorChannel.Red => pixel.R,
                        ColorChannel.Green => pixel.G,
                        ColorChannel.Blue => pixel.B,
                        ColorChannel.Alpha => pixel.A,
                        ColorChannel.Luminance => (byte)((0.299 * pixel.R) + (0.587 * pixel.G) + (0.114 * pixel.B)),
                        _ => throw new ArgumentException($"未サポートのチャンネル: {channel}")
                    };
                    
                    histogram[value]++;
                }
            }
            
            return Task.FromResult(histogram);
        }
        
        /// <inheritdoc/>
        public Task<IAdvancedImage> ToGrayscaleAsync()
        {
            ThrowIfDisposed();
            
            if (Format == ImageFormat.Grayscale8)
                return Task.FromResult(this.Clone() as IAdvancedImage);
                
            byte[] resultData = new byte[Width * Height];
            
            // CPU負荷の高い処理なので、Task.Runで実行
            return Task.Run(() => {
                for (int y = 0; y < Height; y++)
                {
                    for (int x = 0; x < Width; x++)
                    {
                        Color pixel = GetPixel(x, y);
                        byte gray = (byte)((0.299 * pixel.R) + (0.587 * pixel.G) + (0.114 * pixel.B));
                        resultData[y * Width + x] = gray;
                    }
                }
                
                return (IAdvancedImage)new AdvancedImage(resultData, Width, Height, ImageFormat.Grayscale8, Width);
            });
        }
        
        /// <inheritdoc/>
        public IAdvancedImage ToGrayscale()
        {
            ThrowIfDisposed();
            
            if (Format == ImageFormat.Grayscale8)
                return this.Clone() as IAdvancedImage;
                
            byte[] resultData = new byte[Width * Height];
            
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    Color pixel = GetPixel(x, y);
                    byte gray = (byte)((0.299 * pixel.R) + (0.587 * pixel.G) + (0.114 * pixel.B));
                    resultData[y * Width + x] = gray;
                }
            }
            
            return new AdvancedImage(resultData, Width, Height, ImageFormat.Grayscale8, Width);
        }
        
        /// <inheritdoc/>
        public async Task<IAdvancedImage> ToBinaryAsync(byte threshold)
        {
            ThrowIfDisposed();
            
            // まずグレースケールに変換
            var grayscaleImage = await ToGrayscaleAsync().ConfigureAwait(false);
            byte[] resultData = new byte[Width * Height];
            
            // グレースケール画像から二値化データを生成
            byte[] sourceData = await grayscaleImage.ToByteArrayAsync().ConfigureAwait(false);
            
            // CPU負荷の高い処理なので、Task.Runで実行
            return await Task.Run(() => {
                for (int i = 0; i < sourceData.Length; i++)
                {
                    resultData[i] = sourceData[i] >= threshold ? (byte)255 : (byte)0;
                }
                
                return (IAdvancedImage)new AdvancedImage(resultData, Width, Height, ImageFormat.Grayscale8, Width);
            }).ConfigureAwait(false);
        }
        
        /// <inheritdoc/>
        public async Task<IAdvancedImage> ExtractRegionAsync(Rectangle rectangle)
        {
            ThrowIfDisposed();
            
            if (rectangle.X < 0) throw new ArgumentOutOfRangeException(nameof(rectangle), "X座標が0より小さいです");
            if (rectangle.Y < 0) throw new ArgumentOutOfRangeException(nameof(rectangle), "Y座標が0より小さいです");
            if (rectangle.X + rectangle.Width > Width) throw new ArgumentOutOfRangeException(nameof(rectangle), "幅が画像の範囲を超えています");
            if (rectangle.Y + rectangle.Height > Height) throw new ArgumentOutOfRangeException(nameof(rectangle), "高さが画像の範囲を超えています");
            
            int bytesPerPixel = BytesPerPixel;
            byte[] resultData = new byte[rectangle.Width * rectangle.Height * bytesPerPixel];
            
            // CPU負荷の高い処理なので、Task.Runで実行
            return await Task.Run(() => {
                for (int y = 0; y < rectangle.Height; y++)
                {
                    for (int x = 0; x < rectangle.Width; x++)
                    {
                        Color pixel = GetPixel(rectangle.X + x, rectangle.Y + y);
                        int targetOffset = (y * rectangle.Width + x) * bytesPerPixel;
                        
                        switch (Format)
                        {
                            case ImageFormat.Rgb24:
                                resultData[targetOffset] = pixel.R;
                                resultData[targetOffset + 1] = pixel.G;
                                resultData[targetOffset + 2] = pixel.B;
                                break;
                                
                            case ImageFormat.Rgba32:
                                resultData[targetOffset] = pixel.R;
                                resultData[targetOffset + 1] = pixel.G;
                                resultData[targetOffset + 2] = pixel.B;
                                resultData[targetOffset + 3] = pixel.A;
                                break;
                                
                            case ImageFormat.Grayscale8:
                                resultData[targetOffset] = pixel.R; // グレースケールではRGB値は同じ
                                break;
                        }
                    }
                }
                
                return (IAdvancedImage)new AdvancedImage(resultData, rectangle.Width, rectangle.Height, Format, rectangle.Width * bytesPerPixel);
            }).ConfigureAwait(false);
        }
        
        /// <inheritdoc/>
        public Task<IAdvancedImage> OptimizeForOcrAsync()
        {
            return OptimizeForOcrAsync(new OcrImageOptions());
        }
        
        /// <inheritdoc/>
        public async Task<IAdvancedImage> OptimizeForOcrAsync(OcrImageOptions options)
        {
            ThrowIfDisposed();
            
            ArgumentNullException.ThrowIfNull(options, nameof(options));
                
            // グレースケールに変換
            IAdvancedImage result = await ToGrayscaleAsync().ConfigureAwait(false);
            
            // コントラスト強調
            if (Math.Abs(options.ContrastEnhancement - 1.0f) > 0.01f)
            {
                // コントラスト調整フィルターの実装が必要
                // ここではサンプル実装のため省略
            }
            
            // ノイズ除去
            if (options.NoiseReduction > 0)
            {
                // ノイズ除去フィルターの実装が必要
                // ここではサンプル実装のため省略
            }
            
            // 二値化処理
            if (options.BinarizationThreshold > 0)
            {
                result = await result.ToBinaryAsync((byte)options.BinarizationThreshold).ConfigureAwait(false);
            }
            
            return result;
        }
        
        /// <inheritdoc/>
        public async Task<float> CalculateSimilarityAsync(IImage other)
        {
            ThrowIfDisposed();
            
            ArgumentNullException.ThrowIfNull(other, nameof(other));
                
            if (Width != other.Width || Height != other.Height)
                return 0.0f; // サイズが異なる場合は類似度0
                
            // 両方の画像をグレースケールに変換して比較
            var thisGray = await ToGrayscaleAsync().ConfigureAwait(false);
            
            // otherがIAdvancedImageかチェック
            IAdvancedImage otherGray;
            if (other is IAdvancedImage advancedOther)
            {
                otherGray = await advancedOther.ToGrayscaleAsync().ConfigureAwait(false);
            }
            else
            {
                // IAdvancedImageでない場合の処理（実際の実装では適切な変換が必要）
                throw new NotSupportedException("比較対象の画像がIAdvancedImageではありません");
            }
            
            // ヒストグラム比較による類似度計算
            int[] hist1 = await thisGray.ComputeHistogramAsync().ConfigureAwait(false);
            int[] hist2 = await otherGray.ComputeHistogramAsync().ConfigureAwait(false);
            
            // 相関係数の計算
            double sum1 = hist1.Sum();
            double sum2 = hist2.Sum();
            
            var norm1 = hist1.Select(v => v / sum1).ToArray();
            var norm2 = hist2.Select(v => v / sum2).ToArray();
            
            double mean1 = norm1.Average();
            double mean2 = norm2.Average();
            
            var numerator = 0.0;
            var denom1 = 0.0;
            var denom2 = 0.0;
            
            for (int i = 0; i < 256; i++)
            {
                double diff1 = norm1[i] - mean1;
                double diff2 = norm2[i] - mean2;
                
                numerator += diff1 * diff2;
                denom1 += diff1 * diff1;
                denom2 += diff2 * diff2;
            }
            
            double correlation = numerator / Math.Sqrt(denom1 * denom2);
            
            // 相関係数を0〜1の範囲に正規化
            return (float)((correlation + 1) / 2);
        }
        
        /// <inheritdoc/>
        public async Task<float> EvaluateTextProbabilityAsync(Rectangle rectangle)
        {
            ThrowIfDisposed();
            
            // 指定領域を抽出
            var region = await ExtractRegionAsync(rectangle).ConfigureAwait(false);
            
            // グレースケールに変換
            var grayRegion = await region.ToGrayscaleAsync().ConfigureAwait(false);
            
            // ヒストグラム分析
            int[] histogram = await grayRegion.ComputeHistogramAsync().ConfigureAwait(false);
            
            // 輝度の標準偏差を計算（テキストはある程度の標準偏差を持つことが多い）
            var sum = 0.0;
            var sumSquared = 0.0;
            int totalPixels = rectangle.Width * rectangle.Height;
            
            for (int i = 0; i < 256; i++)
            {
                double normalizedCount = (double)histogram[i] / totalPixels;
                sum += i * normalizedCount;
                sumSquared += i * i * normalizedCount;
            }
            
            double mean = sum;
            double variance = sumSquared - (mean * mean);
            double stdDev = Math.Sqrt(variance);
            
            // エッジ検出（テキストはエッジが多い）
            // ここでは簡易的な実装のため詳細な実装は省略
            
            // テキスト確率の計算（簡易的な実装）
            // 実際の実装では機械学習やより複雑なアルゴリズムを使用
            
            // 標準偏差とエッジ密度から確率を推定
            float textProbability = (float)(Math.Min(1.0, stdDev / 40.0)); // 標準偏差が高いほどテキスト確率も高い
            
            return textProbability;
        }
        
        /// <inheritdoc/>
        public Task<IAdvancedImage> RotateAsync(float degrees)
        {
            ThrowIfDisposed();
            
            // 回転処理の実装
            // 実際の実装では適切な行列変換を使用
            
            // 簡易実装（90度単位の回転のみサポート）
            int normalizedDegrees = (int)Math.Round(degrees / 90) * 90;
            normalizedDegrees = ((normalizedDegrees % 360) + 360) % 360; // 0〜359の範囲に正規化
            
            if (normalizedDegrees == 0)
                return Task.FromResult(this.Clone() as IAdvancedImage);
                
            // CPU負荷の高い処理なので、Task.Runで実行
            return Task.Run(() => {
                int newWidth, newHeight;
                byte[] resultData;
                
                switch (normalizedDegrees)
                {
                    case 90:
                        newWidth = Height;
                        newHeight = Width;
                        resultData = new byte[newWidth * newHeight * BytesPerPixel];
                        
                        for (int y = 0; y < Height; y++)
                        {
                            for (int x = 0; x < Width; x++)
                            {
                                Color pixel = GetPixel(x, y);
                                // 90度回転: (x,y) -> (HEIGHT-1-y, x)
                                int newX = Height - 1 - y;
                                int newY = x;
                                
                                int targetOffset = (newY * newWidth + newX) * BytesPerPixel;
                                SetPixelToArray(resultData, targetOffset, pixel);
                            }
                        }
                        break;
                        
                    case 180:
                        newWidth = Width;
                        newHeight = Height;
                        resultData = new byte[newWidth * newHeight * BytesPerPixel];
                        
                        for (int y = 0; y < Height; y++)
                        {
                            for (int x = 0; x < Width; x++)
                            {
                                Color pixel = GetPixel(x, y);
                                // 180度回転: (x,y) -> (WIDTH-1-x, HEIGHT-1-y)
                                int newX = Width - 1 - x;
                                int newY = Height - 1 - y;
                                
                                int targetOffset = (newY * newWidth + newX) * BytesPerPixel;
                                SetPixelToArray(resultData, targetOffset, pixel);
                            }
                        }
                        break;
                        
                    case 270:
                        newWidth = Height;
                        newHeight = Width;
                        resultData = new byte[newWidth * newHeight * BytesPerPixel];
                        
                        for (int y = 0; y < Height; y++)
                        {
                            for (int x = 0; x < Width; x++)
                            {
                                Color pixel = GetPixel(x, y);
                                // 270度回転: (x,y) -> (y, WIDTH-1-x)
                                int newX = y;
                                int newY = Width - 1 - x;
                                
                                int targetOffset = (newY * newWidth + newX) * BytesPerPixel;
                                SetPixelToArray(resultData, targetOffset, pixel);
                            }
                        }
                        break;
                        
                    default:
                        throw new NotImplementedException($"回転角度 {degrees} はサポートされていません");
                }
                
                return (IAdvancedImage)new AdvancedImage(resultData, newWidth, newHeight, Format, newWidth * BytesPerPixel);
            });
        }
        
        /// <inheritdoc/>
        public Task<IAdvancedImage> EnhanceAsync(ImageEnhancementOptions options)
        {
            ThrowIfDisposed();
            
            ArgumentNullException.ThrowIfNull(options, nameof(options));
                
            // CPU負荷の高い処理なので、Task.Runで実行
            return Task.Run(async () => {
                AdvancedImage result = this;
                
                // グレースケール変換が必要な場合
                if (options.OptimizeForTextDetection && Format != ImageFormat.Grayscale8)
                {
                    result = (AdvancedImage)await ToGrayscaleAsync().ConfigureAwait(false);
                }
                
                // 明るさ・コントラスト調整
                if (Math.Abs(options.Brightness) > 0.01f || Math.Abs(options.Contrast - 1.0f) > 0.01f)
                {
                    // 明るさ・コントラスト調整の実装
                    // 実際の実装では適切なアルゴリズムを使用
                    // サンプル実装のため、現在の画像をそのまま返す
                }
                
                // ノイズ除去
                if (options.NoiseReduction > 0.01f)
                {
                    // ノイズ除去の実装
                    // 実際の実装では適切なアルゴリズムを使用
                    // サンプル実装のため、現在の画像をそのまま返す
                }
                
                // シャープネス強調
                if (options.Sharpness > 0.01f)
                {
                    // シャープネス強調の実装
                    // 実際の実装では適切なアルゴリズムを使用
                    // サンプル実装のため、現在の画像をそのまま返す
                }
                
                // 二値化処理
                if (options.BinarizationThreshold > 0)
                {
                    result = (AdvancedImage)await result.ToBinaryAsync((byte)options.BinarizationThreshold).ConfigureAwait(false);
                }
                else if (options.UseAdaptiveThreshold)
                {
                    // 適応的二値化の実装
                    // 実際の実装では適切なアルゴリズムを使用
                    // サンプル実装のため、現在の画像をそのまま返す
                }
                
                return (IAdvancedImage)result;
            });
        }
        
        /// <inheritdoc/>
        public Task<List<Rectangle>> DetectTextRegionsAsync()
        {
            ThrowIfDisposed();
            
            // CPU負荷の高い処理なので、Task.Runで実行
            return Task.Run(() => {
                // テキスト領域検出の実装
                // 実際の実装では適切なアルゴリズムを使用
                
                // サンプル実装のため、空のリストを返す
                return new List<Rectangle>();
            });
        }
        
        /// <summary>
        /// オブジェクトのクローンを作成
        /// </summary>
        /// <returns>オブジェクトのクローン</returns>
        public new IAdvancedImage Clone()
        {
            ThrowIfDisposed();
            var resultBytes = new byte[_rawPixelData.Length];
            Buffer.BlockCopy(_rawPixelData, 0, resultBytes, 0, _rawPixelData.Length);
            return new AdvancedImage(resultBytes, Width, Height, Format, _stride);
        }
        
        private void SetPixelToArray(byte[] array, int offset, Color color)
        {
            switch (Format)
            {
                case ImageFormat.Rgb24:
                    array[offset] = color.R;
                    array[offset + 1] = color.G;
                    array[offset + 2] = color.B;
                    break;
                    
                case ImageFormat.Rgba32:
                    array[offset] = color.R;
                    array[offset + 1] = color.G;
                    array[offset + 2] = color.B;
                    array[offset + 3] = color.A;
                    break;
                    
                case ImageFormat.Grayscale8:
                    array[offset] = color.R; // グレースケールではRGB値は同じ
                    break;
            }
        }
    }
