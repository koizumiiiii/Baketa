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
        IOptions<CloudTranslationSettings> settings,
        ILogger<PrimaryCloudTranslator> logger)
    {
        _relayClient = relayClient ?? throw new ArgumentNullException(nameof(relayClient));
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
