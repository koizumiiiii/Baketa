using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Core.Common;
using Baketa.Infrastructure.Platform.Windows;
using DrawingImageFormat = System.Drawing.Imaging.ImageFormat;
using SysRectangle = System.Drawing.Rectangle;

namespace Baketa.Infrastructure.Platform.Adapters
{
    /// <summary>
    /// WindowsイメージをIAdvancedImageインターフェースに変換するアダプター
    /// </summary>
    public class WindowsImageAdapter : DisposableBase, IAdvancedImage
    {
        private readonly IWindowsImage _windowsImage;
        
        /// <summary>
        /// WindowsImageAdapterのコンストラクタ
        /// </summary>
        /// <param name="windowsImage">Windows画像オブジェクト</param>
        /// <exception cref="ArgumentNullException">windowsImageがnullの場合</exception>
        public WindowsImageAdapter(IWindowsImage windowsImage)
        {
        ArgumentNullException.ThrowIfNull(windowsImage, nameof(windowsImage));
            _windowsImage = windowsImage;
    }
        
        /// <summary>
        /// 画像の幅
        /// </summary>
        public int Width => _windowsImage.Width;
        
        /// <summary>
        /// 画像の高さ
        /// </summary>
        public int Height => _windowsImage.Height;
        
        /// <summary>
        /// 画像のクローンを作成します
        /// </summary>
        /// <returns>クローンされた画像</returns>
        public IImage Clone()
        {
            ThrowIfDisposed();
            var nativeImage = _windowsImage.GetNativeImage();
            using var clonedBitmap = new Bitmap(nativeImage);
            // 所有権が移転されるので、Disposeされないクローンを作成
            var persistentBitmap = (Bitmap)clonedBitmap.Clone();
            var clonedWindowsImage = new WindowsImage(persistentBitmap);
            
            return new WindowsImageAdapter(clonedWindowsImage);
        }
        
        /// <summary>
        /// 画像をバイト配列に変換します
        /// </summary>
        /// <returns>画像のバイト配列</returns>
        public Task<byte[]> ToByteArrayAsync()
        {
            ThrowIfDisposed();
            using var stream = new MemoryStream();
            var nativeImage = _windowsImage.GetNativeImage();
            nativeImage.Save(stream, DrawingImageFormat.Png);
            return Task.FromResult(stream.ToArray());
        }
        
        /// <summary>
        /// 画像のサイズを変更します
        /// </summary>
        /// <param name="width">新しい幅</param>
        /// <param name="height">新しい高さ</param>
        /// <returns>リサイズされた画像</returns>
        public Task<IImage> ResizeAsync(int width, int height)
        {
            ThrowIfDisposed();
            var nativeImage = _windowsImage.GetNativeImage();
            using var resized = new Bitmap(nativeImage, width, height);
            // 所有権が移転されるので、Disposeされないクローンを作成
            var persistentBitmap = (Bitmap)resized.Clone();
            var resizedWindowsImage = new WindowsImage(persistentBitmap);
            
            return Task.FromResult<IImage>(new WindowsImageAdapter(resizedWindowsImage));
        }
        
        /// <summary>
        /// 指定座標のピクセル値を取得します
        /// </summary>
        /// <param name="x">X座標</param>
        /// <param name="y">Y座標</param>
        /// <returns>ピクセル値</returns>
        public Color GetPixel(int x, int y)
        {
            ThrowIfDisposed();
            var nativeImage = _windowsImage.GetNativeImage();
            
            if (x < 0 || x >= Width || y < 0 || y >= Height)
            {
                throw new ArgumentOutOfRangeException($"指定された座標 ({x}, {y}) は画像の範囲外です");
            }
            
            if (nativeImage is not Bitmap bitmap)
            {
                throw new InvalidOperationException("ピクセル取得はBitmapでのみサポートされています");
            }
            
            return bitmap.GetPixel(x, y);
        }
        
        /// <summary>
        /// 指定座標にピクセル値を設定します
        /// </summary>
        /// <param name="x">X座標</param>
        /// <param name="y">Y座標</param>
        /// <param name="color">設定する色</param>
        public void SetPixel(int x, int y, Color color)
        {
            ThrowIfDisposed();
            var nativeImage = _windowsImage.GetNativeImage();
            
            if (x < 0 || x >= Width || y < 0 || y >= Height)
            {
                throw new ArgumentOutOfRangeException($"指定された座標 ({x}, {y}) は画像の範囲外です");
            }
            
            if (nativeImage is not Bitmap bitmap)
            {
                throw new InvalidOperationException("ピクセル設定はBitmapでのみサポートされています");
            }
            
            bitmap.SetPixel(x, y, color);
        }
        
        /// <summary>
        /// 画像にフィルターを適用します
        /// </summary>
        /// <param name="filter">適用するフィルター</param>
        /// <returns>フィルター適用後の新しい画像</returns>
        public async Task<IAdvancedImage> ApplyFilterAsync(IImageFilter filter)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(filter);
            
            if (_windowsImage.GetNativeImage() is not Bitmap bitmap)
            {
                throw new InvalidOperationException("フィルター適用はBitmapでのみサポートされています");
            }
            
            // Bitmapをバイトデータとしてロックして取得
            var bitmapData = bitmap.LockBits(
                new SysRectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);
            
            try
            {
                // ピクセルデータをバイト配列にコピー
                var length = bitmapData.Stride * bitmapData.Height;
                var sourceData = new byte[length];
                System.Runtime.InteropServices.Marshal.Copy(bitmapData.Scan0, sourceData, 0, length);
                
                // フィルター適用
                // フィルター適用（IReadOnlyList<byte>の結果を受け取る）
var resultDataList = await Task.Run(() => filter.Apply(sourceData, bitmap.Width, bitmap.Height, bitmapData.Stride))
                    .ConfigureAwait(false);
                
                // IReadOnlyList<byte>をbyte[]に変換
                var resultData = resultDataList.ToArray();
                
                // 新しいBitmapを作成
                using var resultBitmap = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb);
                var resultBitmapData = resultBitmap.LockBits(
                    new SysRectangle(0, 0, resultBitmap.Width, resultBitmap.Height),
                    ImageLockMode.WriteOnly,
                    PixelFormat.Format32bppArgb);
                
                try
                {
                    // 結果データを新しいBitmapにコピー
                    System.Runtime.InteropServices.Marshal.Copy(resultData, 0, resultBitmapData.Scan0, length);
                }
                finally
                {
                    resultBitmap.UnlockBits(resultBitmapData);
                }
                
                // 新しいWindowsImageを作成
                // 所有権が移転されるので、Disposeされないクローンを作成
                using var persistentBitmap = (Bitmap)resultBitmap.Clone();
                var resultWindowsImage = new WindowsImage(persistentBitmap);
                return new WindowsImageAdapter(resultWindowsImage);
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }
        }
        
        /// <summary>
        /// 複数のフィルターを順番に適用します
        /// </summary>
        /// <param name="filters">適用するフィルターのコレクション</param>
        /// <returns>フィルター適用後の新しい画像</returns>
        public async Task<IAdvancedImage> ApplyFiltersAsync(IEnumerable<IImageFilter> filters)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(filters);
            
            IAdvancedImage result = this;
            foreach (var filter in filters)
            {
                result = await result.ApplyFilterAsync(filter).ConfigureAwait(false);
            }
            
            return result;
        }
        
        /// <summary>
        /// 画像のヒストグラムを生成します
        /// </summary>
        /// <param name="channel">対象チャンネル</param>
        /// <returns>ヒストグラムデータ</returns>
        public async Task<int[]> ComputeHistogramAsync(ColorChannel channel = ColorChannel.Luminance)
        {
            ThrowIfDisposed();
            
            if (_windowsImage.GetNativeImage() is not Bitmap bitmap)
            {
                throw new InvalidOperationException("ヒストグラム計算はBitmapでのみサポートされています");
            }
            
            return await Task.Run(() => {
                var histogram = new int[256];
                
                for (int y = 0; y < bitmap.Height; y++)
                {
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        var pixel = bitmap.GetPixel(x, y);
                        int value = channel switch
                        {
                            ColorChannel.Red => pixel.R,
                            ColorChannel.Green => pixel.G,
                            ColorChannel.Blue => pixel.B,
                            ColorChannel.Alpha => pixel.A,
                            ColorChannel.Luminance => (int)(0.299f * pixel.R + 0.587f * pixel.G + 0.114f * pixel.B),
                            _ => throw new ArgumentException("無効なカラーチャンネル", nameof(channel))
                        };
                        
                        histogram[value]++;
                    }
                }
                
                return histogram;
            }).ConfigureAwait(false);
        }
        
        /// <summary>
        /// 画像をグレースケールに変換します
        /// </summary>
        /// <returns>グレースケール変換された新しい画像</returns>
        public async Task<IAdvancedImage> ToGrayscaleAsync()
        {
            ThrowIfDisposed();
            
            if (_windowsImage.GetNativeImage() is not Bitmap bitmap)
            {
                throw new InvalidOperationException("グレースケール変換はBitmapでのみサポートされています");
            }
            
            return await Task.Run(() => {
                using var result = new Bitmap(bitmap.Width, bitmap.Height);

                for (int y = 0; y < bitmap.Height; y++)
                {
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        var pixel = bitmap.GetPixel(x, y);
                        int gray = (int)(0.299f * pixel.R + 0.587f * pixel.G + 0.114f * pixel.B);
                        var grayColor = Color.FromArgb(pixel.A, gray, gray, gray);
                        result.SetPixel(x, y, grayColor);
                    }
                }

                // 結果画像を作成（クローンを作成して所有権を移転）
                using Bitmap clonedBitmap = (Bitmap)result.Clone();
                var resultWindowsImage = new WindowsImage(clonedBitmap);
                return new WindowsImageAdapter(resultWindowsImage);
            }).ConfigureAwait(false);
        }
        
        /// <summary>
        /// 画像を二値化します
        /// </summary>
        /// <param name="threshold">閾値（0～255）</param>
        /// <returns>二値化された新しい画像</returns>
        public async Task<IAdvancedImage> ToBinaryAsync(byte threshold)
        {
            ThrowIfDisposed();
            
            if (_windowsImage.GetNativeImage() is not Bitmap bitmap)
            {
                throw new InvalidOperationException("二値化処理はBitmapでのみサポートされています");
            }
            
            return await Task.Run(() => {
                using var result = new Bitmap(bitmap.Width, bitmap.Height);

                for (int y = 0; y < bitmap.Height; y++)
                {
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        var pixel = bitmap.GetPixel(x, y);
                        int gray = (int)(0.299f * pixel.R + 0.587f * pixel.G + 0.114f * pixel.B);
                        var binaryColor = gray > threshold ? Color.White : Color.Black;
                        result.SetPixel(x, y, Color.FromArgb(pixel.A, binaryColor));
                    }
                }

                // 結果画像を作成（クローンを作成して所有権を移転）
                using Bitmap clonedBitmap = (Bitmap)result.Clone();
                var resultWindowsImage = new WindowsImage(clonedBitmap);
                return new WindowsImageAdapter(resultWindowsImage);
            }).ConfigureAwait(false);
        }
        
        /// <summary>
        /// 画像の特定領域を抽出します
        /// </summary>
        /// <param name="rectangle">抽出する領域</param>
        /// <returns>抽出された新しい画像</returns>
        public async Task<IAdvancedImage> ExtractRegionAsync(Rectangle rectangle)
        {
            ThrowIfDisposed();
            
            if (rectangle.Width <= 0 || rectangle.Height <= 0)
            {
                throw new ArgumentException("抽出領域の幅と高さは0より大きい値である必要があります", nameof(rectangle));
            }
            
            if (rectangle.X < 0 || rectangle.Y < 0 || 
                rectangle.X + rectangle.Width > Width || 
                rectangle.Y + rectangle.Height > Height)
            {
                throw new ArgumentException("抽出領域は画像の範囲内である必要があります", nameof(rectangle));
            }
            
            var nativeImage = _windowsImage.GetNativeImage();
            
            return await Task.Run(() => {
                using var cropBitmap = new Bitmap(rectangle.Width, rectangle.Height);
                using var g = Graphics.FromImage(cropBitmap);

                var sysRect = new SysRectangle(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
                g.DrawImage(nativeImage, new SysRectangle(0, 0, rectangle.Width, rectangle.Height), 
                    sysRect, GraphicsUnit.Pixel);

                // 結果画像を作成（クローンを作成して所有権を移転）
                using Bitmap clonedBitmap = (Bitmap)cropBitmap.Clone();
                var resultWindowsImage = new WindowsImage(clonedBitmap);
                return new WindowsImageAdapter(resultWindowsImage);
            }).ConfigureAwait(false);
        }
        
        /// <summary>
        /// OCR前処理の最適化を行います
        /// </summary>
        /// <returns>OCR向けに最適化された新しい画像</returns>
        public Task<IAdvancedImage> OptimizeForOcrAsync()
        {
            return OptimizeForOcrAsync(new OcrImageOptions());
        }
        
        /// <summary>
        /// OCR前処理の最適化を指定されたオプションで行います
        /// </summary>
        /// <param name="options">最適化オプション</param>
        /// <returns>OCR向けに最適化された新しい画像</returns>
        public async Task<IAdvancedImage> OptimizeForOcrAsync(OcrImageOptions options)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(options);
            
            // まずグレースケール変換
            var grayscaleImage = await ToGrayscaleAsync().ConfigureAwait(false);
            var result = grayscaleImage;
            
            // コントラスト強調
            if (Math.Abs(options.ContrastEnhancement - 1.0f) > 0.01f)
            {
                // コントラスト強調フィルターの実装が必要
                // ここではダミー実装として同じ画像を返す
                // TODO: コントラスト強調フィルターの実装
            }
            
            // ノイズ除去
            if (options.NoiseReduction > 0.01f)
            {
                // ノイズ除去フィルターの実装が必要
                // TODO: ノイズ除去フィルターの実装
            }
            
            // 二値化
            if (options.UseAdaptiveThreshold)
            {
                // 適応的二値化の実装が必要
                // TODO: 適応的二値化の実装
                // 簡易実装として通常の二値化を使用
                if (options.BinarizationThreshold > 0)
                {
                    result = await result.ToBinaryAsync((byte)options.BinarizationThreshold).ConfigureAwait(false);
                }
            }
            else if (options.BinarizationThreshold > 0)
            {
                result = await result.ToBinaryAsync((byte)options.BinarizationThreshold).ConfigureAwait(false);
            }
            
            // シャープネス強調
            if (options.SharpnessEnhancement > 0.01f)
            {
                // シャープネス強調フィルターの実装が必要
                // TODO: シャープネス強調フィルターの実装
            }
            
            // テキスト方向の検出と修正
            if (options.DetectAndCorrectOrientation)
            {
                // テキスト方向検出の実装が必要
                // TODO: テキスト方向検出と修正の実装
            }
            
            return result;
        }
        
        /// <summary>
        /// 2つの画像の類似度を計算します
        /// </summary>
        /// <param name="other">比較対象の画像</param>
        /// <returns>0.0〜1.0の類似度（1.0が完全一致）</returns>
        public async Task<float> CalculateSimilarityAsync(IImage other)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(other);
            
            if (other.Width != Width || other.Height != Height)
            {
                // サイズが異なる場合、低い類似度を返す
                return 0.2f;
            }
            
            if (_windowsImage.GetNativeImage() is not Bitmap bitmap)
            {
                throw new InvalidOperationException("類似度計算はBitmapでのみサポートされています");
            }
            
            // 他の画像のバイトデータを取得
var otherBytes = await other.ToByteArrayAsync().ConfigureAwait(false);

return await Task.Run(() => {
    using var otherImage = new Bitmap(new MemoryStream(otherBytes));
                int samePixels = 0;
                int totalPixels = Width * Height;
                
                // サンプリングベースの比較（全ピクセルではなく間引いて比較）
                int samplingRate = Math.Max(1, totalPixels / 10000); // 最大1万ピクセルをサンプリング
                int sampledPixels = 0;
                
                for (int y = 0; y < Height; y += samplingRate)
                {
                    for (int x = 0; x < Width; x += samplingRate)
                    {
                        var pixel1 = bitmap.GetPixel(x, y);
                        var pixel2 = otherImage.GetPixel(x, y);
                        
                        // RGBの差分を計算
                        int rDiff = Math.Abs(pixel1.R - pixel2.R);
                        int gDiff = Math.Abs(pixel1.G - pixel2.G);
                        int bDiff = Math.Abs(pixel1.B - pixel2.B);
                        int avgDiff = (rDiff + gDiff + bDiff) / 3;
                        
                        // 差分が閾値以下なら類似ピクセルとみなす
                        if (avgDiff < 30) // 閾値: 30/255
                        {
                            samePixels++;
                        }
                        
                        sampledPixels++;
                    }
                }
                
                // 類似度の計算
                return (float)samePixels / sampledPixels;
            }).ConfigureAwait(false);
        }
        
        /// <summary>
        /// 画像の特定領域におけるテキスト存在可能性を評価します
        /// </summary>
        /// <param name="rectangle">評価する領域</param>
        /// <returns>テキスト存在可能性（0.0〜1.0）</returns>
        public async Task<float> EvaluateTextProbabilityAsync(Rectangle rectangle)
        {
            ThrowIfDisposed();
            
            // 指定領域を抽出
            var regionImage = await ExtractRegionAsync(rectangle).ConfigureAwait(false);
            
            // グレースケール変換
            var grayImage = await regionImage.ToGrayscaleAsync().ConfigureAwait(false);
            
            // ヒストグラム分析
            var histogram = await grayImage.ComputeHistogramAsync(ColorChannel.Luminance).ConfigureAwait(false);
            
            return await Task.Run(() => {
                // テキスト検出のヒューリスティック
                // 1. エッジ検出（テキストはエッジが多い）
                // 2. コントラスト分析（テキストは背景とコントラストがある）
                // 3. ヒストグラム分析（テキストは特定の明度分布を示す）
                
                // 簡易実装: ヒストグラムの分散を使用してテキスト可能性を評価
                // 分散が大きい = テキストの可能性が高い
                
                // ヒストグラムの平均値計算
                float sum = 0;
                float totalPixels = histogram.Sum();
                
                if (totalPixels == 0)
                {
                    return 0.0f;
                }
                
                for (int i = 0; i < histogram.Length; i++)
                {
                    sum += i * histogram[i];
                }
                
                float mean = sum / totalPixels;
                
                // 分散の計算
                float variance = 0;
                for (int i = 0; i < histogram.Length; i++)
                {
                    variance += histogram[i] * (i - mean) * (i - mean);
                }
                variance /= totalPixels;
                
                // 正規化された分散値からテキスト確率を推定
                // 分散が大きいほどテキストの可能性が高い（0.0〜1.0の範囲に正規化）
                float normalizedVariance = Math.Min(1.0f, variance / 2000.0f);
                
                // 他の特徴も考慮してスコアを調整
                // この実装はシンプルな例で、実際にはもっと複雑なアルゴリズムが必要
                return normalizedVariance;
            }).ConfigureAwait(false);
        }
        
        /// <summary>
        /// 画像の回転を行います
        /// </summary>
        /// <param name="degrees">回転角度（度数法）</param>
        /// <returns>回転された新しい画像</returns>
        public async Task<IAdvancedImage> RotateAsync(float degrees)
        {
            ThrowIfDisposed();
            
            var nativeImage = _windowsImage.GetNativeImage();
            
            return await Task.Run(() => {
                using var rotatedBitmap = new Bitmap(nativeImage.Width, nativeImage.Height);
                using var g = Graphics.FromImage(rotatedBitmap);

                // 回転の品質設定
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                // 中心を基準に回転
                g.TranslateTransform(nativeImage.Width / 2f, nativeImage.Height / 2f);
                g.RotateTransform(degrees);
                g.TranslateTransform(-nativeImage.Width / 2f, -nativeImage.Height / 2f);

                // 画像の描画
                g.DrawImage(nativeImage, new PointF(0, 0));

                // 結果画像を作成（クローンを作成して所有権を移転）
                using Bitmap clonedBitmap = (Bitmap)rotatedBitmap.Clone();
                var resultWindowsImage = new WindowsImage(clonedBitmap);
                return new WindowsImageAdapter(resultWindowsImage);
            }).ConfigureAwait(false);
        }
        
        /// <summary>
        /// マネージドリソースを解放します
        /// </summary>
        protected override void DisposeManagedResources()
        {
            if (_windowsImage is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}