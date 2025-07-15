using System.ComponentModel;
using Baketa.Core.Settings;

namespace Baketa.Core.Abstractions.Update;

/// <summary>
/// アプリケーション更新チェックサービスインターフェース
/// GitHub Releases APIベースの堅牢なバージョン管理
/// </summary>
public interface IUpdateCheckService
{
    /// <summary>
    /// 利用可能な更新があるかどうか
    /// </summary>
    bool IsUpdateAvailable { get; }

    /// <summary>
    /// 現在のアプリケーションバージョン
    /// </summary>
    Version CurrentVersion { get; }

    /// <summary>
    /// 最新の利用可能バージョン（チェック完了後）
    /// </summary>
    Version? LatestVersion { get; }

    /// <summary>
    /// 最後に更新チェックを実行した日時
    /// </summary>
    DateTime? LastCheckTime { get; }

    /// <summary>
    /// 更新チェックが進行中かどうか
    /// </summary>
    bool IsCheckInProgress { get; }

    /// <summary>
    /// 更新情報（リリースノート等）
    /// </summary>
    UpdateInfo? LatestUpdateInfo { get; }

    /// <summary>
    /// 更新チェックを実行
    /// </summary>
    /// <param name="forceCheck">強制チェック（キャッシュ無視）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>チェック結果</returns>
    Task<UpdateCheckResult> CheckForUpdatesAsync(bool forceCheck = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定したバージョンが現在のバージョンより新しいかを判定
    /// </summary>
    /// <param name="version">比較対象バージョン</param>
    /// <returns>新しい場合true</returns>
    bool IsVersionNewer(Version version);

    /// <summary>
    /// 指定したバージョン文字列が現在のバージョンより新しいかを判定
    /// </summary>
    /// <param name="versionString">比較対象バージョン文字列（Semver準拠）</param>
    /// <returns>新しい場合true</returns>
    bool IsVersionNewer(string versionString);

    /// <summary>
    /// 更新チェック設定を取得
    /// </summary>
    /// <returns>現在の設定</returns>
    UpdateCheckSettings GetSettings();

    /// <summary>
    /// 更新チェック設定を更新
    /// </summary>
    /// <param name="settings">新しい設定</param>
    /// <returns>設定更新タスク</returns>
    Task UpdateSettingsAsync(UpdateCheckSettings settings);

    /// <summary>
    /// 自動更新チェックを開始
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>自動チェックタスク</returns>
    Task StartPeriodicCheckAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 自動更新チェックを停止
    /// </summary>
    void StopPeriodicCheck();

    /// <summary>
    /// 更新状態が変更されたときのイベント
    /// </summary>
    event EventHandler<UpdateStateChangedEventArgs>? UpdateStateChanged;

    /// <summary>
    /// 更新チェック完了時のイベント
    /// </summary>
    event EventHandler<UpdateCheckCompletedEventArgs>? UpdateCheckCompleted;
}

/// <summary>
/// 更新チェック結果
/// </summary>
public enum UpdateCheckResult
{
    /// <summary>
    /// 更新なし（最新版使用中）
    /// </summary>
    [Description("最新版を使用中")]
    UpToDate,

    /// <summary>
    /// 更新利用可能
    /// </summary>
    [Description("更新が利用可能")]
    UpdateAvailable,

    /// <summary>
    /// チェック失敗（ネットワークエラー等）
    /// </summary>
    [Description("チェック失敗")]
    CheckFailed,

    /// <summary>
    /// スキップ（設定により無効）
    /// </summary>
    [Description("チェックスキップ")]
    Skipped,

    /// <summary>
    /// キャッシュからの結果
    /// </summary>
    [Description("キャッシュからの結果")]
    FromCache
}

/// <summary>
/// 更新情報
/// </summary>
public sealed record UpdateInfo
{
    /// <summary>
    /// バージョン
    /// </summary>
    public required Version Version { get; init; }

    /// <summary>
    /// リリース名
    /// </summary>
    public required string ReleaseName { get; init; }

    /// <summary>
    /// リリースノート
    /// </summary>
    public string? ReleaseNotes { get; init; }

    /// <summary>
    /// ダウンロードURL
    /// </summary>
    public required Uri DownloadUrl { get; init; }

    /// <summary>
    /// ファイルサイズ（バイト）
    /// </summary>
    public long? FileSize { get; init; }

    /// <summary>
    /// リリース日時
    /// </summary>
    public required DateTime PublishedAt { get; init; }

    /// <summary>
    /// プレリリースかどうか
    /// </summary>
    public bool IsPrerelease { get; init; }

    /// <summary>
    /// セキュリティ修正を含むかどうか
    /// </summary>
    public bool ContainsSecurityFixes { get; init; }

    /// <summary>
    /// 重要度レベル
    /// </summary>
    public UpdateImportance Importance { get; init; } = UpdateImportance.Normal;
}

/// <summary>
/// 更新の重要度
/// </summary>
public enum UpdateImportance
{
    /// <summary>
    /// 低（オプション更新）
    /// </summary>
    [Description("オプション")]
    Low,

    /// <summary>
    /// 通常
    /// </summary>
    [Description("通常")]
    Normal,

    /// <summary>
    /// 高（推奨更新）
    /// </summary>
    [Description("推奨")]
    High,

    /// <summary>
    /// 緊急（セキュリティ修正等）
    /// </summary>
    [Description("緊急")]
    Critical
}

/// <summary>
/// 更新状態変更イベント引数
/// </summary>
public sealed class UpdateStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// 新しい更新状態
    /// </summary>
    public required bool IsUpdateAvailable { get; init; }

    /// <summary>
    /// 更新情報
    /// </summary>
    public UpdateInfo? UpdateInfo { get; init; }

    /// <summary>
    /// 変更日時
    /// </summary>
    public required DateTime Timestamp { get; init; }
}

/// <summary>
/// 更新チェック完了イベント引数
/// </summary>
public sealed class UpdateCheckCompletedEventArgs : EventArgs
{
    /// <summary>
    /// チェック結果
    /// </summary>
    public required UpdateCheckResult Result { get; init; }

    /// <summary>
    /// 更新情報（利用可能な場合）
    /// </summary>
    public UpdateInfo? UpdateInfo { get; init; }

    /// <summary>
    /// エラー情報（失敗時）
    /// </summary>
    public Exception? Error { get; init; }

    /// <summary>
    /// チェック実行日時
    /// </summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// チェック所要時間
    /// </summary>
    public TimeSpan Duration { get; init; }
}