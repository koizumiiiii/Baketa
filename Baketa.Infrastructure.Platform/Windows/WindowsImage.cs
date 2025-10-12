using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Platform.Windows;

namespace Baketa.Infrastructure.Platform.Windows;

    /// <summary>
    /// Windows画像の実装
    /// </summary>
    public sealed class WindowsImage : IWindowsImage
    {
        private readonly Bitmap _bitmap;
        private bool _disposed;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="bitmap">Bitmapインスタンス</param>
        public WindowsImage(Bitmap bitmap)
        {
            ArgumentNullException.ThrowIfNull(bitmap, nameof(bitmap));
    
            try
            {
                // 幅と高さにアクセスして有効性を確認（これがArgumentExceptionを発生させる可能性がある）
                int width = bitmap.Width;
                int height = bitmap.Height;
        
                if (width <= 0 || height <= 0)
                {
                    throw new ArgumentException("無効なBitmapです。幅と高さは正の値である必要があります。", nameof(bitmap));
                }
        
                // 問題がなければインスタンス変数を設定
                _bitmap = bitmap;
                _disposed = false;
            }
            catch (Exception ex) when (ex is not ArgumentNullException)
            {
                // 例外発生時、渡されたbitmapを破棄
                bitmap.Dispose();
                throw new ArgumentException("無効なBitmapが渡されました", nameof(bitmap), ex);
            }
        }

        /// <summary>
        /// 画像の幅を取得
        /// </summary>
        public int Width
        {
            get
            {
               ThrowIfDisposed();
                try
                {
                    // _bitmapが有効であることを確認
                    if (_bitmap is Bitmap bitmap)
                    {
                        return bitmap.Width;
                    }
                    throw new InvalidOperationException("内部ビットマップが無効です");
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("画像の幅の取得に失敗しました", ex);
                }
            }
        }

        /// <summary>
        /// 画像の高さを取得
        /// </summary>
        public int Height
        {
            get
            {
                ThrowIfDisposed();
                try
                {
                    // _bitmapが有効であることを確認
                    if (_bitmap is Bitmap bitmap)
                    {
                        return bitmap.Height;
                    }
                    throw new InvalidOperationException("内部ビットマップが無効です");
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("画像の高さの取得に失敗しました", ex);
                }
            }
        }

        /// <summary>
        /// ネイティブImageオブジェクトを取得
        /// </summary>
        /// <returns>System.Drawing.Image インスタンス</returns>
        public Image GetNativeImage()
        {
            ThrowIfDisposed();
            try
           {
                // _bitmapが有効であることを確認
                if (_bitmap is Bitmap bitmap)
                {
                    return bitmap;
                }
                throw new InvalidOperationException("内部ビットマップが無効です");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("ネイティブイメージの取得に失敗しました", ex);
            }
        }

        /// <summary>
        /// Bitmapとして取得（async版）
        /// </summary>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <returns>System.Drawing.Bitmap インスタンス</returns>
        /// <remarks>🔥 [PHASE5.2] スレッドブロッキング防止のため追加</remarks>
        public Task<Bitmap> GetBitmapAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return Task.FromResult(_bitmap);
        }

        /// <summary>
        /// Bitmapとして取得
        /// </summary>
        /// <returns>System.Drawing.Bitmap インスタンス</returns>
        public Bitmap GetBitmap()
        {
            ThrowIfDisposed();
            return _bitmap;
        }

        /// <summary>
        /// ネイティブImageオブジェクトを取得（async版）
        /// </summary>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <returns>System.Drawing.Image インスタンス</returns>
        /// <remarks>🔥 [PHASE5.2] スレッドブロッキング防止のため追加</remarks>
        public Task<Image> GetNativeImageAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Image>(GetBitmap());
        }

        /// <summary>
        /// リソースを解放
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// リソースを解放
        /// </summary>
        /// <param name="disposing">trueの場合、マネージドとアンマネージドリソースを解放、falseの場合はアンマネージドリソースのみ</param>
        private void Dispose(bool disposing)
        {
            if (_disposed is true)
                return;

            if (disposing)
            {
                // マネージドリソースの解放
                _bitmap?.Dispose();
            }

            // アンマネージドリソースを解放するコードがあればここに記述

            _disposed = true;
        }

        /// <summary>
        /// オブジェクトが破棄済みの場合に例外をスロー
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (_disposed is true)
            {
                throw new ObjectDisposedException(nameof(WindowsImage), "このWindowsImageインスタンスは既に破棄されています");
            }
    
            // ビットマップがnullの場合も例外をスロー
            if (_bitmap is null)
            {
                _disposed = true; // 回復不能なので破棄済みとマーク
                throw new ObjectDisposedException(nameof(WindowsImage), "内部ビットマップリソースが無効です");
            }
        }
        
        /// <summary>
        /// 指定したパスに画像を保存
        /// </summary>
        /// <param name="path">保存先パス</param>
        /// <param name="format">画像フォーマット（省略時はPNG）</param>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <returns>非同期タスク</returns>
        /// <remarks>🔥 [PHASE5.2] CancellationToken追加</remarks>
        public async Task SaveAsync(string path, ImageFormat? format = null, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            // フォーマットが指定されていない場合はPNGを使用
            format ??= ImageFormat.Png;

            try
            {
                await Task.Run(() =>
                {
                // 保存前に再度チェック
                ObjectDisposedException.ThrowIf(_disposed is true || _bitmap is null, nameof(WindowsImage));

                _bitmap.Save(path, format);
                }).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not ObjectDisposedException)
            {
                throw new InvalidOperationException($"画像の保存に失敗しました: {path}", ex);
            }
        }
        
        /// <summary>
        /// 画像のサイズを変更
        /// </summary>
        /// <param name="width">新しい幅</param>
        /// <param name="height">新しい高さ</param>
        /// <returns>リサイズされた新しい画像インスタンス</returns>
        public async Task<IWindowsImage> ResizeAsync(int width, int height)
        {
            ThrowIfDisposed();
            
            return await Task.Run(() => 
            {
                var resizedBitmap = new Bitmap(_bitmap, width, height);
                return new WindowsImage(resizedBitmap);
            }).ConfigureAwait(false);
        }
        
        /// <summary>
        /// 画像の一部を切り取る
        /// </summary>
        /// <param name="rectangle">切り取る領域</param>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <returns>切り取られた新しい画像インスタンス</returns>
        /// <remarks>🔥 [PHASE5.2] CancellationToken追加</remarks>
        public async Task<IWindowsImage> CropAsync(Rectangle rectangle, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            
            return await Task.Run(() => 
            {
                // 範囲チェック
                if (rectangle.X < 0 || rectangle.Y < 0 || 
                    rectangle.X + rectangle.Width > _bitmap.Width || 
                    rectangle.Y + rectangle.Height > _bitmap.Height)
                {
                    throw new ArgumentOutOfRangeException(nameof(rectangle), "切り取り範囲が画像の範囲外です");
                }
                
                // 切り抜き
                var croppedBitmap = new Bitmap(rectangle.Width, rectangle.Height);
                using var g = Graphics.FromImage(croppedBitmap);
                g.DrawImage(_bitmap, 
                    new Rectangle(0, 0, rectangle.Width, rectangle.Height),
                    rectangle,
                    GraphicsUnit.Pixel);
                
                return new WindowsImage(croppedBitmap);
            }).ConfigureAwait(false);
        }

    /// <summary>
    /// 画像をバイト配列に変換
    /// </summary>
    /// <param name="format">画像フォーマット（省略時はPNG）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>画像データのバイト配列</returns>
    /// <remarks>🔥 [PHASE5.2] CancellationToken追加</remarks>
    public async Task<byte[]> ToByteArrayAsync(ImageFormat? format = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // フォーマットが指定されていない場合はPNGを使用
        format ??= ImageFormat.Png;

        try
        {
            return await Task.Run(() =>
            {
                // 実行前に再度チェック
                // IDE0083警告に対応するためパターンマッチングを使用
                ObjectDisposedException.ThrowIf(_disposed is true || _bitmap is null, nameof(WindowsImage));

                using var stream = new MemoryStream();
                try
                {
                    _bitmap.Save(stream, format);
                    return stream.ToArray();
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("画像のバイト配列変換中にエラーが発生しました", ex);
                }
            }).ConfigureAwait(false);
        }
            catch (Exception ex) when (ex is not ObjectDisposedException)
        {
            throw new InvalidOperationException("画像のバイト配列変換に失敗しました", ex);
        }
    }
}
