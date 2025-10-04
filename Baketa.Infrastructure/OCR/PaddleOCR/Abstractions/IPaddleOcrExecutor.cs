using OpenCvSharp;
using Sdcb.PaddleOCR;

namespace Baketa.Infrastructure.OCR.PaddleOCR.Abstractions;

/// <summary>
/// 実際のPaddleOCR実行、タイムアウト管理、リトライ処理を担当するサービス
/// </summary>
public interface IPaddleOcrExecutor
{
    /// <summary>
    /// OCR実行（認識付き）
    /// </summary>
    Task<PaddleOcrResult[]> ExecuteOcrAsync(Mat processedMat, IProgress<OcrProgress>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// 検出専用OCR実行
    /// </summary>
    Task<PaddleOcrResult[]> ExecuteDetectionOnlyAsync(Mat processedMat, CancellationToken cancellationToken);

    /// <summary>
    /// 現在のOCRタイムアウトをキャンセル
    /// </summary>
    void CancelCurrentOcrTimeout();
}

/// <summary>
/// OCR進捗情報
/// </summary>
public record OcrProgress(int Current, int Total, string Message);
