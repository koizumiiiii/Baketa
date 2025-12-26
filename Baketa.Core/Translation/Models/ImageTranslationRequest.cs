namespace Baketa.Core.Translation.Abstractions;

/// <summary>
/// 画像翻訳リクエスト
/// </summary>
public sealed class ImageTranslationRequest
{
    /// <summary>
    /// リクエストID（重複防止・トレース用）
    /// </summary>
    public string RequestId { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 画像データ（Base64エンコード）
    /// </summary>
    public required string ImageBase64 { get; init; }

    /// <summary>
    /// 画像MIMEタイプ（"image/png", "image/jpeg"）
    /// </summary>
    public string MimeType { get; init; } = "image/png";

    /// <summary>
    /// ソース言語コード（"auto"で自動検出）
    /// </summary>
    public string SourceLanguage { get; init; } = "auto";

    /// <summary>
    /// ターゲット言語コード
    /// </summary>
    public required string TargetLanguage { get; init; }

    /// <summary>
    /// 画像幅（トークン推定用）
    /// </summary>
    public int Width { get; init; }

    /// <summary>
    /// 画像高さ（トークン推定用）
    /// </summary>
    public int Height { get; init; }

    /// <summary>
    /// 翻訳コンテキスト（ゲームジャンル等）
    /// </summary>
    public string? Context { get; init; }

    /// <summary>
    /// セッショントークン（認証用）
    /// </summary>
    public required string SessionToken { get; init; }

    /// <summary>
    /// バイト配列からリクエストを作成
    /// </summary>
    public static ImageTranslationRequest FromBytes(
        byte[] imageData,
        string targetLanguage,
        string sessionToken,
        int width = 0,
        int height = 0,
        string mimeType = "image/png",
        string? context = null)
    {
        return new ImageTranslationRequest
        {
            ImageBase64 = Convert.ToBase64String(imageData),
            TargetLanguage = targetLanguage,
            SessionToken = sessionToken,
            Width = width,
            Height = height,
            MimeType = mimeType,
            Context = context
        };
    }
}
