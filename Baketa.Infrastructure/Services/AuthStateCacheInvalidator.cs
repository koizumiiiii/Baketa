using Baketa.Core.Abstractions.Auth;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Services;

/// <summary>
/// [Issue #299] 認証状態変更時にAPIキャッシュを無効化するサービス
/// </summary>
/// <remarks>
/// <para>
/// ユーザーがログイン/ログアウトした際に、前のユーザーのキャッシュデータが
/// 新しいユーザーに表示されないようにするため、全キャッシュを無効化します。
/// </para>
/// <para>
/// IHostedServiceとして実装することで、DIコンテナのライフサイクルに従い
/// 適切なタイミングでイベント購読/解除を行います。
/// </para>
/// </remarks>
public sealed class AuthStateCacheInvalidator : IHostedService, IDisposable
{
    private readonly IAuthService _authService;
    private readonly IApiRequestDeduplicator _deduplicator;
    private readonly ILogger<AuthStateCacheInvalidator> _logger;

    /// <summary>
    /// [Issue #299] デバウンス用: 最後に無効化した時刻
    /// </summary>
    private DateTime _lastInvalidation = DateTime.MinValue;

    /// <summary>
    /// [Issue #299] デバウンス間隔（連続イベント発火対策）
    /// </summary>
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(500);

    private bool _disposed;

    public AuthStateCacheInvalidator(
        IAuthService authService,
        IApiRequestDeduplicator deduplicator,
        ILogger<AuthStateCacheInvalidator> logger)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _deduplicator = deduplicator ?? throw new ArgumentNullException(nameof(deduplicator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _authService.AuthStatusChanged += OnAuthStatusChanged;
        _logger.LogDebug("[Issue #299] AuthStateCacheInvalidator started - subscribed to auth events");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _authService.AuthStatusChanged -= OnAuthStatusChanged;
        _logger.LogDebug("[Issue #299] AuthStateCacheInvalidator stopped - unsubscribed from auth events");
        return Task.CompletedTask;
    }

    private void OnAuthStatusChanged(object? sender, AuthStatusChangedEventArgs e)
    {
        // [Issue #299] デバウンス: 連続イベント発火時は無視
        var now = DateTime.UtcNow;
        if (now - _lastInvalidation < DebounceInterval)
        {
            _logger.LogDebug(
                "[Issue #299] Auth state changed ({AuthState}) - skipped (debounce)",
                e.IsLoggedIn ? "SignedIn" : "SignedOut");
            return;
        }

        _lastInvalidation = now;

        // ログイン/ログアウト時に全キャッシュを無効化
        // 前のユーザーのデータが新しいユーザーに見えないようにする
        _deduplicator.InvalidateAll();

        _logger.LogInformation(
            "[Issue #299] Auth state changed ({AuthState}) - API cache invalidated",
            e.IsLoggedIn ? "SignedIn" : "SignedOut");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _authService.AuthStatusChanged -= OnAuthStatusChanged;
    }
}
