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
        private readonly int _width;
        private readonly int _height;
        private readonly ImageFormat _format;
        
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
            _width = width;
            _height = height;
            _format = format;
        }
        
        /// <inheritdoc/>
        public int Width => _width;
        
        /// <inheritdoc/>
        public int Height => _height;
        
        /// <summary>
        /// 画像フォーマット
        /// </summary>
        public ImageFormat Format => _format;
        
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
            return new CoreImage(resultBytes, _width, _height, _format);
        }
        
        /// <inheritdoc/>
        public Task<IImage> ResizeAsync(int width, int height)
        {
            ThrowIfDisposed();
            
            // 実際の実装では適切なリサイズアルゴリズムを使用する
            // ここではC# 12のコレクション式を使用した簡易的な実装
            byte[] newData = [];
            
            // リサイズロジックを実装...
            
            return Task.FromResult<IImage>(new CoreImage(newData, width, height, _format));
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
        protected int BytesPerPixel => _format switch
        {
            ImageFormat.Rgb24 => 3,
            ImageFormat.Rgba32 => 4,
            ImageFormat.Grayscale8 => 1,
            _ => throw new NotSupportedException($"未サポートのフォーマット: {_format}")
        };
        
        /// <summary>
        /// マネージドリソースを解放します
        /// </summary>
        protected override void DisposeManagedResources()
        {
            _pixelData = Array.Empty<byte>();
        }
    }
}