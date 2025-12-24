using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Baketa.Core.Abstractions.License;
using Baketa.Core.License.Models;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.License.Services;

/// <summary>
/// Patreon OAuth認証サービス実装
/// 中継サーバー経由でPatreon APIと通信し、ライセンス状態を同期する
/// </summary>
public sealed class PatreonOAuthService : IPatreonOAuthService, IDisposable
{
    private readonly ILogger<PatreonOAuthService> _logger;
    private readonly PatreonSettings _settings;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _credentialsFilePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    /// <summary>
    /// HttpClient名（IHttpClientFactory用）
    /// </summary>
    public const string HttpClientName = "PatreonOAuth";

    private PatreonLocalCredentials? _cachedCredentials;
    private PatreonSyncStatus _syncStatus = PatreonSyncStatus.NotConnected;
    private bool _disposed;

    /// <inheritdoc/>
    public bool IsAuthenticated => _cachedCredentials != null && !string.IsNullOrEmpty(_cachedCredentials.PatreonUserId);

    /// <inheritdoc/>
    public PatreonSyncStatus SyncStatus => _syncStatus;

    /// <inheritdoc/>
    public DateTime? LastSyncTime => _cachedCredentials?.LastSyncTime;

    /// <inheritdoc/>
    public string? PatreonUserId => _cachedCredentials?.PatreonUserId;

    /// <inheritdoc/>
    public event EventHandler<PatreonStatusChangedEventArgs>? StatusChanged;

    /// <summary>
    /// PatreonOAuthServiceを初期化
    /// </summary>
    public PatreonOAuthService(
        ILogger<PatreonOAuthService> logger,
        IOptions<PatreonSettings> settings,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));

        // 資格情報保存パス
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var credentialsDir = Path.Combine(userProfile, ".baketa", "license");
        Directory.CreateDirectory(credentialsDir);
        _credentialsFilePath = Path.Combine(credentialsDir, "patreon-credentials.json");

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            PropertyNameCaseInsensitive = true
        };

        // 起動時に保存された資格情報を読み込む
        _ = Task.Run(async () =>
        {
            await LoadCredentialsAsync().ConfigureAwait(false);
        });

        _logger.LogInformation(
            "🔗 PatreonOAuthService初期化完了 - RelayServer={RelayServer}",
            _settings.RelayServerUrl);
    }

    /// <inheritdoc/>
    public string GenerateAuthorizationUrl(string state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);

        // Patreon OAuth認証URL
        var baseUrl = "https://www.patreon.com/oauth2/authorize";
        var queryParams = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = _settings.ClientId,
            ["redirect_uri"] = _settings.RedirectUri,
            ["scope"] = "identity identity.memberships",
            ["state"] = state
        };

        var queryString = string.Join("&", queryParams.Select(kvp =>
            $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

        var authUrl = $"{baseUrl}?{queryString}";

        _logger.LogDebug("Patreon認証URLを生成: {Url}", authUrl);

        return authUrl;
    }

    /// <inheritdoc/>
    public async Task<PatreonAuthResult> HandleCallbackAsync(
        string authorizationCode,
        string state,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizationCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            _logger.LogInformation("Patreon認証コールバック処理開始");

            // 1. 中継サーバーでトークン交換
            var tokenResponse = await ExchangeCodeForTokenAsync(authorizationCode, cancellationToken)
                .ConfigureAwait(false);

            if (tokenResponse == null)
            {
                return PatreonAuthResult.CreateFailure("TOKEN_EXCHANGE_FAILED", "トークン交換に失敗しました");
            }

            // 2. Identity APIでユーザー情報とTier情報を取得
            var identityResponse = await GetPatreonIdentityAsync(tokenResponse.AccessToken, cancellationToken)
                .ConfigureAwait(false);

            if (identityResponse == null)
            {
                return PatreonAuthResult.CreateFailure("IDENTITY_FETCH_FAILED", "ユーザー情報の取得に失敗しました");
            }

            // 3. プランを判定
            var (plan, tierId, patronStatus, nextChargeDate) = DeterminePatreonPlan(identityResponse);

            // 4. 資格情報をローカルに保存
            var credentials = new PatreonLocalCredentials
            {
                PatreonUserId = identityResponse.Data.Id,
                Email = identityResponse.Data.Attributes?.Email,
                FullName = identityResponse.Data.Attributes?.FullName,
                EncryptedRefreshToken = EncryptToken(tokenResponse.RefreshToken),
                RefreshTokenObtainedAt = DateTime.UtcNow,
                LastKnownPlan = plan,
                LastKnownTierId = tierId,
                SubscriptionEndDate = nextChargeDate,
                LastSyncTime = DateTime.UtcNow,
                PatronStatus = patronStatus
            };

            await SaveCredentialsAsync(credentials, cancellationToken).ConfigureAwait(false);
            _cachedCredentials = credentials;

            // 5. ステータス更新
            UpdateSyncStatus(PatreonSyncStatus.Synced);

            _logger.LogInformation(
                "✅ Patreon認証成功: UserId={UserId}, Plan={Plan}, PatronStatus={PatronStatus}",
                MaskIdentifier(identityResponse.Data.Id),
                plan,
                patronStatus);

            return PatreonAuthResult.CreateSuccess(
                identityResponse.Data.Id,
                identityResponse.Data.Attributes?.FullName,
                identityResponse.Data.Attributes?.Email,
                plan);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Patreon認証中にネットワークエラー");
            return PatreonAuthResult.CreateFailure("NETWORK_ERROR", $"ネットワークエラー: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Patreon認証中に予期せぬエラー");
            return PatreonAuthResult.CreateFailure("UNKNOWN_ERROR", ex.Message);
        }
    }

    /// <inheritdoc/>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _syncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 資格情報ファイルを削除
            if (File.Exists(_credentialsFilePath))
            {
                File.Delete(_credentialsFilePath);
            }

            _cachedCredentials = null;
            UpdateSyncStatus(PatreonSyncStatus.NotConnected);

            _logger.LogInformation("🔓 Patreon連携を解除しました");
        }
        finally
        {
            _syncLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<PatreonLocalCredentials?> LoadCredentialsAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_cachedCredentials != null)
        {
            return _cachedCredentials;
        }

        await _syncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_credentialsFilePath))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(_credentialsFilePath, cancellationToken).ConfigureAwait(false);
            _cachedCredentials = JsonSerializer.Deserialize<PatreonLocalCredentials>(json, _jsonOptions);

            if (_cachedCredentials != null)
            {
                _logger.LogDebug(
                    "Patreon資格情報を読み込み: UserId={UserId}, Plan={Plan}",
                    MaskIdentifier(_cachedCredentials.PatreonUserId),
                    _cachedCredentials.LastKnownPlan);

                // ステータスを設定
                UpdateSyncStatus(
                    _cachedCredentials.LastSyncError != null
                        ? PatreonSyncStatus.Error
                        : PatreonSyncStatus.Offline);
            }

            return _cachedCredentials;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Patreon資格情報ファイルの解析に失敗");
            return null;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Patreon資格情報ファイルの読み込みに失敗");
            return null;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<PatreonSyncResult> SyncLicenseAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // 資格情報がない場合
        if (_cachedCredentials == null)
        {
            await LoadCredentialsAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_cachedCredentials == null || string.IsNullOrEmpty(_cachedCredentials.EncryptedRefreshToken))
        {
            return PatreonSyncResult.NotConnected;
        }

        await _syncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // キャッシュが有効な場合はキャッシュを使用
            if (!forceRefresh && IsCacheValid())
            {
                _logger.LogDebug("Patreonキャッシュを使用: LastSync={LastSync}", _cachedCredentials.LastSyncTime);
                return PatreonSyncResult.CreateSuccess(
                    _cachedCredentials.LastKnownPlan,
                    _cachedCredentials.SubscriptionEndDate,
                    fromCache: true);
            }

            // リフレッシュトークンを使って新しいアクセストークンを取得
            var refreshToken = DecryptToken(_cachedCredentials.EncryptedRefreshToken);
            if (string.IsNullOrEmpty(refreshToken))
            {
                UpdateSyncStatus(PatreonSyncStatus.TokenExpired);
                return PatreonSyncResult.CreateError(PatreonSyncStatus.TokenExpired, "トークンの復号化に失敗しました");
            }

            var tokenResponse = await RefreshAccessTokenAsync(refreshToken, cancellationToken)
                .ConfigureAwait(false);

            if (tokenResponse == null)
            {
                // オフラインまたはトークン期限切れ
                if (IsOfflineGracePeriodValid())
                {
                    _logger.LogWarning("Patreon同期失敗、グレースピリオド内のためキャッシュを使用");
                    UpdateSyncStatus(PatreonSyncStatus.Offline);
                    return PatreonSyncResult.CreateSuccess(
                        _cachedCredentials.LastKnownPlan,
                        _cachedCredentials.SubscriptionEndDate,
                        fromCache: true);
                }

                UpdateSyncStatus(PatreonSyncStatus.TokenExpired);
                return PatreonSyncResult.CreateError(PatreonSyncStatus.TokenExpired, "トークンの更新に失敗しました。再認証が必要です。");
            }

            // Identity APIでプラン情報を取得
            var identityResponse = await GetPatreonIdentityAsync(tokenResponse.AccessToken, cancellationToken)
                .ConfigureAwait(false);

            if (identityResponse == null)
            {
                if (IsOfflineGracePeriodValid())
                {
                    UpdateSyncStatus(PatreonSyncStatus.Offline);
                    return PatreonSyncResult.CreateSuccess(
                        _cachedCredentials.LastKnownPlan,
                        _cachedCredentials.SubscriptionEndDate,
                        fromCache: true);
                }

                UpdateSyncStatus(PatreonSyncStatus.Error);
                return PatreonSyncResult.CreateError(PatreonSyncStatus.Error, "ユーザー情報の取得に失敗しました");
            }

            // プランを判定
            var (plan, tierId, patronStatus, nextChargeDate) = DeterminePatreonPlan(identityResponse);

            // 資格情報を更新
            _cachedCredentials = _cachedCredentials with
            {
                EncryptedRefreshToken = EncryptToken(tokenResponse.RefreshToken),
                RefreshTokenObtainedAt = DateTime.UtcNow,
                LastKnownPlan = plan,
                LastKnownTierId = tierId,
                SubscriptionEndDate = nextChargeDate,
                LastSyncTime = DateTime.UtcNow,
                PatronStatus = patronStatus,
                LastSyncError = null
            };

            await SaveCredentialsAsync(_cachedCredentials, cancellationToken).ConfigureAwait(false);
            UpdateSyncStatus(PatreonSyncStatus.Synced);

            _logger.LogInformation(
                "✅ Patreon同期成功: Plan={Plan}, PatronStatus={PatronStatus}, NextCharge={NextCharge}",
                plan, patronStatus, nextChargeDate);

            return PatreonSyncResult.CreateSuccess(plan, nextChargeDate);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Patreon同期中にネットワークエラー");

            if (IsOfflineGracePeriodValid())
            {
                UpdateSyncStatus(PatreonSyncStatus.Offline);
                return PatreonSyncResult.CreateSuccess(
                    _cachedCredentials.LastKnownPlan,
                    _cachedCredentials.SubscriptionEndDate,
                    fromCache: true);
            }

            UpdateSyncStatus(PatreonSyncStatus.Error);
            return PatreonSyncResult.CreateError(PatreonSyncStatus.Error, $"ネットワークエラー: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Patreon同期中に予期せぬエラー");
            UpdateSyncStatus(PatreonSyncStatus.Error);
            return PatreonSyncResult.CreateError(PatreonSyncStatus.Error, ex.Message);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    /// <summary>
    /// 認証コードをトークンに交換（中継サーバー経由）
    /// </summary>
    private async Task<PatreonTokenResponse?> ExchangeCodeForTokenAsync(
        string code,
        CancellationToken cancellationToken)
    {
        using var httpClient = _httpClientFactory.CreateClient(HttpClientName);

        var requestBody = new
        {
            code,
            redirect_uri = _settings.RedirectUri
        };

        var response = await httpClient.PostAsJsonAsync(
            $"{_settings.RelayServerUrl}/api/patreon/token",
            requestBody,
            cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogError("トークン交換失敗: Status={Status}, Body={Body}", response.StatusCode, errorContent);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<PatreonTokenResponse>(_jsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// リフレッシュトークンでアクセストークンを更新（中継サーバー経由）
    /// </summary>
    private async Task<PatreonTokenResponse?> RefreshAccessTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        using var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        var requestBody = new { refresh_token = refreshToken };

        try
        {
            var response = await httpClient.PostAsJsonAsync(
                $"{_settings.RelayServerUrl}/api/patreon/refresh",
                requestBody,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("トークン更新失敗: Status={Status}, Body={Body}", response.StatusCode, errorContent);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<PatreonTokenResponse>(_jsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("トークン更新がタイムアウトしました");
            return null;
        }
    }

    /// <summary>
    /// Patreon Identity APIを呼び出し（中継サーバー経由でプロキシ）
    /// 401エラー時は自動的にトークンをリフレッシュしてリトライ
    /// </summary>
    private async Task<PatreonIdentityResponse?> GetPatreonIdentityAsync(
        string accessToken,
        CancellationToken cancellationToken,
        bool isRetry = false)
    {
        try
        {
            using var httpClient = _httpClientFactory.CreateClient(HttpClientName);

            // 中継サーバーを経由してIdentity APIを呼び出す
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_settings.RelayServerUrl}/api/patreon/identity");

            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            // 401 Unauthorized: アクセストークン期限切れ → リフレッシュしてリトライ
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && !isRetry)
            {
                _logger.LogInformation("アクセストークン期限切れを検出、リフレッシュを試行");

                if (_cachedCredentials != null && !string.IsNullOrEmpty(_cachedCredentials.EncryptedRefreshToken))
                {
                    var refreshToken = DecryptToken(_cachedCredentials.EncryptedRefreshToken);
                    if (!string.IsNullOrEmpty(refreshToken))
                    {
                        var tokenResponse = await RefreshAccessTokenAsync(refreshToken, cancellationToken)
                            .ConfigureAwait(false);

                        if (tokenResponse != null)
                        {
                            _logger.LogInformation("トークンリフレッシュ成功、Identity APIをリトライ");
                            // リフレッシュトークンを更新
                            _cachedCredentials = _cachedCredentials with
                            {
                                EncryptedRefreshToken = EncryptToken(tokenResponse.RefreshToken),
                                RefreshTokenObtainedAt = DateTime.UtcNow
                            };
                            await SaveCredentialsAsync(_cachedCredentials, cancellationToken).ConfigureAwait(false);

                            // リトライ（再帰呼び出し、isRetry=trueで無限ループ防止）
                            return await GetPatreonIdentityAsync(tokenResponse.AccessToken, cancellationToken, isRetry: true)
                                .ConfigureAwait(false);
                        }
                    }
                }

                _logger.LogWarning("トークンリフレッシュに失敗、再認証が必要です");
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("Identity API失敗: Status={Status}, Body={Body}", response.StatusCode, errorContent);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<PatreonIdentityResponse>(_jsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Identity APIがタイムアウトしました");
            return null;
        }
    }

    /// <summary>
    /// Identity応答からプランを判定
    /// </summary>
    private (PlanType plan, string? tierId, string? patronStatus, DateTime? nextChargeDate) DeterminePatreonPlan(
        PatreonIdentityResponse identity)
    {
        // メンバーシップを検索
        var membership = identity.Included?
            .FirstOrDefault(i => i.Type == "member");

        if (membership?.Attributes == null)
        {
            return (PlanType.Free, null, null, null);
        }

        var patronStatus = membership.Attributes.PatronStatus;
        var nextChargeDate = membership.Attributes.NextChargeDate;

        // アクティブでなければFree
        if (patronStatus != "active_patron")
        {
            return (PlanType.Free, null, patronStatus, null);
        }

        // 有効なTierを取得
        var entitledTiers = membership.Relationships?.CurrentlyEntitledTiers?.Data;
        if (entitledTiers == null || entitledTiers.Count == 0)
        {
            return (PlanType.Free, null, patronStatus, nextChargeDate);
        }

        // Tier IDからプランを判定
        foreach (var tier in entitledTiers)
        {
            if (tier.Id == _settings.PremiaTierId)
            {
                return (PlanType.Premia, tier.Id, patronStatus, nextChargeDate);
            }
            if (tier.Id == _settings.ProTierId)
            {
                return (PlanType.Pro, tier.Id, patronStatus, nextChargeDate);
            }
            if (tier.Id == _settings.StandardTierId)
            {
                return (PlanType.Standard, tier.Id, patronStatus, nextChargeDate);
            }
        }

        // マッチするTierがない場合、支払額から推測
        var amountCents = membership.Attributes.CurrentlyEntitledAmountCents ?? 0;
        var plan = amountCents switch
        {
            >= 500 => PlanType.Premia,  // $5+
            >= 300 => PlanType.Pro,     // $3+
            >= 100 => PlanType.Standard, // $1+
            _ => PlanType.Free
        };

        return (plan, entitledTiers.FirstOrDefault()?.Id, patronStatus, nextChargeDate);
    }

    /// <summary>
    /// キャッシュが有効かどうか
    /// </summary>
    private bool IsCacheValid()
    {
        if (_cachedCredentials?.LastSyncTime == null)
        {
            return false;
        }

        var elapsed = DateTime.UtcNow - _cachedCredentials.LastSyncTime.Value;
        return elapsed.TotalMinutes < _settings.CacheDurationMinutes;
    }

    /// <summary>
    /// オフライングレースピリオド内かどうか
    /// </summary>
    private bool IsOfflineGracePeriodValid()
    {
        if (_cachedCredentials?.LastSyncTime == null || _cachedCredentials?.SubscriptionEndDate == null)
        {
            return false;
        }

        // サブスクリプション有効期限内かつグレースピリオド内
        var now = DateTime.UtcNow;
        var subscriptionValid = _cachedCredentials.SubscriptionEndDate > now;
        var elapsed = now - _cachedCredentials.LastSyncTime.Value;
        var withinGracePeriod = elapsed.TotalDays < _settings.OfflineGracePeriodDays;

        return subscriptionValid && withinGracePeriod;
    }

    /// <summary>
    /// 資格情報をファイルに保存
    /// </summary>
    private async Task SaveCredentialsAsync(PatreonLocalCredentials credentials, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(credentials, _jsonOptions);
        await File.WriteAllTextAsync(_credentialsFilePath, json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// DPAPI暗号化用エントロピー（追加保護層）
    /// マシン名とアプリ名を組み合わせてハッシュ化
    /// </summary>
    private static readonly byte[] DpapiEntropy = GenerateDpapiEntropy();

    /// <summary>
    /// DPAPIエントロピーを生成
    /// </summary>
    private static byte[] GenerateDpapiEntropy()
    {
        var machineName = Environment.MachineName;
        var userName = Environment.UserName;
        var appIdentifier = "Baketa.PatreonLicense.v1";
        var entropySource = $"{machineName}:{userName}:{appIdentifier}";
        return SHA256.HashData(Encoding.UTF8.GetBytes(entropySource));
    }

    /// <summary>
    /// トークンを暗号化（DPAPI + エントロピー）
    /// </summary>
    private static string? EncryptToken(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        try
        {
            var tokenBytes = Encoding.UTF8.GetBytes(token);
            var encryptedBytes = ProtectedData.Protect(tokenBytes, DpapiEntropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encryptedBytes);
        }
        catch
        {
            // DPAPIが使えない環境ではBase64のみ（セキュリティ低下）
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(token));
        }
    }

    /// <summary>
    /// トークンを復号化（DPAPI + エントロピー）
    /// </summary>
    private static string? DecryptToken(string? encryptedToken)
    {
        if (string.IsNullOrEmpty(encryptedToken))
        {
            return null;
        }

        try
        {
            var encryptedBytes = Convert.FromBase64String(encryptedToken);
            var decryptedBytes = ProtectedData.Unprotect(encryptedBytes, DpapiEntropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decryptedBytes);
        }
        catch
        {
            // DPAPIで暗号化されていない場合はBase64デコードを試行
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(encryptedToken));
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// 識別子をマスク（ログ出力用）
    /// 先頭4文字と末尾2文字を表示、中間を***でマスク
    /// </summary>
    private static string MaskIdentifier(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length <= 6)
        {
            return "***";
        }
        return $"{id[..4]}***{id[^2..]}";
    }

    /// <summary>
    /// 同期ステータスを更新
    /// </summary>
    private void UpdateSyncStatus(PatreonSyncStatus newStatus)
    {
        var previousStatus = _syncStatus;
        _syncStatus = newStatus;

        if (previousStatus != newStatus)
        {
            StatusChanged?.Invoke(this, new PatreonStatusChangedEventArgs
            {
                NewStatus = newStatus,
                PreviousStatus = previousStatus,
                Plan = _cachedCredentials?.LastKnownPlan ?? PlanType.Free,
                LastSyncTime = _cachedCredentials?.LastSyncTime,
                ErrorMessage = _cachedCredentials?.LastSyncError
            });
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _syncLock.Dispose();
        _disposed = true;
    }
}
