using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Baketa.Core.Abstractions.Services;
using Baketa.Infrastructure.Services.Setup;
using Baketa.Infrastructure.Services.Setup.Models;
using Baketa.UI.ViewModels;
using Baketa.UI.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Services;

/// <summary>
/// [Issue #256] コンポーネント更新通知サービス実装
/// 更新チェック・ダイアログ表示・更新実行のオーケストレーション
/// </summary>
public sealed class ComponentUpdateNotificationService : IComponentUpdateNotificationService
{
    private readonly ILogger<ComponentUpdateNotificationService> _logger;
    private readonly IManifestFetcher _manifestFetcher;
    private readonly IComponentVersionService _versionService;
    private readonly IGpuEnvironmentService _gpuEnvironmentService;
    private readonly TimeSpan _remindLaterInterval;

    /// <summary>
    /// 最後にダイアログを表示した時刻（後で通知用）
    /// </summary>
    private DateTimeOffset _lastDialogShownTime = DateTimeOffset.MinValue;

    /// <summary>
    /// キャッシュされたアーキテクチャ（起動時に1回判定）
    /// </summary>
    private string? _cachedArchitecture;

    public ComponentUpdateNotificationService(
        ILogger<ComponentUpdateNotificationService> logger,
        IManifestFetcher manifestFetcher,
        IComponentVersionService versionService,
        IGpuEnvironmentService gpuEnvironmentService,
        IConfiguration configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _manifestFetcher = manifestFetcher ?? throw new ArgumentNullException(nameof(manifestFetcher));
        _versionService = versionService ?? throw new ArgumentNullException(nameof(versionService));
        _gpuEnvironmentService = gpuEnvironmentService ?? throw new ArgumentNullException(nameof(gpuEnvironmentService));

        // 設定から「後で通知」間隔を取得（デフォルト: 24時間）
        var remindLaterHours = configuration.GetValue("ComponentUpdate:RemindLaterIntervalHours", 24);
        _remindLaterInterval = TimeSpan.FromHours(remindLaterHours);
        _logger.LogDebug("[Issue #256] RemindLaterInterval configured to {Hours} hours", remindLaterHours);
    }

    /// <inheritdoc/>
    public async Task<bool> CheckForUpdatesOnStartupAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("[Issue #256] Checking for component updates on startup...");

            // 「後で通知」の間隔チェック
            if (DateTimeOffset.UtcNow - _lastDialogShownTime < _remindLaterInterval)
            {
                _logger.LogDebug("[Issue #256] Skipping update check: remind later interval not elapsed");
                return false;
            }

            var appVersion = GetAppVersion();
            var architecture = await GetArchitectureAsync().ConfigureAwait(false);
            var updates = await _manifestFetcher
                .CheckForUpdatesAsync(_versionService, appVersion, architecture, cancellationToken)
                .ConfigureAwait(false);

            var availableUpdates = updates
                .Where(u => u.UpdateAvailable && u.MeetsAppVersionRequirement)
                .ToList();

            if (availableUpdates.Count == 0)
            {
                _logger.LogDebug("[Issue #256] No updates available");
                return false;
            }

            _logger.LogInformation(
                "[Issue #256] {Count} component updates available",
                availableUpdates.Count);

            // UIスレッドでダイアログを表示
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var result = await ShowUpdateDialogAsync(updates, cancellationToken)
                    .ConfigureAwait(false);

                if (result == ComponentUpdateDialogResult.RemindLater)
                {
                    _lastDialogShownTime = DateTimeOffset.UtcNow;
                }
            }).ConfigureAwait(false);

            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("[Issue #256] Update check cancelled");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Issue #256] Failed to check for updates on startup");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<IComponentUpdateCheckResult>> CheckForUpdatesManuallyAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("[Issue #256] Manual update check requested");

            var appVersion = GetAppVersion();
            var architecture = await GetArchitectureAsync().ConfigureAwait(false);
            var updates = await _manifestFetcher
                .CheckForUpdatesAsync(_versionService, appVersion, architecture, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "[Issue #256] Manual check complete. {UpdateCount} updates available",
                updates.Count(u => u.UpdateAvailable && u.MeetsAppVersionRequirement));

            // Infrastructure層の具象型をCore層のインターフェースにキャスト
            return updates.Cast<IComponentUpdateCheckResult>().ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Issue #256] Manual update check failed");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<ComponentUpdateDialogResult> ShowUpdateDialogAsync(
        IReadOnlyList<IComponentUpdateCheckResult> updates,
        CancellationToken cancellationToken = default)
    {
        var availableUpdates = updates
            .Where(u => u.UpdateAvailable && u.MeetsAppVersionRequirement)
            .ToList();

        if (availableUpdates.Count == 0)
        {
            _logger.LogDebug("[Issue #256] No updates to show in dialog");
            return ComponentUpdateDialogResult.None;
        }

        // UIスレッドで実行を保証
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var viewModel = new ComponentUpdateDialogViewModel(availableUpdates);
            var dialog = new ComponentUpdateDialogWindow(viewModel);

            _logger.LogDebug("[Issue #256] Showing update dialog with {Count} updates", availableUpdates.Count);

            // ShowDialogAsyncはnullable結果を返すが、ウィンドウはComponentUpdateDialogResultで閉じる
            var result = await dialog.ShowDialog<ComponentUpdateDialogResult?>(GetMainWindow());

            _logger.LogInformation("[Issue #256] Update dialog result: {Result}", result ?? ComponentUpdateDialogResult.Closed);

            return result ?? ComponentUpdateDialogResult.Closed;
        }).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<bool> UpdateSelectedComponentsAsync(
        IEnumerable<ComponentUpdateItem> selectedItems,
        CancellationToken cancellationToken = default)
    {
        // TODO: Phase 5で実装
        // ComponentDownloadServiceとComponentInstallerServiceを使用して
        // 選択されたコンポーネントをダウンロード・インストール
        _logger.LogWarning("[Issue #256] UpdateSelectedComponentsAsync not yet implemented");
        return Task.FromResult(false);
    }

    /// <summary>
    /// アーキテクチャを動的に判定（GPU/CPU）
    /// </summary>
    /// <returns>"cuda" または "cpu"</returns>
    private async Task<string> GetArchitectureAsync()
    {
        // キャッシュがあればそれを使用
        if (_cachedArchitecture != null)
        {
            return _cachedArchitecture;
        }

        try
        {
            var hasNvidiaGpu = await _gpuEnvironmentService.IsNvidiaGpuAvailableAsync().ConfigureAwait(false);
            _cachedArchitecture = hasNvidiaGpu ? "cuda" : "cpu";
            _logger.LogInformation(
                "[Issue #256] Architecture detected: {Architecture} (NVIDIA GPU: {HasGpu})",
                _cachedArchitecture,
                hasNvidiaGpu);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Issue #256] GPU detection failed, defaulting to CPU");
            _cachedArchitecture = "cpu";
        }

        return _cachedArchitecture;
    }

    /// <summary>
    /// アプリケーションバージョンを取得
    /// </summary>
    private static string GetAppVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        return version?.ToString(3) ?? "0.0.0";
    }

    /// <summary>
    /// メインウィンドウを取得
    /// </summary>
    private static Avalonia.Controls.Window? GetMainWindow()
    {
        return Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
    }
}
