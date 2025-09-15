using System;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;

namespace Baketa.Core.Abstractions.Memory;

/// <summary>
/// 参照カウント付きSafeImage管理クラス
/// SmartProcessingPipelineServiceでの段階的処理における
/// SafeImageの早期破棄問題を解決するためのWrapper
///
/// Phase 3.11: UltraThink設計による参照カウント機能実装
/// - Thread-safe参照カウント管理
/// - 自動的なリソース解放
/// - Clean Architecture準拠
/// </summary>
public sealed class ReferencedSafeImage : IImage, IDisposable
{
    private readonly SafeImage _safeImage;
    private readonly object _lockObject = new();
    private int _referenceCount;
    private bool _disposed;

    /// <summary>
    /// SafeImageの幅
    /// </summary>
    public int Width => _safeImage.Width;

    /// <summary>
    /// SafeImageの高さ
    /// </summary>
    public int Height => _safeImage.Height;

    /// <summary>
    /// ピクセルフォーマット
    /// </summary>
    public ImagePixelFormat PixelFormat => _safeImage.PixelFormat;

    /// <summary>
    /// 画像フォーマット（IImageBase互換）
    /// </summary>
    public ImageFormat Format
    {
        get
        {
            // ImagePixelFormatからImageFormatへの変換
            return _safeImage.PixelFormat switch
            {
                ImagePixelFormat.Rgb24 => ImageFormat.Rgb24,
                ImagePixelFormat.Rgba32 => ImageFormat.Rgba32,
                ImagePixelFormat.Bgra32 => ImageFormat.Rgba32, // BGRA32をRGBA32にマップ
                _ => ImageFormat.Unknown
            };
        }
    }

    /// <summary>
    /// 作成日時
    /// </summary>
    public DateTime CreatedAt => _safeImage.CreatedAt;

    /// <summary>
    /// 破棄済みかどうか
    /// </summary>
    public bool IsDisposed
    {
        get
        {
            lock (_lockObject)
            {
                return _disposed || _safeImage.IsDisposed;
            }
        }
    }

    /// <summary>
    /// 実際のデータサイズ
    /// </summary>
    public int DataLength => _safeImage.DataLength;

    /// <summary>
    /// 現在の参照カウント（デバッグ用）
    /// </summary>
    public int ReferenceCount
    {
        get
        {
            lock (_lockObject)
            {
                return _referenceCount;
            }
        }
    }

    /// <summary>
    /// SafeImageから参照カウント付きインスタンスを作成
    /// 初期参照カウントは1
    /// </summary>
    public ReferencedSafeImage(SafeImage safeImage)
    {
        _safeImage = safeImage ?? throw new ArgumentNullException(nameof(safeImage));
        _referenceCount = 1;
    }

    /// <summary>
    /// 参照カウントを増加
    /// SmartProcessingPipelineServiceの各段階で呼び出される
    /// </summary>
    /// <returns>参照を追加した同一インスタンス</returns>
    /// <exception cref="ObjectDisposedException">既に破棄済みの場合</exception>
    public ReferencedSafeImage AddReference()
    {
        lock (_lockObject)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _referenceCount++;
            return this;
        }
    }

    /// <summary>
    /// 参照カウントを減少
    /// カウントが0になった場合、内部のSafeImageを破棄
    /// </summary>
    public void ReleaseReference()
    {
        lock (_lockObject)
        {
            if (_disposed) return;

            _referenceCount--;
            if (_referenceCount <= 0)
            {
                _safeImage.Dispose();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// 画像データの読み取り専用スパンを取得
    /// 参照カウント管理により安全にアクセス可能
    /// </summary>
    /// <returns>画像データの読み取り専用スパン</returns>
    /// <exception cref="ObjectDisposedException">オブジェクトが破棄済みの場合</exception>
    public ReadOnlySpan<byte> GetImageData()
    {
        lock (_lockObject)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _safeImage.GetImageData();
        }
    }

    /// <summary>
    /// 画像データの読み取り専用メモリを取得
    /// 参照カウント管理により安全にアクセス可能
    /// </summary>
    /// <returns>画像データの読み取り専用メモリ</returns>
    /// <exception cref="ObjectDisposedException">オブジェクトが破棄済みの場合</exception>
    public ReadOnlyMemory<byte> GetImageMemory()
    {
        lock (_lockObject)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _safeImage.GetImageMemory();
        }
    }

    /// <summary>
    /// 内部のSafeImageインスタンスへの安全なアクセス
    /// 参照カウント管理により破棄されることはない
    /// </summary>
    /// <returns>内部のSafeImageインスタンス</returns>
    /// <exception cref="ObjectDisposedException">オブジェクトが破棄済みの場合</exception>
    public SafeImage GetUnderlyingSafeImage()
    {
        lock (_lockObject)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _safeImage;
        }
    }

    /// <summary>
    /// 画像データのバイト配列への変換（IImageBase互換）
    /// </summary>
    public async Task<byte[]> ToByteArrayAsync()
    {
        lock (_lockObject)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _safeImage.GetImageMemory().ToArray();
        }
    }

    /// <summary>
    /// 画像のクローン作成（IImage互換）
    /// </summary>
    public IImage Clone()
    {
        lock (_lockObject)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            // 新しい参照を追加して同じReferencedSafeImageを返す
            return AddReference();
        }
    }

    /// <summary>
    /// 画像のリサイズ（IImage互換）
    /// </summary>
    public async Task<IImage> ResizeAsync(int width, int height)
    {
        lock (_lockObject)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            // SafeImageに対応するリサイズ機能がないため、NotSupportedExceptionをスロー
            throw new NotSupportedException("ReferencedSafeImage does not support resizing. Use factory methods instead.");
        }
    }

    /// <summary>
    /// リソースの破棄
    /// 参照カウントを強制的に0にして内部SafeImageを破棄
    /// </summary>
    public void Dispose()
    {
        lock (_lockObject)
        {
            if (_disposed) return;

            _safeImage.Dispose();
            _disposed = true;
            _referenceCount = 0;
        }
    }
}