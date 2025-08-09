using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Processing;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.UI;

namespace Baketa.Application.Services.Processing;

/// <summary>
/// 翻訳処理ファサード実装
/// 翻訳に関連するサービス群を統合管理し、依存関係注入の複雑さを軽減
/// </summary>
public sealed class TranslationProcessingFacade : ITranslationProcessingFacade
{
    /// <inheritdoc />
    public IBatchOcrProcessor OcrProcessor { get; }
    
    /// <inheritdoc />
    public ITranslationService TranslationService { get; }
    
    /// <inheritdoc />
    public IInPlaceTranslationOverlayManager OverlayManager { get; }

    public TranslationProcessingFacade(
        IBatchOcrProcessor ocrProcessor,
        ITranslationService translationService,
        IInPlaceTranslationOverlayManager overlayManager)
    {
        OcrProcessor = ocrProcessor ?? throw new ArgumentNullException(nameof(ocrProcessor));
        TranslationService = translationService ?? throw new ArgumentNullException(nameof(translationService));
        OverlayManager = overlayManager ?? throw new ArgumentNullException(nameof(overlayManager));
    }
}