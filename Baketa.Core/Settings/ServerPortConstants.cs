namespace Baketa.Core.Settings;

/// <summary>
/// サーバーポート定数
/// Issue #292: 統合サーバーモード対応でポート番号を一元管理
/// </summary>
public static class ServerPortConstants
{
    /// <summary>
    /// 翻訳サーバー (NLLB-200 gRPC) のデフォルトポート
    /// </summary>
    public const int TranslationServerPort = 50051;

    /// <summary>
    /// Surya OCRサーバーのデフォルトポート
    /// </summary>
    public const int OcrServerPort = 50052;

    /// <summary>
    /// 統合サーバー (OCR + 翻訳) のデフォルトポート
    /// UnifiedServerSettings.DefaultPort と同値
    /// </summary>
    public const int UnifiedServerPort = 50053;

    /// <summary>
    /// 翻訳サーバーのデフォルトアドレス
    /// </summary>
    public const string TranslationServerAddress = "http://localhost:50051";

    /// <summary>
    /// OCRサーバーのデフォルトアドレス
    /// </summary>
    public const string OcrServerAddress = "http://localhost:50052";

    /// <summary>
    /// 統合サーバーのデフォルトアドレス
    /// </summary>
    public const string UnifiedServerAddress = "http://localhost:50053";
}
