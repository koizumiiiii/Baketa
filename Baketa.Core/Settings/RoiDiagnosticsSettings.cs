namespace Baketa.Core.Settings;

/// <summary>
/// ROI診断・画像出力設定
/// </summary>
public sealed class RoiDiagnosticsSettings
{
    /// <summary>
    /// ROI画像出力を有効にするか
    /// </summary>
    public bool EnableRoiImageOutput { get; set; } = true;

    /// <summary>
    /// ROI画像出力パス（環境変数展開対応）
    /// </summary>
    public string RoiImageOutputPath { get; set; } = "%AppData%\\Baketa\\ROI\\Images";

    /// <summary>
    /// ROI画像フォーマット（PNG/JPEG）
    /// </summary>
    public string RoiImageFormat { get; set; } = "PNG";

    /// <summary>
    /// 高度診断機能を有効にするか
    /// </summary>
    public bool EnableAdvancedDiagnostics { get; set; } = true;

    /// <summary>
    /// 画像ファイル保持日数
    /// </summary>
    public int MaxImageFileRetentionDays { get; set; } = 7;

    /// <summary>
    /// メタデータ出力を有効にするか
    /// </summary>
    public bool EnableMetadataOutput { get; set; } = true;

    /// <summary>
    /// 注釈付き画像（テキスト領域ハイライト）を有効にするか
    /// </summary>
    public bool EnableAnnotatedImages { get; set; } = true;

    /// <summary>
    /// キャプチャ時の元画像を保存するか
    /// </summary>
    public bool EnableCaptureImageSaving { get; set; } = false;

    /// <summary>
    /// キャプチャ画像保存パス（環境変数展開対応）
    /// </summary>
    public string CaptureImageOutputPath { get; set; } = "%AppData%\\Baketa\\Capture\\Images";

    /// <summary>
    /// 縮小後画像も保存するか
    /// </summary>
    public bool EnableScaledImageSaving { get; set; } = false;

    /// <summary>
    /// 高度設定
    /// </summary>
    public RoiAdvancedSettings AdvancedSettings { get; set; } = new();

    /// <summary>
    /// 環境変数展開済みの出力パスを取得
    /// </summary>
    public string GetExpandedOutputPath()
    {
        return Environment.ExpandEnvironmentVariables(RoiImageOutputPath);
    }

    /// <summary>
    /// 環境変数展開済みのキャプチャ画像出力パスを取得
    /// </summary>
    public string GetExpandedCaptureOutputPath()
    {
        return Environment.ExpandEnvironmentVariables(CaptureImageOutputPath);
    }
}

/// <summary>
/// ROI診断高度設定
/// </summary>
public sealed class RoiAdvancedSettings
{
    /// <summary>
    /// 元画像を保存するか
    /// </summary>
    public bool SaveOriginalImages { get; set; } = true;

    /// <summary>
    /// 処理済み画像（個別切り抜き領域）を保存するか
    /// </summary>
    public bool SaveProcessedImages { get; set; } = false;

    /// <summary>
    /// エラー画像を保存するか
    /// </summary>
    public bool SaveErrorImages { get; set; } = true;

    /// <summary>
    /// 画像圧縮を有効にするか
    /// </summary>
    public bool CompressImages { get; set; } = false;

    /// <summary>
    /// 画像品質（1-100）
    /// </summary>
    public int ImageQuality { get; set; } = 95;

    /// <summary>
    /// ファイル名にタイムスタンプ接頭辞を追加するか
    /// </summary>
    public bool EnableTimestampPrefix { get; set; } = true;

    /// <summary>
    /// ファイル名にオペレーションID接尾辞を追加するか
    /// </summary>
    public bool EnableOperationIdSuffix { get; set; } = true;
}
