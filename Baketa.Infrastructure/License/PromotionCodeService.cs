using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Baketa.Core.Abstractions.Auth;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.License;
using Baketa.Core.Constants;
using Baketa.Core.License.Events;
using Baketa.Core.License.Models;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.License;

/// <summary>
/// プロモーションコードサービス実装
/// Issue #237 Phase 2: プロモーションコード機能
/// </summary>
/// <remarks>
/// - Relay Server経由でコード検証
/// - ローカルキャッシュ（LicenseSettings経由）
/// - Base32 Crockford形式のコード検証
/// </remarks>
public sealed class PromotionCodeService : IPromotionCodeService, IDisposable
{
    #region 定数定義

    /// <summary>
    /// コード形式: BAKETA-XXXX-XXXX (Base32 Crockford、O/0/I/1除外)
    /// </summary>
    private static readonly Regex CodeFormatRegex = new(
        ValidationPatterns.PromotionCode,
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// コードの最大長（DoS対策）
    /// </summary>
    private const int MaxCodeLength = 20;

    /// <summary>
    /// レスポンスメッセージの最大長
    /// </summary>
    private const int MaxMessageLength = 500;

    /// <summary>
    /// Relay ServerからのAPIエラーコード定数
    /// </summary>
    private static class ApiErrorCodes
    {
        public const string InvalidCode = "INVALID_CODE";
        public const string CodeNotFound = "CODE_NOT_FOUND";
        public const string AlreadyRedeemed = "CODE_ALREADY_REDEEMED";
        public const string AlreadyRedeemedAlt = "ALREADY_REDEEMED";
        public const string CodeExpired = "CODE_EXPIRED";
        public const string ExpiredAlt = "EXPIRED";
        public const string NotApplicable = "CODE_NOT_APPLICABLE";
        public const string AlreadyPro = "ALREADY_PRO";
        public const string RateLimited = "RATE_LIMITED";
        public const string TooManyRequests = "TOO_MANY_REQUESTS";
        public const string ParseError = "PARSE_ERROR";
        public const string Unknown = "UNKNOWN";
    }

    /// <summary>
    /// プランタイプ文字列定数
    /// </summary>
    private static class PlanTypeStrings
    {
        public const string Free = "free";
        public const string Standard = "standard";
        public const string Pro = "pro";
        public const string Premia = "premia";
    }

    #endregion

    private readonly HttpClient _httpClient;
    private readonly ILogger<PromotionCodeService> _logger;
    private readonly IOptionsMonitor<LicenseSettings> _settingsMonitor;
    private readonly IPromotionSettingsPersistence _settingsPersistence;
    private readonly IAuthService _authService;
    private readonly IEventAggregator _eventAggregator;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _disposed;

    /// <inheritdoc/>
    public event EventHandler<PromotionStateChangedEventArgs>? PromotionStateChanged;

    public PromotionCodeService(
        HttpClient httpClient,
        IOptionsMonitor<LicenseSettings> settingsMonitor,
        IPromotionSettingsPersistence settingsPersistence,
        IAuthService authService,
        IEventAggregator eventAggregator,
        ILogger<PromotionCodeService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _settingsMonitor = settingsMonitor ?? throw new ArgumentNullException(nameof(settingsMonitor));
        _settingsPersistence = settingsPersistence ?? throw new ArgumentNullException(nameof(settingsPersistence));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        _logger.LogDebug("PromotionCodeService initialized");
    }

    /// <inheritdoc/>
    public async Task<PromotionCodeResult> ApplyCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        // 形式検証
        var normalizedCode = NormalizeCode(code);
        if (!ValidateCodeFormat(normalizedCode))
        {
            _logger.LogWarning("Invalid promotion code format: {Code}", MaskCode(code));
            return PromotionCodeResult.CreateFailure(
                PromotionErrorCode.InvalidFormat,
                "プロモーションコードの形式が正しくありません。BAKETA-XXXX-XXXX の形式で入力してください。");
        }

        // 既にPro以上の場合は適用不可
        var currentPromotion = GetCurrentPromotion();
        if (currentPromotion?.IsValid == true && currentPromotion.Plan >= PlanType.Pro)
        {
            _logger.LogInformation("User already has Pro or higher plan via promotion");
            return PromotionCodeResult.CreateFailure(
                PromotionErrorCode.AlreadyProOrHigher,
                "既にProプラン以上が適用されています。");
        }

        // Relay Server APIに検証リクエスト
        try
        {
            var response = await SendRedeemRequestAsync(normalizedCode, cancellationToken).ConfigureAwait(false);

            if (response.Success)
            {
                // プラン情報を解析
                var appliedPlan = ParsePlanType(response.PlanType);
                var expiresAt = response.ExpiresAt ?? DateTime.UtcNow.AddMonths(1);

                // ローカルに保存
                await SavePromotionToSettingsAsync(normalizedCode, response, cancellationToken).ConfigureAwait(false);

                _logger.LogInformation(
                    "Promotion code applied successfully: Plan={Plan}, ExpiresAt={ExpiresAt}",
                    appliedPlan, expiresAt);

                // Issue #237: 保存とは別に直接PromotionInfoを作成（IOptionsMonitorの遅延更新を回避）
                var promotionInfo = new PromotionInfo
                {
                    Code = normalizedCode,
                    Plan = appliedPlan,
                    ExpiresAt = expiresAt,
                    AppliedAt = DateTime.UtcNow
                };

                // イベント発火
                PromotionStateChanged?.Invoke(this, new PromotionStateChangedEventArgs
                {
                    NewPromotion = promotionInfo,
                    Reason = "Promotion code applied"
                });

                // Issue #237: EventAggregator経由でLicenseManagerに通知（UI即時更新のため）
                await _eventAggregator.PublishAsync(new PromotionAppliedEvent(promotionInfo))
                    .ConfigureAwait(false);

                return PromotionCodeResult.CreateSuccess(
                    appliedPlan,
                    expiresAt,
                    TruncateMessage(response.Message, "プロモーションコードが適用されました。"));
            }
            else
            {
                var errorCode = MapErrorCode(response.ErrorCode);
                _logger.LogWarning(
                    "Promotion code rejected: ErrorCode={ErrorCode}, Message={Message}",
                    response.ErrorCode, response.Message);

                return PromotionCodeResult.CreateFailure(
                    errorCode,
                    TruncateMessage(response.Message, "コードの適用に失敗しました。"));
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error while applying promotion code");
            return PromotionCodeResult.CreateFailure(
                PromotionErrorCode.NetworkError,
                "サーバーに接続できません。インターネット接続を確認してください。");
        }
        catch (TaskCanceledException ex) when (ex.CancellationToken != cancellationToken)
        {
            _logger.LogError(ex, "Timeout while applying promotion code");
            return PromotionCodeResult.CreateFailure(
                PromotionErrorCode.NetworkError,
                "サーバーからの応答がタイムアウトしました。");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while applying promotion code");
            return PromotionCodeResult.CreateFailure(
                PromotionErrorCode.ServerError,
                "予期しないエラーが発生しました。");
        }
    }

    /// <inheritdoc/>
    public PromotionInfo? GetCurrentPromotion()
    {
        var settings = _settingsMonitor.CurrentValue;

        if (string.IsNullOrEmpty(settings.AppliedPromotionCode) ||
            !settings.PromotionPlanType.HasValue ||
            string.IsNullOrEmpty(settings.PromotionExpiresAt))
        {
            return null;
        }

        if (!DateTime.TryParse(settings.PromotionExpiresAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiresAt))
        {
            _logger.LogWarning("Failed to parse PromotionExpiresAt: {Value}", settings.PromotionExpiresAt);
            return null;
        }

        if (!DateTime.TryParse(settings.PromotionAppliedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var appliedAt))
        {
            _logger.LogWarning("Failed to parse PromotionAppliedAt: {Value}, using current time", settings.PromotionAppliedAt);
            appliedAt = DateTime.UtcNow;
        }

        return new PromotionInfo
        {
            Code = settings.AppliedPromotionCode,
            Plan = (PlanType)settings.PromotionPlanType.Value,
            ExpiresAt = expiresAt,
            AppliedAt = appliedAt
        };
    }

    /// <inheritdoc/>
    public bool ValidateCodeFormat(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;

        return CodeFormatRegex.IsMatch(NormalizeCode(code));
    }

    #region Private Methods

    /// <summary>
    /// Supabase Auth JWTをAuthorizationヘッダーに追加
    /// </summary>
    /// <remarks>
    /// ログイン済みの場合のみJWTを追加。未ログインでもプロモーション適用は可能。
    /// サーバー側でJWTを検証し、user_idを監査ログに記録する。
    /// </remarks>
    private async Task AddAuthorizationHeaderAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            var session = await _authService.GetCurrentSessionAsync(cancellationToken).ConfigureAwait(false);
            if (session?.IsValid == true && !string.IsNullOrEmpty(session.AccessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
                _logger.LogDebug("Added JWT authorization header for user tracking");
            }
            else
            {
                _logger.LogDebug("No valid session, proceeding without authorization header (anonymous promotion)");
            }
        }
        catch (Exception ex)
        {
            // 認証情報取得に失敗しても処理は継続（未ログイン扱い）
            _logger.LogDebug(ex, "Failed to get auth session, proceeding without authorization header");
        }
    }

    /// <summary>
    /// コードを正規化（大文字変換、空白除去、長さ制限）
    /// </summary>
    private static string NormalizeCode(string code)
    {
        var normalized = code.Trim().ToUpperInvariant();
        // DoS対策: 過度に長い入力を制限
        return normalized.Length > MaxCodeLength
            ? normalized[..MaxCodeLength]
            : normalized;
    }

    /// <summary>
    /// メッセージを安全な長さに切り詰め
    /// </summary>
    private static string TruncateMessage(string? message, string defaultMessage)
    {
        if (string.IsNullOrEmpty(message))
            return defaultMessage;

        return message.Length > MaxMessageLength
            ? message[..MaxMessageLength] + "..."
            : message;
    }

    /// <summary>
    /// コードをマスク（ログ用）
    /// プレフィックス（BAKETA-）のみ表示し、残りはマスク
    /// </summary>
    private static string MaskCode(string code)
    {
        if (string.IsNullOrEmpty(code))
            return "***";

        // BAKETA-XXXX-XXXX 形式の場合、BAKETA-****-**** と表示
        const string prefix = "BAKETA-";
        if (code.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return $"{prefix}****-****";
        }

        // その他の形式の場合は先頭3文字のみ表示
        if (code.Length <= 3)
            return "***";

        return $"{code[..3]}***";
    }

    /// <summary>
    /// Relay ServerにRedeemリクエストを送信
    /// </summary>
    private async Task<RedeemResponse> SendRedeemRequestAsync(string code, CancellationToken cancellationToken)
    {
        var request = new RedeemRequest { Code = code };
        var content = JsonContent.Create(request, options: _jsonOptions);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _settingsMonitor.CurrentValue.PromotionApiEndpoint)
        {
            Content = content
        };

        // Supabase Auth JWTをヘッダーに追加（ユーザー追跡用）
        // Issue #237: サーバー側でJWTを検証してuser_idを監査ログに記録
        await AddAuthorizationHeaderAsync(httpRequest, cancellationToken).ConfigureAwait(false);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        // レスポンスのセキュリティ検証
        var validationError = ValidateApiResponse(response, responseContent);
        if (validationError != null)
        {
            return validationError;
        }

        if (response.IsSuccessStatusCode)
        {
            return ParseAndValidateSuccessResponse(responseContent);
        }

        // エラーレスポンスの解析
        return ParseErrorResponse(response, responseContent);
    }

    /// <summary>
    /// APIレスポンスのセキュリティ検証
    /// </summary>
    private RedeemResponse? ValidateApiResponse(HttpResponseMessage response, string responseContent)
    {
        // Content-Type厳格検証（application/json のみ許可）
        var contentTypeHeader = response.Content.Headers.ContentType;
        if (contentTypeHeader != null)
        {
            var mediaType = contentTypeHeader.MediaType;
            // application/json または application/problem+json を許可
            if (!string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(mediaType, "application/problem+json", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Unexpected Content-Type: {ContentType}", contentTypeHeader);
                return new RedeemResponse
                {
                    Success = false,
                    ErrorCode = ApiErrorCodes.ParseError,
                    Message = "サーバーからの応答形式が不正です"
                };
            }
        }

        // レスポンスサイズ検証（DoS対策: 1MBまで）
        const int maxResponseSize = 1024 * 1024;
        if (responseContent.Length > maxResponseSize)
        {
            _logger.LogWarning("Response too large: {Size} bytes", responseContent.Length);
            return new RedeemResponse
            {
                Success = false,
                ErrorCode = ApiErrorCodes.ParseError,
                Message = "サーバーからの応答が大きすぎます"
            };
        }

        return null;
    }

    /// <summary>
    /// 成功レスポンスの解析と検証
    /// </summary>
    private RedeemResponse ParseAndValidateSuccessResponse(string responseContent)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<RedeemResponse>(responseContent, _jsonOptions);

            if (parsed == null)
            {
                return new RedeemResponse
                {
                    Success = false,
                    ErrorCode = ApiErrorCodes.ParseError,
                    Message = "応答の解析に失敗しました"
                };
            }

            // 成功時の必須フィールド検証
            if (parsed.Success)
            {
                if (string.IsNullOrEmpty(parsed.PlanType))
                {
                    _logger.LogWarning("Success response missing PlanType");
                    return new RedeemResponse
                    {
                        Success = false,
                        ErrorCode = ApiErrorCodes.ParseError,
                        Message = "サーバーからの応答にプラン情報がありません"
                    };
                }
            }

            return parsed;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse success response");
            return new RedeemResponse
            {
                Success = false,
                ErrorCode = ApiErrorCodes.ParseError,
                Message = "応答の解析に失敗しました"
            };
        }
    }

    /// <summary>
    /// エラーレスポンスの解析
    /// </summary>
    private RedeemResponse ParseErrorResponse(HttpResponseMessage response, string responseContent)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<RedeemResponse>(responseContent, _jsonOptions);
            if (parsed != null)
            {
                // メッセージの安全な切り詰め
                parsed.Message = TruncateMessage(parsed.Message, "不明なエラーが発生しました");
                return parsed;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogDebug(ex, "Could not parse error response as JSON");
        }

        return new RedeemResponse
        {
            Success = false,
            ErrorCode = $"HTTP_{(int)response.StatusCode}",
            Message = $"サーバーエラー: {response.StatusCode}"
        };
    }

    /// <summary>
    /// プロモーション情報を設定に保存
    /// </summary>
    private async Task SavePromotionToSettingsAsync(string code, RedeemResponse response, CancellationToken cancellationToken)
    {
        var plan = ParsePlanType(response.PlanType);
        var expiresAt = response.ExpiresAt ?? DateTime.UtcNow.AddMonths(1);

        var saved = await _settingsPersistence.SavePromotionAsync(
            code,
            plan,
            expiresAt,
            cancellationToken).ConfigureAwait(false);

        if (saved)
        {
            _logger.LogDebug("Promotion saved to settings: Code={Code}, Plan={Plan}", MaskCode(code), response.PlanType);
        }
        else
        {
            _logger.LogWarning("Failed to save promotion to settings: Code={Code}", MaskCode(code));
        }
    }

    /// <summary>
    /// プランタイプを解析
    /// </summary>
    private static PlanType ParsePlanType(string? planType)
    {
        return planType?.ToLowerInvariant() switch
        {
            PlanTypeStrings.Free => PlanType.Free,
            PlanTypeStrings.Standard => PlanType.Standard,
            PlanTypeStrings.Pro => PlanType.Pro,
            PlanTypeStrings.Premia => PlanType.Premia,
            _ => PlanType.Pro // デフォルトはPro
        };
    }

    /// <summary>
    /// エラーコードをマップ
    /// </summary>
    private static PromotionErrorCode MapErrorCode(string? errorCode)
    {
        return errorCode?.ToUpperInvariant() switch
        {
            ApiErrorCodes.InvalidCode or ApiErrorCodes.CodeNotFound => PromotionErrorCode.CodeNotFound,
            ApiErrorCodes.AlreadyRedeemed or ApiErrorCodes.AlreadyRedeemedAlt => PromotionErrorCode.AlreadyRedeemed,
            ApiErrorCodes.CodeExpired or ApiErrorCodes.ExpiredAlt => PromotionErrorCode.CodeExpired,
            ApiErrorCodes.NotApplicable or ApiErrorCodes.AlreadyPro => PromotionErrorCode.AlreadyProOrHigher,
            ApiErrorCodes.RateLimited or ApiErrorCodes.TooManyRequests => PromotionErrorCode.RateLimited,
            _ => PromotionErrorCode.ServerError
        };
    }

    #endregion

    #region Request/Response Models

    private sealed class RedeemRequest
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = string.Empty;
    }

    private sealed class RedeemResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("plan_type")]
        public string? PlanType { get; set; }

        [JsonPropertyName("expires_at")]
        public DateTime? ExpiresAt { get; set; }

        [JsonPropertyName("error_code")]
        public string? ErrorCode { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _logger.LogDebug("PromotionCodeService disposed");
    }
}
