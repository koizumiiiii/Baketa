using Baketa.Core.Abstractions.License;
using Baketa.Core.Abstractions.Settings;
using Baketa.Core.License.Extensions;
using Baketa.Core.License.Models;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.License.Clients;

/// <summary>
/// 開発・テスト用モックライセンスAPIクライアント
/// 実際のサーバー通信なしでライセンス機能をテスト可能
/// </summary>
public sealed class MockLicenseApiClient : ILicenseApiClient
{
    private readonly ILogger<MockLicenseApiClient> _logger;
    private readonly LicenseSettings _settings;
    private readonly IUnifiedSettingsService _unifiedSettingsService;

    // モック状態
    private long _totalTokensConsumed;
    private readonly HashSet<string> _usedIdempotencyKeys = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    /// <inheritdoc/>
    public bool IsAvailable => true;

    /// <inheritdoc/>
    /// <remarks>
    /// モッククライアントはLicenseManager内で自動的に認証情報が設定されるため不要
    /// </remarks>
    public bool RequiresCredentials => false;

    /// <summary>
    /// MockLicenseApiClientを初期化
    /// </summary>
    public MockLicenseApiClient(
        ILogger<MockLicenseApiClient> logger,
        IOptions<LicenseSettings> settings,
        IUnifiedSettingsService unifiedSettingsService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        _unifiedSettingsService = unifiedSettingsService ?? throw new ArgumentNullException(nameof(unifiedSettingsService));

        // [Issue #258] 初期トークン使用量を永続化設定から読み込み（プロモーション設定優先）
        var promotionSettings = _unifiedSettingsService.GetPromotionSettings();
        _totalTokensConsumed = promotionSettings.MockTokenUsage > 0
            ? promotionSettings.MockTokenUsage
            : _settings.MockTokenUsage;

        _logger.LogInformation(
            "MockLicenseApiClient初期化: PlanType={Plan}, InitialTokenUsage={Usage}",
            (PlanType)_settings.MockPlanType,
            _totalTokensConsumed);
    }

    /// <inheritdoc/>
    public async Task<LicenseApiResponse?> GetLicenseStateAsync(
        string userId,
        string sessionToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionToken);

        // 非同期シミュレーション
        await Task.Delay(50, cancellationToken).ConfigureAwait(false);

        var planType = (PlanType)_settings.MockPlanType;
        var monthlyLimit = planType.GetMonthlyTokenLimit();

        long currentUsage;
        lock (_lock)
        {
            currentUsage = _totalTokensConsumed;
        }

        var state = new LicenseState
        {
            CurrentPlan = planType,
            UserId = userId,
            SessionId = sessionToken,
            ContractStartDate = DateTime.UtcNow.AddDays(-15), // 契約開始から15日経過
            ExpirationDate = DateTime.UtcNow.AddDays(15), // 残り15日
            CloudAiTokensUsed = currentUsage,
            IsCached = false,
            LastServerSync = DateTime.UtcNow
        };

        _logger.LogDebug(
            "Mock: ライセンス状態取得 UserId={UserId}, Plan={Plan}, TokensUsed={Used}/{Limit}",
            userId, planType, currentUsage, monthlyLimit);

        return LicenseApiResponse.CreateSuccess(state);
    }

    /// <inheritdoc/>
    public async Task<TokenConsumptionApiResponse> ConsumeTokensAsync(
        TokenConsumptionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 非同期シミュレーション
        await Task.Delay(30, cancellationToken).ConfigureAwait(false);

        var planType = (PlanType)_settings.MockPlanType;
        var monthlyLimit = planType.GetMonthlyTokenLimit();

        // [Issue #258] プランチェック削除
        // 理由:
        // 1. トークン消費記録は「課金追跡」であり「認可」ではない
        // 2. 認可は翻訳実行前にサーバー側で行われる
        // 3. Mockでプランチェックすると、LicenseManagerのプロモーション適用が反映されない

        TokenConsumptionApiResponse response;
        bool shouldPersist = false;
        long newTotal = 0;

        lock (_lock)
        {
            // Idempotency Keyチェック（二重消費防止）
            if (_usedIdempotencyKeys.Contains(request.IdempotencyKey))
            {
                _logger.LogDebug(
                    "Mock: IdempotencyKey重複 Key={Key}",
                    request.IdempotencyKey);

                response = new TokenConsumptionApiResponse
                {
                    Success = true,
                    WasIdempotent = true,
                    NewUsageTotal = _totalTokensConsumed,
                    RemainingTokens = monthlyLimit - _totalTokensConsumed
                };
            }
            // [Issue #258] クォータチェック
            // monthlyLimit == 0 の場合はスキップ（Freeプランでもプロモーション適用時は制限なし）
            else if (monthlyLimit > 0 && _totalTokensConsumed + request.TokenCount > monthlyLimit)
            {
                _logger.LogWarning(
                    "Mock: トークンクォータ超過 Current={Current}, Requested={Requested}, Limit={Limit}",
                    _totalTokensConsumed, request.TokenCount, monthlyLimit);

                response = new TokenConsumptionApiResponse
                {
                    Success = false,
                    ErrorCode = "QUOTA_EXCEEDED",
                    ErrorMessage = "今月のクラウドAI翻訳上限に達しました",
                    NewUsageTotal = _totalTokensConsumed,
                    RemainingTokens = monthlyLimit - _totalTokensConsumed
                };
            }
            else
            {
                // トークン消費を記録
                _totalTokensConsumed += request.TokenCount;
                _usedIdempotencyKeys.Add(request.IdempotencyKey);
                newTotal = _totalTokensConsumed;
                shouldPersist = true;

                _logger.LogDebug(
                    "Mock: トークン消費成功 Consumed={Consumed}, NewTotal={NewTotal}, Remaining={Remaining}",
                    request.TokenCount,
                    _totalTokensConsumed,
                    monthlyLimit - _totalTokensConsumed);

                response = new TokenConsumptionApiResponse
                {
                    Success = true,
                    WasIdempotent = false,
                    NewUsageTotal = _totalTokensConsumed,
                    RemainingTokens = monthlyLimit - _totalTokensConsumed
                };
            }
        }

        // [Issue #258] トークン消費成功後に永続化（lock外で非同期実行）
        // 永続化の責務はUnifiedSettingsServiceに委譲
        if (shouldPersist)
        {
            await _unifiedSettingsService.UpdateMockTokenUsageAsync(newTotal, cancellationToken)
                .ConfigureAwait(false);
        }

        return response;
    }

    /// <inheritdoc/>
    public async Task<SessionValidationResult> ValidateSessionAsync(
        string userId,
        string sessionToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionToken);

        // 非同期シミュレーション
        await Task.Delay(20, cancellationToken).ConfigureAwait(false);

        // モックモードでは常にセッション有効
        _logger.LogDebug(
            "Mock: セッション検証OK UserId={UserId}", userId);

        return SessionValidationResult.Valid;
    }

    /// <summary>
    /// モック状態をリセット（テスト用）
    /// </summary>
    public void ResetMockState()
    {
        lock (_lock)
        {
            _totalTokensConsumed = _settings.MockTokenUsage;
            _usedIdempotencyKeys.Clear();
        }

        _logger.LogInformation("Mock: 状態をリセットしました");
    }

    /// <summary>
    /// トークン使用量を設定（テスト用）
    /// </summary>
    public void SetTokenUsage(long usage)
    {
        lock (_lock)
        {
            _totalTokensConsumed = usage;
        }

        _logger.LogDebug("Mock: トークン使用量を設定 Usage={Usage}", usage);
    }
}
