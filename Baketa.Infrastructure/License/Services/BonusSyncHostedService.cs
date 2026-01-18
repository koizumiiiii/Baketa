using Baketa.Core.Abstractions.Auth;
using Baketa.Core.Abstractions.License;
using Baketa.Core.Translation.Abstractions;
using Baketa.Infrastructure.Translation.Cloud;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.License.Services;

/// <summary>
/// ボーナストークンをサーバーと同期するバックグラウンドサービス
/// ログイン時にサーバーから取得し、定期的にローカル消費量をサーバーへ同期
/// [Issue #296] 起動時にクォータ状態もサーバーと同期
/// </summary>
public sealed class BonusSyncHostedService : BackgroundService, IDisposable
{
    private readonly IAuthService _authService;
    private readonly IPatreonOAuthService? _patreonOAuthService;
    private readonly IBonusTokenService? _bonusTokenService;
    private readonly ILicenseManager _licenseManager;
    private readonly RelayServerClient? _relayServerClient;
    private readonly IJwtTokenService? _jwtTokenService;
    private readonly ILogger<BonusSyncHostedService> _logger;

    /// <summary>
    /// [Issue #305] 同期間隔（1時間）- KV消費削減のため30分から延長
    /// </summary>
    private static readonly TimeSpan SyncInterval = TimeSpan.FromHours(1);

    /// <summary>
    /// 起動時の初期遅延（DI完了待ち）
    /// </summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(3);

    /// <summary>
    /// ログイン後のフェッチ遅延（セッション安定化待ち）
    /// </summary>
    private static readonly TimeSpan LoginFetchDelay = TimeSpan.FromSeconds(2);

    private bool _disposed;

    public BonusSyncHostedService(
        IAuthService authService,
        IPatreonOAuthService? patreonOAuthService,
        IBonusTokenService? bonusTokenService,
        ILicenseManager licenseManager,
        RelayServerClient? relayServerClient,
        IJwtTokenService? jwtTokenService,
        ILogger<BonusSyncHostedService> logger)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _patreonOAuthService = patreonOAuthService;
        _bonusTokenService = bonusTokenService;
        _licenseManager = licenseManager ?? throw new ArgumentNullException(nameof(licenseManager));
        _relayServerClient = relayServerClient;
        _jwtTokenService = jwtTokenService;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // ログイン/ログアウトイベントを購読
        _authService.AuthStatusChanged += OnAuthStatusChanged;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_bonusTokenService == null)
        {
            _logger.LogWarning("[Issue #280] IBonusTokenServiceが登録されていません。ボーナス同期は無効です");
            return;
        }

        _logger.LogInformation("[Issue #280] BonusSyncHostedService開始");

        // 起動直後は少し待機（他のサービスの初期化完了を待つ）
        await Task.Delay(StartupDelay, stoppingToken).ConfigureAwait(false);

        // [Issue #299] 起動時に統合エンドポイントで一括取得（4回→1回に削減）
        await TrySyncInitAsync(stoppingToken).ConfigureAwait(false);

        // 定期同期ループ
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncBonusConsumptionAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Issue #280] ボーナス同期中にエラーが発生しました");
            }

            // 次の同期まで待機
            try
            {
                await Task.Delay(SyncInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        // シャットダウン前に未同期の消費量を同期
        await FinalSyncAsync().ConfigureAwait(false);

        _logger.LogInformation("[Issue #280] BonusSyncHostedService停止");
    }

    /// <summary>
    /// 認証状態変更時のハンドラ
    /// </summary>
    private async void OnAuthStatusChanged(object? sender, AuthStatusChangedEventArgs e)
    {
        if (_bonusTokenService == null) return;

        if (e.IsLoggedIn && e.User != null)
        {
            _logger.LogInformation("[Issue #280] ログイン検知 - ボーナストークン取得開始");

            // 少し待機してセッション安定化を待つ
            await Task.Delay(LoginFetchDelay).ConfigureAwait(false);

            // [Issue #280+#281] ログイン時にセッショントークンを設定
            await UpdateSessionTokenAsync().ConfigureAwait(false);

            // [Issue #299] ログイン時に統合エンドポイントで一括取得
            await TrySyncInitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        else if (!e.IsLoggedIn && e.User == null)
        {
            _logger.LogInformation("[Issue #280] ログアウト検知 - 同期して終了");

            // [Issue #280+#281] ログアウト時にセッショントークンをクリア
            _licenseManager.SetSessionToken(null);

            // ログアウト前に未同期の消費量を同期
            await FinalSyncAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// セッショントークンをLicenseManagerに設定（共通化: Issue #280+#281）
    /// [Issue #299] JWTを最優先で使用（Patreon再認証後の翻訳失敗を修正）
    /// </summary>
    private async Task UpdateSessionTokenAsync()
    {
        try
        {
            // [Issue #299] JWTが有効な場合は最優先で使用
            // Patreon再認証後、/api/auth/tokenで取得したJWTはJwtTokenServiceに格納される
            // このJWTを使用しないと、古いPatreonセッショントークンが使われて翻訳が失敗する
            if (_jwtTokenService?.HasValidToken == true)
            {
                try
                {
                    var jwt = await _jwtTokenService.GetAccessTokenAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(jwt))
                    {
                        _licenseManager.SetSessionToken(jwt);
                        _logger.LogInformation("[Issue #299] JWTをLicenseManagerに設定しました");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "[Issue #299] JWT取得失敗（フォールバックへ）");
                }
            }

            // [Issue #296] JWTがない場合はPatreonセッショントークンを使用
            // これにより、Relay ServerのPatreonセッション認証パスを通り、
            // MEMBERSHIPS KVのキャッシュが正しく参照される
            if (_patreonOAuthService != null)
            {
                try
                {
                    var patreonToken = await _patreonOAuthService.GetSessionTokenAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(patreonToken))
                    {
                        _licenseManager.SetSessionToken(patreonToken);
                        _logger.LogInformation("[Issue #296] PatreonセッショントークンをLicenseManagerに設定しました");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "[Issue #296] Patreonセッショントークン取得失敗（Supabase JWTにフォールバック）");
                }
            }

            // Patreonトークンもない場合はSupabase JWTを使用
            var session = await _authService.GetCurrentSessionAsync(CancellationToken.None).ConfigureAwait(false);
            if (session?.IsValid == true)
            {
                _licenseManager.SetSessionToken(session.AccessToken);
                _logger.LogInformation("[Issue #280+#281] Supabase JWTをLicenseManagerに設定しました");
            }
            else
            {
                // ログインしているはずがセッション無効の場合はクリア
                _licenseManager.SetSessionToken(null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Issue #280+#281] セッショントークン設定中にエラー");
        }
    }

    /// <summary>
    /// [Issue #299] 統合エンドポイントで全ステータスを一括取得
    /// 起動時・ログイン時に4回のAPI呼び出しを1回に削減
    /// </summary>
    private async Task TrySyncInitAsync(CancellationToken cancellationToken)
    {
        if (_relayServerClient == null)
        {
            _logger.LogDebug("[Issue #299] RelayServerClientが未登録のため統合同期スキップ - フォールバック実行");
            // フォールバック: 従来の個別取得
            await TryFetchBonusTokensAsync(cancellationToken).ConfigureAwait(false);
            await SyncQuotaStatusAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            // [Issue #280+#281] 起動時にも（既にログイン済みなら）セッショントークンを設定
            await UpdateSessionTokenAsync().ConfigureAwait(false);

            var session = await _authService.GetCurrentSessionAsync(cancellationToken).ConfigureAwait(false);
            if (session == null || !session.IsValid)
            {
                _logger.LogDebug("[Issue #299] 未認証のため統合同期スキップ");
                return;
            }

            _logger.LogInformation("[Issue #299] 統合エンドポイントで全ステータス一括取得中...");

            var syncResult = await _relayServerClient.SyncInitAsync(session.AccessToken, cancellationToken).ConfigureAwait(false);

            if (syncResult == null)
            {
                _logger.LogWarning("[Issue #299] 統合同期失敗 - フォールバック実行");
                // フォールバック: 従来の個別取得
                await TryFetchBonusTokensAsync(cancellationToken).ConfigureAwait(false);
                await SyncQuotaStatusAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            // 部分的失敗時のログと個別フォールバック
            if (syncResult.PartialFailure)
            {
                var failedComponents = syncResult.FailedComponents ?? [];
                _logger.LogWarning(
                    "[Issue #299] 統合同期で部分的失敗: FailedComponents=[{Components}]",
                    string.Join(", ", failedComponents));

                // 失敗したコンポーネントは個別フォールバック
                if (failedComponents.Contains("bonus_tokens"))
                {
                    _logger.LogInformation("[Issue #299] ボーナストークン取得をフォールバック");
                    await TryFetchBonusTokensAsync(cancellationToken).ConfigureAwait(false);
                }

                if (failedComponents.Contains("quota"))
                {
                    _logger.LogInformation("[Issue #299] クォータ状態取得をフォールバック");
                    await SyncQuotaStatusAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            // ボーナストークン状態を適用（成功した場合、または部分的失敗でもbonus_tokensが成功した場合）
            var failedList = syncResult.FailedComponents ?? [];
            if (syncResult.BonusTokens != null && _bonusTokenService != null && !failedList.Contains("bonus_tokens"))
            {
                _bonusTokenService.ApplySyncedData(
                    syncResult.BonusTokens.Bonuses.Select(b => new Core.License.Models.BonusTokenInfo
                    {
                        BonusId = b.BonusId,
                        RemainingTokens = b.RemainingTokens,
                        IsExpired = b.IsExpired,
                        ExpiresAt = b.ExpiresAt
                    }).ToList(),
                    syncResult.BonusTokens.TotalRemaining);

                _logger.LogInformation(
                    "[Issue #299] ボーナストークン同期成功: 合計={TotalRemaining}, アイテム数={ItemCount}",
                    syncResult.BonusTokens.TotalRemaining,
                    syncResult.BonusTokens.ActiveCount);

                // CloudTranslationAvailabilityServiceがIsEntitledを再評価できるようにする
                _licenseManager.NotifyBonusTokensLoaded();
            }

            // クォータ状態を適用（成功した場合、または部分的失敗でもquotaが成功した場合）
            if (syncResult.Quota != null && !failedList.Contains("quota"))
            {
                var monthlyUsage = new ServerMonthlyUsage
                {
                    YearMonth = syncResult.Quota.YearMonth,
                    TokensUsed = syncResult.Quota.TokensUsed,
                    TokensLimit = syncResult.Quota.TokensLimit
                };

                _licenseManager.SyncMonthlyUsageFromServer(monthlyUsage);

                _logger.LogInformation(
                    "[Issue #299] クォータ状態同期成功: YearMonth={YearMonth}, Used={Used}",
                    syncResult.Quota.YearMonth,
                    syncResult.Quota.TokensUsed);
            }

            _logger.LogInformation(
                "[Issue #299] 統合同期完了{PartialNote}",
                syncResult.PartialFailure ? "（一部コンポーネントはフォールバック使用）" : string.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Issue #299] 統合同期中にエラー - フォールバック実行");
            // フォールバック: 従来の個別取得
            await TryFetchBonusTokensAsync(cancellationToken).ConfigureAwait(false);
            await SyncQuotaStatusAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// サーバーからボーナストークンを取得
    /// </summary>
    private async Task TryFetchBonusTokensAsync(CancellationToken cancellationToken)
    {
        if (_bonusTokenService == null) return;

        try
        {
            // [Issue #280+#281] 起動時にも（既にログイン済みなら）セッショントークンを設定
            // OnAuthStatusChangedは起動時には呼ばれないため、ここでも設定する
            await UpdateSessionTokenAsync().ConfigureAwait(false);

            var session = await _authService.GetCurrentSessionAsync(cancellationToken).ConfigureAwait(false);
            if (session == null || !session.IsValid)
            {
                _logger.LogDebug("[Issue #280] 未認証のためボーナス取得スキップ");
                return;
            }

            _logger.LogInformation("[Issue #280] サーバーからボーナストークン取得中...");

            var result = await _bonusTokenService.FetchFromServerAsync(session.AccessToken, cancellationToken).ConfigureAwait(false);

            if (result.Success)
            {
                _logger.LogInformation(
                    "[Issue #280] ボーナストークン取得成功: 合計={TotalRemaining}, アイテム数={ItemCount}",
                    result.TotalRemaining, result.Bonuses.Count);

                // [Issue #280+#281] ボーナストークン取得後にLicenseManagerへ通知
                // CloudTranslationAvailabilityServiceがIsEntitledを再評価できるようにする
                _licenseManager.NotifyBonusTokensLoaded();
            }
            else
            {
                _logger.LogWarning("[Issue #280] ボーナストークン取得失敗: {Error}", result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Issue #280] ボーナストークン取得中にエラー");
        }
    }

    /// <summary>
    /// [Issue #296] サーバーからクォータ状態を取得してLicenseManagerに同期
    /// </summary>
    private async Task SyncQuotaStatusAsync(CancellationToken cancellationToken)
    {
        if (_relayServerClient == null)
        {
            _logger.LogDebug("[Issue #296] RelayServerClientが未登録のためクォータ同期スキップ");
            return;
        }

        try
        {
            var session = await _authService.GetCurrentSessionAsync(cancellationToken).ConfigureAwait(false);
            if (session == null || !session.IsValid)
            {
                _logger.LogDebug("[Issue #296] 未認証のためクォータ同期スキップ");
                return;
            }

            _logger.LogInformation("[Issue #296] サーバーからクォータ状態取得中...");

            var quotaStatus = await _relayServerClient.GetQuotaStatusAsync(session.AccessToken, cancellationToken).ConfigureAwait(false);

            if (quotaStatus == null)
            {
                _logger.LogWarning("[Issue #296] クォータ状態取得失敗");
                return;
            }

            // ServerMonthlyUsageに変換してLicenseManagerに同期
            var monthlyUsage = new ServerMonthlyUsage
            {
                YearMonth = quotaStatus.YearMonth,
                TokensUsed = quotaStatus.TokensUsed,
                TokensLimit = quotaStatus.TokensLimit
            };

            _licenseManager.SyncMonthlyUsageFromServer(monthlyUsage);

            _logger.LogInformation(
                "[Issue #296] クォータ状態同期成功: YearMonth={YearMonth}, Used={Used}, Limit={Limit}, Exceeded={Exceeded}",
                quotaStatus.YearMonth,
                quotaStatus.TokensUsed,
                quotaStatus.TokensLimit,
                quotaStatus.IsExceeded);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Issue #296] クォータ状態同期中にエラー（翻訳機能への影響なし）");
        }
    }

    /// <summary>
    /// ローカル消費量をサーバーへ同期
    /// </summary>
    private async Task SyncBonusConsumptionAsync(CancellationToken cancellationToken)
    {
        if (_bonusTokenService == null) return;

        // 未同期の消費がなければスキップ
        if (!_bonusTokenService.HasPendingSync)
        {
            _logger.LogDebug("[Issue #280] 未同期の消費なし - スキップ");
            return;
        }

        try
        {
            var session = await _authService.GetCurrentSessionAsync(cancellationToken).ConfigureAwait(false);
            if (session == null || !session.IsValid)
            {
                _logger.LogDebug("[Issue #280] 未認証のため同期スキップ");
                return;
            }

            _logger.LogInformation("[Issue #280] ボーナス消費量をサーバーへ同期中...");

            var result = await _bonusTokenService.SyncToServerAsync(session.AccessToken, cancellationToken).ConfigureAwait(false);

            if (result.Success)
            {
                _logger.LogInformation(
                    "[Issue #280] ボーナス同期成功: 残り合計={TotalRemaining}",
                    result.TotalRemaining);
            }
            else
            {
                _logger.LogWarning("[Issue #280] ボーナス同期失敗: {Error}", result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Issue #280] ボーナス同期中にエラー");
        }
    }

    /// <summary>
    /// シャットダウン/ログアウト前の最終同期
    /// </summary>
    private async Task FinalSyncAsync()
    {
        if (_bonusTokenService == null || !_bonusTokenService.HasPendingSync)
        {
            return;
        }

        try
        {
            var session = await _authService.GetCurrentSessionAsync(CancellationToken.None).ConfigureAwait(false);
            if (session == null || !session.IsValid)
            {
                _logger.LogDebug("[Issue #280] 未認証のため最終同期スキップ");
                return;
            }

            _logger.LogInformation("[Issue #280] 最終同期実行中...");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var result = await _bonusTokenService.SyncToServerAsync(session.AccessToken, cts.Token).ConfigureAwait(false);

            if (result.Success)
            {
                _logger.LogInformation("[Issue #280] 最終同期成功");
            }
            else
            {
                _logger.LogWarning("[Issue #280] 最終同期失敗: {Error}", result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Issue #280] 最終同期中にエラー");
        }
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        if (_disposed) return;

        _authService.AuthStatusChanged -= OnAuthStatusChanged;
        _disposed = true;

        base.Dispose();
    }
}
