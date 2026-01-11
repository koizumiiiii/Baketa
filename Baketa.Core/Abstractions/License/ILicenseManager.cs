using Baketa.Core.License.Events;
using Baketa.Core.License.Models;

namespace Baketa.Core.Abstractions.License;

/// <summary>
/// ライセンス管理の中核インターフェース
/// サブスクリプション状態の管理、機能ゲート、トークン消費を担当
/// </summary>
public interface ILicenseManager
{
    /// <summary>
    /// 現在のライセンス状態（同期的に取得）
    /// </summary>
    LicenseState CurrentState { get; }

    /// <summary>
    /// 現在のライセンス状態を非同期で取得（キャッシュ優先）
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>ライセンス状態</returns>
    Task<LicenseState> GetCurrentStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// サーバーからライセンス状態を再取得（オンラインの場合）
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>更新されたライセンス状態</returns>
    Task<LicenseState> RefreshStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 強制的にサーバーから再取得（キャッシュをバイパス）
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>更新されたライセンス状態</returns>
    Task<LicenseState> ForceRefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定された機能が現在のプランで利用可能かどうか
    /// </summary>
    /// <param name="feature">機能タイプ</param>
    /// <returns>利用可能な場合true</returns>
    bool IsFeatureAvailable(FeatureType feature);

    /// <summary>
    /// クラウドAIトークンを消費（Idempotency Key対応）
    /// </summary>
    /// <param name="tokenCount">消費するトークン数</param>
    /// <param name="idempotencyKey">二重消費防止用のユニークキー</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>消費結果</returns>
    Task<TokenConsumptionResult> ConsumeCloudAiTokensAsync(
        int tokenCount,
        string idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// ライセンス状態変更イベント
    /// </summary>
    event EventHandler<LicenseStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// トークン使用量警告イベント（閾値到達時）
    /// </summary>
    event EventHandler<TokenUsageWarningEventArgs>? TokenUsageWarning;

    /// <summary>
    /// セッション無効化イベント（別デバイスログイン検出時）
    /// </summary>
    event EventHandler<SessionInvalidatedEventArgs>? SessionInvalidated;

    /// <summary>
    /// プラン期限切れ警告イベント（7日前等）
    /// </summary>
    event EventHandler<PlanExpirationWarningEventArgs>? PlanExpirationWarning;

    /// <summary>
    /// テストモード用：プランを直接設定（モックモード有効時のみ動作）
    /// QA検証でサーバー連携なしにプラン変更をテストするために使用
    /// </summary>
    /// <param name="plan">設定するプラン</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>設定成功時true</returns>
    /// <remarks>
    /// このメソッドはLicenseSettings.EnableMockModeがtrueの場合のみ動作します。
    /// 本番環境では何も行わずfalseを返します。
    /// </remarks>
    Task<bool> SetTestPlanAsync(PlanType plan, CancellationToken cancellationToken = default);

    /// <summary>
    /// 外部ソースから取得したライセンス状態を設定
    /// Patreon同期など、独自の認証機構を持つサービスからの状態更新に使用
    /// </summary>
    /// <param name="state">設定するライセンス状態</param>
    /// <param name="source">状態の取得元（ログ用）</param>
    /// <param name="reason">状態変更理由</param>
    void SetResolvedLicenseState(LicenseState state, string source, LicenseChangeReason reason);

    /// <summary>
    /// トークン使用量を更新（Issue #275）
    /// TokenUsageRepositoryから読み込んだ実際の使用量で内部状態を同期するために使用
    /// </summary>
    /// <param name="tokensUsed">トークン使用量</param>
    /// <remarks>
    /// プランやその他の状態は変更せず、CloudAiTokensUsedのみを更新します。
    /// UI層がリポジトリから正確な使用量を取得した後に呼び出すことを想定。
    /// </remarks>
    void SyncTokenUsage(long tokensUsed);

    /// <summary>
    /// ボーナストークンがロードされたことを通知（Issue #280+#281）
    /// BonusSyncHostedServiceがサーバーからボーナストークンを取得した後に呼び出す
    /// </summary>
    /// <remarks>
    /// このメソッドは StateChanged イベントを発火して、
    /// CloudTranslationAvailabilityService などのリスナーが
    /// IsFeatureAvailable(CloudAiTranslation) を再評価できるようにします。
    /// </remarks>
    void NotifyBonusTokensLoaded();
}

/// <summary>
/// トークン消費結果
/// </summary>
public sealed record TokenConsumptionResult
{
    /// <summary>
    /// 消費に成功したかどうか
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// 消費後の合計使用量
    /// </summary>
    public required long NewUsageTotal { get; init; }

    /// <summary>
    /// 残りトークン数
    /// </summary>
    public required long RemainingTokens { get; init; }

    /// <summary>
    /// 失敗理由（失敗時のみ）
    /// </summary>
    public TokenConsumptionFailureReason? FailureReason { get; init; }

    /// <summary>
    /// エラーメッセージ（失敗時のみ）
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// ローカル翻訳にフォールバックすべきかどうか
    /// </summary>
    public bool ShouldFallbackToLocal =>
        !Success && FailureReason is TokenConsumptionFailureReason.QuotaExceeded
                                   or TokenConsumptionFailureReason.NetworkError;

    /// <summary>
    /// 成功結果を作成
    /// </summary>
    public static TokenConsumptionResult CreateSuccess(long newUsageTotal, long remainingTokens) => new()
    {
        Success = true,
        NewUsageTotal = newUsageTotal,
        RemainingTokens = remainingTokens
    };

    /// <summary>
    /// 失敗結果を作成
    /// </summary>
    public static TokenConsumptionResult CreateFailure(
        TokenConsumptionFailureReason reason,
        string? errorMessage = null,
        long currentUsage = 0,
        long remainingTokens = 0) => new()
    {
        Success = false,
        NewUsageTotal = currentUsage,
        RemainingTokens = remainingTokens,
        FailureReason = reason,
        ErrorMessage = errorMessage ?? reason.GetDefaultMessage()
    };
}

/// <summary>
/// トークン消費失敗理由
/// </summary>
public enum TokenConsumptionFailureReason
{
    /// <summary>月間クォータ超過</summary>
    QuotaExceeded,

    /// <summary>ネットワークエラー</summary>
    NetworkError,

    /// <summary>セッション無効</summary>
    SessionInvalid,

    /// <summary>サーバーエラー</summary>
    ServerError,

    /// <summary>レート制限超過</summary>
    RateLimited,

    /// <summary>プランがクラウドAI非対応</summary>
    PlanNotSupported
}

/// <summary>
/// TokenConsumptionFailureReasonの拡張メソッド
/// </summary>
public static class TokenConsumptionFailureReasonExtensions
{
    /// <summary>
    /// デフォルトのエラーメッセージを取得
    /// </summary>
    public static string GetDefaultMessage(this TokenConsumptionFailureReason reason) => reason switch
    {
        TokenConsumptionFailureReason.QuotaExceeded =>
            "今月のクラウドAI翻訳上限に達しました。ローカル翻訳を使用します。",
        TokenConsumptionFailureReason.NetworkError =>
            "クラウドAI翻訳に接続できません。ローカル翻訳を使用します。",
        TokenConsumptionFailureReason.SessionInvalid =>
            "セッションが無効です。再度ログインしてください。",
        TokenConsumptionFailureReason.ServerError =>
            "サーバーエラーが発生しました。しばらく経ってからお試しください。",
        TokenConsumptionFailureReason.RateLimited =>
            "リクエストが多すぎます。しばらく経ってからお試しください。",
        TokenConsumptionFailureReason.PlanNotSupported =>
            "現在のプランではクラウドAI翻訳を利用できません。",
        _ => "不明なエラーが発生しました。"
    };
}
