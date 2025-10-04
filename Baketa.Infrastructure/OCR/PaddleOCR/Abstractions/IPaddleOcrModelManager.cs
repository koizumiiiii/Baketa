using Sdcb.PaddleOCR.Models;

namespace Baketa.Infrastructure.OCR.PaddleOCR.Abstractions;

/// <summary>
/// モデル管理、モデルロード、モデル選択を担当するサービス
/// </summary>
public interface IPaddleOcrModelManager
{
    /// <summary>
    /// モデル準備
    /// </summary>
    Task<FullOcrModel?> PrepareModelsAsync(string language, CancellationToken cancellationToken);

    /// <summary>
    /// PP-OCRv5モデル作成試行
    /// </summary>
    Task<FullOcrModel?> TryCreatePPOCRv5ModelAsync(string language, CancellationToken cancellationToken);

    /// <summary>
    /// 言語別デフォルトモデル取得
    /// </summary>
    FullOcrModel? GetDefaultModelForLanguage(string language);

    /// <summary>
    /// V5モデル検出
    /// </summary>
    bool DetectIfV5Model(FullOcrModel model);
}
