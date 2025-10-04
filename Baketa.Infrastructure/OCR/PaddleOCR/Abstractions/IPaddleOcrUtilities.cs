using OpenCvSharp;

namespace Baketa.Infrastructure.OCR.PaddleOCR.Abstractions;

/// <summary>
/// ユーティリティメソッド、テスト環境判定、ログ出力を担当するサービス
/// </summary>
public interface IPaddleOcrUtilities
{
    /// <summary>
    /// テスト環境判定
    /// </summary>
    bool IsTestEnvironment();

    /// <summary>
    /// ダミーMat作成
    /// </summary>
    Mat CreateDummyMat();

    /// <summary>
    /// デバッグログパス取得
    /// </summary>
    string GetDebugLogPath();

    /// <summary>
    /// 安全なデバッグログ書き込み
    /// </summary>
    void SafeWriteDebugLog(string message);
}
