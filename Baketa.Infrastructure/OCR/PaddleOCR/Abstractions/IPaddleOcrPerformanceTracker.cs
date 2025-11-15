using Baketa.Core.Abstractions.OCR;
using OpenCvSharp;

namespace Baketa.Infrastructure.OCR.PaddleOCR.Abstractions;

/// <summary>
/// パフォーマンス統計、タイムアウト管理、エラー追跡を担当するサービス
/// </summary>
public interface IPaddleOcrPerformanceTracker
{
    /// <summary>
    /// パフォーマンス統計更新
    /// </summary>
    void UpdatePerformanceStats(double processingTimeMs, bool success);

    /// <summary>
    /// パフォーマンス統計取得
    /// </summary>
    OcrPerformanceStats GetPerformanceStats();

    /// <summary>
    /// タイムアウト計算
    /// </summary>
    int CalculateTimeout(Mat mat);

    /// <summary>
    /// 適応的タイムアウト取得
    /// </summary>
    int GetAdaptiveTimeout(int baseTimeout);

    /// <summary>
    /// 失敗カウンタリセット
    /// </summary>
    void ResetFailureCounter();

    /// <summary>
    /// 連続失敗数取得
    /// </summary>
    int GetConsecutiveFailureCount();
}
