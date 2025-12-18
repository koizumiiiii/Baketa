using System.Net.Http;
using System.Threading;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.License;
using Baketa.Core.License.Events;
using Baketa.Core.License.Extensions;
using Baketa.Core.License.Models;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.License.Services;

/// <summary>
/// ライセンス管理の中核実装
/// サブスクリプション状態管理、機能ゲート、トークン消費を統合的に処理
/// </summary>
public sealed class LicenseManager : ILicenseManager, IDisposable
{
    private readonly ILogger<LicenseManager> _logger;
    private readonly ILicenseApiClient _apiClient;
    private readonly ILicenseCacheService _cacheService;
    private readonly IEventAggregator _eventAggregator;
    private readonly LicenseSettings _settings;

    // 現在のライセンス状態
    private LicenseState _currentState;
    private readonly object _stateLock = new();

    // ユーザー情報（認証連携後に設定）
    private string? _userId;
    private string? _sessionToken;

    // レート制限
    private readonly SemaphoreSlim _refreshRateLimiter;
    private readonly SemaphoreSlim _consumeRateLimiter;
    private DateTime _lastRefresh = DateTime.MinValue;
    private int _refreshCountThisMinute;
    private int _consumeCountThisMinute;
    private DateTime _rateLimitResetTime = DateTime.UtcNow;

    // バックグラウンド更新
    private readonly System.Threading.Timer? _backgroundRefreshTimer;
    private int _backgroundUpdateCount;
    private bool _disposed;

    /// <inheritdoc/>
    public LicenseState CurrentState
    {
        get
        {
            lock (_stateLock)
            {
                return _currentState;
            }
        }
    }

    /// <inheritdoc/>
    public event EventHandler<LicenseStateChangedEventArgs>? StateChanged;

    /// <inheritdoc/>
    public event EventHandler<TokenUsageWarningEventArgs>? TokenUsageWarning;

    /// <inheritdoc/>
    public event EventHandler<SessionInvalidatedEventArgs>? SessionInvalidated;

    /// <inheritdoc/>
    public event EventHandler<PlanExpirationWarningEventArgs>? PlanExpirationWarning;

    /// <summary>
    /// LicenseManagerを初期化
    /// </summary>
    public LicenseManager(
        ILogger<LicenseManager> logger,
        ILicenseApiClient apiClient,
        ILicenseCacheService cacheService,
        IEventAggregator eventAggregator,
        IOptions<LicenseSettings> settings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));

        // 初期状態はFreeプラン
        _currentState = LicenseState.Default;

        // レート制限セマフォ
        _refreshRateLimiter = new SemaphoreSlim(1, 1);
        _consumeRateLimiter = new SemaphoreSlim(_settings.CloudAiRateLimitPerMinute, _settings.CloudAiRateLimitPerMinute);

        // バックグラウンド更新タイマー（モックモード以外）
        if (!_settings.EnableMockMode)
        {
            var interval = TimeSpan.FromMinutes(_settings.BackgroundRefreshIntervalMinutes);
            _backgroundRefreshTimer = new System.Threading.Timer(
                OnBackgroundRefreshTimerElapsed,
                null,
                interval,
                interval);
        }

        _logger.LogInformation(
            "LicenseManager初期化: MockMode={MockMode}, BackgroundRefreshInterval={Interval}min",
            _settings.EnableMockMode,
            _settings.BackgroundRefreshIntervalMinutes);
    }

    /// <summary>
    /// ユーザー認証情報を設定
    /// </summary>
    public void SetUserCredentials(string userId, string sessionToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionToken);

        _userId = userId;
        _sessionToken = sessionToken;

        _logger.LogDebug("ユーザー認証情報を設定: UserId={UserId}", userId);
    }

    /// <inheritdoc/>
    public async Task<LicenseState> GetCurrentStateAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // ユーザー未認証の場合はデフォルト状態
        if (string.IsNullOrEmpty(_userId))
        {
            return LicenseState.Default;
        }

        // キャッシュから取得を試行
        var cachedState = await _cacheService.GetCachedStateAsync(_userId, cancellationToken)
            .ConfigureAwait(false);

        if (cachedState is not null)
        {
            UpdateCurrentState(cachedState, LicenseChangeReason.CacheLoad);
            return cachedState;
        }

        // キャッシュがない場合はサーバーから取得
        return await RefreshStateAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<LicenseState> RefreshStateAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // ユーザー未認証の場合
        if (string.IsNullOrEmpty(_userId) || string.IsNullOrEmpty(_sessionToken))
        {
            _logger.LogDebug("ユーザー未認証のためリフレッシュをスキップ");
            return LicenseState.Default;
        }

        // レート制限チェック
        if (!await TryAcquireRefreshRateLimitAsync(cancellationToken).ConfigureAwait(false))
        {
            _logger.LogWarning("ライセンスリフレッシュがレート制限されました");
            return _currentState;
        }

        // キャッシュが有効な場合はキャッシュを返す
        if (await _cacheService.IsCacheValidAsync(_userId, cancellationToken).ConfigureAwait(false))
        {
            var cachedState = await _cacheService.GetCachedStateAsync(_userId, cancellationToken)
                .ConfigureAwait(false);
            if (cachedState is not null)
            {
                return cachedState;
            }
        }

        // サーバーから取得
        return await FetchFromServerAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<LicenseState> ForceRefreshAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // ユーザー未認証の場合
        if (string.IsNullOrEmpty(_userId) || string.IsNullOrEmpty(_sessionToken))
        {
            _logger.LogDebug("ユーザー未認証のため強制リフレッシュをスキップ");
            return LicenseState.Default;
        }

        // キャッシュをクリア
        await _cacheService.ClearCacheAsync(_userId, cancellationToken).ConfigureAwait(false);

        // サーバーから取得
        return await FetchFromServerAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public bool IsFeatureAvailable(FeatureType feature)
    {
        lock (_stateLock)
        {
            return _currentState.CurrentPlan.IsFeatureAvailable(feature);
        }
    }

    /// <inheritdoc/>
    public async Task<TokenConsumptionResult> ConsumeCloudAiTokensAsync(
        int tokenCount,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tokenCount);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        // ユーザー未認証の場合
        if (string.IsNullOrEmpty(_userId) || string.IsNullOrEmpty(_sessionToken))
        {
            return TokenConsumptionResult.CreateFailure(
                TokenConsumptionFailureReason.SessionInvalid,
                "ユーザーが認証されていません");
        }

        // プランがクラウドAI対応かチェック
        if (!_currentState.CurrentPlan.HasCloudAiAccess())
        {
            return TokenConsumptionResult.CreateFailure(
                TokenConsumptionFailureReason.PlanNotSupported);
        }

        // ローカルでクォータチェック（楽観的）
        if (_currentState.IsQuotaExceeded)
        {
            return TokenConsumptionResult.CreateFailure(
                TokenConsumptionFailureReason.QuotaExceeded,
                currentUsage: _currentState.CloudAiTokensUsed,
                remainingTokens: 0);
        }

        // APIが利用不可（オフライン）の場合
        if (!_apiClient.IsAvailable)
        {
            return await HandleOfflineConsumptionAsync(tokenCount, idempotencyKey, cancellationToken)
                .ConfigureAwait(false);
        }

        // サーバーに消費を記録
        try
        {
            var request = new TokenConsumptionRequest
            {
                UserId = _userId,
                SessionToken = _sessionToken,
                TokenCount = tokenCount,
                IdempotencyKey = idempotencyKey
            };

            var response = await _apiClient.ConsumeTokensAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (response.Success)
            {
                // ローカル状態を更新
                var newState = _currentState with
                {
                    CloudAiTokensUsed = response.NewUsageTotal,
                    LastServerSync = DateTime.UtcNow
                };
                UpdateCurrentState(newState, LicenseChangeReason.TokenConsumption);

                // トークン使用量警告をチェック
                CheckTokenUsageThresholds(newState);

                return TokenConsumptionResult.CreateSuccess(
                    response.NewUsageTotal,
                    response.RemainingTokens);
            }

            // エラーコードに応じて失敗理由を判定
            var failureReason = MapErrorCodeToFailureReason(response.ErrorCode);
            return TokenConsumptionResult.CreateFailure(
                failureReason,
                response.ErrorMessage,
                _currentState.CloudAiTokensUsed,
                _currentState.RemainingTokens);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "トークン消費APIエラー、オフラインフォールバック");

            // オフラインフォールバック
            return await HandleOfflineConsumptionAsync(tokenCount, idempotencyKey, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// サーバーからライセンス状態を取得
    /// </summary>
    private async Task<LicenseState> FetchFromServerAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _apiClient.GetLicenseStateAsync(_userId!, _sessionToken!, cancellationToken)
                .ConfigureAwait(false);

            if (response is { Success: true, LicenseState: not null })
            {
                var state = response.LicenseState;

                // キャッシュに保存
                await _cacheService.SetCachedStateAsync(_userId!, state, cancellationToken)
                    .ConfigureAwait(false);

                // 状態を更新
                UpdateCurrentState(state, LicenseChangeReason.ServerRefresh);

                // プラン期限切れ警告をチェック
                CheckPlanExpirationWarning(state);

                // 未同期消費の同期を試行
                await SyncPendingConsumptionsAsync(cancellationToken).ConfigureAwait(false);

                _lastRefresh = DateTime.UtcNow;
                return state;
            }

            // セッション無効の場合
            if (response?.ErrorCode == "SESSION_INVALID")
            {
                OnSessionInvalidated(response.ErrorMessage ?? "セッションが無効です", null);
            }

            _logger.LogWarning(
                "ライセンス状態取得失敗: ErrorCode={ErrorCode}, Message={Message}",
                response?.ErrorCode, response?.ErrorMessage);

            // キャッシュにフォールバック
            var cachedState = await _cacheService.GetCachedStateAsync(_userId!, cancellationToken)
                .ConfigureAwait(false);
            return cachedState ?? _currentState;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "ライセンスサーバーに接続できません。キャッシュを使用します");

            // キャッシュにフォールバック
            var cachedState = await _cacheService.GetCachedStateAsync(_userId!, cancellationToken)
                .ConfigureAwait(false);
            return cachedState ?? _currentState;
        }
    }

    /// <summary>
    /// オフライン時のトークン消費処理
    /// </summary>
    private async Task<TokenConsumptionResult> HandleOfflineConsumptionAsync(
        int tokenCount,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        // ローカルでトークン使用量を更新
        var updatedState = await _cacheService.UpdateTokenUsageAsync(_userId!, tokenCount, cancellationToken)
            .ConfigureAwait(false);

        if (updatedState is null)
        {
            return TokenConsumptionResult.CreateFailure(
                TokenConsumptionFailureReason.NetworkError,
                "オフラインでローカルキャッシュが利用できません");
        }

        // 未同期消費記録を保存
        var pendingConsumption = new PendingTokenConsumption
        {
            UserId = _userId!,
            IdempotencyKey = idempotencyKey,
            TokenCount = tokenCount,
            ConsumedAt = DateTime.UtcNow
        };
        await _cacheService.AddPendingConsumptionAsync(pendingConsumption, cancellationToken)
            .ConfigureAwait(false);

        // ローカル状態を更新
        UpdateCurrentState(updatedState, LicenseChangeReason.TokenConsumption);

        _logger.LogDebug(
            "オフライントークン消費: Tokens={Tokens}, Key={Key}",
            tokenCount, idempotencyKey);

        return TokenConsumptionResult.CreateSuccess(
            updatedState.CloudAiTokensUsed,
            updatedState.RemainingTokens);
    }

    /// <summary>
    /// 未同期消費記録をサーバーに同期
    /// </summary>
    private async Task SyncPendingConsumptionsAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_userId) || string.IsNullOrEmpty(_sessionToken))
        {
            return;
        }

        var pendingConsumptions = await _cacheService.GetPendingConsumptionsAsync(_userId, cancellationToken)
            .ConfigureAwait(false);

        if (pendingConsumptions.Count == 0)
        {
            return;
        }

        _logger.LogInformation("未同期消費記録を同期中: Count={Count}", pendingConsumptions.Count);

        var syncedKeys = new List<string>();

        foreach (var consumption in pendingConsumptions)
        {
            try
            {
                var request = new TokenConsumptionRequest
                {
                    UserId = _userId,
                    SessionToken = _sessionToken,
                    TokenCount = consumption.TokenCount,
                    IdempotencyKey = consumption.IdempotencyKey,
                    Metadata = consumption.Metadata
                };

                var response = await _apiClient.ConsumeTokensAsync(request, cancellationToken)
                    .ConfigureAwait(false);

                // 成功または既に処理済み（Idempotent）の場合は同期済みとしてマーク
                if (response.Success || response.WasIdempotent)
                {
                    syncedKeys.Add(consumption.IdempotencyKey);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "消費記録の同期失敗: Key={Key}",
                    consumption.IdempotencyKey);
            }
        }

        // 同期済み記録を削除
        if (syncedKeys.Count > 0)
        {
            await _cacheService.RemoveSyncedConsumptionsAsync(syncedKeys, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation("消費記録同期完了: Count={Count}", syncedKeys.Count);
        }
    }

    /// <summary>
    /// 現在の状態を更新し、イベントを発行
    /// </summary>
    private void UpdateCurrentState(LicenseState newState, LicenseChangeReason reason)
    {
        LicenseState oldState;
        lock (_stateLock)
        {
            oldState = _currentState;
            _currentState = newState;
        }

        // プランが変更された場合のみイベント発行
        if (oldState.CurrentPlan != newState.CurrentPlan || reason == LicenseChangeReason.ServerRefresh)
        {
            OnStateChanged(oldState, newState, reason);
        }
    }

    /// <summary>
    /// トークン使用量の警告閾値をチェック
    /// </summary>
    private void CheckTokenUsageThresholds(LicenseState state)
    {
        if (state.MonthlyTokenLimit == 0)
        {
            return;
        }

        var usagePercent = (double)state.CloudAiTokensUsed / state.MonthlyTokenLimit * 100;

        TokenWarningLevel? warningLevel = null;
        if (usagePercent >= 100)
        {
            warningLevel = TokenWarningLevel.Exceeded;
        }
        else if (usagePercent >= _settings.TokenCriticalThresholdPercent)
        {
            warningLevel = TokenWarningLevel.Critical;
        }
        else if (usagePercent >= _settings.TokenWarningThresholdPercent)
        {
            warningLevel = TokenWarningLevel.Warning;
        }

        if (warningLevel.HasValue)
        {
            OnTokenUsageWarning(
                state.CloudAiTokensUsed,
                state.MonthlyTokenLimit,
                (int)usagePercent,
                warningLevel.Value);
        }
    }

    /// <summary>
    /// プラン期限切れ警告をチェック
    /// </summary>
    private void CheckPlanExpirationWarning(LicenseState state)
    {
        if (state.ExpirationDate is null)
        {
            return;
        }

        var daysRemaining = (state.ExpirationDate.Value - DateTime.UtcNow).Days;

        if (daysRemaining <= _settings.PlanExpirationWarningDays)
        {
            OnPlanExpirationWarning(state.ExpirationDate.Value, daysRemaining);
        }
    }

    /// <summary>
    /// リフレッシュレート制限を取得
    /// </summary>
    private async Task<bool> TryAcquireRefreshRateLimitAsync(CancellationToken cancellationToken)
    {
        // 1分ごとにカウンターをリセット
        if (DateTime.UtcNow >= _rateLimitResetTime)
        {
            _refreshCountThisMinute = 0;
            _consumeCountThisMinute = 0;
            _rateLimitResetTime = DateTime.UtcNow.AddMinutes(1);
        }

        if (_refreshCountThisMinute >= _settings.RefreshRateLimitPerMinute)
        {
            return false;
        }

        await _refreshRateLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _refreshCountThisMinute++;
            return true;
        }
        finally
        {
            _refreshRateLimiter.Release();
        }
    }

    /// <summary>
    /// エラーコードを失敗理由にマッピング
    /// </summary>
    private static TokenConsumptionFailureReason MapErrorCodeToFailureReason(string? errorCode)
    {
        return errorCode switch
        {
            "QUOTA_EXCEEDED" => TokenConsumptionFailureReason.QuotaExceeded,
            "SESSION_INVALID" => TokenConsumptionFailureReason.SessionInvalid,
            "RATE_LIMITED" => TokenConsumptionFailureReason.RateLimited,
            "PLAN_NOT_SUPPORTED" => TokenConsumptionFailureReason.PlanNotSupported,
            _ => TokenConsumptionFailureReason.ServerError
        };
    }

    /// <summary>
    /// バックグラウンド更新タイマーコールバック
    /// </summary>
    private async void OnBackgroundRefreshTimerElapsed(object? state)
    {
        if (_disposed || string.IsNullOrEmpty(_userId))
        {
            return;
        }

        var attemptNumber = Interlocked.Increment(ref _backgroundUpdateCount);

        try
        {
            await RefreshStateAsync(CancellationToken.None).ConfigureAwait(false);

            if (_settings.EnableDebugMode)
            {
                _logger.LogDebug(
                    "バックグラウンドライセンス更新成功: UserId={UserId}, Attempt={Attempt}",
                    _userId, attemptNumber);
            }
        }
        catch (HttpRequestException httpEx)
        {
            _logger.LogWarning(
                httpEx,
                "バックグラウンドライセンス更新失敗（ネットワークエラー）: UserId={UserId}, Attempt={Attempt}, StatusCode={StatusCode}",
                _userId ?? "Unknown",
                attemptNumber,
                httpEx.StatusCode);
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning(
                "バックグラウンドライセンス更新タイムアウト: UserId={UserId}, Attempt={Attempt}",
                _userId ?? "Unknown",
                attemptNumber);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "バックグラウンドライセンス更新失敗: UserId={UserId}, Attempt={Attempt}, ErrorType={ErrorType}",
                _userId ?? "Unknown",
                attemptNumber,
                ex.GetType().Name);
        }
    }

    #region Event Raising

    private void OnStateChanged(LicenseState oldState, LicenseState newState, LicenseChangeReason reason)
    {
        var args = new LicenseStateChangedEventArgs(oldState, newState, reason);
        StateChanged?.Invoke(this, args);

        // EventAggregatorにも発行
        _ = _eventAggregator.PublishAsync(new LicenseStateChangedEvent(oldState, newState, reason));

        if (_settings.EnableDebugMode)
        {
            _logger.LogDebug(
                "ライセンス状態変更: {OldPlan} -> {NewPlan}, Reason={Reason}",
                oldState.CurrentPlan, newState.CurrentPlan, reason);
        }
    }

    private void OnTokenUsageWarning(long currentUsage, long limit, int percentage, TokenWarningLevel level)
    {
        var args = new TokenUsageWarningEventArgs(currentUsage, limit, level);
        TokenUsageWarning?.Invoke(this, args);

        // EventAggregatorにも発行
        _ = _eventAggregator.PublishAsync(new TokenUsageWarningEvent(currentUsage, limit, level));

        _logger.LogWarning(
            "トークン使用量警告: {Percentage}% ({Current}/{Limit}), Level={Level}",
            percentage, currentUsage, limit, level);
    }

    private void OnSessionInvalidated(string reason, string? newDeviceInfo)
    {
        var args = new SessionInvalidatedEventArgs(reason, newDeviceInfo);
        SessionInvalidated?.Invoke(this, args);

        // EventAggregatorにも発行
        _ = _eventAggregator.PublishAsync(new SessionInvalidatedEvent(reason, newDeviceInfo));

        _logger.LogWarning("セッション無効化: Reason={Reason}", reason);
    }

    private void OnPlanExpirationWarning(DateTime expirationDate, int daysRemaining)
    {
        var args = new PlanExpirationWarningEventArgs(expirationDate, daysRemaining);
        PlanExpirationWarning?.Invoke(this, args);

        // EventAggregatorにも発行
        _ = _eventAggregator.PublishAsync(new PlanExpirationWarningEvent(expirationDate, daysRemaining));

        _logger.LogWarning(
            "プラン期限切れ警告: ExpirationDate={Date}, DaysRemaining={Days}",
            expirationDate, daysRemaining);
    }

    #endregion

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // 1. タイマーを停止（新しいコールバック実行を防ぐ）
        _backgroundRefreshTimer?.Change(Timeout.Infinite, Timeout.Infinite);

        // 2. フラグを設定（実行中のコールバックをスキップさせる）
        _disposed = true;

        // 3. タイマーを破棄
        _backgroundRefreshTimer?.Dispose();

        // 4. セマフォを破棄
        _refreshRateLimiter.Dispose();
        _consumeRateLimiter.Dispose();

        _logger.LogDebug("LicenseManager disposed");
    }
}
