using OpenCvSharp;
using Sdcb.PaddleOCR;
using CoreOcrProgress = Baketa.Core.Abstractions.OCR.OcrProgress;

namespace Baketa.Infrastructure.OCR.PaddleOCR.Abstractions;

/// <summary>
/// 実際のPaddleOCR実行、タイムアウト管理、リトライ処理を担当するサービス
/// </summary>
public interface IPaddleOcrExecutor
{
    /// <summary>
    /// OCR実行（認識付き）
    /// ✅ [PHASE2.9.3.3] 型統一: Infrastructure独自OcrProgressを廃止しCore層に統一
    /// </summary>
    Task<PaddleOcrResult> ExecuteOcrAsync(Mat processedMat, IProgress<CoreOcrProgress>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// 検出専用OCR実行
    /// </summary>
    Task<PaddleOcrResult> ExecuteDetectionOnlyAsync(Mat processedMat, CancellationToken cancellationToken);

    /// <summary>
    /// 現在のOCRタイムアウトをキャンセル
    /// </summary>
    void CancelCurrentOcrTimeout();
}
