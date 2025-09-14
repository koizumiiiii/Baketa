using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Memory;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.Services.Memory;

/// <summary>
/// 安全な画像ライフサイクル管理の実装（Phase 2暫定版）
/// ArrayPool&lt;byte&gt;を使用したメモリ効率的な画像データ管理
///
/// ⚠️ 重要なリスク：メモリリーク防止のため、生成したSafeImageは必ずDisposeすること
/// 呼び出し側でusing文などを使用して、SafeImageの破棄を100%保証する必要があります
/// </summary>
public sealed class ImageLifecycleManager : IImageLifecycleManager, IDisposable
{
    private readonly ArrayPool<byte> _arrayPool;
    private readonly ISafeImageFactory _safeImageFactory;
    private readonly ILogger<ImageLifecycleManager> _logger;
    private readonly ConcurrentDictionary<Guid, SafeImageInfo> _activeImages;

    private long _totalMemoryUsage;
    private bool _disposed;

    /// <summary>
    /// SafeImageFactory、ArrayPoolとロガーを注入してImageLifecycleManagerを初期化
    /// </summary>
    public ImageLifecycleManager(
        ISafeImageFactory safeImageFactory,
        ILogger<ImageLifecycleManager> logger)
    {
        _safeImageFactory = safeImageFactory ?? throw new ArgumentNullException(nameof(safeImageFactory));
        _arrayPool = ArrayPool<byte>.Shared;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _activeImages = new ConcurrentDictionary<Guid, SafeImageInfo>();

        _logger.LogInformation("ImageLifecycleManager initialized with SafeImageFactory and ArrayPool<byte>.Shared");
    }

    /// <summary>
    /// 管理中の画像数を取得（診断用）
    /// </summary>
    public int ActiveImageCount => _activeImages.Count;

    /// <summary>
    /// メモリ使用量を取得（診断用）
    /// </summary>
    public long TotalMemoryUsage => Interlocked.Read(ref _totalMemoryUsage);

    /// <summary>
    /// 生バイトデータからSafeImageを作成（Phase 2暫定実装）
    /// ArrayPool&lt;byte&gt;を使用してメモリ効率を最適化
    /// </summary>
    public async Task<SafeImage> CreateSafeImageAsync(
        ReadOnlyMemory<byte> sourceData,
        int width,
        int height,
        ImagePixelFormat pixelFormat = ImagePixelFormat.Bgra32,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (sourceData.Length == 0)
            throw new ArgumentException("Source data cannot be empty", nameof(sourceData));

        if (width <= 0 || height <= 0)
            throw new ArgumentException("Width and height must be positive");

        await Task.CompletedTask.ConfigureAwait(false);

        var imageId = Guid.NewGuid();
        var rentedBuffer = _arrayPool.Rent(sourceData.Length);

        try
        {
            // データをArrayPoolから借りたバッファにコピー
            sourceData.Span.CopyTo(rentedBuffer);

            // Phase 3: SafeImageFactoryを使用して安全にインスタンス生成
            var safeImage = _safeImageFactory.CreateSafeImage(rentedBuffer, _arrayPool, sourceData.Length, width, height, pixelFormat, imageId);

            var imageInfo = new SafeImageInfo
            {
                Id = imageId,
                CreatedAt = DateTime.UtcNow,
                Size = sourceData.Length,
                Width = width,
                Height = height,
                PixelFormat = pixelFormat
            };

            _activeImages[imageId] = imageInfo;
            Interlocked.Add(ref _totalMemoryUsage, sourceData.Length);

            _logger.LogDebug("Created SafeImage {ImageId}: {Width}x{Height}, {Size} bytes, Format: {Format}",
                imageId, width, height, sourceData.Length, pixelFormat);

            return safeImage;
        }
        catch
        {
            // エラー時はバッファを返却
            _arrayPool.Return(rentedBuffer);
            throw;
        }
    }

    /// <summary>
    /// SafeImageのクローンを作成（深いコピー）
    /// </summary>
    public async Task<SafeImage> CloneImageAsync(
        SafeImage original,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(original);

        ObjectDisposedException.ThrowIf(_disposed, this);

        if (original.IsDisposed)
            throw new ObjectDisposedException(nameof(original), "Cannot clone disposed SafeImage");

        await Task.CompletedTask.ConfigureAwait(false);

        var originalData = original.GetImageMemory();
        return await CreateSafeImageAsync(
            originalData,
            original.Width,
            original.Height,
            original.PixelFormat,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 画像が有効な状態かを確認
    /// </summary>
    public bool IsImageValid(SafeImage image)
    {
        if (image == null) return false;
        if (image.IsDisposed) return false;

        // Phase 2暫定実装：基本的な検証のみ
        return true;
    }

    /// <summary>
    /// 画像データのハッシュ値を計算（変更検知用）
    /// </summary>
    public async Task<string> ComputeImageHashAsync(SafeImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        ObjectDisposedException.ThrowIf(_disposed, this);

        if (image.IsDisposed)
            throw new ObjectDisposedException(nameof(image), "Cannot compute hash of disposed SafeImage");

        await Task.CompletedTask.ConfigureAwait(false);

        using var sha256 = SHA256.Create();
        var imageMemory = image.GetImageMemory();
        var hashBytes = sha256.ComputeHash(imageMemory.ToArray());

        return Convert.ToHexString(hashBytes);
    }

    #region IDisposable

    public void Dispose()
    {
        if (_disposed) return;

        _logger.LogInformation("Disposing ImageLifecycleManager. Active images: {Count}", _activeImages.Count);

        if (_activeImages.Count > 0)
        {
            _logger.LogWarning("Disposing with {Count} active images. Potential memory leaks detected",
                _activeImages.Count);
        }

        _activeImages.Clear();
        _disposed = true;
    }

    #endregion

    /// <summary>
    /// アクティブな画像情報（Phase 2暫定版）
    /// </summary>
    private sealed record SafeImageInfo
    {
        public required Guid Id { get; init; }
        public required DateTime CreatedAt { get; init; }
        public required int Size { get; init; }
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required ImagePixelFormat PixelFormat { get; init; }
    }
}