namespace Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece;

/// <summary>
/// SentencePieceトークナイザーの設定オプション
/// </summary>
public class SentencePieceOptions
{
    /// <summary>
    /// モデルファイルの保存ディレクトリ
    /// </summary>
    public string ModelsDirectory { get; set; } = "Models/SentencePiece";

    /// <summary>
    /// デフォルトで使用するモデル名
    /// </summary>
    public string DefaultModel { get; set; } = "opus-mt-ja-en";

    /// <summary>
    /// モデルダウンロード用のURL（{0}にモデル名が入る）
    /// </summary>
    public string DownloadUrl { get; set; } = "https://your-storage.blob.core.windows.net/models/{0}.model";

    /// <summary>
    /// モデルキャッシュの有効期限（日数）
    /// </summary>
    public int ModelCacheDays { get; set; } = 30;

    /// <summary>
    /// ダウンロード時の最大リトライ回数
    /// </summary>
    public int MaxDownloadRetries { get; set; } = 3;

    /// <summary>
    /// ダウンロードタイムアウト（分）
    /// </summary>
    public int DownloadTimeoutMinutes { get; set; } = 5;

    /// <summary>
    /// 最大入力テキスト長
    /// </summary>
    public int MaxInputLength { get; set; } = 10000;

    /// <summary>
    /// チェックサム検証を有効にするかどうか
    /// </summary>
    public bool EnableChecksumValidation { get; set; } = true;

    /// <summary>
    /// 自動クリーンアップを有効にするかどうか
    /// </summary>
    public bool EnableAutoCleanup { get; set; } = true;

    /// <summary>
    /// 未使用モデルのクリーンアップ閾値（日数）
    /// </summary>
    public int CleanupThresholdDays { get; set; } = 90;
}
