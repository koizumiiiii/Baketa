using Baketa.Core.License.Models;
using Baketa.Core.Settings;

namespace Baketa.Core.Abstractions.Settings;

/// <summary>
/// 利用規約・プライバシーポリシー同意管理サービスインターフェース
/// [Issue #261] GDPR/CCPA準拠のクリックラップ同意フローを提供
/// </summary>
public interface IConsentService
{
    /// <summary>
    /// 同意が変更されたときに発生するイベント
    /// </summary>
    event EventHandler<LegalConsentChangedEventArgs>? ConsentChanged;

    /// <summary>
    /// 現在の同意状態を非同期で取得
    /// [Gemini Review] UIスレッドブロック回避のため非同期化
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>同意設定</returns>
    Task<ConsentSettings> GetConsentStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// プライバシーポリシーへの再同意が必要か（非同期）
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>再同意が必要な場合true</returns>
    Task<bool> NeedsPrivacyPolicyReConsentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 利用規約への再同意が必要か（非同期）
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>再同意が必要な場合true</returns>
    Task<bool> NeedsTermsOfServiceReConsentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 初回起動時の同意が必要か（プライバシーポリシー）（非同期）
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>初回同意が必要な場合true</returns>
    Task<bool> NeedsInitialConsentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// アカウント作成に必要な同意が完了しているか（非同期）
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>アカウント作成可能な場合true</returns>
    Task<bool> CanCreateAccountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定した同意タイプの現在のバージョンを取得
    /// </summary>
    /// <param name="consentType">同意タイプ</param>
    /// <returns>バージョン文字列（例: "2026-01"）</returns>
    string GetCurrentVersion(ConsentType consentType);

    /// <summary>
    /// プライバシーポリシー同意を記録
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>同意記録タスク</returns>
    Task AcceptPrivacyPolicyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 利用規約同意を記録
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>同意記録タスク</returns>
    Task AcceptTermsOfServiceAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 両方の同意を記録（アカウント作成時）
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>同意記録タスク</returns>
    Task AcceptAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// サーバーに同意記録を送信（監査ログ用）
    /// [Gemini Review] セキュリティ強化: 認証トークンを必須化
    /// </summary>
    /// <param name="userId">ユーザーID</param>
    /// <param name="accessToken">Supabase認証トークン（JWT）</param>
    /// <param name="consentType">同意タイプ</param>
    /// <param name="version">同意したバージョン</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>サーバー記録タスク</returns>
    Task RecordConsentToServerAsync(
        string userId,
        string accessToken,
        ConsentType consentType,
        string version,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// ローカルの同意状態をサーバーに同期（認証成功後に呼び出し）
    /// [Issue #261] 認証前に同意したローカル記録をDBに同期
    /// [Gemini Review] セキュリティ強化: 認証トークンを必須化
    /// </summary>
    /// <param name="userId">認証されたユーザーID</param>
    /// <param name="accessToken">Supabase認証トークン（JWT）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>同期タスク</returns>
    Task SyncLocalConsentToServerAsync(
        string userId,
        string accessToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// サーバーから同意状態を同期
    /// Issue #277: ローカルファイル依存からの脱却
    /// </summary>
    /// <param name="accessToken">Supabase認証トークン（JWT）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>同期結果</returns>
    Task<ServerSyncResult> SyncFromServerAsync(
        string accessToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 同意が有効か確認（24時間以上経過していれば再同期）
    /// Issue #277: GDPR撤回の即時反映
    /// </summary>
    /// <param name="accessToken">Supabase認証トークン（JWT）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>検証状態</returns>
    Task<ConsentVerificationState> EnsureConsentValidAsync(
        string accessToken,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 法的同意変更イベント引数
/// </summary>
public sealed class LegalConsentChangedEventArgs : EventArgs
{
    /// <summary>
    /// 変更された同意タイプ
    /// </summary>
    public required ConsentType ConsentType { get; init; }

    /// <summary>
    /// 同意したバージョン
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// 同意日時（UTC）
    /// </summary>
    public required DateTime AcceptedAt { get; init; }
}
