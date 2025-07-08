using System.ComponentModel;
using Baketa.Core.Settings;

namespace Baketa.Core.Abstractions.Feedback;

/// <summary>
/// フィードバック収集サービスインターフェース
/// GitHub Issues API連携によるバグ報告・機能要望管理
/// </summary>
public interface IFeedbackService
{
    /// <summary>
    /// フィードバック送信が可能かどうか
    /// </summary>
    bool CanSubmitFeedback { get; }

    /// <summary>
    /// バグ報告を送信
    /// </summary>
    /// <param name="report">バグ報告</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>送信結果</returns>
    Task<FeedbackSubmissionResult> SubmitBugReportAsync(BugReport report, CancellationToken cancellationToken = default);

    /// <summary>
    /// 機能要望を送信
    /// </summary>
    /// <param name="request">機能要望</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>送信結果</returns>
    Task<FeedbackSubmissionResult> SubmitFeatureRequestAsync(FeatureRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 一般フィードバックを送信
    /// </summary>
    /// <param name="feedback">一般フィードバック</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>送信結果</returns>
    Task<FeedbackSubmissionResult> SubmitGeneralFeedbackAsync(GeneralFeedback feedback, CancellationToken cancellationToken = default);

    /// <summary>
    /// フィードバック設定を取得
    /// </summary>
    /// <returns>現在の設定</returns>
    FeedbackSettings GetSettings();

    /// <summary>
    /// フィードバック設定を更新
    /// </summary>
    /// <param name="settings">新しい設定</param>
    /// <returns>設定更新タスク</returns>
    Task UpdateSettingsAsync(FeedbackSettings settings);

    /// <summary>
    /// システム情報を収集（プライバシー設定に基づく）
    /// </summary>
    /// <returns>システム情報</returns>
    Task<SystemInfo?> CollectSystemInfoAsync();

    /// <summary>
    /// フィードバック送信完了時のイベント
    /// </summary>
    event EventHandler<FeedbackSubmittedEventArgs>? FeedbackSubmitted;
}

/// <summary>
/// フィードバック送信結果
/// </summary>
public enum FeedbackSubmissionResult
{
    /// <summary>
    /// 送信成功
    /// </summary>
    [Description("送信成功")]
    Success,

    /// <summary>
    /// 送信失敗（ネットワークエラー）
    /// </summary>
    [Description("ネットワークエラー")]
    NetworkError,

    /// <summary>
    /// 送信失敗（認証エラー）
    /// </summary>
    [Description("認証エラー")]
    AuthenticationError,

    /// <summary>
    /// 送信失敗（レート制限）
    /// </summary>
    [Description("レート制限")]
    RateLimited,

    /// <summary>
    /// 送信失敗（バリデーションエラー）
    /// </summary>
    [Description("バリデーションエラー")]
    ValidationError,

    /// <summary>
    /// 送信拒否（プライバシー設定）
    /// </summary>
    [Description("プライバシー設定により拒否")]
    PrivacyBlocked,

    /// <summary>
    /// 送信スキップ（機能無効）
    /// </summary>
    [Description("機能無効")]
    Disabled
}

/// <summary>
/// バグ報告
/// </summary>
public sealed record BugReport
{
    /// <summary>
    /// バグタイトル
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// バグ詳細説明
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// 再現手順
    /// </summary>
    public string? StepsToReproduce { get; init; }

    /// <summary>
    /// 期待される動作
    /// </summary>
    public string? ExpectedBehavior { get; init; }

    /// <summary>
    /// 実際の動作
    /// </summary>
    public string? ActualBehavior { get; init; }

    /// <summary>
    /// 重要度
    /// </summary>
    public BugSeverity Severity { get; init; } = BugSeverity.Medium;

    /// <summary>
    /// エラーメッセージ・スタックトレース
    /// </summary>
    public string? ErrorDetails { get; init; }

    /// <summary>
    /// システム情報
    /// </summary>
    public SystemInfo? SystemInfo { get; init; }

    /// <summary>
    /// 添付ファイル（スクリーンショット等）
    /// </summary>
    public IReadOnlyList<FeedbackAttachment>? Attachments { get; init; }

    /// <summary>
    /// 連絡先（任意）
    /// </summary>
    public string? ContactInfo { get; init; }
}

/// <summary>
/// 機能要望
/// </summary>
public sealed record FeatureRequest
{
    /// <summary>
    /// 要望タイトル
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// 要望詳細説明
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// 使用ケース・利用シーン
    /// </summary>
    public string? UseCase { get; init; }

    /// <summary>
    /// 優先度
    /// </summary>
    public FeaturePriority Priority { get; init; } = FeaturePriority.Medium;

    /// <summary>
    /// 類似機能の参考例
    /// </summary>
    public string? References { get; init; }

    /// <summary>
    /// 連絡先（任意）
    /// </summary>
    public string? ContactInfo { get; init; }
}

/// <summary>
/// 一般フィードバック
/// </summary>
public sealed record GeneralFeedback
{
    /// <summary>
    /// フィードバックタイトル
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// フィードバック内容
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// フィードバック種別
    /// </summary>
    public FeedbackType Type { get; init; } = FeedbackType.General;

    /// <summary>
    /// システム情報
    /// </summary>
    public SystemInfo? SystemInfo { get; init; }

    /// <summary>
    /// 連絡先（任意）
    /// </summary>
    public string? ContactInfo { get; init; }
}

/// <summary>
/// システム情報
/// </summary>
public sealed record SystemInfo
{
    /// <summary>
    /// アプリケーションバージョン
    /// </summary>
    public required string AppVersion { get; init; }

    /// <summary>
    /// OS情報
    /// </summary>
    public required string OperatingSystem { get; init; }

    /// <summary>
    /// .NET Runtime バージョン
    /// </summary>
    public required string RuntimeVersion { get; init; }

    /// <summary>
    /// CPU アーキテクチャ
    /// </summary>
    public required string Architecture { get; init; }

    /// <summary>
    /// 画面解像度
    /// </summary>
    public string? ScreenResolution { get; init; }

    /// <summary>
    /// 使用中のOCRエンジン
    /// </summary>
    public string? OcrEngine { get; init; }

    /// <summary>
    /// 使用中の翻訳エンジン
    /// </summary>
    public string? TranslationEngine { get; init; }

    /// <summary>
    /// インストール言語
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// 追加情報（メモリ使用量等）
    /// </summary>
    public Dictionary<string, string>? AdditionalInfo { get; init; }
}

/// <summary>
/// フィードバック添付ファイル
/// </summary>
public sealed record FeedbackAttachment
{
    /// <summary>
    /// ファイル名
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// ファイルデータ（Base64エンコード）
    /// </summary>
    public required string Data { get; init; }

    /// <summary>
    /// ファイルタイプ
    /// </summary>
    public required string ContentType { get; init; }

    /// <summary>
    /// ファイルサイズ（バイト）
    /// </summary>
    public long Size { get; init; }

    /// <summary>
    /// 説明
    /// </summary>
    public string? Description { get; init; }
}

/// <summary>
/// バグの重要度
/// </summary>
public enum BugSeverity
{
    /// <summary>
    /// 低（軽微な問題）
    /// </summary>
    [Description("軽微")]
    Low,

    /// <summary>
    /// 中（通常の問題）
    /// </summary>
    [Description("通常")]
    Medium,

    /// <summary>
    /// 高（重要な問題）
    /// </summary>
    [Description("重要")]
    High,

    /// <summary>
    /// 緊急（クリティカル）
    /// </summary>
    [Description("緊急")]
    Critical
}

/// <summary>
/// 機能要望の優先度
/// </summary>
public enum FeaturePriority
{
    /// <summary>
    /// 低（あると便利）
    /// </summary>
    [Description("あると便利")]
    Low,

    /// <summary>
    /// 中（欲しい機能）
    /// </summary>
    [Description("欲しい機能")]
    Medium,

    /// <summary>
    /// 高（重要な機能）
    /// </summary>
    [Description("重要な機能")]
    High,

    /// <summary>
    /// 緊急（必須機能）
    /// </summary>
    [Description("必須機能")]
    Critical
}

/// <summary>
/// フィードバック種別
/// </summary>
public enum FeedbackType
{
    /// <summary>
    /// 一般的なフィードバック
    /// </summary>
    [Description("一般")]
    General,

    /// <summary>
    /// 使用感・UI改善
    /// </summary>
    [Description("使用感")]
    Usability,

    /// <summary>
    /// パフォーマンス
    /// </summary>
    [Description("パフォーマンス")]
    Performance,

    /// <summary>
    /// ドキュメント・ヘルプ
    /// </summary>
    [Description("ドキュメント")]
    Documentation,

    /// <summary>
    /// その他
    /// </summary>
    [Description("その他")]
    Other
}

/// <summary>
/// フィードバック送信完了イベント引数
/// </summary>
public sealed class FeedbackSubmittedEventArgs : EventArgs
{
    /// <summary>
    /// 送信結果
    /// </summary>
    public required FeedbackSubmissionResult Result { get; init; }

    /// <summary>
    /// GitHub Issue URL（成功時）
    /// </summary>
    public Uri? IssueUrl { get; init; }

    /// <summary>
    /// エラー情報（失敗時）
    /// </summary>
    public Exception? Error { get; init; }

    /// <summary>
    /// 送信日時
    /// </summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// フィードバック種別
    /// </summary>
    public required string FeedbackType { get; init; }
}