using Baketa.Core.Abstractions.License;
using Baketa.Core.License.Events;
using Baketa.Core.License.Models;
using Baketa.Infrastructure.License.Mapping;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.License.Services;

/// <summary>
/// Patreonライセンス状態を自動同期するバックグラウンドサービス
/// アプリ起動時と定期的に同期を実行
/// </summary>
public sealed class PatreonSyncHostedService : BackgroundService
{
    private readonly IPatreonOAuthService _patreonService;
    private readonly ILicenseManager _licenseManager;
    private readonly ILogger<PatreonSyncHostedService> _logger;

    /// <summary>
    /// [Issue #305] 同期間隔（1時間）- KV消費削減のため30分から延長
    /// </summary>
    private static readonly TimeSpan SyncInterval = TimeSpan.FromHours(1);

    /// <summary>
    /// 起動時の同期遅延（DI完了待ち）
    /// </summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);

    public PatreonSyncHostedService(
        IPatreonOAuthService patreonService,
        ILicenseManager licenseManager,
        ILogger<PatreonSyncHostedService> logger)
    {
        _patreonService = patreonService ?? throw new ArgumentNullException(nameof(patreonService));
        _licenseManager = licenseManager ?? throw new ArgumentNullException(nameof(licenseManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🔄 PatreonSyncHostedService開始");

        // 起動直後は少し待機（他のサービスの初期化完了を待つ）
        await Task.Delay(StartupDelay, stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncPatreonStatusAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // 正常なキャンセル
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Patreon自動同期中にエラーが発生しました");
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

        _logger.LogInformation("🔄 PatreonSyncHostedService停止");
    }

    /// <summary>
    /// Patreon状態を同期します
    /// </summary>
    private async Task SyncPatreonStatusAsync(CancellationToken cancellationToken)
    {
        // 資格情報を読み込んで連携済みか確認
        var credentials = await _patreonService.LoadCredentialsAsync(cancellationToken).ConfigureAwait(false);

        if (credentials == null || !_patreonService.IsAuthenticated)
        {
            _logger.LogDebug("Patreon未連携のため同期スキップ");
            return;
        }

        _logger.LogInformation("🔄 Patreon自動同期開始: UserId={UserId}", MaskId(credentials.PatreonUserId));

        var result = await _patreonService.SyncLicenseAsync(forceRefresh: false, cancellationToken).ConfigureAwait(false);

        if (result.Success)
        {
            _logger.LogInformation("✅ Patreon自動同期成功: Plan={Plan}, FromCache={FromCache}",
                result.Plan, result.FromCache);

            // LicenseStateを構築してLicenseManagerに伝播 (DRY: PatreonLicenseMapper使用)
            var state = PatreonLicenseMapper.ToLicenseState(result, credentials);

            _licenseManager.SetResolvedLicenseState(
                state,
                "PatreonSyncHostedService",
                LicenseChangeReason.ServerRefresh);
        }
        else
        {
            _logger.LogWarning("⚠️ Patreon自動同期失敗: Status={Status}, Error={Error}",
                result.Status, result.ErrorMessage);
        }
    }

    /// <summary>
    /// IDをマスク（ログ用）
    /// </summary>
    private static string MaskId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length <= 6)
        {
            return "***";
        }
        return $"{id[..4]}***";
    }
}
