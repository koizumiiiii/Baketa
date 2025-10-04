using Baketa.Core.Abstractions.OCR;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models;

namespace Baketa.Infrastructure.OCR.PaddleOCR.Abstractions;

/// <summary>
/// PaddleOcrAllエンジンの初期化、設定適用、ウォームアップを担当するサービス
/// </summary>
public interface IPaddleOcrEngineInitializer
{
    /// <summary>
    /// エンジン初期化
    /// </summary>
    Task<bool> InitializeEnginesAsync(FullOcrModel models, OcrEngineSettings settings, CancellationToken cancellationToken);

    /// <summary>
    /// ウォームアップ
    /// </summary>
    Task<bool> WarmupAsync(CancellationToken cancellationToken);

    /// <summary>
    /// ネイティブライブラリチェック
    /// </summary>
    bool CheckNativeLibraries();

    /// <summary>
    /// OCRエンジン取得
    /// </summary>
    PaddleOcrAll? GetOcrEngine();

    /// <summary>
    /// キューイング型OCRエンジン取得
    /// </summary>
    QueuedPaddleOcrAll? GetQueuedEngine();
}
