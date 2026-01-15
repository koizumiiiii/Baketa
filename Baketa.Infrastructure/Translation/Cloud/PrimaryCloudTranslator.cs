using Baketa.Core.Abstractions.License;
using Baketa.Core.Settings;
using Baketa.Core.Translation.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.Translation.Cloud;

/// <summary>
/// Primary Cloud AI翻訳エンジン（Gemini）
/// </summary>
/// <remarks>
/// - Relay Server経由でGemini APIを呼び出し
/// - Pro/Premiaプラン専用
/// - 画像からテキストを検出・翻訳
/// </remarks>
public sealed class PrimaryCloudTranslator : ICloudImageTranslator
{
    private readonly RelayServerClient _relayClient;
    private readonly ITokenConsumptionTracker _tokenTracker;
    private readonly ILicenseManager _licenseManager;
    private readonly ILogger<PrimaryCloudTranslator> _logger;
    private readonly CloudTranslationSettings _settings;

    private bool _disposed;
    private bool? _lastAvailableState;

    /// <inheritdoc />
    public string ProviderId => "primary";

    /// <inheritdoc />
    public string DisplayName => "Gemini (Primary)";

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public PrimaryCloudTranslator(
        RelayServerClient relayClient,
        ITokenConsumptionTracker tokenTracker,
        ILicenseManager licenseManager,
        IOptions<CloudTranslationSettings> settings,
        ILogger<PrimaryCloudTranslator> logger)
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
                _logger.LogDebug("Cloud翻訳が無効化されています");
                _lastAvailableState = false;
                return false;
            }

            var isHealthy = await _relayClient.IsHealthyAsync(cancellationToken).ConfigureAwait(false);
            _lastAvailableState = isHealthy;

            if (!isHealthy)
            {
                _logger.LogWarning("Relay Serverが利用できません");
            }

            return isHealthy;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "可用性チェック中にエラー");
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
            "Primary翻訳開始: RequestId={RequestId}, Target={Target}",
            request.RequestId,
            request.TargetLanguage);

        try
        {
            var response = await _relayClient.TranslateImageAsync(
                request,
                request.SessionToken,
                cancellationToken).ConfigureAwait(false);

            if (response.IsSuccess)
            {
                _logger.LogInformation(
                    "Primary翻訳成功: RequestId={RequestId}, Tokens={Tokens}",
                    request.RequestId,
                    response.TokenUsage?.TotalTokens ?? 0);

                // [Issue #296] サーバーの月間使用量でローカルを同期（優先）
                if (response.MonthlyUsage is not null)
                {
                    try
                    {
                        await _tokenTracker.SyncFromServerAsync(
                            response.MonthlyUsage,
                            cancellationToken).ConfigureAwait(false);

                        // [Issue #296] LicenseManagerの状態も同期（警告通知トリガー）
                        _licenseManager.SyncTokenUsage(response.MonthlyUsage.TokensUsed);

                        _logger.LogDebug(
                            "[Issue #296] サーバーからローカルにトークン使用量を同期: YearMonth={YearMonth}, TokensUsed={TokensUsed}",
                            response.MonthlyUsage.YearMonth,
                            response.MonthlyUsage.TokensUsed);
                    }
                    catch (Exception ex)
                    {
                        // 同期失敗は翻訳結果に影響させない
                        _logger.LogWarning(ex, "[Issue #296] サーバー同期に失敗（翻訳は成功）");
                    }
                }
                else
                {
                    // サーバーから月間使用量が返されなかった場合はローカルに加算記録
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

                            _logger.LogDebug(
                                "[Issue #296] トークン消費記録（フォールバック）: Provider={Provider}, Tokens={Tokens}",
                                ProviderId, totalTokens);
                        }
                        catch (Exception ex)
                        {
                            // トークン記録の失敗は翻訳結果に影響させない
                            _logger.LogWarning(ex, "[Issue #296] トークン消費記録に失敗（翻訳は成功）");
                        }
                    }
                }
            }
            else
            {
                _logger.LogWarning(
                    "Primary翻訳失敗: RequestId={RequestId}, Error={Error}",
                    request.RequestId,
                    response.Error?.Message);
            }

            return response;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Primary翻訳がキャンセルされました: RequestId={RequestId}", request.RequestId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Primary翻訳中に予期せぬエラー: RequestId={RequestId}", request.RequestId);

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

        // RelayServerClientはDIで管理されるため、ここではDisposeしない
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
