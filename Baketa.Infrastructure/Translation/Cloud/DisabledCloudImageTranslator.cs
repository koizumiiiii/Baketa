using Baketa.Core.Translation.Abstractions;

namespace Baketa.Infrastructure.Translation.Cloud;

/// <summary>Preserves the cloud extension point without making network requests.</summary>
public sealed class DisabledCloudImageTranslator(string providerId) : ICloudImageTranslator
{
    public string ProviderId { get; } = providerId;
    public string DisplayName => ProviderId;

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task<ImageTranslationResponse> TranslateImageAsync(
        ImageTranslationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(ImageTranslationResponse.Failure(request.RequestId,
            new TranslationErrorDetail
            {
                Code = TranslationErrorDetail.Codes.CloudAllFailed,
                Message = "Cloud translation is disabled in the local edition.",
                IsRetryable = false
            }, TimeSpan.Zero));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
