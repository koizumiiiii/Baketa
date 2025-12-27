using Baketa.Core.Translation.Abstractions;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Cloud;

/// <summary>
/// Secondary Cloud AI翻訳エンジン（OpenAI - スタブ実装）
/// </summary>
/// <remarks>
/// Phase 2ではスタブ実装。将来的にOpenAI GPT-4 Vision APIを統合予定。
/// Primary (Gemini) 失敗時のフォールバック先として使用。
/// </remarks>
public sealed class SecondaryCloudTranslator : ICloudImageTranslator
{
    private readonly ILogger<SecondaryCloudTranslator> _logger;
    private bool _disposed;

    /// <inheritdoc />
    public string ProviderId => "secondary";

    /// <inheritdoc />
    public string DisplayName => "GPT-4 Vision (Secondary)";

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public SecondaryCloudTranslator(ILogger<SecondaryCloudTranslator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        // Phase 2ではSecondaryは常に利用不可
        _logger.LogDebug("Secondary翻訳エンジン（OpenAI）は未実装です");
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<ImageTranslationResponse> TranslateImageAsync(
        ImageTranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        _logger.LogWarning(
            "Secondary翻訳エンジン（OpenAI）は未実装です: RequestId={RequestId}",
            request.RequestId);

        var response = ImageTranslationResponse.Failure(
            request.RequestId,
            new TranslationErrorDetail
            {
                Code = TranslationErrorDetail.Codes.NotImplemented,
                Message = "Secondary翻訳エンジン（OpenAI GPT-4 Vision）は未実装です",
                IsRetryable = false
            },
            TimeSpan.Zero);

        return Task.FromResult(response);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        return ValueTask.CompletedTask;
    }
}
