using System.Text.Json.Serialization;

namespace Baketa.Core.License.Models;

/// <summary>
/// Patreon OAuth トークン応答
/// </summary>
public sealed record PatreonTokenResponse
{
    /// <summary>
    /// アクセストークン
    /// </summary>
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    /// <summary>
    /// リフレッシュトークン
    /// </summary>
    [JsonPropertyName("refresh_token")]
    public required string RefreshToken { get; init; }

    /// <summary>
    /// トークン有効期限（秒）
    /// </summary>
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }

    /// <summary>
    /// スコープ
    /// </summary>
    [JsonPropertyName("scope")]
    public string? Scope { get; init; }

    /// <summary>
    /// トークンタイプ
    /// </summary>
    [JsonPropertyName("token_type")]
    public string TokenType { get; init; } = "Bearer";
}

/// <summary>
/// Patreon Identity API 応答
/// </summary>
public sealed record PatreonIdentityResponse
{
    /// <summary>
    /// データ
    /// </summary>
    [JsonPropertyName("data")]
    public required PatreonUserData Data { get; init; }

    /// <summary>
    /// 関連データ（メンバーシップ等）
    /// </summary>
    [JsonPropertyName("included")]
    public List<PatreonIncludedData>? Included { get; init; }
}

/// <summary>
/// Patreon ユーザーデータ
/// </summary>
public sealed record PatreonUserData
{
    /// <summary>
    /// ユーザーID
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// タイプ（常に "user"）
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "user";

    /// <summary>
    /// 属性
    /// </summary>
    [JsonPropertyName("attributes")]
    public PatreonUserAttributes? Attributes { get; init; }

    /// <summary>
    /// 関連
    /// </summary>
    [JsonPropertyName("relationships")]
    public PatreonUserRelationships? Relationships { get; init; }
}

/// <summary>
/// Patreon ユーザー属性
/// </summary>
public sealed record PatreonUserAttributes
{
    /// <summary>
    /// メールアドレス
    /// </summary>
    [JsonPropertyName("email")]
    public string? Email { get; init; }

    /// <summary>
    /// フルネーム
    /// </summary>
    [JsonPropertyName("full_name")]
    public string? FullName { get; init; }

    /// <summary>
    /// 画像URL
    /// </summary>
    [JsonPropertyName("image_url")]
    public string? ImageUrl { get; init; }
}

/// <summary>
/// Patreon ユーザー関連データ
/// </summary>
public sealed record PatreonUserRelationships
{
    /// <summary>
    /// メンバーシップ
    /// </summary>
    [JsonPropertyName("memberships")]
    public PatreonRelationshipData? Memberships { get; init; }
}

/// <summary>
/// Patreon 関連データ参照
/// </summary>
public sealed record PatreonRelationshipData
{
    /// <summary>
    /// データ
    /// </summary>
    [JsonPropertyName("data")]
    public List<PatreonResourceIdentifier>? Data { get; init; }
}

/// <summary>
/// Patreon リソース識別子
/// </summary>
public sealed record PatreonResourceIdentifier
{
    /// <summary>
    /// リソースID
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// リソースタイプ
    /// </summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }
}

/// <summary>
/// Patreon included データ（メンバーシップ、Tier等）
/// </summary>
public sealed record PatreonIncludedData
{
    /// <summary>
    /// リソースID
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// リソースタイプ
    /// </summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>
    /// 属性（タイプによって内容が異なる）
    /// </summary>
    [JsonPropertyName("attributes")]
    public PatreonMembershipAttributes? Attributes { get; init; }

    /// <summary>
    /// 関連
    /// </summary>
    [JsonPropertyName("relationships")]
    public PatreonMembershipRelationships? Relationships { get; init; }
}

/// <summary>
/// Patreon メンバーシップ属性
/// </summary>
public sealed record PatreonMembershipAttributes
{
    /// <summary>
    /// パトロンステータス
    /// </summary>
    /// <remarks>
    /// - "active_patron": アクティブなパトロン
    /// - "declined_patron": 支払い失敗
    /// - "former_patron": 過去のパトロン
    /// - null: メンバーではない
    /// </remarks>
    [JsonPropertyName("patron_status")]
    public string? PatronStatus { get; init; }

    /// <summary>
    /// 次回の課金日
    /// </summary>
    [JsonPropertyName("next_charge_date")]
    public DateTime? NextChargeDate { get; init; }

    /// <summary>
    /// 最後の課金日
    /// </summary>
    [JsonPropertyName("last_charge_date")]
    public DateTime? LastChargeDate { get; init; }

    /// <summary>
    /// 最後の課金ステータス
    /// </summary>
    [JsonPropertyName("last_charge_status")]
    public string? LastChargeStatus { get; init; }

    /// <summary>
    /// 累計支払額（セント）
    /// </summary>
    [JsonPropertyName("lifetime_support_cents")]
    public int? LifetimeSupportCents { get; init; }

    /// <summary>
    /// 現在の支払額（セント）
    /// </summary>
    [JsonPropertyName("currently_entitled_amount_cents")]
    public int? CurrentlyEntitledAmountCents { get; init; }

    /// <summary>
    /// メンバーシップ開始日
    /// </summary>
    [JsonPropertyName("pledge_relationship_start")]
    public DateTime? PledgeRelationshipStart { get; init; }
}

/// <summary>
/// Patreon メンバーシップ関連
/// </summary>
public sealed record PatreonMembershipRelationships
{
    /// <summary>
    /// 現在有効なTier
    /// </summary>
    [JsonPropertyName("currently_entitled_tiers")]
    public PatreonRelationshipData? CurrentlyEntitledTiers { get; init; }
}

/// <summary>
/// ローカルに保存するPatreon認証情報
/// </summary>
public sealed record PatreonLocalCredentials
{
    /// <summary>
    /// Patreon ユーザーID
    /// </summary>
    public required string PatreonUserId { get; init; }

    /// <summary>
    /// ユーザーのメールアドレス（表示用）
    /// </summary>
    public string? Email { get; init; }

    /// <summary>
    /// ユーザー名（表示用）
    /// </summary>
    public string? FullName { get; init; }

    /// <summary>
    /// 暗号化されたリフレッシュトークン
    /// </summary>
    /// <remarks>
    /// Windows DPAPI または SecureStorage で暗号化。
    /// アクセストークンは短命のため保存しない。
    /// </remarks>
    public string? EncryptedRefreshToken { get; init; }

    /// <summary>
    /// リフレッシュトークンの取得日時
    /// </summary>
    public DateTime? RefreshTokenObtainedAt { get; init; }

    /// <summary>
    /// 最後に確認したプランタイプ
    /// </summary>
    public PlanType LastKnownPlan { get; init; } = PlanType.Free;

    /// <summary>
    /// 最後に確認したTier ID
    /// </summary>
    public string? LastKnownTierId { get; init; }

    /// <summary>
    /// サブスクリプション有効期限（次回課金日）
    /// </summary>
    public DateTime? SubscriptionEndDate { get; init; }

    /// <summary>
    /// 最終同期日時
    /// </summary>
    public DateTime? LastSyncTime { get; init; }

    /// <summary>
    /// 同期エラーメッセージ（最後のエラー）
    /// </summary>
    public string? LastSyncError { get; init; }

    /// <summary>
    /// パトロンステータス
    /// </summary>
    public string? PatronStatus { get; init; }
}

/// <summary>
/// Patreon同期ステータス
/// </summary>
public enum PatreonSyncStatus
{
    /// <summary>
    /// 未接続（Patreon連携未設定）
    /// </summary>
    NotConnected,

    /// <summary>
    /// 同期済み（正常）
    /// </summary>
    Synced,

    /// <summary>
    /// オフライン（キャッシュ使用中）
    /// </summary>
    Offline,

    /// <summary>
    /// トークン期限切れ（再認証必要）
    /// </summary>
    TokenExpired,

    /// <summary>
    /// 同期エラー
    /// </summary>
    Error
}

/// <summary>
/// Patreonライセンス同期結果
/// </summary>
public sealed record PatreonSyncResult
{
    /// <summary>
    /// 成功したかどうか
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// 同期ステータス
    /// </summary>
    public PatreonSyncStatus Status { get; init; }

    /// <summary>
    /// 判定されたプラン
    /// </summary>
    public PlanType Plan { get; init; } = PlanType.Free;

    /// <summary>
    /// エラーメッセージ
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 次回課金日
    /// </summary>
    public DateTime? NextChargeDate { get; init; }

    /// <summary>
    /// キャッシュからの結果かどうか
    /// </summary>
    public bool FromCache { get; init; }

    /// <summary>
    /// 成功結果を作成
    /// </summary>
    public static PatreonSyncResult CreateSuccess(PlanType plan, DateTime? nextChargeDate = null, bool fromCache = false) => new()
    {
        Success = true,
        Status = fromCache ? PatreonSyncStatus.Offline : PatreonSyncStatus.Synced,
        Plan = plan,
        NextChargeDate = nextChargeDate,
        FromCache = fromCache
    };

    /// <summary>
    /// エラー結果を作成
    /// </summary>
    public static PatreonSyncResult CreateError(PatreonSyncStatus status, string errorMessage) => new()
    {
        Success = false,
        Status = status,
        ErrorMessage = errorMessage,
        Plan = PlanType.Free
    };

    /// <summary>
    /// 未接続結果を作成
    /// </summary>
    public static PatreonSyncResult NotConnected => new()
    {
        Success = true,
        Status = PatreonSyncStatus.NotConnected,
        Plan = PlanType.Free
    };
}
