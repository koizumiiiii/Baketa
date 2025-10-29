using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Memory;
using Rectangle = Baketa.Core.Abstractions.Memory.Rectangle;

namespace Baketa.Core.Abstractions.Imaging.Filters;

    /// <summary>
    /// フィルターチェーンを表すクラス
    /// </summary>
    public class FilterChain : IImageFilter
    {
        private readonly List<IImageFilter> _filters = [];
        
        /// <summary>
        /// フィルターの名前
        /// </summary>
        public string Name => "フィルターチェーン";
        
        /// <summary>
        /// フィルターの説明
        /// </summary>
        public string Description => "複数のフィルターを順番に適用します";
        
        /// <summary>
        /// フィルターのカテゴリ
        /// </summary>
        public FilterCategory Category => FilterCategory.Composite;
        
        /// <summary>
        /// フィルターチェーンの新しいインスタンスを初期化します
        /// </summary>
        public FilterChain()
        {
        }
        
        /// <summary>
        /// 指定したフィルターのコレクションでフィルターチェーンの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="filters">フィルターのコレクション</param>
        public FilterChain(IEnumerable<IImageFilter> filters)
        {
            if (filters != null)
            {
                _filters.AddRange(filters);
            }
        }
        
        /// <summary>
        /// フィルターをチェーンに追加します
        /// </summary>
        /// <param name="filter">追加するフィルター</param>
        /// <returns>このフィルターチェーンインスタンス（メソッドチェーン用）</returns>
        public FilterChain AddFilter(IImageFilter filter)
        {
            ArgumentNullException.ThrowIfNull(filter);
                
            _filters.Add(filter);
            return this;
        }
        
        /// <summary>
        /// フィルターをチェーンから削除します
        /// </summary>
        /// <param name="filter">削除するフィルター</param>
        /// <returns>このフィルターチェーンインスタンス（メソッドチェーン用）</returns>
        public FilterChain RemoveFilter(IImageFilter filter)
        {
            _filters.Remove(filter);
            return this;
        }
        
        /// <summary>
        /// すべてのフィルターをチェーンからクリアします
        /// </summary>
        /// <returns>このフィルターチェーンインスタンス（メソッドチェーン用）</returns>
        public FilterChain ClearFilters()
        {
            _filters.Clear();
            return this;
        }
        
        /// <summary>
        /// インデックスを指定してフィルターを取得します
        /// </summary>
        /// <param name="index">フィルターのインデックス</param>
        /// <returns>指定したインデックスのフィルター</returns>
        public IImageFilter GetFilter(int index)
        {
            if (index < 0 || index >= _filters.Count)
                throw new ArgumentOutOfRangeException(nameof(index), "指定されたインデックスは範囲外です。");
                
            return _filters[index];
        }
        
        /// <summary>
        /// 名前を指定してフィルターを取得します
        /// </summary>
        /// <param name="name">フィルターの名前</param>
        /// <returns>指定した名前のフィルター（見つからない場合はnull）</returns>
        public IImageFilter? GetFilterByName(string name)
        {
            return _filters.FirstOrDefault(f => f.Name == name);
        }
        
        /// <summary>
        /// チェーン内のフィルター数を取得します
        /// </summary>
        public int Count => _filters.Count;
        
        /// <summary>
        /// このチェーン内のすべてのフィルターを列挙します
        /// </summary>
        /// <returns>フィルターの列挙子</returns>
        public IEnumerable<IImageFilter> GetFilters()
        {
            return _filters.AsReadOnly();
        }
        
        /// <summary>
        /// 画像にフィルターを適用します
        /// </summary>
        /// <param name="inputImage">入力画像</param>
        /// <returns>フィルター適用後の新しい画像</returns>
        public async Task<IAdvancedImage> ApplyAsync(IAdvancedImage inputImage)
        {
            ArgumentNullException.ThrowIfNull(inputImage);
                
            if (_filters.Count == 0)
                return inputImage;
                
            var currentImage = inputImage;
            
            foreach (var filter in _filters)
            {
                currentImage = await filter.ApplyAsync(currentImage).ConfigureAwait(false);
            }
            
            return currentImage;
        }
        
        /// <summary>
        /// フィルターのパラメータをリセットします
        /// </summary>
        public void ResetParameters()
        {
            foreach (var filter in _filters)
            {
                filter.ResetParameters();
            }
        }
        
        /// <summary>
        /// フィルターの現在のパラメータを取得します
        /// </summary>
        /// <returns>空のパラメータディクショナリ</returns>
        /// <remarks>
        /// フィルターチェーン自体はパラメータを持ちません。
        /// 個々のフィルターのパラメータを取得するには各フィルターのGetParametersメソッドを使用してください。
        /// </remarks>
        public IDictionary<string, object> GetParameters()
        {
            // フィルターチェーンでは個別フィルターのパラメータを直接公開しない
            return new Dictionary<string, object> { };
        }
        
        /// <summary>
        /// フィルターのパラメータを設定します
        /// </summary>
        /// <param name="name">パラメータ名</param>
        /// <param name="value">パラメータ値</param>
        /// <exception cref="NotSupportedException">
        /// フィルターチェーンでは個別のパラメータを直接設定できません。
        /// </exception>
        public void SetParameter(string name, object value)
        {
            // フィルターチェーンでは個別フィルターのパラメータを直接設定しない
            throw new NotSupportedException("フィルターチェーンでは個別のパラメータを直接設定できません。");
        }
        
        /// <summary>
        /// 指定された画像フォーマットに対応しているかを確認します
        /// </summary>
        /// <param name="format">確認する画像フォーマット</param>
        /// <returns>対応している場合はtrue、そうでない場合はfalse</returns>
        public bool SupportsFormat(ImageFormat format)
        {
            // チェーン内のすべてのフィルターがサポートしている場合のみtrue
            return _filters.Count == 0 || _filters.All(f => f.SupportsFormat(format));
        }
        
        /// <summary>
        /// フィルター適用後の画像情報を取得します
        /// </summary>
        /// <param name="inputImage">入力画像</param>
        /// <returns>出力画像の情報</returns>
        public ImageInfo GetOutputImageInfo(IAdvancedImage inputImage)
        {
            ArgumentNullException.ThrowIfNull(inputImage);
                
            // 件数だけチェックする場合は仮想画像を作成しない
            if (_filters.Count == 0)
                return ImageInfo.FromImage(inputImage);
            
            // チェーン内の各フィルターを適用した後の最終的な画像情報を計算
            var currentImageInfo = ImageInfo.FromImage(inputImage);
            
            foreach (var filter in _filters)
            {
            // 件数値を判定した後なので、安全にループ処理可能
            using var virtualImage = new VirtualImage(currentImageInfo);
                currentImageInfo = filter.GetOutputImageInfo(virtualImage);
                }
            
            return currentImageInfo;
        }
        
        /// <summary>
        /// GetOutputImageInfo用の仮想画像クラス
        /// </summary>
        private sealed class VirtualImage(ImageInfo imageInfo) : IAdvancedImage
        {
            private readonly ImageInfo _imageInfo = imageInfo;

        public int Width => _imageInfo.Width;
            public int Height => _imageInfo.Height;
            public ImageFormat Format => _imageInfo.Format;

            /// <summary>
            /// ピクセルフォーマット（IImage拡張対応）
            /// </summary>
            public ImagePixelFormat PixelFormat
            {
                get
                {
                    // ImageFormatからImagePixelFormatへの変換
                    return Format switch
                    {
                        ImageFormat.Rgb24 => ImagePixelFormat.Rgb24,
                        ImageFormat.Rgba32 => ImagePixelFormat.Rgba32,
                        ImageFormat.Grayscale8 => ImagePixelFormat.Bgra32, // マッピング
                        _ => ImagePixelFormat.Bgra32 // デフォルト
                    };
                }
            }
            
            // IAdvancedImageインターフェース実装
            public bool IsGrayscale => Format == ImageFormat.Grayscale8;
            public int BitsPerPixel => Format switch
            {
                ImageFormat.Grayscale8 => 8,
                ImageFormat.Rgb24 => 24,
                ImageFormat.Rgba32 => 32,
                _ => throw new NotSupportedException($"未サポートのフォーマット: {Format}")
            };
            public int ChannelCount => Format switch
            {
                ImageFormat.Grayscale8 => 1,
                ImageFormat.Rgb24 => 3,
                ImageFormat.Rgba32 => 4,
                _ => throw new NotSupportedException($"未サポートのフォーマット: {Format}")
            };

            /// <summary>
            /// Phase 2.5: ROI座標変換対応 - VirtualImageはメタデータ専用なので常にnull
            /// </summary>
            public System.Drawing.Rectangle? CaptureRegion => null;

            /// <summary>
            /// 画像データの読み取り専用メモリを取得（IImage拡張対応）
            /// </summary>
            /// <returns>画像データの読み取り専用メモリ</returns>
            public ReadOnlyMemory<byte> GetImageMemory() => throw new NotImplementedException();

            /// <summary>
            /// 🔥 [PHASE5.2G-A] 生ピクセルデータへの直接アクセス（VirtualImageは非サポート）
            /// </summary>
            public PixelDataLock LockPixelData() => throw new NotSupportedException("VirtualImageは生ピクセルデータアクセスをサポートしません");

            // 以下は仮想的な実装（実際にはGetOutputImageInfoでのみ使用）
            public Task<byte[]> ToByteArrayAsync() => throw new NotImplementedException();
            public IImage Clone() => throw new NotImplementedException();
            public Task<IImage> ResizeAsync(int width, int height) => throw new NotImplementedException();
            public Task SaveAsync(string path, ImageFormat? format = null) => throw new NotImplementedException();
            public Task<IImage> CropAsync(System.Drawing.Rectangle rectangle) => throw new NotImplementedException();
            public Task<byte[]> GetPixelsAsync(int x, int y, int width, int height) => throw new NotImplementedException();
            public Task<IAdvancedImage> ApplyFilterAsync(IImageFilter filter) => throw new NotImplementedException();
            public Task<IAdvancedImage> ApplyFiltersAsync(IEnumerable<IImageFilter> filters) => throw new NotImplementedException();
            public Task<int[]> ComputeHistogramAsync(ColorChannel channel = ColorChannel.Luminance) => throw new NotImplementedException();
            public Task<IAdvancedImage> ToGrayscaleAsync() => throw new NotImplementedException();
            public IAdvancedImage ToGrayscale() => throw new NotImplementedException();
            public Task<IAdvancedImage> ToBinaryAsync(byte threshold) => throw new NotImplementedException();
            public Task<IAdvancedImage> ExtractRegionAsync(Rectangle rectangle) => throw new NotImplementedException();
            public Task<IAdvancedImage> OptimizeForOcrAsync() => throw new NotImplementedException();
            public Task<IAdvancedImage> OptimizeForOcrAsync(OcrImageOptions options) => throw new NotImplementedException();
            public Task<float> CalculateSimilarityAsync(IImage other) => throw new NotImplementedException();
            public Task<float> EvaluateTextProbabilityAsync(Rectangle rectangle) => throw new NotImplementedException();
            public Task<IAdvancedImage> RotateAsync(float degrees) => throw new NotImplementedException();
            public Color GetPixel(int x, int y) => throw new NotImplementedException();
            public void SetPixel(int x, int y, Color color) => throw new NotImplementedException();
            public Task<IAdvancedImage> EnhanceAsync(ImageEnhancementOptions options) => throw new NotImplementedException();
            public Task<List<Rectangle>> DetectTextRegionsAsync() => throw new NotImplementedException();
            public void Dispose() { }
        }
    }
