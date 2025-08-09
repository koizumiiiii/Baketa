using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.UI;

namespace Baketa.Core.Abstractions.Processing;

/// <summary>
/// 翻訳処理に関連するサービス群のファサード
/// 単一責任原則に基づいて翻訳処理の依存関係を統合管理
/// </summary>
public interface ITranslationProcessingFacade
{
    /// <summary>
    /// バッチOCR処理サービス
    /// </summary>
    IBatchOcrProcessor OcrProcessor { get; }
    
    /// <summary>
    /// 翻訳サービス
    /// </summary>
    ITranslationService TranslationService { get; }
    
    /// <summary>
    /// インプレース翻訳オーバーレイマネージャー
    /// </summary>
    IInPlaceTranslationOverlayManager OverlayManager { get; }
}