using System;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Memory;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Services.Memory;

/// <summary>
/// IImage → ReferencedSafeImage型変換コンバーターの実装
/// Phase 3.14: UltraThink設計による効率的な型変換ブリッジ
///
/// 実装方針:
/// - IImageToSafeImageConverterと連携してSafeImage作成
/// - IReferencedSafeImageFactoryでReferencedSafeImageにラップ
/// - ArrayPool<byte>によるメモリ効率化を継承
/// </summary>
public sealed class ImageToReferencedSafeImageConverter : IImageToReferencedSafeImageConverter
{
    private readonly IImageToSafeImageConverter _safeImageConverter;
    private readonly IReferencedSafeImageFactory _referencedFactory;
    private readonly ILogger<ImageToReferencedSafeImageConverter> _logger;

    public ImageToReferencedSafeImageConverter(
        IImageToSafeImageConverter safeImageConverter,
        IReferencedSafeImageFactory referencedFactory,
        ILogger<ImageToReferencedSafeImageConverter> logger)
    {
        _safeImageConverter = safeImageConverter ?? throw new ArgumentNullException(nameof(safeImageConverter));
        _referencedFactory = referencedFactory ?? throw new ArgumentNullException(nameof(referencedFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogDebug("🎯 [PHASE3.14] ImageToReferencedSafeImageConverter初期化完了");
    }

    /// <inheritdoc/>
    public async Task<ReferencedSafeImage> ConvertAsync(
        IImage image,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);

        _logger.LogDebug("🔄 [PHASE3.14] IImage → ReferencedSafeImage変換開始 (Width: {Width}, Height: {Height})",
            image.Width, image.Height);

        try
        {
            // Step 1: IImage → SafeImage変換（効率的なSpan.CopyTo使用）
            var safeImage = await _safeImageConverter.ConvertAsync(image).ConfigureAwait(false);

            // Step 2: SafeImage → ReferencedSafeImage（参照カウント付きラップ）
            var referencedImage = _referencedFactory.CreateFromSafeImage(safeImage);

            _logger.LogDebug("✅ [PHASE3.14] ReferencedSafeImage作成完了 (RefCount: 1)");

            return referencedImage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE3.14] IImage → ReferencedSafeImage変換エラー");
            throw;
        }
    }

    /// <inheritdoc/>
    public ReferencedSafeImage Convert(IImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        _logger.LogDebug("🔄 [PHASE3.14] IImage → ReferencedSafeImage同期変換開始");

        try
        {
            // Step 1: IImage → SafeImage同期変換
            var safeImage = _safeImageConverter.Convert(image);

            // Step 2: SafeImage → ReferencedSafeImage
            var referencedImage = _referencedFactory.CreateFromSafeImage(safeImage);

            _logger.LogDebug("✅ [PHASE3.14] ReferencedSafeImage同期作成完了");

            return referencedImage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE3.14] IImage → ReferencedSafeImage同期変換エラー");
            throw;
        }
    }

    /// <inheritdoc/>
    public ReferencedSafeImage ConvertFromSafeImage(SafeImage safeImage)
    {
        ArgumentNullException.ThrowIfNull(safeImage);

        _logger.LogDebug("🔄 [PHASE3.14] SafeImage → ReferencedSafeImage直接変換");

        try
        {
            // SafeImageの所有権をReferencedSafeImageに移譲
            var referencedImage = _referencedFactory.CreateFromSafeImage(safeImage);

            _logger.LogDebug("✅ [PHASE3.14] SafeImage → ReferencedSafeImage変換完了");

            return referencedImage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE3.14] SafeImage → ReferencedSafeImage変換エラー");
            throw;
        }
    }
}
