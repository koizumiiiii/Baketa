namespace Baketa.Core.Settings;

/// <summary>
/// [Issue #252] クラッシュレポート設定クラス
/// クラッシュレポートの送信に関する設定を管理
/// </summary>
public sealed class CrashReportSettings
{
    /// <summary>
    /// クラッシュレポートの自動送信を有効にする
    /// true: ダイアログを表示せずに自動送信
    /// false: ダイアログでユーザーに確認（デフォルト）
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Privacy", "クラッシュレポート自動送信",
        Description = "アプリケーションのクラッシュ時にレポートを自動的に送信します")]
    public bool AutoSendCrashReports { get; set; } = false;

    /// <summary>
    /// システム情報を含める（自動送信時）
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Privacy", "システム情報を含める",
        Description = "クラッシュレポートにCPU/メモリなどのシステム情報を含めます")]
    public bool IncludeSystemInfo { get; set; } = true;

    /// <summary>
    /// アプリログを含める（自動送信時）
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Privacy", "アプリログを含める",
        Description = "クラッシュレポートに最近のアプリケーションログを含めます")]
    public bool IncludeLogs { get; set; } = true;
}
