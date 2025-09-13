using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Memory;

/// <summary>
/// 安全な画像ライフサイクル管理インターフェース
/// ArrayPool&lt;byte&gt;を使用したメモリ効率的な画像データ管理
/// </summary>
public interface IImageLifecycleManager
{
    /// <summary>
    /// 生バイトデータからSafeImageを作成
    /// ArrayPool&lt;byte&gt;を使用してメモリ効率を最適化
    /// </summary>
    /// <param name="sourceData">元画像データ</param>
    /// <param name="width">画像幅</param>
    /// <param name="height">画像高さ</param>
    /// <param name="pixelFormat">ピクセルフォーマット</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>安全な画像オブジェクト</returns>
    Task<SafeImage> CreateSafeImageAsync(
        ReadOnlyMemory<byte> sourceData,
        int width,
        int height,
        ImagePixelFormat pixelFormat = ImagePixelFormat.Bgra32,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// SafeImageのクローンを作成（深いコピー）
    /// </summary>
    /// <param name="original">元画像</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>クローンされた画像</returns>
    Task<SafeImage> CloneImageAsync(
        SafeImage original,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 画像が有効な状態かを確認
    /// </summary>
    /// <param name="image">確認対象画像</param>
    /// <returns>有効な場合true</returns>
    bool IsImageValid(SafeImage image);

    /// <summary>
    /// 画像データのハッシュ値を計算（変更検知用）
    /// </summary>
    /// <param name="image">対象画像</param>
    /// <returns>SHA256ハッシュ値</returns>
    Task<string> ComputeImageHashAsync(SafeImage image);

    /// <summary>
    /// 管理中の画像数を取得（診断用）
    /// </summary>
    int ActiveImageCount { get; }

    /// <summary>
    /// メモリ使用量を取得（診断用）
    /// </summary>
    long TotalMemoryUsage { get; }
}

/// <summary>
/// 安全な画像クラス
/// ArrayPool&lt;byte&gt;を使用したメモリ効率的な実装
/// </summary>
public sealed class SafeImage : IDisposable
{
    private readonly byte[] _rentedBuffer;
    private readonly ArrayPool<byte> _arrayPool;
    private readonly int _actualDataLength;
    private bool _disposed;

    /// <summary>
    /// 画像の幅
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// 画像の高さ
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// ピクセルフォーマット
    /// </summary>
    public ImagePixelFormat PixelFormat { get; }

    /// <summary>
    /// 作成日時
    /// </summary>
    public DateTime CreatedAt { get; }

    /// <summary>
    /// 破棄済みかどうか
    /// </summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// 実際のデータサイズ
    /// </summary>
    public int DataLength => _actualDataLength;

    /// <summary>
    /// 内部コンストラクタ（IImageLifecycleManagerのみが作成可能）
    /// </summary>
    internal SafeImage(
        byte[] rentedBuffer,
        ArrayPool<byte> arrayPool,
        int actualDataLength,
        int width,
        int height,
        ImagePixelFormat pixelFormat)
    {
        _rentedBuffer = rentedBuffer ?? throw new ArgumentNullException(nameof(rentedBuffer));
        _arrayPool = arrayPool ?? throw new ArgumentNullException(nameof(arrayPool));
        _actualDataLength = actualDataLength;
        Width = width;
        Height = height;
        PixelFormat = pixelFormat;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 画像データの読み取り専用スパンを取得
    /// </summary>
    /// <returns>画像データの読み取り専用スパン</returns>
    /// <exception cref="ObjectDisposedException">オブジェクトが破棄済みの場合</exception>
    public ReadOnlySpan<byte> GetImageData()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new ReadOnlySpan<byte>(_rentedBuffer, 0, _actualDataLength);
    }

    /// <summary>
    /// 画像データの読み取り専用メモリを取得
    /// </summary>
    /// <returns>画像データの読み取り専用メモリ</returns>
    /// <exception cref="ObjectDisposedException">オブジェクトが破棄済みの場合</exception>
    public ReadOnlyMemory<byte> GetImageMemory()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new ReadOnlyMemory<byte>(_rentedBuffer, 0, _actualDataLength);
    }

    /// <summary>
    /// リソースの破棄
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        // セキュリティ要件：画像データに機密情報が含まれる場合のみクリアを実行
        // パフォーマンス最適化：通常のゲーム画像の場合はクリアを省略可能
        #if SECURE_IMAGE_DISPOSAL
        Array.Clear(_rentedBuffer, 0, _actualDataLength);
        #endif

        _arrayPool.Return(_rentedBuffer);
        _disposed = true;
    }
}

/// <summary>
/// 画像ピクセルフォーマット
/// </summary>
public enum ImagePixelFormat
{
    /// <summary>
    /// BGRA 32bit (Windows標準)
    /// </summary>
    Bgra32,

    /// <summary>
    /// RGBA 32bit
    /// </summary>
    Rgba32,

    /// <summary>
    /// RGB 24bit
    /// </summary>
    Rgb24,

    /// <summary>
    /// グレースケール 8bit
    /// </summary>
    Gray8
}