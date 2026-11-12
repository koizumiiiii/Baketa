using Baketa.Core.Abstractions.Auth;
using Baketa.Infrastructure.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.Auth;

public class LocalCredentialMigrationServiceTests : IDisposable
{
    // Isolated test files stay inside the build output, never the user's real profile.
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "migration-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task StartAsync_RetiredFiles_DeletesOnlyRetiredDataOnceWithoutReadingTokens()
    {
        var storage = new Mock<ITokenStorage>(MockBehavior.Strict);
        storage.Setup(x => x.ClearTokensAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var directory = Path.Combine(_root, "license");
        Directory.CreateDirectory(directory);
        string[] retired = ["patreon-credentials.json", "license-cache.json", "pending-consumptions.json"];
        foreach (var file in retired)
            await File.WriteAllTextAsync(Path.Combine(directory, file), "retired");
        var keep = Path.Combine(directory, "unrelated.json");
        await File.WriteAllTextAsync(keep, "keep");
        var service = Create(storage.Object);

        await service.StartAsync(default);
        await Create(storage.Object).StartAsync(default);

        Assert.All(retired, file => Assert.False(File.Exists(Path.Combine(directory, file))));
        Assert.True(File.Exists(keep));
        storage.Verify(x => x.ClearTokensAsync(It.IsAny<CancellationToken>()), Times.Once);
        storage.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartAsync_CredentialDeletionFails_DoesNotBlockStartupAndRetries(bool throws)
    {
        var storage = new Mock<ITokenStorage>(MockBehavior.Strict);
        var setup = storage.SetupSequence(x => x.ClearTokensAsync(It.IsAny<CancellationToken>()));
        if (throws)
            setup.ThrowsAsync(new InvalidOperationException("Unavailable storage"));
        else
            setup.ReturnsAsync(false);
        setup.ReturnsAsync(true);

        await Create(storage.Object).StartAsync(default);
        Assert.False(File.Exists(Path.Combine(_root, "local-credentials-migrated.flag")));
        await Create(storage.Object).StartAsync(default);

        Assert.True(File.Exists(Path.Combine(_root, "local-credentials-migrated.flag")));
        storage.Verify(x => x.ClearTokensAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StartAsync_FileDeletionFails_RetriesAfterFailureIsResolved()
    {
        var storage = new Mock<ITokenStorage>();
        storage.Setup(x => x.ClearTokensAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var blockedPath = Path.Combine(_root, "license", "license-cache.json");
        Directory.CreateDirectory(blockedPath);

        await Create(storage.Object).StartAsync(default);
        Assert.False(File.Exists(Path.Combine(_root, "local-credentials-migrated.flag")));
        Directory.Delete(blockedPath);
        await Create(storage.Object).StartAsync(default);

        Assert.True(File.Exists(Path.Combine(_root, "local-credentials-migrated.flag")));
    }

    private LocalCredentialMigrationService Create(ITokenStorage storage) => new(storage, NullLogger<LocalCredentialMigrationService>.Instance, _root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}
