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
    private readonly IBonusTokenService? _bonusTokenService;
    private readonly ILicenseManager _licenseManager;
    private readonly RelayServerClient? _relayServerClient;
    private readonly ILogger<BonusSyncHostedService> _logger;

    /// <summary>
    /// 同期間隔（デフォルト: 30分）
    /// </summary>
    private static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(30);

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
        IBonusTokenService? bonusTokenService,
        ILicenseManager licenseManager,
        RelayServerClient? relayServerClient,
        ILogger<BonusSyncHostedService> logger)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _bonusTokenService = bonusTokenService;
        _licenseManager = licenseManager ?? throw new ArgumentNullException(nameof(licenseManager));
        _relayServerClient = relayServerClient;
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

        // 起動時に既にログイン済みならフェッチ
        await TryFetchBonusTokensAsync(stoppingToken).ConfigureAwait(false);

        // [Issue #296] 起動時にクォータ状態をサーバーと同期
        await SyncQuotaStatusAsync(stoppingToken).ConfigureAwait(false);

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

            await TryFetchBonusTokensAsync(CancellationToken.None).ConfigureAwait(false);

            // [Issue #296] ログイン時にクォータ状態もサーバーと同期
            await SyncQuotaStatusAsync(CancellationToken.None).ConfigureAwait(false);
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
    /// </summary>
    private async Task UpdateSessionTokenAsync()
    {
        try
        {
            var session = await _authService.GetCurrentSessionAsync(CancellationToken.None).ConfigureAwait(false);
            if (session?.IsValid == true)
            {
                _licenseManager.SetSessionToken(session.AccessToken);
                _logger.LogInformation("[Issue #280+#281] セッショントークンをLicenseManagerに設定しました");
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
