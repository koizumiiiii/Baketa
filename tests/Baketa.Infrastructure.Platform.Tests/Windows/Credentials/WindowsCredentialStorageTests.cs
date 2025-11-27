using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Auth;
using Baketa.Infrastructure.Platform.Windows.Credentials;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Platform.Tests.Windows.Credentials;

/// <summary>
/// WindowsCredentialStorage の統合テスト
/// 実際のWindows Credential Manager APIを使用してテスト
/// Windows環境でのみ実行される
/// </summary>
[Collection("WindowsCredentialStorage")]
public sealed class WindowsCredentialStorageTests : IAsyncLifetime
{
    private readonly Mock<ILogger<WindowsCredentialStorage>> _mockLogger;
    private readonly WindowsCredentialStorage _storage;

    // テスト用の固定トークン
    private const string TestAccessToken = "test-access-token-12345";
    private const string TestRefreshToken = "test-refresh-token-67890";

    public WindowsCredentialStorageTests()
    {
        _mockLogger = new Mock<ILogger<WindowsCredentialStorage>>();
        _storage = new WindowsCredentialStorage(_mockLogger.Object);
    }

    /// <summary>
    /// テスト前にクリーンアップ
    /// </summary>
    public async Task InitializeAsync()
    {
        // テスト前に既存のテストトークンをクリア
        await _storage.ClearTokensAsync();
    }

    /// <summary>
    /// テスト後にクリーンアップ
    /// </summary>
    public async Task DisposeAsync()
    {
        // テスト後にトークンをクリア
        await _storage.ClearTokensAsync();
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new WindowsCredentialStorage(null!));
    }

    [Fact]
    public void Constructor_WithValidLogger_InitializesCorrectly()
    {
        // Arrange & Act
        var storage = new WindowsCredentialStorage(_mockLogger.Object);

        // Assert
        storage.Should().NotBeNull();
    }

    #endregion

    #region StoreTokensAsync Tests

    [SkippableFact]
    public async Task StoreTokensAsync_WithValidTokens_ReturnsTrue()
    {
        // Skip on non-Windows
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Windows-only test");

        // Act
        var result = await _storage.StoreTokensAsync(TestAccessToken, TestRefreshToken);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(null, "refresh")]
    [InlineData("access", null)]
    [InlineData("", "refresh")]
    [InlineData("access", "")]
    [InlineData("   ", "refresh")]
    [InlineData("access", "   ")]
    public async Task StoreTokensAsync_WithInvalidTokens_ThrowsArgumentException(string? accessToken, string? refreshToken)
    {
        // Act & Assert
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            _storage.StoreTokensAsync(accessToken!, refreshToken!));
    }

    [SkippableFact]
    public async Task StoreTokensAsync_WithRealisticJwtTokens_ReturnsTrue()
    {
        // Skip on non-Windows
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Windows-only test");

        // Arrange - Realistic JWT token sizes (typically 500-1200 bytes)
        // Windows Credential Manager has a size limit (~2560 bytes per credential)
        var realisticAccessToken = new string('a', 500);  // ~500 bytes (typical JWT access token)
        var realisticRefreshToken = new string('r', 200); // ~200 bytes (typical refresh token)

        // Act
        var result = await _storage.StoreTokensAsync(realisticAccessToken, realisticRefreshToken);

        // Assert
        result.Should().BeTrue();
    }

    [SkippableFact]
    public async Task StoreTokensAsync_WithSpecialCharacters_ReturnsTrue()
    {
        // Skip on non-Windows
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Windows-only test");

        // Arrange - JWTには特殊文字が含まれる
        var accessTokenWithSpecial = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ";
        var refreshTokenWithSpecial = "dGhpcyBpcyBhIHRlc3QgcmVmcmVzaCB0b2tlbiB3aXRoIHNwZWNpYWwgY2hhcmFjdGVyczogQCMkJV4mKigpXys9";

        // Act
        var result = await _storage.StoreTokensAsync(accessTokenWithSpecial, refreshTokenWithSpecial);

        // Assert
        result.Should().BeTrue();
    }

    #endregion

    #region RetrieveTokensAsync Tests

    [SkippableFact]
    public async Task RetrieveTokensAsync_WhenTokensStored_ReturnsBothTokens()
    {
        // Skip on non-Windows
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Windows-only test");

        // Arrange
        await _storage.StoreTokensAsync(TestAccessToken, TestRefreshToken);

        // Act
        var result = await _storage.RetrieveTokensAsync();

        // Assert
        result.Should().NotBeNull();
        result!.Value.AccessToken.Should().Be(TestAccessToken);
        result.Value.RefreshToken.Should().Be(TestRefreshToken);
    }

    [SkippableFact]
    public async Task RetrieveTokensAsync_WhenNoTokensStored_ReturnsNull()
    {
        // Skip on non-Windows
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Windows-only test");

        // Arrange - クリア済みの状態

        // Act
        var result = await _storage.RetrieveTokensAsync();

        // Assert
        result.Should().BeNull();
    }

    [SkippableFact]
    public async Task RetrieveTokensAsync_WithUnicodeTokens_ReturnsCorrectValues()
    {
        // Skip on non-Windows
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Windows-only test");

        // Arrange - Unicode文字を含むトークン
        var unicodeAccessToken = "アクセストークン_テスト_日本語";
        var unicodeRefreshToken = "リフレッシュトークン_テスト_日本語";
        await _storage.StoreTokensAsync(unicodeAccessToken, unicodeRefreshToken);

        // Act
        var result = await _storage.RetrieveTokensAsync();

        // Assert
        result.Should().NotBeNull();
        result!.Value.AccessToken.Should().Be(unicodeAccessToken);
        result.Value.RefreshToken.Should().Be(unicodeRefreshToken);
    }

    #endregion

    #region ClearTokensAsync Tests

    [SkippableFact]
    public async Task ClearTokensAsync_WhenTokensExist_ReturnsTrue()
    {
        // Skip on non-Windows
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Windows-only test");

        // Arrange
        await _storage.StoreTokensAsync(TestAccessToken, TestRefreshToken);

        // Act
        var result = await _storage.ClearTokensAsync();

        // Assert
        result.Should().BeTrue();
    }

    [SkippableFact]
    public async Task ClearTokensAsync_WhenNoTokensExist_ReturnsTrue()
    {
        // Skip on non-Windows
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Windows-only test");

        // Act
        var result = await _storage.ClearTokensAsync();

        // Assert
        result.Should().BeTrue();
    }

    [SkippableFact]
    public async Task ClearTokensAsync_AfterClear_RetrieveReturnsNull()
    {
        // Skip on non-Windows
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Windows-only test");

        // Arrange
        await _storage.StoreTokensAsync(TestAccessToken, TestRefreshToken);

        // Act
        await _storage.ClearTokensAsync();
        var result = await _storage.RetrieveTokensAsync();

        // Assert
        result.Should().BeNull();
    }

    #endregion

    #region HasStoredTokensAsync Tests

    [SkippableFact]
    public async Task HasStoredTokensAsync_WhenTokensExist_ReturnsTrue()
    {
        // Skip on non-Windows
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Windows-only test");

        // Arrange
        await _storage.StoreTokensAsync(TestAccessToken, TestRefreshToken);

        // Act
        var result = await _storage.HasStoredTokensAsync();

        // Assert
        result.Should().BeTrue();
    }

    [SkippableFact]
    public async Task HasStoredTokensAsync_WhenNoTokensExist_ReturnsFalse()
    {
        // Skip on non-Windows
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Windows-only test");

        // Arrange - クリア済みの状態

        // Act
        var result = await _storage.HasStoredTokensAsync();

        // Assert
        result.Should().BeFalse();
    }

    [SkippableFact]
    public async Task HasStoredTokensAsync_AfterClear_ReturnsFalse()
    {
        // Skip on non-Windows
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Windows-only test");

        // Arrange
        await _storage.StoreTokensAsync(TestAccessToken, TestRefreshToken);
        await _storage.ClearTokensAsync();

        // Act
        var result = await _storage.HasStoredTokensAsync();

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Integration Tests (Full Workflow)

    [SkippableFact]
    public async Task FullWorkflow_StoreRetrieveClear_WorksCorrectly()
    {
        // Skip on non-Windows
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Windows-only test");

        // Step 1: Verify no tokens exist initially
        var hasTokensInitial = await _storage.HasStoredTokensAsync();
        hasTokensInitial.Should().BeFalse();

        // Step 2: Store tokens
        var storeResult = await _storage.StoreTokensAsync(TestAccessToken, TestRefreshToken);
        storeResult.Should().BeTrue();

        // Step 3: Verify tokens exist
        var hasTokensAfterStore = await _storage.HasStoredTokensAsync();
        hasTokensAfterStore.Should().BeTrue();

        // Step 4: Retrieve and verify tokens
        var retrievedTokens = await _storage.RetrieveTokensAsync();
        retrievedTokens.Should().NotBeNull();
        retrievedTokens!.Value.AccessToken.Should().Be(TestAccessToken);
        retrievedTokens.Value.RefreshToken.Should().Be(TestRefreshToken);

        // Step 5: Clear tokens
        var clearResult = await _storage.ClearTokensAsync();
        clearResult.Should().BeTrue();

        // Step 6: Verify tokens are cleared
        var hasTokensAfterClear = await _storage.HasStoredTokensAsync();
        hasTokensAfterClear.Should().BeFalse();

        var retrievedAfterClear = await _storage.RetrieveTokensAsync();
        retrievedAfterClear.Should().BeNull();
    }

    [SkippableFact]
    public async Task OverwriteTokens_WithNewValues_OverwritesSuccessfully()
    {
        // Skip on non-Windows
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Windows-only test");

        // Arrange - Store initial tokens
        await _storage.StoreTokensAsync(TestAccessToken, TestRefreshToken);

        // Act - Overwrite with new tokens
        var newAccessToken = "new-access-token";
        var newRefreshToken = "new-refresh-token";
        var storeResult = await _storage.StoreTokensAsync(newAccessToken, newRefreshToken);

        // Assert
        storeResult.Should().BeTrue();

        var retrievedTokens = await _storage.RetrieveTokensAsync();
        retrievedTokens.Should().NotBeNull();
        retrievedTokens!.Value.AccessToken.Should().Be(newAccessToken);
        retrievedTokens.Value.RefreshToken.Should().Be(newRefreshToken);
    }

    #endregion

    #region Concurrency Tests

    [SkippableFact]
    public async Task ConcurrentStoreAndRetrieve_HandlesGracefully()
    {
        // Skip on non-Windows
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Windows-only test");

        // Arrange - 50回のイテレーションで並行性をテスト
        const int iterations = 50;
        var tasks = new Task[iterations];

        // Act - 複数の同時アクセス
        for (int i = 0; i < iterations; i++)
        {
            var accessToken = $"access-{i}";
            var refreshToken = $"refresh-{i}";
            tasks[i] = Task.Run(async () =>
            {
                await _storage.StoreTokensAsync(accessToken, refreshToken);
                await _storage.RetrieveTokensAsync();
            });
        }

        // Assert - すべてのタスクが例外なく完了
        await Task.WhenAll(tasks);
        tasks.Should().AllSatisfy(t => t.IsCompletedSuccessfully.Should().BeTrue());
    }

    [SkippableFact]
    public async Task ConcurrentStoreRetrieveClear_MixedOperations_HandlesGracefully()
    {
        // Skip on non-Windows
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Windows-only test");

        // Arrange - 混合操作の並行テスト
        const int iterations = 30;
        var tasks = new List<Task>();

        // Act - Store, Retrieve, Clearの混合操作
        for (int i = 0; i < iterations; i++)
        {
            var index = i;
            // Store操作
            tasks.Add(Task.Run(async () =>
            {
                await _storage.StoreTokensAsync($"access-{index}", $"refresh-{index}");
            }));
            // Retrieve操作
            tasks.Add(Task.Run(async () =>
            {
                await _storage.RetrieveTokensAsync();
            }));
            // HasStoredTokens操作
            tasks.Add(Task.Run(async () =>
            {
                await _storage.HasStoredTokensAsync();
            }));
        }

        // Assert - すべてのタスクが例外なく完了
        await Task.WhenAll(tasks);
        tasks.Should().AllSatisfy(t => t.IsCompletedSuccessfully.Should().BeTrue());
    }

    #endregion

    #region Size Limit Tests

    [SkippableFact]
    public async Task StoreTokensAsync_WithMaxSizeTokens_ReturnsTrue()
    {
        // Skip on non-Windows
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Windows-only test");

        // Arrange - Windows Credential Managerの上限に近いサイズ（2560バイト制限）
        // 各トークンは約1200バイトまで安全
        var largeAccessToken = new string('A', 1200);
        var largeRefreshToken = new string('R', 1200);

        // Act
        var result = await _storage.StoreTokensAsync(largeAccessToken, largeRefreshToken);

        // Assert
        result.Should().BeTrue();

        // Verify retrieval
        var retrieved = await _storage.RetrieveTokensAsync();
        retrieved.Should().NotBeNull();
        retrieved!.Value.AccessToken.Should().Be(largeAccessToken);
        retrieved.Value.RefreshToken.Should().Be(largeRefreshToken);
    }

    [SkippableFact]
    public async Task StoreTokensAsync_WithTypicalJwtSize_ReturnsTrue()
    {
        // Skip on non-Windows
        Skip.IfNot(RuntimeInformation.IsOSPlatform(OSPlatform.Windows), "Windows-only test");

        // Arrange - 実際のJWTトークンサイズ（通常500-1500バイト）
        // ヘッダー(36) + "." + ペイロード(~500) + "." + 署名(43)
        var typicalJwtAccessToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9." +
            new string('a', 500) + "." +
            "SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";
        var typicalJwtRefreshToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9." +
            new string('r', 300) + "." +
            "refreshsignature123456789";

        // Act
        var result = await _storage.StoreTokensAsync(typicalJwtAccessToken, typicalJwtRefreshToken);

        // Assert
        result.Should().BeTrue();

        // Verify retrieval
        var retrieved = await _storage.RetrieveTokensAsync();
        retrieved.Should().NotBeNull();
        retrieved!.Value.AccessToken.Should().Be(typicalJwtAccessToken);
        retrieved.Value.RefreshToken.Should().Be(typicalJwtRefreshToken);
    }

    #endregion
}

/// <summary>
/// xUnit Test Collection for WindowsCredentialStorage
/// Sequential execution to avoid credential conflicts
/// </summary>
[CollectionDefinition("WindowsCredentialStorage", DisableParallelization = true)]
public class WindowsCredentialStorageCollection : ICollectionFixture<WindowsCredentialStorageFixture>
{
}

/// <summary>
/// Shared fixture for WindowsCredentialStorage tests
/// </summary>
public class WindowsCredentialStorageFixture : IAsyncLifetime
{
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;
}
