using System;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Memory;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.Services.Memory;

/// <summary>
/// 参照カウント付きSafeImage生成ファクトリの実装
/// 既存のImageLifecycleManagerと統合してSafeImageの早期破棄問題を解決
///
/// Phase 3.11: UltraThink設計による参照カウント機能
/// - 既存システムとの互換性維持
/// - SmartProcessingPipelineServiceでの段階的処理対応
/// - メモリ効率とThread Safety確保
/// </summary>
public sealed class ReferencedSafeImageFactory : IReferencedSafeImageFactory
{
    private readonly IImageLifecycleManager _imageLifecycleManager;
    private readonly ILogger<ReferencedSafeImageFactory> _logger;

    /// <summary>
    /// ImageLifecycleManagerとロガーを注入してファクトリを初期化
    /// </summary>
    public ReferencedSafeImageFactory(
        IImageLifecycleManager imageLifecycleManager,
        ILogger<ReferencedSafeImageFactory> logger)
    {
        _imageLifecycleManager = imageLifecycleManager ?? throw new ArgumentNullException(nameof(imageLifecycleManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogInformation("ReferencedSafeImageFactory initialized with ImageLifecycleManager integration");
    }

    /// <summary>
    /// 生バイトデータから参照カウント付きSafeImageを作成
    /// 内部でImageLifecycleManagerを使用してSafeImageを生成し、
    /// ReferencedSafeImageでラップして参照カウント機能を追加
    /// </summary>
    public async Task<ReferencedSafeImage> CreateReferencedSafeImageAsync(
        ReadOnlyMemory<byte> sourceData,
        int width,
        int height,
        ImagePixelFormat pixelFormat = ImagePixelFormat.Bgra32,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 既存のImageLifecycleManagerを使用してSafeImageを作成
            var safeImage = await _imageLifecycleManager.CreateSafeImageAsync(
                sourceData, width, height, pixelFormat, cancellationToken).ConfigureAwait(false);

            // ReferencedSafeImageでラップして参照カウント機能を追加
            var referencedSafeImage = new ReferencedSafeImage(safeImage);

            _logger.LogDebug("Created ReferencedSafeImage: {Width}x{Height}, {Size} bytes, Format: {Format}, ReferenceCount: {RefCount}",
                width, height, sourceData.Length, pixelFormat, referencedSafeImage.ReferenceCount);

            return referencedSafeImage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create ReferencedSafeImage: {Width}x{Height}, {Size} bytes",
                width, height, sourceData.Length);
            throw;
        }
    }

    /// <summary>
    /// 既存のSafeImageから参照カウント付きSafeImageを作成
    /// 既存のSafeImageの所有権はReferencedSafeImageに移譲される
    /// 元のSafeImageを直接Disposeしてはいけない
    /// </summary>
    public ReferencedSafeImage CreateFromSafeImage(SafeImage safeImage)
    {
        ArgumentNullException.ThrowIfNull(safeImage);

        if (safeImage.IsDisposed)
        {
            throw new ObjectDisposedException(nameof(safeImage), "Cannot create ReferencedSafeImage from disposed SafeImage");
        }

        try
        {
            var referencedSafeImage = new ReferencedSafeImage(safeImage);

            _logger.LogDebug("Created ReferencedSafeImage from existing SafeImage: {Width}x{Height}, Format: {Format}, ReferenceCount: {RefCount}",
                safeImage.Width, safeImage.Height, safeImage.PixelFormat, referencedSafeImage.ReferenceCount);

            return referencedSafeImage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create ReferencedSafeImage from existing SafeImage: {Width}x{Height}",
                safeImage.Width, safeImage.Height);
            throw;
        }
    }

    /// <summary>
    /// 参照カウント付きSafeImageのクローンを作成
    /// 元のインスタンスとは独立した新しい参照カウント付きSafeImage
    /// 深いコピーを実行して完全に独立したインスタンスを作成
    /// </summary>
    public async Task<ReferencedSafeImage> CloneReferencedSafeImageAsync(
        ReferencedSafeImage original,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(original);

        if (original.IsDisposed)
        {
            throw new ObjectDisposedException(nameof(original), "Cannot clone disposed ReferencedSafeImage");
        }

        try
        {
            // 元のReferencedSafeImageから内部のSafeImageを取得
            var originalSafeImage = original.GetUnderlyingSafeImage();

            // ImageLifecycleManagerを使用してクローンを作成
            var clonedSafeImage = await _imageLifecycleManager.CloneImageAsync(
                originalSafeImage, cancellationToken).ConfigureAwait(false);

            // クローンされたSafeImageを新しいReferencedSafeImageでラップ
            var clonedReferencedSafeImage = new ReferencedSafeImage(clonedSafeImage);

            _logger.LogDebug("Cloned ReferencedSafeImage: {Width}x{Height}, Format: {Format}, Original RefCount: {OriginalRefCount}, Clone RefCount: {CloneRefCount}",
                original.Width, original.Height, original.PixelFormat,
                original.ReferenceCount, clonedReferencedSafeImage.ReferenceCount);

            return clonedReferencedSafeImage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clone ReferencedSafeImage: {Width}x{Height}",
                original.Width, original.Height);
            throw;
        }
    }
}