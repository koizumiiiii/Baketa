using OpenCvSharp;

namespace Baketa.Infrastructure.OCR.PaddleOCR.Abstractions;

/// <summary>
/// エラー診断、エラーメッセージ生成、解決策提案を担当するサービス
/// </summary>
public interface IPaddleOcrErrorHandler
{
    /// <summary>
    /// エラー情報収集
    /// </summary>
    string CollectErrorInfo(Mat mat, Exception ex);

    /// <summary>
    /// エラー解決策生成
    /// </summary>
    string GenerateErrorSuggestion(string errorMessage);

    /// <summary>
    /// エラーからのリカバリー試行
    /// </summary>
    Task<bool> TryRecoverFromError(Exception ex, Func<Task<bool>> retryAction);
}
