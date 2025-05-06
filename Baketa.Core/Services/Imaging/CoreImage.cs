using System;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Common;
using Baketa.Core.Extensions;

namespace Baketa.Core.Services.Imaging
{
    /// <summary>
    /// IImageの基本実装
    /// </summary>
    public class CoreImage : DisposableBase, IImage
    {
        private byte[] _pixelData;
        
        /// <inheritdoc/>
        public int Width { get; }
        
        /// <inheritdoc/>
        public int Height { get; }
        
        /// <summary>
        /// 画像フォーマット
        /// </summary>
        public ImageFormat Format { get; }
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="pixelData">ピクセルデータ</param>
        /// <param name="width">幅</param>
        /// <param name="height">高さ</param>
        /// <param name="format">画像フォーマット</param>
        public CoreImage(byte[] pixelData, int width, int height, ImageFormat format)
        {
            _pixelData = pixelData ?? throw new ArgumentNullException(nameof(pixelData));
            Width = width;
            Height = height;
            Format = format;
        }
        
        /// <inheritdoc/>
        public Task<byte[]> ToByteArrayAsync()
        {
            ThrowIfDisposed();
            return Task.FromResult(_pixelData.ToArray());
        }
        
        /// <summary>
        /// 画像データのバイト配列を取得します
        /// </summary>
        /// <returns>画像データのバイト配列</returns>
        public IReadOnlyList<byte> Bytes => _pixelData.ToArray();
        
        /// <inheritdoc/>
        public IImage Clone()
        {
            ThrowIfDisposed();
            var resultBytes = new byte[_pixelData.Length];
            Buffer.BlockCopy(_pixelData, 0, resultBytes, 0, _pixelData.Length);
            return new CoreImage(resultBytes, Width, Height, Format);
        }
        
        /// <inheritdoc/>
        public Task<IImage> ResizeAsync(int width, int height)
        {
            ThrowIfDisposed();
            
            // 実際の実装では適切なリサイズアルゴリズムを使用する
            // 空の配列を使用
            byte[] newData = [];
            
            // リサイズロジックを実装...
            
            return Task.FromResult<IImage>(new CoreImage(newData, width, height, Format));
        }
        
        /// <summary>
        /// オブジェクトが破棄されている場合に例外をスローします
        /// </summary>
        protected new void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(IsDisposed(), nameof(CoreImage));
        }
        
        /// <summary>
        /// ピクセルあたりのバイト数
        /// </summary>
        protected int BytesPerPixel => Format switch
        {
            ImageFormat.Rgb24 => 3,
            ImageFormat.Rgba32 => 4,
            ImageFormat.Grayscale8 => 1,
            _ => throw new NotSupportedException($"未サポートのフォーマット: {Format}")
        };
        
        /// <summary>
        /// マネージドリソースを解放します
        /// </summary>
        protected override void DisposeManagedResources()
        {
            _pixelData = [];
        }
    }
}