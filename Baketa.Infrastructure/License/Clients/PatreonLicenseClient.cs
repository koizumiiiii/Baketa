using System.Net.Http;
using Baketa.Core.Abstractions.License;
using Baketa.Core.License.Extensions;
using Baketa.Core.License.Models;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.License.Clients;

/// <summary>
/// Patreon連携を使用したライセンスAPIクライアント
/// IPatreonOAuthServiceを使用してPatreonからライセンス状態を取得
/// </summary>
public sealed class PatreonLicenseClient : ILicenseApiClient
{
    private readonly ILogger<PatreonLicenseClient> _logger;
    private readonly IPatreonOAuthService _oauthService;
    private readonly LicenseSettings _licenseSettings;
    private readonly PatreonSettings _patreonSettings;
    private bool _isAvailable = true;

    /// <inheritdoc/>
    public bool IsAvailable => _isAvailable && _oauthService.IsAuthenticated;

    /// <summary>
    /// PatreonLicenseClientを初期化
    /// </summary>
    public PatreonLicenseClient(
        ILogger<PatreonLicenseClient> logger,
        IPatreonOAuthService oauthService,
        IOptions<LicenseSettings> licenseSettings,
        IOptions<PatreonSettings> patreonSettings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _oauthService = oauthService ?? throw new ArgumentNullException(nameof(oauthService));
        _licenseSettings = licenseSettings?.Value ?? throw new ArgumentNullException(nameof(licenseSettings));
        _patreonSettings = patreonSettings?.Value ?? throw new ArgumentNullException(nameof(patreonSettings));

        _logger.LogInformation("🔗 PatreonLicenseClient初期化完了");
    }

    /// <inheritdoc/>
    public async Task<LicenseApiResponse?> GetLicenseStateAsync(
        string userId,
        string sessionToken,
        CancellationToken cancellationToken = default)
    {
        // Patreon連携の場合、userId/sessionTokenはPatreonのものを使用
        // ただし、既存のインターフェース互換性のためパラメータは受け取る

        try
        {
            _logger.LogDebug("Patreonからライセンス状態を取得中...");

            // PatreonOAuthServiceを使って同期
            var syncResult = await _oauthService.SyncLicenseAsync(forceRefresh: false, cancellationToken)
                .ConfigureAwait(false);

            if (!syncResult.Success)
            {
                _isAvailable = syncResult.Status != PatreonSyncStatus.TokenExpired;

                if (syncResult.Status == PatreonSyncStatus.NotConnected)
                {
                    // Patreon未接続 → Freeプラン
                    return LicenseApiResponse.CreateSuccess(LicenseState.Default);
                }

                return LicenseApiResponse.CreateFailure(
                    MapSyncStatusToErrorCode(syncResult.Status),
                    syncResult.ErrorMessage ?? "ライセンス同期に失敗しました");
            }

            // PatreonOAuthServiceから資格情報を取得
            var credentials = await _oauthService.LoadCredentialsAsync(cancellationToken)
                .ConfigureAwait(false);

            if (credentials == null)
            {
                return LicenseApiResponse.CreateSuccess(LicenseState.Default);
            }

            // LicenseStateを構築
            var state = new LicenseState
            {
                CurrentPlan = syncResult.Plan,
                UserId = credentials.PatreonUserId,
                PatreonUserId = credentials.PatreonUserId,
                PatreonSyncStatus = syncResult.FromCache ? PatreonSyncStatus.Offline : PatreonSyncStatus.Synced,
                PatronStatus = credentials.PatronStatus,
                PatreonTierId = credentials.LastKnownTierId,
                ExpirationDate = syncResult.NextChargeDate,
                ContractStartDate = credentials.SessionTokenObtainedAt,
                BillingCycleEnd = syncResult.NextChargeDate,
                LastServerSync = credentials.LastSyncTime ?? DateTime.UtcNow,
                IsCached = syncResult.FromCache,
                CloudAiTokensUsed = 0 // Patreonではトークン消費管理はクライアント側
            };

            _isAvailable = true;

            _logger.LogDebug(
                "ライセンス状態取得成功: Plan={Plan}, PatronStatus={PatronStatus}, FromCache={FromCache}",
                state.CurrentPlan,
                state.PatronStatus,
                syncResult.FromCache);

            return LicenseApiResponse.CreateSuccess(state);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Patreon API通信エラー");
            _isAvailable = false;
            return LicenseApiResponse.CreateFailure("NETWORK_ERROR", $"ネットワークエラー: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "予期せぬエラー");
            _isAvailable = false;
            return LicenseApiResponse.CreateFailure("UNKNOWN_ERROR", ex.Message);
        }
    }

    /// <inheritdoc/>
    public Task<TokenConsumptionApiResponse> ConsumeTokensAsync(
        TokenConsumptionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Patreon連携ではトークン消費はサーバーで管理しない（クライアントローカル管理）
        // ここでは楽観的に成功を返す
        // 注意: 本番環境では、トークン消費管理用の別サービスが必要な場合がある

        _logger.LogDebug(
            "トークン消費（ローカル管理）: TokenCount={Count}, IdempotencyKey={Key}",
            request.TokenCount,
            request.IdempotencyKey);

        // ローカルキャッシュでの消費管理は LicenseManager/LicenseCacheService に委譲
        return Task.FromResult(new TokenConsumptionApiResponse
        {
            Success = true,
            NewUsageTotal = request.TokenCount, // 実際の累計はLicenseCacheServiceで管理
            RemainingTokens = long.MaxValue // ローカル管理のため上限なし表示
        });
    }

    /// <inheritdoc/>
    public Task<SessionValidationResult> ValidateSessionAsync(
        string userId,
        string sessionToken,
        CancellationToken cancellationToken = default)
    {
        // Patreon連携ではセッション検証 = PatreonOAuthServiceの認証状態チェック

        if (!_oauthService.IsAuthenticated)
        {
            return Task.FromResult(SessionValidationResult.Invalid("Patreon連携が設定されていません"));
        }

        // 同期ステータスをチェック
        var syncStatus = _oauthService.SyncStatus;

        var result = syncStatus switch
        {
            PatreonSyncStatus.Synced => SessionValidationResult.Valid,
            PatreonSyncStatus.Offline => SessionValidationResult.Valid, // オフラインでもセッションは有効
            PatreonSyncStatus.TokenExpired => SessionValidationResult.Invalid("Patreon認証の有効期限が切れました。再認証が必要です。"),
            PatreonSyncStatus.Error => SessionValidationResult.Invalid("Patreon連携でエラーが発生しています"),
            PatreonSyncStatus.NotConnected => SessionValidationResult.Invalid("Patreon連携が設定されていません"),
            _ => SessionValidationResult.Valid
        };

        return Task.FromResult(result);
    }

    /// <summary>
    /// PatreonSyncStatusをエラーコードにマッピング
    /// </summary>
    private static string MapSyncStatusToErrorCode(PatreonSyncStatus status)
    {
        return status switch
        {
            PatreonSyncStatus.TokenExpired => "SESSION_INVALID",
            PatreonSyncStatus.Error => "SYNC_ERROR",
            PatreonSyncStatus.Offline => "OFFLINE",
            PatreonSyncStatus.NotConnected => "NOT_CONNECTED",
            _ => "UNKNOWN_ERROR"
        };
    }
}
