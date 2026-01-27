namespace Baketa.Core.Settings;

/// <summary>
/// Issue #292: 統合AIサーバー設定
/// OCR + 翻訳を単一プロセスで実行する統合サーバーの設定
/// </summary>
public sealed class UnifiedServerSettings
{
    /// <summary>
    /// 設定セクション名
    /// </summary>
    public const string SectionName = "UnifiedServer";

    /// <summary>
    /// デフォルトポート番号
    /// Issue #330: 翻訳サーバーと同じポート50051を使用（統合サーバーは両方を提供）
    /// </summary>
    public const int DefaultPort = 50051;

    /// <summary>
    /// 統合サーバーを有効にするか
    /// </summary>
    /// <remarks>
    /// true: OCRと翻訳を単一プロセスで実行（VRAM削減、起動時間短縮）
    /// false: 既存の分離サーバー（SuryaOcrServer + TranslationServer）を使用
    /// </remarks>
    [SettingMetadata(SettingLevel.Advanced, "Unified Server", "Enabled",
        Description = "OCRと翻訳を統合サーバーで実行")]
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 統合サーバーのポート番号
    /// </summary>
    /// <remarks>
    /// デフォルト: 50051
    /// 統合サーバーは翻訳とOCRの両方を提供するため、翻訳サーバーのポートを使用
    /// </remarks>
    [SettingMetadata(SettingLevel.Advanced, "Unified Server", "Port",
        Description = "統合サーバーのポート番号")]
    public int Port { get; set; } = DefaultPort;

    /// <summary>
    /// サーバー起動タイムアウト（秒）
    /// </summary>
    /// <remarks>
    /// OCR + 翻訳両方のモデルをロードするため、長めに設定。
    /// デフォルト: 300秒（5分）
    /// </remarks>
    [SettingMetadata(SettingLevel.Advanced, "Unified Server", "Startup Timeout",
        Description = "サーバー起動のタイムアウト時間（秒）")]
    public int StartupTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// ヘルスチェック間隔（秒）
    /// </summary>
    /// <remarks>
    /// gRPCでサーバーの準備状態を定期的に確認する間隔。
    /// デフォルト: 30秒
    /// </remarks>
    [SettingMetadata(SettingLevel.Advanced, "Unified Server", "Health Check Interval",
        Description = "ヘルスチェックの間隔（秒）")]
    public int HealthCheckIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// サーバー停止タイムアウト（秒）
    /// </summary>
    /// <remarks>
    /// サーバープロセスの正常終了を待機する時間。
    /// デフォルト: 10秒
    /// </remarks>
    [SettingMetadata(SettingLevel.Debug, "Unified Server", "Stop Timeout",
        Description = "サーバー停止のタイムアウト時間（秒）")]
    public int StopTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// gRPCヘルスチェックタイムアウト（秒）
    /// </summary>
    /// <remarks>
    /// 個々のヘルスチェックRPC呼び出しのタイムアウト。
    /// デフォルト: 5秒
    /// </remarks>
    [SettingMetadata(SettingLevel.Debug, "Unified Server", "Health Check Timeout",
        Description = "個々のヘルスチェックのタイムアウト時間（秒）")]
    public int HealthCheckTimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// 設定を検証
    /// </summary>
    /// <returns>検証結果</returns>
    public SettingsValidationResult ValidateSettings()
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // ポート検証
        if (Port < 1024 || Port > 65535)
        {
            errors.Add("ポート番号は1024から65535の間で設定してください");
        }

        // ポート競合警告（統合サーバーモードでは50051を使用するため、50052のみ警告）
        if (Port == 50052)
        {
            warnings.Add($"ポート{Port}は分離モードのOCRサーバーと競合する可能性があります");
        }

        // 起動タイムアウト検証
        if (StartupTimeoutSeconds < 30 || StartupTimeoutSeconds > 600)
        {
            warnings.Add("起動タイムアウトは30秒から600秒の間で設定することを推奨します");
        }

        // ヘルスチェック間隔検証
        if (HealthCheckIntervalSeconds < 5 || HealthCheckIntervalSeconds > 120)
        {
            warnings.Add("ヘルスチェック間隔は5秒から120秒の間で設定することを推奨します");
        }

        return errors.Count > 0
            ? SettingsValidationResult.CreateFailure(errors, warnings)
            : SettingsValidationResult.CreateSuccess(warnings);
    }
}
