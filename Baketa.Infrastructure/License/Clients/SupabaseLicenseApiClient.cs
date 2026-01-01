using System.Net.Http;
using System.Text.Json;
using Baketa.Core.Abstractions.License;
using Baketa.Core.License.Extensions;
using Baketa.Core.License.Models;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Postgrest;
using Postgrest.Exceptions;
using SupabaseClient = Supabase.Client;

namespace Baketa.Infrastructure.License.Clients;

/// <summary>
/// Supabaseを使用したライセンスAPIクライアント
/// subscriptionsテーブルからライセンス状態を取得し、トークン消費を記録
/// </summary>
public sealed class SupabaseLicenseApiClient : ILicenseApiClient
{
    private readonly SupabaseClient _supabase;
    private readonly ILogger<SupabaseLicenseApiClient> _logger;
    private readonly LicenseSettings _settings;
    private bool _isAvailable = true;

    /// <inheritdoc/>
    public bool IsAvailable => _isAvailable;

    /// <summary>
    /// SupabaseLicenseApiClientを初期化
    /// </summary>
    public SupabaseLicenseApiClient(
        SupabaseClient supabase,
        ILogger<SupabaseLicenseApiClient> logger,
        IOptions<LicenseSettings> settings)
    {
        _supabase = supabase ?? throw new ArgumentNullException(nameof(supabase));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));

        _logger.LogInformation("SupabaseLicenseApiClient初期化完了");
    }

    /// <inheritdoc/>
    public async Task<LicenseApiResponse?> GetLicenseStateAsync(
        string userId,
        string sessionToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionToken);

        try
        {
            _logger.LogDebug("ライセンス状態取得開始: UserId={UserId}", userId);

            // subscriptionsテーブルからユーザーのサブスクリプション情報を取得
            // RPC呼び出しを使用してライセンス情報を取得
            var rpcParams = new Dictionary<string, object>
            {
                ["p_user_id"] = userId
            };

            var rpcResponse = await _supabase.Rpc("get_user_subscription", rpcParams)
                .ConfigureAwait(false);

            if (rpcResponse?.Content == null || rpcResponse.Content == "null" || rpcResponse.Content == "[]")
            {
                _logger.LogDebug("サブスクリプションが見つかりません: UserId={UserId}, デフォルト（Free）を返します", userId);
                return LicenseApiResponse.CreateSuccess(LicenseState.Default with
                {
                    UserId = userId,
                    SessionId = sessionToken
                });
            }

            var response = JsonSerializer.Deserialize<SubscriptionRecord>(
                rpcResponse.Content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (response == null)
            {
                _logger.LogDebug("サブスクリプションが見つかりません: UserId={UserId}, デフォルト（Free）を返します", userId);
                return LicenseApiResponse.CreateSuccess(LicenseState.Default with
                {
                    UserId = userId,
                    SessionId = sessionToken
                });
            }

            var state = MapToLicenseState(response, sessionToken);
            _isAvailable = true;

            _logger.LogDebug(
                "ライセンス状態取得成功: UserId={UserId}, Plan={Plan}, TokensUsed={Used}/{Limit}",
                userId,
                state.CurrentPlan,
                state.CloudAiTokensUsed,
                state.MonthlyTokenLimit);

            return LicenseApiResponse.CreateSuccess(state);
        }
        catch (PostgrestException ex)
        {
            _logger.LogError(ex, "Supabaseエラー: {Message}", ex.Message);
            _isAvailable = false;
            return LicenseApiResponse.CreateFailure("SUPABASE_ERROR", ex.Message);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "ネットワークエラー: {Message}", ex.Message);
            _isAvailable = false;
            return LicenseApiResponse.CreateFailure("NETWORK_ERROR", "ネットワーク接続に問題があります");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "予期せぬエラー: {Message}", ex.Message);
            _isAvailable = false;
            return LicenseApiResponse.CreateFailure("UNKNOWN_ERROR", ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<TokenConsumptionApiResponse> ConsumeTokensAsync(
        TokenConsumptionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            _logger.LogDebug(
                "トークン消費開始: UserId={UserId}, TokenCount={Count}, IdempotencyKey={Key}",
                request.UserId,
                request.TokenCount,
                request.IdempotencyKey);

            // RPCを呼び出してトークンを消費（アトミックなトランザクション処理）
            var rpcParams = new Dictionary<string, object>
            {
                ["p_user_id"] = request.UserId,
                ["p_token_count"] = request.TokenCount,
                ["p_idempotency_key"] = request.IdempotencyKey
            };

            var response = await _supabase.Rpc("consume_cloud_ai_tokens", rpcParams)
                .ConfigureAwait(false);

            if (response?.Content == null)
            {
                _logger.LogWarning("RPC応答がnullです");
                return new TokenConsumptionApiResponse
                {
                    Success = false,
                    ErrorCode = "RPC_ERROR",
                    ErrorMessage = "サーバーからの応答がありません"
                };
            }

            var result = JsonSerializer.Deserialize<TokenConsumptionRpcResult>(
                response.Content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result == null)
            {
                return new TokenConsumptionApiResponse
                {
                    Success = false,
                    ErrorCode = "PARSE_ERROR",
                    ErrorMessage = "応答の解析に失敗しました"
                };
            }

            _isAvailable = true;

            if (result.Success)
            {
                _logger.LogDebug(
                    "トークン消費成功: NewTotal={NewTotal}, Remaining={Remaining}",
                    result.NewTotal,
                    result.Remaining);

                return new TokenConsumptionApiResponse
                {
                    Success = true,
                    NewUsageTotal = result.NewTotal,
                    RemainingTokens = result.Remaining,
                    WasIdempotent = result.Message == "Already processed"
                };
            }

            _logger.LogWarning("トークン消費失敗: {Error}", result.Error);
            return new TokenConsumptionApiResponse
            {
                Success = false,
                ErrorCode = MapErrorToCode(result.Error),
                ErrorMessage = result.Error,
                NewUsageTotal = result.Current,
                RemainingTokens = result.Limit - result.Current
            };
        }
        catch (PostgrestException ex)
        {
            _logger.LogError(ex, "Supabaseエラー: {Message}", ex.Message);
            _isAvailable = false;
            return new TokenConsumptionApiResponse
            {
                Success = false,
                ErrorCode = "SUPABASE_ERROR",
                ErrorMessage = ex.Message
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "ネットワークエラー: {Message}", ex.Message);
            _isAvailable = false;
            return new TokenConsumptionApiResponse
            {
                Success = false,
                ErrorCode = "NETWORK_ERROR",
                ErrorMessage = "ネットワーク接続に問題があります"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "予期せぬエラー: {Message}", ex.Message);
            _isAvailable = false;
            return new TokenConsumptionApiResponse
            {
                Success = false,
                ErrorCode = "UNKNOWN_ERROR",
                ErrorMessage = ex.Message
            };
        }
    }

    /// <inheritdoc/>
    public async Task<SessionValidationResult> ValidateSessionAsync(
        string userId,
        string sessionToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionToken);

        try
        {
            // Supabase Authのセッション検証はAuth側で行われるため、
            // ここではユーザーIDとサブスクリプションの存在確認のみ
            // RPC呼び出しを使用
            var rpcParams = new Dictionary<string, object>
            {
                ["p_user_id"] = userId
            };

            var rpcResponse = await _supabase.Rpc("get_user_subscription", rpcParams)
                .ConfigureAwait(false);

            _isAvailable = true;

            // サブスクリプションがない場合でもセッションは有効
            // （Freeプランとして扱う）
            if (rpcResponse?.Content == null || rpcResponse.Content == "null" || rpcResponse.Content == "[]")
            {
                return SessionValidationResult.Valid;
            }

            var response = JsonSerializer.Deserialize<SubscriptionRecord>(
                rpcResponse.Content,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (response == null)
            {
                return SessionValidationResult.Valid;
            }

            // セッションIDが異なる場合は別デバイスからのログイン
            if (!string.IsNullOrEmpty(response.SessionId) && response.SessionId != sessionToken)
            {
                _logger.LogWarning(
                    "セッション競合検出: UserId={UserId}, StoredSession={Stored}, CurrentSession={Current}",
                    userId,
                    response.SessionId[..Math.Min(8, response.SessionId.Length)] + "...",
                    sessionToken[..Math.Min(8, sessionToken.Length)] + "...");

                return SessionValidationResult.Invalid("別のデバイスからログインされました");
            }

            return SessionValidationResult.Valid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "セッション検証エラー: {Message}", ex.Message);
            _isAvailable = false;

            // ネットワークエラーの場合はセッションを有効とみなす（キャッシュを信頼）
            return SessionValidationResult.Valid;
        }
    }

    /// <summary>
    /// SubscriptionRecordをLicenseStateにマッピング
    /// </summary>
    private static LicenseState MapToLicenseState(SubscriptionRecord record, string sessionToken)
    {
        var planType = ParsePlanType(record.PlanType);
        var nextPlan = string.IsNullOrEmpty(record.NextPlanType)
            ? (PlanType?)null
            : ParsePlanType(record.NextPlanType);

        return new LicenseState
        {
            CurrentPlan = planType,
            NextPlan = nextPlan,
            UserId = record.UserId,
            SessionId = record.FastspringCustomerId ?? sessionToken,
            ContractStartDate = record.CreatedAt,
            ExpirationDate = record.ExpiresAt,
            BillingCycleStart = record.BillingCycleStart,
            BillingCycleEnd = record.NextBillingDate,
            CloudAiTokensUsed = record.TokensUsed ?? 0,
            IsCached = false,
            LastServerSync = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 文字列からPlanTypeを解析
    /// </summary>
    private static PlanType ParsePlanType(string? planType)
    {
        if (string.IsNullOrEmpty(planType))
            return PlanType.Free;

        // Issue #125: Standardプラン廃止、後方互換性のためFreeにフォールバック
        return planType.ToLowerInvariant() switch
        {
            "free" => PlanType.Free,
            "standard" => PlanType.Free, // Issue #125: Standardプラン廃止
            "pro" => PlanType.Pro,
            "premia" => PlanType.Premia,
            _ => PlanType.Free
        };
    }

    /// <summary>
    /// エラーメッセージをエラーコードにマッピング
    /// </summary>
    private static string MapErrorToCode(string? error)
    {
        if (string.IsNullOrEmpty(error))
            return "UNKNOWN_ERROR";

        return error.ToLowerInvariant() switch
        {
            "quota exceeded" => "QUOTA_EXCEEDED",
            "user not found" => "USER_NOT_FOUND",
            _ => "SERVER_ERROR"
        };
    }
}

/// <summary>
/// subscriptionsテーブルのレコード
/// </summary>
internal sealed class SubscriptionRecord
{
    public string UserId { get; set; } = string.Empty;
    public string PlanType { get; set; } = "free";
    public string? NextPlanType { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? BillingCycleStart { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public DateTime? LastPaymentDate { get; set; }
    public long? TokensUsed { get; set; }
    public string? FastspringSubscriptionId { get; set; }
    public string? FastspringCustomerId { get; set; }
    public string? PaymentMethod { get; set; }
    public string? SessionId { get; set; }
    public string? SubscriptionSource { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// consume_cloud_ai_tokens RPC の結果
/// </summary>
internal sealed class TokenConsumptionRpcResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
    public long NewTotal { get; set; }
    public long Remaining { get; set; }
    public long Current { get; set; }
    public long Limit { get; set; }
}
