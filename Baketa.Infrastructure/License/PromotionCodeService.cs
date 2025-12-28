using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Baketa.Core.Abstractions.License;
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
    /// <summary>
    /// コード形式: BAKETA-XXXX-XXXX (Base32 Crockford、O/0/I/1除外)
    /// </summary>
    private static readonly Regex CodeFormatRegex = new(
        @"^BAKETA-[0-9A-HJ-NP-TV-Z]{4}-[0-9A-HJ-NP-TV-Z]{4}$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly HttpClient _httpClient;
    private readonly ILogger<PromotionCodeService> _logger;
    private readonly LicenseSettings _settings;
    private readonly IOptionsMonitor<LicenseSettings> _settingsMonitor;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _disposed;

    /// <inheritdoc/>
    public event EventHandler<PromotionStateChangedEventArgs>? PromotionStateChanged;

    public PromotionCodeService(
        HttpClient httpClient,
        IOptionsMonitor<LicenseSettings> settingsMonitor,
        ILogger<PromotionCodeService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _settingsMonitor = settingsMonitor ?? throw new ArgumentNullException(nameof(settingsMonitor));
        _settings = settingsMonitor.CurrentValue;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        _logger.LogInformation("PromotionCodeService initialized");
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

        // モックモード対応
        if (_settings.EnableMockMode)
        {
            return await ApplyCodeMockAsync(normalizedCode, cancellationToken).ConfigureAwait(false);
        }

        // Relay Server APIに検証リクエスト
        try
        {
            var response = await SendRedeemRequestAsync(normalizedCode, cancellationToken).ConfigureAwait(false);

            if (response.Success)
            {
                // ローカルに保存
                SavePromotionToSettings(normalizedCode, response);

                _logger.LogInformation(
                    "Promotion code applied successfully: Plan={Plan}, ExpiresAt={ExpiresAt}",
                    response.PlanType, response.ExpiresAt);

                // イベント発火
                var promotionInfo = GetCurrentPromotion();
                PromotionStateChanged?.Invoke(this, new PromotionStateChangedEventArgs
                {
                    NewPromotion = promotionInfo,
                    Reason = "Promotion code applied"
                });

                return PromotionCodeResult.CreateSuccess(
                    ParsePlanType(response.PlanType),
                    response.ExpiresAt ?? DateTime.UtcNow.AddMonths(1),
                    response.Message ?? "プロモーションコードが適用されました。");
            }
            else
            {
                var errorCode = MapErrorCode(response.ErrorCode);
                _logger.LogWarning(
                    "Promotion code rejected: ErrorCode={ErrorCode}, Message={Message}",
                    response.ErrorCode, response.Message);

                return PromotionCodeResult.CreateFailure(errorCode, response.Message ?? "コードの適用に失敗しました。");
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

        if (!DateTime.TryParse(settings.PromotionExpiresAt, out var expiresAt))
        {
            return null;
        }

        if (!DateTime.TryParse(settings.PromotionAppliedAt, out var appliedAt))
        {
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
    /// コードを正規化（大文字変換、空白除去）
    /// </summary>
    private static string NormalizeCode(string code)
    {
        return code.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// コードをマスク（ログ用）
    /// </summary>
    private static string MaskCode(string code)
    {
        if (code.Length <= 8)
            return "***";
        return $"{code[..8]}****";
    }

    /// <summary>
    /// モックモードでのコード適用
    /// </summary>
    private async Task<PromotionCodeResult> ApplyCodeMockAsync(string code, CancellationToken cancellationToken)
    {
        await Task.Delay(500, cancellationToken).ConfigureAwait(false); // シミュレート

        // テスト用: BAKETA-TEST-PROx で始まるコードはProプランを適用
        if (code.StartsWith("BAKETA-TEST", StringComparison.OrdinalIgnoreCase))
        {
            var expiresAt = DateTime.UtcNow.AddMonths(1);
            SavePromotionToSettings(code, new RedeemResponse
            {
                Success = true,
                PlanType = "pro",
                ExpiresAt = expiresAt,
                Message = "[モックモード] プロモーションコードが適用されました。"
            });

            var promotionInfo = GetCurrentPromotion();
            PromotionStateChanged?.Invoke(this, new PromotionStateChangedEventArgs
            {
                NewPromotion = promotionInfo,
                Reason = "Mock promotion code applied"
            });

            _logger.LogInformation("[MockMode] Promotion code applied: {Code}", MaskCode(code));
            return PromotionCodeResult.CreateSuccess(PlanType.Pro, expiresAt, "[モックモード] Proプランが適用されました。");
        }

        return PromotionCodeResult.CreateFailure(
            PromotionErrorCode.CodeNotFound,
            "[モックモード] 無効なプロモーションコードです。");
    }

    /// <summary>
    /// Relay ServerにRedeemリクエストを送信
    /// </summary>
    private async Task<RedeemResponse> SendRedeemRequestAsync(string code, CancellationToken cancellationToken)
    {
        var request = new RedeemRequest { Code = code };
        var content = JsonContent.Create(request, options: _jsonOptions);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _settings.PromotionApiEndpoint)
        {
            Content = content
        };

        // TODO: セッショントークンがある場合はヘッダーに追加
        // httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessionToken);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            return JsonSerializer.Deserialize<RedeemResponse>(responseContent, _jsonOptions)
                   ?? new RedeemResponse { Success = false, ErrorCode = "PARSE_ERROR", Message = "応答の解析に失敗しました" };
        }

        // エラーレスポンスの解析
        try
        {
            return JsonSerializer.Deserialize<RedeemResponse>(responseContent, _jsonOptions)
                   ?? new RedeemResponse { Success = false, ErrorCode = "UNKNOWN", Message = "不明なエラーが発生しました" };
        }
        catch
        {
            return new RedeemResponse
            {
                Success = false,
                ErrorCode = $"HTTP_{(int)response.StatusCode}",
                Message = $"サーバーエラー: {response.StatusCode}"
            };
        }
    }

    /// <summary>
    /// プロモーション情報を設定に保存
    /// </summary>
    private void SavePromotionToSettings(string code, RedeemResponse response)
    {
        var settings = _settingsMonitor.CurrentValue;
        settings.AppliedPromotionCode = code;
        settings.PromotionPlanType = (int)ParsePlanType(response.PlanType);
        settings.PromotionExpiresAt = response.ExpiresAt?.ToString("O");
        settings.PromotionAppliedAt = DateTime.UtcNow.ToString("O");
        settings.LastOnlineVerification = DateTime.UtcNow.ToString("O");

        // TODO: 設定の永続化をトリガー
        _logger.LogDebug("Promotion saved to settings: Code={Code}, Plan={Plan}", MaskCode(code), response.PlanType);
    }

    /// <summary>
    /// プランタイプを解析
    /// </summary>
    private static PlanType ParsePlanType(string? planType)
    {
        return planType?.ToLowerInvariant() switch
        {
            "free" => PlanType.Free,
            "standard" => PlanType.Standard,
            "pro" => PlanType.Pro,
            "premia" => PlanType.Premia,
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
            "INVALID_CODE" or "CODE_NOT_FOUND" => PromotionErrorCode.CodeNotFound,
            "CODE_ALREADY_REDEEMED" or "ALREADY_REDEEMED" => PromotionErrorCode.AlreadyRedeemed,
            "CODE_EXPIRED" or "EXPIRED" => PromotionErrorCode.CodeExpired,
            "CODE_NOT_APPLICABLE" or "ALREADY_PRO" => PromotionErrorCode.AlreadyProOrHigher,
            "RATE_LIMITED" or "TOO_MANY_REQUESTS" => PromotionErrorCode.RateLimited,
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
