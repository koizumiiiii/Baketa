using Baketa.Core.Translation.Abstractions;
using Baketa.Infrastructure.Translation.Cloud;
using Xunit;

namespace Baketa.Infrastructure.Tests.Translation.Cloud;

public sealed class DisabledCloudImageTranslatorTests
{
    [Theory]
    [InlineData("primary")]
    [InlineData("secondary")]
    public async Task TranslateImageAsync_LocalEdition_ReturnsUnavailableWithoutRetry(string provider)
    {
        // Arrange
        await using var translator = new DisabledCloudImageTranslator(provider);
        var request = ImageTranslationRequest.FromBytes([1, 2, 3], "ja", "legacy-session");

        // Act
        var available = await translator.IsAvailableAsync();
        var result = await translator.TranslateImageAsync(request);

        // Assert
        Assert.False(available);
        Assert.False(result.IsSuccess);
        Assert.False(result.Error!.IsRetryable);
        Assert.Equal(request.RequestId, result.RequestId);
    }
}
