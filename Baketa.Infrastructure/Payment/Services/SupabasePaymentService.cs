using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Baketa.Core.Abstractions.Payment;
using Baketa.Core.License.Models;
using Baketa.Core.Payment.Models;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SupabaseClient = Supabase.Client;

namespace Baketa.Infrastructure.Payment.Services;

/// <summary>
/// Supabase Edge Functionsを使用した決済サービス
/// FastSpring統合のためのチェックアウトセッション作成、サブスクリプション管理を提供
/// </summary>
public sealed class SupabasePaymentService : IPaymentService
{
    private readonly SupabaseClient _supabase;
    private readonly HttpClient _httpClient;
    private readonly ILogger<SupabasePaymentService> _logger;
    private readonly PaymentSettings _settings;
    private bool _isAvailable = true;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <inheritdoc/>
    public bool IsAvailable => _isAvailable;

    /// <summary>
    /// SupabasePaymentServiceを初期化
    /// </summary>
    public SupabasePaymentService(
        SupabaseClient supabase,
        HttpClient httpClient,
        ILogger<SupabasePaymentService> logger,
        IOptions<PaymentSettings> settings)
    {
        _supabase = supabase ?? throw new ArgumentNullException(nameof(supabase));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));

        _logger.LogInformation("SupabasePaymentService初期化完了");
    }

    /// <inheritdoc/>
    public async Task<PaymentResult<CheckoutSession>> CreateCheckoutSessionAsync(
        string userId,
        PlanType targetPlan,
        BillingCycle billingCycle,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (targetPlan == PlanType.Free)
        {
            return PaymentResult<CheckoutSession>.CreateFailure(
                "INVALID_PLAN",
                "無料プランでは決済セッションを作成できません");
        }

        try
        {
            _logger.LogInformation(
                "チェックアウトセッション作成開始: UserId={UserId}, Plan={Plan}, Cycle={Cycle}",
                userId, targetPlan, billingCycle);

            var request = new CreateCheckoutRequest
            {
                UserId = userId,
                TargetPlan = targetPlan,
                BillingCycle = billingCycle
            };

            var response = await CallEdgeFunctionAsync<CheckoutSessionResponse>(
                "create-checkout-session",
                new
                {
                    planType = targetPlan.ToString().ToLowerInvariant(),
                    billingCycle = billingCycle.GetProductSuffix()
                },
                cancellationToken).ConfigureAwait(false);

            if (response == null)
            {
                return PaymentResult<CheckoutSession>.CreateFailure(
                    "RESPONSE_NULL",
                    "サーバーからの応答がありません");
            }

            if (!string.IsNullOrEmpty(response.Error))
            {
                return PaymentResult<CheckoutSession>.CreateFailure(
                    response.ErrorCode ?? "SERVER_ERROR",
                    response.Error);
            }

            var session = new CheckoutSession
            {
                SessionId = response.SessionId ?? string.Empty,
                CheckoutUrl = response.CheckoutUrl ?? string.Empty,
                ExpiresAt = response.ExpiresAt ?? DateTime.UtcNow.AddMinutes(30)
            };

            _isAvailable = true;
            _logger.LogInformation(
                "チェックアウトセッション作成成功: SessionId={SessionId}",
                session.SessionId);

            return PaymentResult<CheckoutSession>.CreateSuccess(session);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "ネットワークエラー: {Message}", ex.Message);
            _isAvailable = false;
            return PaymentResult<CheckoutSession>.CreateFailure(
                "NETWORK_ERROR",
                "ネットワーク接続に問題があります");
        }
        catch (TaskCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "チェックアウトセッション作成エラー: {Message}", ex.Message);
            return PaymentResult<CheckoutSession>.CreateFailure(
                "UNKNOWN_ERROR",
                ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<PaymentResult<SubscriptionInfo?>> GetSubscriptionAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        try
        {
            _logger.LogDebug("サブスクリプション情報取得: UserId={UserId}", userId);

            // RPC呼び出しを使用してサブスクリプション情報を取得
            var rpcParams = new Dictionary<string, object>
            {
                ["p_user_id"] = userId
            };

            var rpcResponse = await _supabase.Rpc("get_user_subscription", rpcParams)
                .ConfigureAwait(false);

            if (rpcResponse?.Content == null || rpcResponse.Content == "null" || rpcResponse.Content == "[]")
            {
                _logger.LogDebug("サブスクリプションが見つかりません: UserId={UserId}", userId);
                return PaymentResult<SubscriptionInfo?>.CreateSuccess(null);
            }

            var response = JsonSerializer.Deserialize<SubscriptionRecord>(
                rpcResponse.Content,
                JsonOptions);

            if (response == null)
            {
                _logger.LogDebug("サブスクリプションが見つかりません: UserId={UserId}", userId);
                return PaymentResult<SubscriptionInfo?>.CreateSuccess(null);
            }

            var info = MapToSubscriptionInfo(response);
            _isAvailable = true;

            _logger.LogDebug(
                "サブスクリプション情報取得成功: Plan={Plan}, Status={Status}",
                info.CurrentPlan, info.Status);

            return PaymentResult<SubscriptionInfo?>.CreateSuccess(info);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "サブスクリプション情報取得エラー: {Message}", ex.Message);
            _isAvailable = false;
            return PaymentResult<SubscriptionInfo?>.CreateFailure(
                "UNKNOWN_ERROR",
                ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<PaymentResult> CancelSubscriptionAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        try
        {
            _logger.LogInformation("サブスクリプションキャンセル開始: UserId={UserId}", userId);

            // Edge Functionを呼び出してFastSpringのサブスクリプションをキャンセル
            var response = await CallEdgeFunctionAsync<CancelSubscriptionResponse>(
                "cancel-subscription",
                new { userId },
                cancellationToken).ConfigureAwait(false);

            if (response == null)
            {
                return PaymentResult.CreateFailure(
                    "RESPONSE_NULL",
                    "サーバーからの応答がありません");
            }

            if (!response.Success)
            {
                return PaymentResult.CreateFailure(
                    response.ErrorCode ?? "CANCEL_ERROR",
                    response.Error ?? "キャンセルに失敗しました");
            }

            _isAvailable = true;
            _logger.LogInformation("サブスクリプションキャンセル成功: UserId={UserId}", userId);

            return PaymentResult.CreateSuccess();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "サブスクリプションキャンセルエラー: {Message}", ex.Message);
            return PaymentResult.CreateFailure("UNKNOWN_ERROR", ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task<PaymentResult<CustomerPortalUrl>> GetSecurePortalUrlAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        try
        {
            _logger.LogDebug("セキュアポータルURL取得: UserId={UserId}", userId);

            var response = await CallEdgeFunctionAsync<PortalUrlResponse>(
                "get-secure-portal-url",
                new { userId },
                cancellationToken).ConfigureAwait(false);

            if (response == null || string.IsNullOrEmpty(response.PortalUrl))
            {
                // フォールバック: 通常のポータルURL
                return PaymentResult<CustomerPortalUrl>.CreateSuccess(new CustomerPortalUrl
                {
                    Url = _settings.StorefrontUrl + "/account",
                    ExpiresAt = DateTime.UtcNow.AddHours(24)
                });
            }

            var portalUrl = new CustomerPortalUrl
            {
                Url = response.PortalUrl,
                ExpiresAt = response.ExpiresAt ?? DateTime.UtcNow.AddSeconds(_settings.PortalUrlExpirySeconds)
            };

            _isAvailable = true;
            return PaymentResult<CustomerPortalUrl>.CreateSuccess(portalUrl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "セキュアポータルURL取得エラー、フォールバック使用: {Message}", ex.Message);

            // フォールバック
            return PaymentResult<CustomerPortalUrl>.CreateSuccess(new CustomerPortalUrl
            {
                Url = _settings.StorefrontUrl + "/account",
                ExpiresAt = DateTime.UtcNow.AddHours(24)
            });
        }
    }

    /// <inheritdoc/>
    public async Task<PaymentResult<IReadOnlyList<PaymentHistoryEntry>>> GetPaymentHistoryAsync(
        string userId,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        try
        {
            _logger.LogDebug("決済履歴取得: UserId={UserId}, Limit={Limit}", userId, limit);

            // RPC呼び出しを使用して決済履歴を取得
            var rpcParams = new Dictionary<string, object>
            {
                ["p_user_id"] = userId,
                ["p_limit"] = limit
            };

            var rpcResponse = await _supabase.Rpc("get_payment_history", rpcParams)
                .ConfigureAwait(false);

            if (rpcResponse?.Content == null || rpcResponse.Content == "null" || rpcResponse.Content == "[]")
            {
                _logger.LogDebug("決済履歴が見つかりません: UserId={UserId}", userId);
                return PaymentResult<IReadOnlyList<PaymentHistoryEntry>>.CreateSuccess([]);
            }

            var records = JsonSerializer.Deserialize<List<PaymentHistoryRecord>>(
                rpcResponse.Content,
                JsonOptions) ?? [];

            var entries = records
                .Select(MapToPaymentHistoryEntry)
                .ToList();

            _isAvailable = true;
            _logger.LogDebug("決済履歴取得成功: Count={Count}", entries.Count);

            return PaymentResult<IReadOnlyList<PaymentHistoryEntry>>.CreateSuccess(entries);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "決済履歴取得エラー: {Message}", ex.Message);
            _isAvailable = false;
            return PaymentResult<IReadOnlyList<PaymentHistoryEntry>>.CreateFailure(
                "UNKNOWN_ERROR",
                ex.Message);
        }
    }

    /// <summary>
    /// Edge Functionを呼び出し
    /// </summary>
    private async Task<T?> CallEdgeFunctionAsync<T>(
        string functionName,
        object payload,
        CancellationToken cancellationToken) where T : class
    {
        if (string.IsNullOrEmpty(_settings.EdgeFunctionsUrl))
        {
            throw new InvalidOperationException("Edge Functions URLが設定されていません");
        }

        var url = $"{_settings.EdgeFunctionsUrl.TrimEnd('/')}/{functionName}";
        var json = JsonSerializer.Serialize(payload, JsonOptions);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(_settings.CheckoutTimeoutSeconds));

        // HttpRequestMessageを使用してリクエストごとにヘッダーを設定（スレッドセーフ）
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        // 認証ヘッダーを追加
        var session = _supabase.Auth.CurrentSession;
        if (session?.AccessToken != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        }

        var response = await _httpClient.SendAsync(request, cts.Token)
            .ConfigureAwait(false);

        var responseContent = await response.Content.ReadAsStringAsync(cts.Token)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Edge Function呼び出し失敗: Function={Function}, Status={Status}, Body={Body}",
                functionName, response.StatusCode, responseContent);

            throw new HttpRequestException(
                $"Edge Function '{functionName}' returned {response.StatusCode}: {responseContent}");
        }

        return JsonSerializer.Deserialize<T>(responseContent, JsonOptions);
    }

    /// <summary>
    /// SubscriptionRecordをSubscriptionInfoにマッピング
    /// </summary>
    private static SubscriptionInfo MapToSubscriptionInfo(SubscriptionRecord record)
    {
        return new SubscriptionInfo
        {
            SubscriptionId = record.FastspringSubscriptionId ?? string.Empty,
            CustomerId = record.FastspringCustomerId ?? string.Empty,
            CurrentPlan = ParsePlanType(record.PlanType),
            BillingCycle = ParseBillingCycle(record.BillingCycle),
            Status = ParseSubscriptionStatus(record),
            NextBillingDate = record.NextBillingDate,
            LastPaymentDate = record.LastPaymentDate,
            PaymentMethod = record.PaymentMethod,
            NextPlan = string.IsNullOrEmpty(record.NextPlanType)
                ? null
                : ParsePlanType(record.NextPlanType)
        };
    }

    /// <summary>
    /// PaymentHistoryRecordをPaymentHistoryEntryにマッピング
    /// </summary>
    private static PaymentHistoryEntry MapToPaymentHistoryEntry(PaymentHistoryRecord record)
    {
        return new PaymentHistoryEntry
        {
            PaymentId = record.Id,
            OrderId = record.FastspringOrderId ?? string.Empty,
            Plan = ParsePlanType(record.PlanType),
            BillingCycle = ParseBillingCycle(record.BillingCycle),
            AmountYen = record.AmountJpy,
            Currency = record.Currency ?? "JPY",
            Status = ParsePaymentStatus(record.Status),
            EventType = record.EventType ?? string.Empty,
            CreatedAt = record.CreatedAt
        };
    }

    private static PlanType ParsePlanType(string? planType)
    {
        if (string.IsNullOrEmpty(planType))
            return PlanType.Free;

        return planType.ToLowerInvariant() switch
        {
            "free" => PlanType.Free,
            "standard" => PlanType.Standard,
            "pro" => PlanType.Pro,
            "premia" => PlanType.Premia,
            _ => PlanType.Free
        };
    }

    private static BillingCycle ParseBillingCycle(string? cycle)
    {
        if (string.IsNullOrEmpty(cycle))
            return BillingCycle.Monthly;

        return cycle.ToLowerInvariant() switch
        {
            "yearly" => BillingCycle.Yearly,
            _ => BillingCycle.Monthly
        };
    }

    private static SubscriptionStatus ParseSubscriptionStatus(SubscriptionRecord record)
    {
        if (record.ExpiresAt.HasValue && record.ExpiresAt.Value <= DateTime.UtcNow)
            return SubscriptionStatus.Expired;

        if (record.NextPlanType?.ToLowerInvariant() == "free")
            return SubscriptionStatus.Canceled;

        return SubscriptionStatus.Active;
    }

    private static PaymentStatus ParsePaymentStatus(string? status)
    {
        if (string.IsNullOrEmpty(status))
            return PaymentStatus.Pending;

        return status.ToLowerInvariant() switch
        {
            "completed" => PaymentStatus.Completed,
            "refunded" => PaymentStatus.Refunded,
            "failed" => PaymentStatus.Failed,
            _ => PaymentStatus.Pending
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
    public string? BillingCycle { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? BillingCycleStart { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public DateTime? LastPaymentDate { get; set; }
    public string? FastspringSubscriptionId { get; set; }
    public string? FastspringCustomerId { get; set; }
    public string? PaymentMethod { get; set; }
}

/// <summary>
/// payment_historyテーブルのレコード
/// </summary>
internal sealed class PaymentHistoryRecord
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? FastspringOrderId { get; set; }
    public string PlanType { get; set; } = "free";
    public string? BillingCycle { get; set; }
    public int AmountJpy { get; set; }
    public string? Currency { get; set; }
    public string? Status { get; set; }
    public string? EventType { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// チェックアウトセッションEdge Function応答
/// </summary>
internal sealed class CheckoutSessionResponse
{
    public string? SessionId { get; set; }
    public string? CheckoutUrl { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? Error { get; set; }
    public string? ErrorCode { get; set; }
}

/// <summary>
/// キャンセルEdge Function応答
/// </summary>
internal sealed class CancelSubscriptionResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? ErrorCode { get; set; }
}

/// <summary>
/// ポータルURLEdge Function応答
/// </summary>
internal sealed class PortalUrlResponse
{
    public string? PortalUrl { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
