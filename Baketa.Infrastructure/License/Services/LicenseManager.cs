using System.Globalization;
using System.Net.Http;
using System.Threading;
using Baketa.Core.Abstractions.Auth;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.License;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Events;
using Baketa.Core.Extensions;
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
    private readonly IUnifiedSettingsService? _unifiedSettingsService;
    private readonly IUsageAnalyticsService? _analyticsService;
    private readonly IBonusTokenService? _bonusTokenService;
    private readonly IAuthService? _authService;

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

    // Issue #243: プロモーションイベント購読用プロセッサ
    private readonly IEventProcessor<PromotionAppliedEvent> _promotionAppliedProcessor;
    private readonly IEventProcessor<PromotionRemovedEvent> _promotionRemovedProcessor;

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
        IOptions<LicenseSettings> settings,
        IUnifiedSettingsService? unifiedSettingsService = null,
        IUsageAnalyticsService? analyticsService = null,
        IBonusTokenService? bonusTokenService = null,
        IAuthService? authService = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        _unifiedSettingsService = unifiedSettingsService;
        _analyticsService = analyticsService;
        _bonusTokenService = bonusTokenService;
        _authService = authService;

        // 初期状態はFreeプラン
        _currentState = LicenseState.Default;

        // [Issue #258] 非モックモードでも起動時にプロモーション設定をチェック
        ApplyPersistedPromotionIfValid();

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

        // Issue #243: PromotionAppliedEventを購読（循環依存回避）
        _promotionAppliedProcessor = new InlineEventProcessor<PromotionAppliedEvent>(evt =>
        {
            OnPromotionApplied(evt);
            return Task.CompletedTask;
        });
        _promotionRemovedProcessor = new InlineEventProcessor<PromotionRemovedEvent>(evt =>
        {
            OnPromotionRemoved(evt);
            return Task.CompletedTask;
        });
        _eventAggregator.Subscribe(_promotionAppliedProcessor);
        _eventAggregator.Subscribe(_promotionRemovedProcessor);

        // モックモードの場合、自動的にテスト用認証情報を設定
        if (_settings.EnableMockMode)
        {
            // Issue #243: プロモーションが有効なら優先
            var effectivePlan = DetermineEffectivePlan();
            _userId = "mock_user_" + Guid.NewGuid().ToString("N")[..8];
            _sessionToken = "mock_session_" + Guid.NewGuid().ToString("N");

            // [Issue #258] 永続化されたトークン使用量を読み込み（IUnifiedSettingsService優先）
            var persistedTokenUsage = _unifiedSettingsService?.GetPromotionSettings().MockTokenUsage ?? 0;
            var initialTokenUsage = persistedTokenUsage > 0 ? persistedTokenUsage : _settings.MockTokenUsage;

            // [Issue #275] ApplyPersistedPromotionIfValid()で設定されたExpirationDateを保持
            var promotionExpirationDate = _currentState.ExpirationDate;
            // プロモーションが設定されており、かつ有効期限が切れていないかを確認
            var hasActivePromotion = promotionExpirationDate.HasValue && promotionExpirationDate > DateTime.UtcNow;

            // モックモード用の初期状態を設定
            _currentState = new LicenseState
            {
                CurrentPlan = effectivePlan,
                UserId = _userId,
                SessionId = _sessionToken,
                ContractStartDate = DateTime.UtcNow.AddDays(-15),
                // 有効なプロモーションがあればその有効期限を、なければデフォルト(15日後)を設定
                ExpirationDate = hasActivePromotion ? promotionExpirationDate : DateTime.UtcNow.AddDays(15),
                CloudAiTokensUsed = initialTokenUsage,
                IsCached = false,
                LastServerSync = DateTime.UtcNow,
                PatreonSyncStatus = PatreonSyncStatus.Synced,
                PatronStatus = "active_patron"
            };

            _logger.LogWarning(
                "🧪 モックモード有効: UserId={UserId}, Plan={Plan}, TokenLimit={TokenLimit}, HasActivePromotion={HasActivePromotion}, ExpiresAt={ExpiresAt}",
                _userId,
                effectivePlan,
                _currentState.MonthlyTokenLimit,
                hasActivePromotion,
                _currentState.ExpirationDate);
        }

        _logger.LogInformation(
            "🔐 LicenseManager初期化完了 - Plan={Plan}, MockMode={MockMode}, TokenLimit={TokenLimit}, BackgroundRefresh={Interval}min",
            _currentState.CurrentPlan,
            _settings.EnableMockMode,
            _currentState.MonthlyTokenLimit,
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

        // [Issue #275] ユーザー未認証の場合でも、プロモーション適用済みの現在状態を返す
        // LicenseState.Defaultを返すと、プロモーションで適用されたプランが無視される
        if (string.IsNullOrEmpty(_userId))
        {
            return _currentState;
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

        // APIクライアントが認証情報を必要とする場合のみチェック
        // Patreonなど独自認証を持つクライアントはこのチェックをスキップ
        if (_apiClient.RequiresCredentials && (string.IsNullOrEmpty(_userId) || string.IsNullOrEmpty(_sessionToken)))
        {
            // [Issue #275] プロモーション適用済みの現在状態を返す
            _logger.LogDebug("ユーザー未認証のためリフレッシュをスキップ（RequiresCredentials={RequiresCredentials}）", _apiClient.RequiresCredentials);
            return _currentState;
        }

        // レート制限チェック
        if (!await TryAcquireRefreshRateLimitAsync(cancellationToken).ConfigureAwait(false))
        {
            _logger.LogWarning("ライセンスリフレッシュがレート制限されました");
            return _currentState;
        }

        // 認証情報がある場合のみキャッシュをチェック
        if (!string.IsNullOrEmpty(_userId))
        {
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
        }

        // サーバーから取得
        return await FetchFromServerAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<LicenseState> ForceRefreshAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // APIクライアントが認証情報を必要とする場合のみチェック
        if (_apiClient.RequiresCredentials && (string.IsNullOrEmpty(_userId) || string.IsNullOrEmpty(_sessionToken)))
        {
            _logger.LogDebug("ユーザー未認証のため強制リフレッシュをスキップ（RequiresCredentials={RequiresCredentials}）", _apiClient.RequiresCredentials);
            // Issue #275: プロモーション適用済み状態を保持するため、Defaultではなく現在の状態を返す
            return _currentState;
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
            // プランで利用可能かチェック
            if (_currentState.CurrentPlan.IsFeatureAvailable(feature))
            {
                return true;
            }

            // [Issue #280+#281] CloudAiTranslationはボーナストークンでも利用可能
            if (feature == FeatureType.CloudAiTranslation)
            {
                return (_bonusTokenService?.GetTotalRemainingTokens() ?? 0) > 0;
            }

            return false;
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
        // [Issue #280+#281] プランまたはボーナストークンでCloud AI利用可能
        if (!_currentState.CurrentPlan.HasCloudAiAccess() &&
            (_bonusTokenService?.GetTotalRemainingTokens() ?? 0) <= 0)
        {
            return TokenConsumptionResult.CreateFailure(
                TokenConsumptionFailureReason.PlanNotSupported);
        }

        // [Gemini Review] スレッドセーフのため状態をスナップショット
        var currentStateSnapshot = CurrentState;

        // [Issue #280+#281] ボーナストークン優先消費
        // [Gemini Review] 消費前にシミュレーション/チェックを行う（ロールバック不要な設計）
        long bonusTokensToConsume = 0;
        var remainingForPlanQuota = tokenCount;

        // Step 1: ボーナストークンでカバーできる量を確認（まだ消費しない）
        if (_bonusTokenService != null)
        {
            bonusTokensToConsume = _bonusTokenService.GetConsumeableAmount(tokenCount);
            remainingForPlanQuota = tokenCount - (int)bonusTokensToConsume;
        }

        // Step 2: 残り分がプランクォータでカバーできるかチェック
        if (remainingForPlanQuota > 0)
        {
            var projectedUsage = currentStateSnapshot.CloudAiTokensUsed + remainingForPlanQuota;
            if (projectedUsage > currentStateSnapshot.MonthlyTokenLimit)
            {
                _logger.LogWarning(
                    "クォータ超過: Used={Used}, Requested={Requested}, Limit={Limit}",
                    currentStateSnapshot.CloudAiTokensUsed, remainingForPlanQuota, currentStateSnapshot.MonthlyTokenLimit);

                return TokenConsumptionResult.CreateFailure(
                    TokenConsumptionFailureReason.QuotaExceeded,
                    currentUsage: currentStateSnapshot.CloudAiTokensUsed,
                    remainingTokens: 0);
            }
        }

        // Step 3: チェック通過後、実際にボーナストークンを消費
        long bonusTokensConsumed = 0;
        if (bonusTokensToConsume > 0 && _bonusTokenService != null)
        {
            bonusTokensConsumed = _bonusTokenService.ConsumeTokens(bonusTokensToConsume);

            // [Issue #280+#281] ボーナストークン消費をサーバーに同期
            if (bonusTokensConsumed > 0 && _authService != null)
            {
                try
                {
                    var session = await _authService.GetCurrentSessionAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (session?.IsValid == true)
                    {
                        var syncResult = await _bonusTokenService.SyncToServerAsync(session.AccessToken, cancellationToken)
                            .ConfigureAwait(false);

                        if (!syncResult.Success)
                        {
                            _logger.LogWarning(
                                "ボーナストークン同期失敗（後で再試行）: {Error}",
                                syncResult.ErrorMessage);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "ボーナストークン同期中に例外発生（後で再試行）");
                }
            }

            // ボーナストークンで全額カバーできた場合
            if (remainingForPlanQuota <= 0)
            {
                return TokenConsumptionResult.CreateSuccess(
                    currentStateSnapshot.CloudAiTokensUsed,
                    currentStateSnapshot.RemainingTokens);
            }
        }

        var remainingToConsume = remainingForPlanQuota;

        // APIが利用不可（オフライン）の場合
        if (!_apiClient.IsAvailable)
        {
            // [Issue #280] ボーナス消費後の残りをオフライン消費
            return await HandleOfflineConsumptionAsync(remainingToConsume, idempotencyKey, cancellationToken)
                .ConfigureAwait(false);
        }

        // サーバーに消費を記録（ボーナス消費後の残り分のみ）
        try
        {
            var request = new TokenConsumptionRequest
            {
                UserId = _userId,
                SessionToken = _sessionToken,
                TokenCount = remainingToConsume,  // [Issue #280] ボーナス消費後の残り
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

            // [Issue #280] ボーナス消費後の残りをオフラインフォールバック
            return await HandleOfflineConsumptionAsync(remainingToConsume, idempotencyKey, cancellationToken)
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
        LicenseState stateToApply;

        lock (_stateLock)
        {
            oldState = _currentState;
            stateToApply = newState;

            // [Issue #275] SessionIdを保持する（Gemini Review: アトミック性保証）
            // 外部ソースから渡されたstateにSessionIdがない場合、現在の値を引き継ぐ
            // string.IsNullOrWhiteSpace を使用してホワイトスペースのみの場合も考慮
            if (string.IsNullOrWhiteSpace(newState.SessionId) && !string.IsNullOrWhiteSpace(_currentState.SessionId))
            {
                _logger.LogDebug(
                    "🔑 [Issue #275] SessionIdを保持: {SessionId}",
                    _currentState.SessionId[..Math.Min(10, _currentState.SessionId.Length)] + "...");

                stateToApply = stateToApply with { SessionId = _currentState.SessionId };
            }

            // [Issue #275] プロモーション適用済みのプランを保持
            // キャッシュ/サーバーから読み込んだ状態が現在のプランより低い場合、
            // 有効なプロモーションがあれば現在のプランを維持
            if (ShouldPreservePromotionPlan(oldState, newState, reason))
            {
                _logger.LogDebug(
                    "🎁 [Issue #275] プロモーション適用済みプランを保持: {OldPlan} (降格防止: {NewPlan})",
                    oldState.CurrentPlan, newState.CurrentPlan);

                stateToApply = stateToApply with
                {
                    CurrentPlan = oldState.CurrentPlan,
                    ExpirationDate = oldState.ExpirationDate
                };
            }

            // [Issue #275] CloudAiTokensUsedを保持
            // Patreon同期時など外部ソースがCloudAiTokensUsed=0を返す場合、
            // 現在の値（プロモーション設定から読み込んだ値）を維持
            if (newState.CloudAiTokensUsed == 0 && oldState.CloudAiTokensUsed > 0)
            {
                _logger.LogDebug(
                    "💰 [Issue #275] CloudAiTokensUsedを保持: {OldValue} (外部ソース: {NewValue})",
                    oldState.CloudAiTokensUsed, newState.CloudAiTokensUsed);

                stateToApply = stateToApply with { CloudAiTokensUsed = oldState.CloudAiTokensUsed };
            }

            _currentState = stateToApply;
        }

        // [Issue #258] プラン変更またはトークン消費時にイベント発行
        // TokenConsumptionを追加: UI側でトークン使用量表示を更新するため
        if (oldState.CurrentPlan != stateToApply.CurrentPlan ||
            reason == LicenseChangeReason.ServerRefresh ||
            reason == LicenseChangeReason.TokenConsumption)
        {
            OnStateChanged(oldState, stateToApply, reason);
        }
    }

    /// <summary>
    /// [Issue #275] プロモーション適用済みのプランを保持すべきか判定
    /// </summary>
    private bool ShouldPreservePromotionPlan(LicenseState oldState, LicenseState newState, LicenseChangeReason reason)
    {
        // プロモーション関連の変更は保持しない（正当な変更）
        if (reason == LicenseChangeReason.PromotionApplied ||
            reason == LicenseChangeReason.PromotionExpired)
        {
            return false;
        }

        // プランが同じか上がる場合は保持不要
        if ((int)newState.CurrentPlan >= (int)oldState.CurrentPlan)
        {
            return false;
        }

        // 有効なプロモーションがあるか確認
        if (_unifiedSettingsService is null)
        {
            return false;
        }

        var promotionSettings = _unifiedSettingsService.GetPromotionSettings();
        if (!promotionSettings.IsCurrentlyActive() || !promotionSettings.PromotionPlanType.HasValue)
        {
            return false;
        }

        var promotionPlan = (PlanType)promotionSettings.PromotionPlanType.Value;

        // 現在のプランがプロモーションプランと一致する場合のみ保持
        return oldState.CurrentPlan == promotionPlan;
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

        // [Issue #271] プラン変更を使用統計に記録
        if (oldState.CurrentPlan != newState.CurrentPlan && _analyticsService != null)
        {
            _analyticsService.TrackEvent("plan_changed", new Dictionary<string, object>
            {
                ["old_plan"] = oldState.CurrentPlan.ToString(),
                ["new_plan"] = newState.CurrentPlan.ToString(),
                ["change_reason"] = reason.ToString()
            });

            _logger.LogInformation(
                "[Issue #271] プラン変更をUsageAnalyticsに記録: {OldPlan} -> {NewPlan}, Reason={Reason}",
                oldState.CurrentPlan, newState.CurrentPlan, reason);
        }

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

    #region Test Support

    /// <summary>
    /// テストモード有効化に必要な環境変数名
    /// </summary>
    private const string TestModeEnvVar = "BAKETA_ALLOW_TEST_MODE";

    /// <inheritdoc/>
    public Task<bool> SetTestPlanAsync(PlanType plan, CancellationToken cancellationToken = default)
    {
        // モックモードでない場合は何もしない
        if (!_settings.EnableMockMode)
        {
            _logger.LogWarning(
                "SetTestPlanAsync呼び出しを無視: EnableMockMode=false（本番環境では使用できません）");
            return Task.FromResult(false);
        }

        // 環境変数チェック（本番誤用防止の追加安全策）
        var envValue = Environment.GetEnvironmentVariable(TestModeEnvVar);
        if (!string.Equals(envValue, "true", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "SetTestPlanAsync呼び出しを無視: 環境変数 {EnvVar}=true が設定されていません",
                TestModeEnvVar);
            return Task.FromResult(false);
        }

        ObjectDisposedException.ThrowIf(_disposed, this);

        // テスト用に新しいLicenseStateを作成
        var newState = new LicenseState
        {
            CurrentPlan = plan,
            UserId = _userId ?? "test_user",
            ContractStartDate = DateTime.UtcNow,
            ExpirationDate = DateTime.UtcNow.AddMonths(plan == PlanType.Free ? 0 : 1),
            CloudAiTokensUsed = 0,
            IsCached = false,
            SessionId = _sessionToken ?? $"test_session_{Guid.NewGuid():N}",
            LastServerSync = DateTime.UtcNow
        };

        // 状態を更新（イベントも発火）
        UpdateCurrentState(newState, LicenseChangeReason.ServerRefresh);

        _logger.LogInformation(
            "🧪 テストモード: プランを {Plan} に設定しました",
            plan);

        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public void SetResolvedLicenseState(LicenseState state, string source, LicenseChangeReason reason)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ObjectDisposedException.ThrowIf(_disposed, this);

        _logger.LogInformation(
            "🔄 外部ソースからライセンス状態を設定: Source={Source}, Plan={Plan}, Reason={Reason}",
            source, state.CurrentPlan, reason);

        // [Issue #279] スレッドセーフにuserIdを取得・設定 (Gemini Review指摘対応)
        string? userIdForCache;
        lock (_stateLock)
        {
            if (!string.IsNullOrEmpty(state.PatreonUserId))
            {
                _userId = state.PatreonUserId;
                _logger.LogDebug("PatreonUserIdをuserIdに設定: {UserId}", MaskUserId(_userId));
            }
            userIdForCache = _userId;
        }

        // [Issue #275] プロモーションが有効な場合、プロモーションのプランと有効期限を優先
        var stateToApply = ApplyPromotionOverride(state);

        // 状態を更新（イベントも発火）
        // [Issue #275] SessionIdの保持はUpdateCurrentState内でアトミックに実行される（Gemini Review対応）
        UpdateCurrentState(stateToApply, reason);

        // [Issue #279] キャッシュも更新（同期済み状態をキャッシュに反映）
        // これにより「ライセンス情報を更新」ボタンでキャッシュの古い値に戻る問題を解消
        if (!string.IsNullOrEmpty(userIdForCache))
        {
            _ = UpdateCacheWithLoggingAsync(userIdForCache, stateToApply);
        }
    }

    /// <summary>
    /// キャッシュを更新し、エラーをログに記録する (Issue #279 Gemini Review対応)
    /// </summary>
    private async Task UpdateCacheWithLoggingAsync(string userId, LicenseState state)
    {
        try
        {
            await _cacheService.SetCachedStateAsync(userId, state, CancellationToken.None)
                .ConfigureAwait(false);
            _logger.LogDebug("キャッシュ更新成功: UserId={UserId}", MaskUserId(userId));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "キャッシュ更新に失敗しましたが、メモリ上の状態は正常に更新されています: UserId={UserId}",
                MaskUserId(userId));
        }
    }

    /// <inheritdoc/>
    public void SyncTokenUsage(long tokensUsed)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(tokensUsed);

        LicenseState oldState;
        LicenseState newState;

        lock (_stateLock)
        {
            if (_currentState.CloudAiTokensUsed == tokensUsed)
            {
                return; // 変更なし
            }

            oldState = _currentState;
            _currentState = _currentState with { CloudAiTokensUsed = tokensUsed };
            newState = _currentState;

            _logger.LogDebug(
                "[Issue #275] トークン使用量を同期: {OldUsage} → {NewUsage}",
                oldState.CloudAiTokensUsed, tokensUsed);
        }

        // [Issue #275] StateChangedイベントを発火して他のViewModelにも通知
        // GeneralSettingsViewModelなどが更新された値を取得できるようにする
        OnStateChanged(oldState, newState, LicenseChangeReason.TokenUsageUpdated);
    }

    /// <inheritdoc/>
    public void NotifyBonusTokensLoaded()
    {
        // [Issue #280+#281] ボーナストークンがロードされた後に呼び出される
        // 状態自体は変更しないが、StateChangedイベントを発火して
        // CloudTranslationAvailabilityServiceなどのリスナーが
        // IsFeatureAvailable(CloudAiTranslation)を再評価できるようにする
        LicenseState currentState;
        lock (_stateLock)
        {
            currentState = _currentState;
        }

        _logger.LogInformation(
            "[Issue #280+#281] ボーナストークンロード完了 - StateChangedイベントを発火 (Plan={Plan})",
            currentState.CurrentPlan);

        // 同じ状態で発火（リスナーがIsFeatureAvailableを再チェックするトリガー）
        OnStateChanged(currentState, currentState, LicenseChangeReason.PromotionApplied);
    }

    /// <inheritdoc/>
    public void SetSessionToken(string? sessionToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_stateLock)
        {
            _sessionToken = sessionToken;
            _currentState = _currentState with { SessionId = sessionToken };
        }

        _logger.LogInformation(
            "[Issue #280+#281] セッショントークン設定: HasToken={HasToken}",
            !string.IsNullOrEmpty(sessionToken));
    }

    /// <summary>
    /// [Issue #275] 有効なプロモーションがある場合、状態にプロモーション設定をマージ
    /// [Issue #280+#281] プロモーションはボーナストークン付与のみ、プランは変更しない
    /// </summary>
    private LicenseState ApplyPromotionOverride(LicenseState incomingState)
    {
        // [Issue #280+#281] プロモーションでプランを上書きしない
        // プロモーションはボーナストークン付与のみで、プランは変更しない
        // 有効期限情報のみ参考としてログ出力

        if (_unifiedSettingsService is null)
        {
            return incomingState;
        }

        var promotionSettings = _unifiedSettingsService.GetPromotionSettings();
        if (!promotionSettings.IsCurrentlyActive() || !promotionSettings.PromotionPlanType.HasValue)
        {
            return incomingState;
        }

        var promotionPlan = (PlanType)promotionSettings.PromotionPlanType.Value;

        // ログのみ出力（プラン上書きはしない）
        // プランは変更せず、そのまま返す
        return incomingState;
    }

    /// <summary>
    /// UserIdをマスクするヘルパー（ログ用）
    /// </summary>
    private static string MaskUserId(string? userId)
    {
        if (string.IsNullOrEmpty(userId)) return "(empty)";
        if (userId.Length <= 4) return "****";
        return userId[..2] + "****" + userId[^2..];
    }

    #endregion

    #region Promotion Support (Issue #243)

    /// <summary>
    /// [Issue #258] プロモーション設定を_currentStateに適用するヘルパー
    /// [Gemini Review] DRY原則に従い共通ロジックを抽出
    /// [Issue #280+#281] プランは変更せず、有効期限とトークン使用量のみ適用
    /// </summary>
    private void ApplyPromotionToState(PlanType plan, DateTime expiresAt, string source)
    {
        // [Issue #275] トークン使用量も永続化設定から読み込む
        long tokenUsage = 0;
        if (_unifiedSettingsService is not null)
        {
            var promotionSettings = _unifiedSettingsService.GetPromotionSettings();
            tokenUsage = promotionSettings.MockTokenUsage;
        }
        if (tokenUsage == 0)
        {
            tokenUsage = _settings.MockTokenUsage;
        }

        // [Issue #280+#281] プロモーションはボーナストークン付与のみ、プランは変更しない
        // CurrentPlanは変更せず、有効期限（参考情報）とトークン使用量のみ適用
        _currentState = _currentState with
        {
            // CurrentPlan = plan,  // 削除: プランはボーナストークンに移行
            ExpirationDate = expiresAt,
            CloudAiTokensUsed = tokenUsage
        };
    }

    /// <summary>
    /// [Issue #258] 永続化されたプロモーション設定を読み込み、有効なら適用
    /// アプリ再起動時にプロモーション設定を反映するため
    /// </summary>
    private void ApplyPersistedPromotionIfValid()
    {
        // IUnifiedSettingsService経由でプロモーション設定を読み込む
        if (_unifiedSettingsService is not null)
        {
            var promotionSettings = _unifiedSettingsService.GetPromotionSettings();

            if (promotionSettings.IsCurrentlyActive() &&
                promotionSettings.PromotionPlanType.HasValue &&
                !string.IsNullOrEmpty(promotionSettings.PromotionExpiresAt) &&
                DateTime.TryParse(promotionSettings.PromotionExpiresAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiresAtUnified))
            {
                var promotionPlan = (PlanType)promotionSettings.PromotionPlanType.Value;
                ApplyPromotionToState(promotionPlan, expiresAtUnified, "Unified");
                return;
            }
        }

        // レガシー: LicenseSettings経由のプロモーションチェック（後方互換性）
        if (_settings.PromotionPlanType.HasValue &&
            !string.IsNullOrEmpty(_settings.PromotionExpiresAt) &&
            DateTime.TryParse(_settings.PromotionExpiresAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiresAt) &&
            expiresAt > DateTime.UtcNow)
        {
            var promotionPlan = (PlanType)_settings.PromotionPlanType.Value;
            ApplyPromotionToState(promotionPlan, expiresAt, "Legacy");
        }
    }

    /// <summary>
    /// 有効なプランを決定
    /// [Issue #280+#281] プロモーションはボーナストークン付与のみ、プランは変更しない
    /// </summary>
    private PlanType DetermineEffectivePlan()
    {
        // プロモーションはボーナストークン付与のみ、プランは変更しない
        return (PlanType)_settings.MockPlanType;
    }

    /// <summary>
    /// プロモーション適用イベントハンドラ
    /// </summary>
    private void OnPromotionApplied(PromotionAppliedEvent evt)
    {
        if (evt?.Promotion == null)
        {
            _logger.LogWarning("PromotionAppliedEvent received with null promotion");
            return;
        }

        _logger.LogInformation(
            "🎁 プロモーション適用イベント受信: BonusEquivalent={Plan}, ExpiresAt={ExpiresAt}",
            evt.AppliedPlan, evt.ExpiresAt);

        // [Issue #283] プロモーションコードはボーナストークン付与のみ
        // CurrentPlanはPatreonプランのまま維持（変更しない）
        // ボーナストークンはサーバー側で付与済み（grant_bonus_tokens RPC）
        // クライアント側でボーナストークンを再取得する

        _logger.LogInformation(
            "🎁 プロモーション適用: CurrentPlan維持={CurrentPlan}, ボーナストークン付与",
            _currentState.CurrentPlan);

        // ボーナストークンをサーバーから再取得（Fire-and-forget）
        _ = RefreshBonusTokensAsync();

        // 状態変更イベントを発火（UIに通知）
        lock (_stateLock)
        {
            var currentState = _currentState;
            OnStateChanged(currentState, currentState, LicenseChangeReason.PromotionApplied);
        }
    }

    /// <summary>
    /// ボーナストークンをサーバーから再取得
    /// [Issue #283] プロモーションコード適用後のトークン反映用
    /// </summary>
    private async Task RefreshBonusTokensAsync()
    {
        if (_bonusTokenService == null)
        {
            _logger.LogDebug("BonusTokenService is not available, skipping refresh");
            return;
        }

        try
        {
            var session = await _authService.GetCurrentSessionAsync(default).ConfigureAwait(false);
            if (session?.IsValid != true || string.IsNullOrEmpty(session.AccessToken))
            {
                _logger.LogDebug("No valid session for bonus token refresh");
                return;
            }

            _logger.LogInformation("🎁 ボーナストークン再取得開始...");
            var result = await _bonusTokenService.FetchFromServerAsync(session.AccessToken, default)
                .ConfigureAwait(false);

            if (result.Success)
            {
                _logger.LogInformation("🎁 ボーナストークン再取得成功: Total={Total}", result.TotalRemaining);

                // UIに通知
                NotifyBonusTokensLoaded();
            }
            else
            {
                _logger.LogWarning("🎁 ボーナストークン再取得失敗: Error={Error}", result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🎁 ボーナストークン再取得中にエラー");
        }
    }

    /// <summary>
    /// プロモーション解除イベントハンドラ
    /// </summary>
    private void OnPromotionRemoved(PromotionRemovedEvent evt)
    {
        _logger.LogInformation("🎁 プロモーション解除イベント受信: Reason={Reason}", evt?.Reason ?? "Unknown");

        lock (_stateLock)
        {
            var oldState = _currentState;
            var basePlan = (PlanType)_settings.MockPlanType;
            var newState = _currentState with
            {
                CurrentPlan = basePlan
            };

            _currentState = newState;
            OnStateChanged(oldState, newState, LicenseChangeReason.PromotionExpired);
        }
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

        // 5. Issue #243: イベント購読を解除
        _eventAggregator.Unsubscribe(_promotionAppliedProcessor);
        _eventAggregator.Unsubscribe(_promotionRemovedProcessor);

        _logger.LogDebug("LicenseManager disposed");
    }

    /// <summary>
    /// インラインイベントプロセッサ（ラムダ式をIEventProcessorにラップ）
    /// </summary>
    /// <remarks>
    /// Issue #243: LicenseManager内でのプロモーションイベント購読に使用
    /// ViewModelBase.csのパターンを踏襲
    /// </remarks>
    private sealed class InlineEventProcessor<TEvent> : IEventProcessor<TEvent>
        where TEvent : IEvent
    {
        private readonly Func<TEvent, Task> _handler;

        public InlineEventProcessor(Func<TEvent, Task> handler)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public int Priority => 100;
        public bool SynchronousExecution => false;
        public Task HandleAsync(TEvent eventData, CancellationToken cancellationToken = default) => _handler(eventData);
    }
}
