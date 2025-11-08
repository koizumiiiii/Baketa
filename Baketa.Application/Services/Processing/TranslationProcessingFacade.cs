using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Processing;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.UI;
using Baketa.Core.Abstractions.UI.Overlays; // 🔧 [OVERLAY_UNIFICATION]

namespace Baketa.Application.Services.Processing;

/// <summary>
/// 翻訳処理ファサード実装
/// 翻訳に関連するサービス群を統合管理し、依存関係注入の複雑さを軽減
/// </summary>
public sealed class TranslationProcessingFacade(
    IBatchOcrProcessor ocrProcessor,
    ITranslationService translationService,
    // 🔧 [OVERLAY_UNIFICATION] IInPlaceTranslationOverlayManager → IOverlayManager に統一
    IOverlayManager overlayManager) : ITranslationProcessingFacade
{
    /// <inheritdoc />
    public IBatchOcrProcessor OcrProcessor { get; } = ocrProcessor ?? throw new ArgumentNullException(nameof(ocrProcessor));

    /// <inheritdoc />
    public ITranslationService TranslationService { get; } = translationService ?? throw new ArgumentNullException(nameof(translationService));

    /// <inheritdoc />
    // 🔧 [OVERLAY_UNIFICATION] IInPlaceTranslationOverlayManager → IOverlayManager に統一
    public IOverlayManager OverlayManager { get; } = overlayManager ?? throw new ArgumentNullException(nameof(overlayManager));
}
