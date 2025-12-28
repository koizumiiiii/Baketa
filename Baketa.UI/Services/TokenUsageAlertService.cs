using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.License;
using Baketa.Core.License.Events;
using Baketa.UI.Resources;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Services;

/// <summary>
/// [Issue #78 Phase 5] トークン使用量警告通知サービス
/// 80%/90%/100%到達時にトースト通知を表示
/// </summary>
public sealed class TokenUsageAlertService : IDisposable
{
    private readonly ILicenseManager _licenseManager;
    private readonly INotificationService _notificationService;
    private readonly ILogger<TokenUsageAlertService>? _logger;

    // 既に通知したアラートレベルを追跡（セッション内で重複通知を防止）
    private readonly HashSet<TokenWarningLevel> _notifiedLevels = [];
    private readonly object _lockObject = new();
    private bool _disposed;

    // 月次リセット用: 通知した月を記録
    private int _lastNotifiedMonth;

    /// <summary>
    /// TokenUsageAlertServiceを初期化します
    /// </summary>
    public TokenUsageAlertService(
        ILicenseManager licenseManager,
        INotificationService notificationService,
        ILogger<TokenUsageAlertService>? logger = null)
    {
        _licenseManager = licenseManager ?? throw new ArgumentNullException(nameof(licenseManager));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _logger = logger;

        // トークン使用量警告イベントを購読
        _licenseManager.TokenUsageWarning += OnTokenUsageWarning;

        // 現在の月を記録
        _lastNotifiedMonth = DateTime.UtcNow.Month;

        _logger?.LogDebug("[Phase 5] TokenUsageAlertService初期化完了");
    }

    /// <summary>
    /// トークン使用量警告イベントハンドラ
    /// </summary>
    private async void OnTokenUsageWarning(object? sender, TokenUsageWarningEventArgs e)
    {
        if (_disposed) return;

        lock (_lockObject)
        {
            // 月が変わった場合は通知済みレベルをリセット
            var currentMonth = DateTime.UtcNow.Month;
            if (currentMonth != _lastNotifiedMonth)
            {
                _notifiedLevels.Clear();
                _lastNotifiedMonth = currentMonth;
                _logger?.LogDebug("[Phase 5] 月次リセット実行: 通知済みレベルをクリア");
            }

            // 既に通知済みのレベルはスキップ
            if (_notifiedLevels.Contains(e.Level))
            {
                _logger?.LogDebug("[Phase 5] トークン警告スキップ（既に通知済み）: {Level}", e.Level);
                return;
            }

            _notifiedLevels.Add(e.Level);
        }

        _logger?.LogInformation("[Phase 5] トークン使用量警告を通知: {Level}, Usage={Usage:N0}/{Limit:N0}",
            e.Level, e.CurrentUsage, e.MonthlyLimit);

        try
        {
            await ShowAlertNotificationAsync(e).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[Phase 5] トークン警告通知の表示に失敗");
        }
    }

    /// <summary>
    /// 警告レベルに応じた通知を表示
    /// </summary>
    /// <remarks>
    /// CA1863抑制理由: この通知は閾値到達時（80%/90%/100%）にのみ発生し、
    /// セッション内で最大3回しか呼ばれないため、CompositeFormatキャッシュの
    /// パフォーマンス効果は無視できる。また、リソース文字列は言語設定で変わる可能性がある。
    /// </remarks>
#pragma warning disable CA1863 // CompositeFormat caching - infrequent notification, localized resource strings
    private async Task ShowAlertNotificationAsync(TokenUsageWarningEventArgs e)
    {
        var remaining = Math.Max(0, e.MonthlyLimit - e.CurrentUsage);
        var remainingFormatted = remaining.ToString("N0");

        switch (e.Level)
        {
            case TokenWarningLevel.Warning:
                // 80%到達警告
                await _notificationService.ShowWarningAsync(
                    Strings.TokenUsage_Warning_Title,
                    string.Format(Strings.TokenUsage_Warning80_Message, remainingFormatted),
                    duration: 6000).ConfigureAwait(false);
                break;

            case TokenWarningLevel.Critical:
                // 90%到達警告
                await _notificationService.ShowWarningAsync(
                    Strings.TokenUsage_Warning_Title,
                    string.Format(Strings.TokenUsage_Warning90_Message, remainingFormatted),
                    duration: 8000).ConfigureAwait(false);
                break;

            case TokenWarningLevel.Exceeded:
                // 100%到達（上限超過）
                await _notificationService.ShowErrorAsync(
                    Strings.TokenUsage_Exceeded_Title,
                    Strings.TokenUsage_Exceeded_Message,
                    duration: 10000).ConfigureAwait(false);
                break;

            case TokenWarningLevel.Notice:
                // 50%到達（通知のみ、オプショナル）
                _logger?.LogDebug("[Phase 5] 50%到達通知はスキップ（ユーザー体験向上のため）");
                break;
        }
    }
#pragma warning restore CA1863

    /// <summary>
    /// 通知済みレベルをリセット（テスト用または月次リセット時）
    /// </summary>
    public void ResetNotifiedLevels()
    {
        lock (_lockObject)
        {
            _notifiedLevels.Clear();
        }
        _logger?.LogDebug("[Phase 5] 通知済みレベルをリセット");
    }

    /// <summary>
    /// リソースを解放
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _licenseManager.TokenUsageWarning -= OnTokenUsageWarning;
        _logger?.LogDebug("[Phase 5] TokenUsageAlertService破棄完了");
    }
}
