using Baketa.Core.Abstractions.License;
using Baketa.Core.Settings;
using Baketa.Core.Translation.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.Translation.Cloud;

/// <summary>
/// Secondary Cloud AI翻訳エンジン（OpenAI GPT-4 Vision）
/// </summary>
/// <remarks>
/// - Relay Server経由でOpenAI APIを呼び出し
/// - Primary (Gemini) 失敗時のフォールバック先として使用
/// - Pro/Premiaプラン専用
/// </remarks>
public sealed class SecondaryCloudTranslator : ICloudImageTranslator
{
    private readonly RelayServerClient _relayClient;
    private readonly ITokenConsumptionTracker _tokenTracker;
    private readonly ILicenseManager _licenseManager;
    private readonly ILogger<SecondaryCloudTranslator> _logger;
    private readonly CloudTranslationSettings _settings;

    private bool _disposed;
    private bool? _lastAvailableState;

    /// <inheritdoc />
    public string ProviderId => "secondary";

    /// <inheritdoc />
    public string DisplayName => "GPT-4 Vision (Secondary)";

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public SecondaryCloudTranslator(
        RelayServerClient relayClient,
        ITokenConsumptionTracker tokenTracker,
        ILicenseManager licenseManager,
        IOptions<CloudTranslationSettings> settings,
        ILogger<SecondaryCloudTranslator> logger)
    {
        _relayClient = relayClient ?? throw new ArgumentNullException(nameof(relayClient));
        _tokenTracker = tokenTracker ?? throw new ArgumentNullException(nameof(tokenTracker));
        _licenseManager = licenseManager ?? throw new ArgumentNullException(nameof(licenseManager));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_settings.Enabled)
            {
                _lastAvailableState = false;
                return false;
            }

            var isHealthy = await _relayClient.IsHealthyAsync(cancellationToken).ConfigureAwait(false);
            _lastAvailableState = isHealthy;

            return isHealthy;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Secondary可用性チェック中にエラー");
            _lastAvailableState = false;
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<ImageTranslationResponse> TranslateImageAsync(
        ImageTranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_settings.Enabled)
        {
            return ImageTranslationResponse.Failure(
                request.RequestId,
                new TranslationErrorDetail
                {
                    Code = TranslationErrorDetail.Codes.NotImplemented,
                    Message = "Cloud翻訳は無効化されています",
                    IsRetryable = false
                },
                TimeSpan.Zero);
        }

        _logger.LogDebug(
            "Secondary翻訳開始: RequestId={RequestId}, Target={Target}, Provider={Provider}",
            request.RequestId,
            request.TargetLanguage,
            _settings.SecondaryProviderId);

        try
        {
            // SecondaryプロバイダーID（openai）を指定して翻訳
            var response = await _relayClient.TranslateImageAsync(
                request,
                request.SessionToken,
                _settings.SecondaryProviderId,
                cancellationToken).ConfigureAwait(false);

            if (response.IsSuccess)
            {
                _logger.LogInformation(
                    "Secondary翻訳成功: RequestId={RequestId}, Provider={Provider}, Tokens={Tokens}",
                    request.RequestId,
                    _settings.SecondaryProviderId,
                    response.TokenUsage?.TotalTokens ?? 0);

                // [Issue #296] サーバーの月間使用量でローカルを同期
                if (response.MonthlyUsage is not null)
                {
                    try
                    {
                        await _tokenTracker.SyncFromServerAsync(
                            response.MonthlyUsage,
                            cancellationToken).ConfigureAwait(false);

                        _licenseManager.SyncMonthlyUsageFromServer(response.MonthlyUsage);

                        _logger.LogDebug(
                            "[Issue #296] サーバーからローカルにトークン使用量を同期: YearMonth={YearMonth}, Used={TokensUsed}",
                            response.MonthlyUsage.YearMonth,
                            response.MonthlyUsage.TokensUsed);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[Issue #296] サーバー同期に失敗（翻訳は成功）");
                    }
                }
                else
                {
                    // フォールバック：ローカルに加算記録
                    var totalTokens = response.TokenUsage?.TotalTokens ?? 0;
                    if (totalTokens > 0)
                    {
                        try
                        {
                            await _tokenTracker.RecordUsageAsync(
                                totalTokens,
                                ProviderId,
                                TokenUsageType.Total,
                                cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "[Issue #296] トークン消費記録に失敗（翻訳は成功）");
                        }
                    }
                }
            }
            else
            {
                _logger.LogWarning(
                    "Secondary翻訳失敗: RequestId={RequestId}, Error={Error}",
                    request.RequestId,
                    response.Error?.Message);

                // QUOTA_EXCEEDEDエラー処理
                if (response.Error?.Code == TranslationErrorDetail.Codes.QuotaExceeded &&
                    response.MonthlyUsage is not null)
                {
                    try
                    {
                        _licenseManager.SyncMonthlyUsageFromServer(response.MonthlyUsage);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[Issue #296] QUOTA_EXCEEDED時のLicenseManager更新に失敗");
                    }
                }
            }

            return response;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Secondary翻訳がキャンセルされました: RequestId={RequestId}", request.RequestId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Secondary翻訳中に予期せぬエラー: RequestId={RequestId}", request.RequestId);

            return ImageTranslationResponse.Failure(
                request.RequestId,
                new TranslationErrorDetail
                {
                    Code = TranslationErrorDetail.Codes.InternalError,
                    Message = $"内部エラー: {ex.Message}",
                    IsRetryable = false
                },
                TimeSpan.Zero);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await Task.CompletedTask.ConfigureAwait(false);
    }
}
