using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Baketa.Core.Abstractions.Auth;
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
    private readonly IJwtTokenService? _jwtTokenService;  // [Issue #287] JWT認証サービス（オプショナル）
    private readonly IAuthService? _authService;  // [Issue #295] Supabase認証サービス（アカウント紐づけ用）
    private readonly IPromotionSettingsPersistence? _promotionSettingsPersistence;  // [Issue #298] プロモーション設定永続化
    private readonly Lazy<IPromotionCodeService>? _lazyPromotionCodeService;  // [Issue #293] 循環依存回避のためLazy化
    private readonly string _credentialsFilePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly object _stateLock = new();

    /// <summary>
    /// HttpClient名（IHttpClientFactory用）
    /// </summary>
    public const string HttpClientName = "PatreonOAuth";

    /// <summary>
    /// State有効期限（10分）
    /// </summary>
    private static readonly TimeSpan StateExpiration = TimeSpan.FromMinutes(10);

    private PatreonLocalCredentials? _cachedCredentials;
    private PatreonSyncStatus _syncStatus = PatreonSyncStatus.NotConnected;
    private bool _disposed;

    /// <summary>
    /// 保留中のOAuth state（CSRF対策用）
    /// Key: state値、Value: 生成日時
    /// </summary>
    private readonly Dictionary<string, DateTime> _pendingStates = new();

    /// <inheritdoc/>
    public bool IsAuthenticated => _cachedCredentials != null &&
                                    !string.IsNullOrEmpty(_cachedCredentials.PatreonUserId) &&
                                    !string.IsNullOrEmpty(_cachedCredentials.EncryptedSessionToken);

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
        IHttpClientFactory httpClientFactory,
        IJwtTokenService? jwtTokenService = null,  // [Issue #287] オプショナル依存
        IAuthService? authService = null,  // [Issue #295] Supabase認証（アカウント紐づけ用、オプショナル）
        IPromotionSettingsPersistence? promotionSettingsPersistence = null,  // [Issue #298] プロモーション設定永続化
        Lazy<IPromotionCodeService>? lazyPromotionCodeService = null)  // [Issue #293] 循環依存回避のためLazy化
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _jwtTokenService = jwtTokenService;  // null可（JWT未設定時）
        _authService = authService;  // [Issue #295] null可（Supabase未ログイン時）
        _promotionSettingsPersistence = promotionSettingsPersistence;  // [Issue #298] null可
        _lazyPromotionCodeService = lazyPromotionCodeService;  // [Issue #293] 循環依存回避のためLazy化

        // [Issue #459] BaketaSettingsPaths経由に統一
        Directory.CreateDirectory(BaketaSettingsPaths.LicenseDirectory);
        _credentialsFilePath = BaketaSettingsPaths.PatreonCredentialsPath;

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            PropertyNameCaseInsensitive = true
        };

        // 起動時に保存された資格情報を読み込む（非同期、例外はログに記録）
        _ = Task.Run(async () =>
        {
            try
            {
                await LoadCredentialsAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "起動時のPatreon資格情報読み込みに失敗しました（後で再試行されます）");
            }
        });

        _logger.LogInformation(
            "🔗 PatreonOAuthService初期化完了 - RelayServer={RelayServer}",
            _settings.RelayServerUrl);
    }

    /// <inheritdoc/>
    public string GenerateSecureState()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // 暗号論的に安全なランダム値を生成（32バイト = 256ビット）
        var randomBytes = new byte[32];
        RandomNumberGenerator.Fill(randomBytes);
        var state = Convert.ToBase64String(randomBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('='); // URL-safe Base64

        lock (_stateLock)
        {
            // 期限切れのstateをクリーンアップ
            CleanupExpiredStates();

            // 新しいstateを保存
            _pendingStates[state] = DateTime.UtcNow;
        }

        _logger.LogDebug("CSRF state生成: {StatePrefix}... (有効期限: {Expiration}分)",
            state[..8], StateExpiration.TotalMinutes);

        return state;
    }

    /// <inheritdoc/>
    public bool ValidateState(string state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            _logger.LogWarning("CSRF state検証失敗: stateが空です");
            return false;
        }

        lock (_stateLock)
        {
            // 検証時にも期限切れstateをクリーンアップ
            CleanupExpiredStates();

            if (!_pendingStates.TryGetValue(state, out var createdAt))
            {
                _logger.LogWarning("CSRF state検証失敗: 未知のstate");
                return false;
            }

            // stateを削除（一度限りの使用）
            _pendingStates.Remove(state);

            // 有効期限チェック
            var elapsed = DateTime.UtcNow - createdAt;
            if (elapsed > StateExpiration)
            {
                _logger.LogWarning("CSRF state検証失敗: 期限切れ (経過時間: {Elapsed}分)",
                    elapsed.TotalMinutes);
                return false;
            }

            _logger.LogDebug("CSRF state検証成功");
            return true;
        }
    }

    /// <summary>
    /// 期限切れのstateをクリーンアップ
    /// </summary>
    private void CleanupExpiredStates()
    {
        var now = DateTime.UtcNow;
        var expiredStates = _pendingStates
            .Where(kvp => now - kvp.Value > StateExpiration)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var expiredState in expiredStates)
        {
            _pendingStates.Remove(expiredState);
        }

        if (expiredStates.Count > 0)
        {
            _logger.LogDebug("期限切れstate {Count}件を削除", expiredStates.Count);
        }
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

            // CSRF対策: stateを検証
            if (!ValidateState(state))
            {
                _logger.LogWarning("CSRF検証に失敗しました。不正なリクエストの可能性があります。");
                return PatreonAuthResult.CreateFailure("CSRF_VALIDATION_FAILED", "セキュリティ検証に失敗しました。もう一度お試しください。");
            }

            // 中継サーバーでトークン交換（セッショントークンを取得）
            // 中継サーバーはPatreonトークンを保持し、クライアントにはセッショントークンのみ返す
            var sessionResponse = await ExchangeCodeForSessionAsync(authorizationCode, state, cancellationToken)
                .ConfigureAwait(false);

            if (sessionResponse == null)
            {
                return PatreonAuthResult.CreateFailure("TOKEN_EXCHANGE_FAILED", "セッショントークンの取得に失敗しました");
            }

            // プラン文字列をPlanType enumに変換
            var plan = ParsePlanType(sessionResponse.Plan);

            // 資格情報をローカルに保存（セッショントークンのみ）
            var credentials = new PatreonLocalCredentials
            {
                PatreonUserId = sessionResponse.PatreonUserId,
                Email = sessionResponse.Email,
                FullName = sessionResponse.FullName,
                EncryptedSessionToken = EncryptToken(sessionResponse.SessionToken),
                SessionTokenObtainedAt = DateTime.UtcNow,
                SessionTokenExpiresIn = sessionResponse.ExpiresIn,
                LastKnownPlan = plan,
                LastKnownTierId = sessionResponse.TierId,
                SubscriptionEndDate = sessionResponse.NextChargeDate,
                LastSyncTime = DateTime.UtcNow,
                PatronStatus = sessionResponse.PatronStatus
            };

            await SaveCredentialsAsync(credentials, cancellationToken).ConfigureAwait(false);
            _cachedCredentials = credentials;

            // [Issue #287] SessionTokenをJWTに交換（利用可能な場合）
            string? accessToken = null;
            if (_jwtTokenService != null)
            {
                try
                {
                    var jwtResult = await _jwtTokenService.ExchangeSessionTokenAsync(
                        sessionResponse.SessionToken, cancellationToken).ConfigureAwait(false);

                    if (jwtResult != null)
                    {
                        accessToken = jwtResult.AccessToken;
                        _logger.LogInformation(
                            "[Issue #287] JWT取得成功: ExpiresAt={ExpiresAt:u}",
                            jwtResult.ExpiresAt);
                    }
                    else
                    {
                        _logger.LogWarning("[Issue #287] JWT取得失敗（SessionToken認証にフォールバック）");
                    }
                }
                catch (Exception ex)
                {
                    // JWT取得失敗はエラーにしない（SessionToken認証は有効）
                    _logger.LogWarning(ex, "[Issue #287] JWT交換中にエラー（SessionToken認証にフォールバック）");
                }
            }

            // [Issue #298] ログイン成功時にプロモーション状態を同期（ユーザー間混在防止）
            // [レビュー対応] JWT未取得時はSupabaseセッションからアクセストークンを取得
            // [Issue #293] 循環依存回避のためLazy<T>経由でアクセス
            if (_lazyPromotionCodeService != null)
            {
                var tokenForSync = accessToken;

                // JWTが取得できなかった場合、Supabaseセッションからアクセストークンを取得
                if (string.IsNullOrEmpty(tokenForSync) && _authService != null)
                {
                    try
                    {
                        var session = await _authService.GetCurrentSessionAsync(cancellationToken).ConfigureAwait(false);
                        if (session?.IsValid == true)
                        {
                            tokenForSync = session.AccessToken;
                            _logger.LogDebug("[Issue #298] Supabaseセッションからアクセストークンを取得");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "[Issue #298] Supabaseセッション取得中にエラー（プロモーション同期をスキップ）");
                    }
                }

                if (!string.IsNullOrEmpty(tokenForSync))
                {
                    try
                    {
                        // [Issue #293] Lazy<T>.Valueで初めて解決（循環依存を回避）
                        var syncResult = await _lazyPromotionCodeService.Value.SyncFromServerAsync(tokenForSync, cancellationToken)
                            .ConfigureAwait(false);
                        _logger.LogInformation(
                            "[Issue #298] プロモーション状態を同期しました: Result={Result}",
                            syncResult);
                    }
                    catch (Exception ex)
                    {
                        // プロモーション同期失敗はエラーにしない（次回同期で再試行される）
                        _logger.LogWarning(ex, "[Issue #298] プロモーション状態の同期に失敗しました（後で再試行されます）");
                    }
                }
            }

            // ステータス更新
            UpdateSyncStatus(PatreonSyncStatus.Synced);

            _logger.LogInformation(
                "✅ Patreon認証成功: UserId={UserId}, Plan={Plan}, PatronStatus={PatronStatus}",
                MaskIdentifier(sessionResponse.PatreonUserId),
                plan,
                sessionResponse.PatronStatus);

            return PatreonAuthResult.CreateSuccess(
                sessionResponse.PatreonUserId,
                sessionResponse.FullName,
                sessionResponse.Email,
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
            // セッショントークンがある場合はサーバー側のセッションも無効化
            if (_cachedCredentials != null && !string.IsNullOrEmpty(_cachedCredentials.EncryptedSessionToken))
            {
                try
                {
                    var sessionToken = DecryptToken(_cachedCredentials.EncryptedSessionToken);
                    if (!string.IsNullOrEmpty(sessionToken))
                    {
                        await RevokeSessionAsync(sessionToken, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    // セッション無効化の失敗はログに記録するが、ローカル切断は続行
                    _logger.LogWarning(ex, "サーバー側セッション無効化に失敗しましたが、ローカル切断を続行します");
                }
            }

            // [Issue #298] プロモーション設定をクリア（ユーザー間混在防止）
            if (_promotionSettingsPersistence != null)
            {
                try
                {
                    await _promotionSettingsPersistence.ClearPromotionAsync(cancellationToken).ConfigureAwait(false);
                    _logger.LogInformation("[Issue #298] プロモーション設定をクリアしました");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Issue #298] プロモーション設定のクリアに失敗しましたが、切断処理を続行します");
                }
            }

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
    public async Task ClearLocalCredentialsAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _syncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // ローカルの認証情報ファイルのみ削除（サーバーセッションは維持）
            if (File.Exists(_credentialsFilePath))
            {
                File.Delete(_credentialsFilePath);
                _logger.LogInformation("ローカルPatreon認証情報をクリアしました（サーバーセッションは維持）");
            }

            // プロモーション設定もクリア（ユーザー間混在防止）
            if (_promotionSettingsPersistence != null)
            {
                try
                {
                    await _promotionSettingsPersistence.ClearPromotionAsync(cancellationToken).ConfigureAwait(false);
                    _logger.LogDebug("プロモーション設定をクリアしました");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "プロモーション設定のクリアに失敗しましたが、処理を続行します");
                }
            }

            _cachedCredentials = null;
            UpdateSyncStatus(PatreonSyncStatus.NotConnected);
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
                if (_cachedCredentials.LastSyncError != null)
                {
                    UpdateSyncStatus(PatreonSyncStatus.Error);
                }
                else if (!_cachedCredentials.IsSessionTokenValid)
                {
                    // セッショントークン期限切れ
                    UpdateSyncStatus(PatreonSyncStatus.TokenExpired);
                }
                else
                {
                    UpdateSyncStatus(PatreonSyncStatus.Offline);
                }
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
    /// <remarks>
    /// [Issue #296] Cloud AI翻訳時にPatreonセッショントークンを優先的に使用するために追加。
    /// Supabase JWTよりPatreonセッショントークンを優先することで、
    /// Relay ServerのPatreonセッション認証パスを通り、MEMBERSHIPS KVが正しく参照される。
    /// </remarks>
    public async Task<string?> GetSessionTokenAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // 資格情報がない場合は読み込み
        if (_cachedCredentials == null)
        {
            await LoadCredentialsAsync(cancellationToken).ConfigureAwait(false);
        }

        // セッショントークンがない場合はnull
        if (_cachedCredentials == null || string.IsNullOrEmpty(_cachedCredentials.EncryptedSessionToken))
        {
            return null;
        }

        // セッショントークンの有効期限チェック
        if (!_cachedCredentials.IsSessionTokenValid)
        {
            _logger.LogDebug("[Issue #296] Patreonセッショントークン期限切れ");
            return null;
        }

        // 復号化して返す
        var sessionToken = DecryptToken(_cachedCredentials.EncryptedSessionToken);
        if (string.IsNullOrEmpty(sessionToken))
        {
            _logger.LogWarning("[Issue #296] Patreonセッショントークンの復号化に失敗");
            return null;
        }

        return sessionToken;
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

        // セッショントークンがない場合は未接続
        if (_cachedCredentials == null || string.IsNullOrEmpty(_cachedCredentials.EncryptedSessionToken))
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

            // セッショントークンを取得
            var sessionToken = DecryptToken(_cachedCredentials.EncryptedSessionToken);
            if (string.IsNullOrEmpty(sessionToken))
            {
                UpdateSyncStatus(PatreonSyncStatus.TokenExpired);
                return PatreonSyncResult.CreateError(PatreonSyncStatus.TokenExpired, "セッショントークンの復号化に失敗しました");
            }

            // 中継サーバーからライセンス状態を取得
            var licenseStatus = await GetLicenseStatusAsync(sessionToken, cancellationToken)
                .ConfigureAwait(false);

            if (licenseStatus == null)
            {
                // オフラインの場合はキャッシュを使用
                if (IsOfflineGracePeriodValid())
                {
                    _logger.LogWarning("Patreon同期失敗、グレースピリオド内のためキャッシュを使用");
                    UpdateSyncStatus(PatreonSyncStatus.Offline);
                    return PatreonSyncResult.CreateSuccess(
                        _cachedCredentials.LastKnownPlan,
                        _cachedCredentials.SubscriptionEndDate,
                        fromCache: true);
                }

                UpdateSyncStatus(PatreonSyncStatus.Error);
                return PatreonSyncResult.CreateError(PatreonSyncStatus.Error, "ライセンス状態の取得に失敗しました");
            }

            // セッション期限切れの場合
            if (licenseStatus.SessionExpired || !licenseStatus.SessionValid)
            {
                // グレースピリオド内なら猶予
                if (IsOfflineGracePeriodValid())
                {
                    UpdateSyncStatus(PatreonSyncStatus.Offline);
                    return PatreonSyncResult.CreateSuccess(
                        _cachedCredentials.LastKnownPlan,
                        _cachedCredentials.SubscriptionEndDate,
                        fromCache: true);
                }

                UpdateSyncStatus(PatreonSyncStatus.TokenExpired);
                return PatreonSyncResult.CreateError(
                    PatreonSyncStatus.TokenExpired,
                    licenseStatus.Error ?? "セッションが期限切れです。再認証が必要です。");
            }

            // プランを判定
            var plan = ParsePlanType(licenseStatus.Plan);

            // 資格情報を更新
            _cachedCredentials = _cachedCredentials with
            {
                LastKnownPlan = plan,
                LastKnownTierId = licenseStatus.TierId,
                SubscriptionEndDate = licenseStatus.NextChargeDate,
                LastSyncTime = DateTime.UtcNow,
                PatronStatus = licenseStatus.PatronStatus,
                LastSyncError = null
            };

            await SaveCredentialsAsync(_cachedCredentials, cancellationToken).ConfigureAwait(false);
            UpdateSyncStatus(PatreonSyncStatus.Synced);

            _logger.LogInformation(
                "✅ Patreon同期成功: Plan={Plan}, PatronStatus={PatronStatus}, NextCharge={NextCharge}",
                plan, licenseStatus.PatronStatus, licenseStatus.NextChargeDate);

            return PatreonSyncResult.CreateSuccess(plan, licenseStatus.NextChargeDate);
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
    /// 認証コードをセッショントークンに交換（中継サーバー経由）
    /// </summary>
    /// <remarks>
    /// 中継サーバーがPatreonトークンを保持し、クライアントにはセッショントークン（JWT）を返します。
    /// [Issue #295] Supabaseにログイン中の場合、JWTを送信してアカウント紐づけを行います。
    /// </remarks>
    private async Task<SessionTokenResponse?> ExchangeCodeForSessionAsync(
        string code,
        string state,
        CancellationToken cancellationToken)
    {
        using var httpClient = _httpClientFactory.CreateClient(HttpClientName);

        // [Issue #295] Supabaseにログイン中であればJWTを取得
        string? supabaseJwt = null;
        if (_authService != null)
        {
            try
            {
                var supabaseSession = await _authService.GetCurrentSessionAsync(cancellationToken).ConfigureAwait(false);
                if (supabaseSession?.IsValid == true)
                {
                    supabaseJwt = supabaseSession.AccessToken;
                    _logger.LogInformation(
                        "[Issue #295] Supabaseセッション検出、アカウント紐づけを試行: UserId={UserId}",
                        supabaseSession.User.Id[..8] + "...");
                }
            }
            catch (Exception ex)
            {
                // Supabase JWT取得失敗はエラーにしない（紐づけなしでPatreon認証は続行）
                _logger.LogWarning(ex, "[Issue #295] Supabaseセッション取得に失敗（アカウント紐づけをスキップ）");
            }
        }

        var requestBody = new
        {
            code,
            state,
            redirect_uri = _settings.RedirectUri,
            supabase_jwt = supabaseJwt  // [Issue #295] nullの場合はJSONから除外される
        };

        try
        {
            _logger.LogDebug(
                "Patreon exchange request: hasSupabaseJwt={HasSupabaseJwt}",
                supabaseJwt != null);

            var response = await httpClient.PostAsJsonAsync(
                $"{_settings.RelayServerUrl}/api/patreon/exchange",
                requestBody,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogError("セッショントークン取得失敗: Status={Status}, Body={Body}", response.StatusCode, errorContent);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<SessionTokenResponse>(_jsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("セッショントークン取得がタイムアウトしました");
            return null;
        }
    }

    /// <summary>
    /// 中継サーバーからライセンス状態を取得
    /// </summary>
    /// <remarks>
    /// セッショントークンを使って中継サーバーにライセンス状態を問い合わせます。
    /// サーバー側でPatreonトークンのリフレッシュが必要な場合は自動的に行われます。
    /// </remarks>
    private async Task<LicenseStatusResponse?> GetLicenseStatusAsync(
        string sessionToken,
        CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = _httpClientFactory.CreateClient(HttpClientName);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{_settings.RelayServerUrl}/api/patreon/license-status");

            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", sessionToken);

            var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("ライセンス状態取得失敗: Status={Status}, Body={Body}", response.StatusCode, errorContent);

                // 401 Unauthorized: セッション期限切れ
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    return new LicenseStatusResponse
                    {
                        Success = false,
                        SessionValid = false,
                        SessionExpired = true,
                        Error = "セッションが期限切れです",
                        ErrorCode = "SESSION_EXPIRED"
                    };
                }

                return null;
            }

            return await response.Content.ReadFromJsonAsync<LicenseStatusResponse>(_jsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("ライセンス状態取得がタイムアウトしました");
            return null;
        }
    }

    /// <summary>
    /// 中継サーバーでセッションを無効化
    /// </summary>
    private async Task RevokeSessionAsync(
        string sessionToken,
        CancellationToken cancellationToken)
    {
        using var httpClient = _httpClientFactory.CreateClient(HttpClientName);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_settings.RelayServerUrl}/api/patreon/revoke");

        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", sessionToken);

        try
        {
            var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("サーバー側セッションを無効化しました");
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("セッション無効化失敗: Status={Status}, Body={Body}", response.StatusCode, errorContent);
            }
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("セッション無効化がタイムアウトしました");
        }
    }

    /// <summary>
    /// プラン文字列をPlanType enumに変換
    /// </summary>
    private static PlanType ParsePlanType(string? planString)
    {
        // Issue #125: Standardプラン廃止
        // Issue #257: Pro/Premium/Ultimate 3段階構成に改定
        return planString?.ToLowerInvariant() switch
        {
            "ultimate" => PlanType.Ultimate,
            "premium" => PlanType.Premium,
            "premia" => PlanType.Premium, // 後方互換性
            "pro" => PlanType.Pro,
            "standard" => PlanType.Free, // Issue #125: Standardプラン廃止
            _ => PlanType.Free
        };
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
        // null チェックを明示的に分離して定数条件警告を回避
        if (_cachedCredentials is null)
        {
            return false;
        }

        if (!_cachedCredentials.LastSyncTime.HasValue || !_cachedCredentials.SubscriptionEndDate.HasValue)
        {
            return false;
        }

        // サブスクリプション有効期限内かつグレースピリオド内
        var now = DateTime.UtcNow;
        var subscriptionValid = _cachedCredentials.SubscriptionEndDate.Value > now;
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
    private string? EncryptToken(string token)
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
        catch (PlatformNotSupportedException ex)
        {
            // DPAPIが使えない環境ではBase64のみ（セキュリティ低下）
            _logger.LogWarning(ex, "DPAPIが利用できません。トークンはBase64エンコードのみで保存されます（セキュリティ低下）");
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(token));
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning(ex, "DPAPI暗号化に失敗しました。Base64エンコードにフォールバックします");
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(token));
        }
    }

    /// <summary>
    /// トークンを復号化（DPAPI + エントロピー）
    /// </summary>
    private string? DecryptToken(string? encryptedToken)
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
        catch (CryptographicException)
        {
            // DPAPIで暗号化されていない場合はBase64デコードを試行
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(encryptedToken));
            }
            catch (FormatException ex)
            {
                _logger.LogWarning(ex, "トークンの復号化に失敗しました。無効なフォーマットです");
                return null;
            }
        }
        catch (PlatformNotSupportedException)
        {
            // DPAPIが使えない環境ではBase64デコードを試行
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(encryptedToken));
            }
            catch (FormatException ex)
            {
                _logger.LogWarning(ex, "トークンの復号化に失敗しました。無効なフォーマットです");
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
