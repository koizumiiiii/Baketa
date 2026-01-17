using Baketa.Core.Abstractions.Auth;
using Baketa.Core.Abstractions.License;
using Baketa.Core.License.Models;
using Baketa.Infrastructure.License.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.License;

/// <summary>
/// BonusSyncHostedService のユニットテスト
/// Issue #280+#281: ボーナストークン同期ホステッドサービス
/// [Gemini Review] Task.Delay排除、using文追加、リソース管理改善
/// </summary>
public sealed class BonusSyncHostedServiceTests
{
    private readonly Mock<IAuthService> _authServiceMock;
    private readonly Mock<IBonusTokenService> _bonusTokenServiceMock;
    private readonly Mock<ILicenseManager> _licenseManagerMock;
    private readonly Mock<IJwtTokenService> _jwtTokenServiceMock;
    private readonly Mock<ILogger<BonusSyncHostedService>> _loggerMock;

    public BonusSyncHostedServiceTests()
    {
        _authServiceMock = new Mock<IAuthService>();
        _bonusTokenServiceMock = new Mock<IBonusTokenService>();
        _licenseManagerMock = new Mock<ILicenseManager>();
        _jwtTokenServiceMock = new Mock<IJwtTokenService>();
        _loggerMock = new Mock<ILogger<BonusSyncHostedService>>();
    }

    [Fact]
    public void Constructor_WithNullAuthService_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new BonusSyncHostedService(null!, null, _bonusTokenServiceMock.Object, _licenseManagerMock.Object, null, null, _loggerMock.Object));
    }

    [Fact]
    public void Constructor_WithNullBonusTokenService_DoesNotThrow()
    {
        // Act - nullは許容（オプショナル依存）
        using var service = new BonusSyncHostedService(
            _authServiceMock.Object,
            null,
            null,
            _licenseManagerMock.Object,
            null,
            null,
            _loggerMock.Object);

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithNullLicenseManager_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new BonusSyncHostedService(
                _authServiceMock.Object,
                null,
                _bonusTokenServiceMock.Object,
                null!,
                null,
                null,
                _loggerMock.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new BonusSyncHostedService(
                _authServiceMock.Object,
                null,
                _bonusTokenServiceMock.Object,
                _licenseManagerMock.Object,
                null,
                null,
                null!));
    }

    [Fact]
    public void Constructor_SubscribesToAuthStatusChanged()
    {
        // Act
        using var service = new BonusSyncHostedService(
            _authServiceMock.Object,
            null,
            _bonusTokenServiceMock.Object,
            _licenseManagerMock.Object,
            null,
            null,
            _loggerMock.Object);

        // Assert - イベント購読確認
        _authServiceMock.VerifyAdd(a => a.AuthStatusChanged += It.IsAny<EventHandler<AuthStatusChangedEventArgs>>(), Times.Once);
    }

    [Fact]
    public void Dispose_UnsubscribesFromAuthStatusChanged()
    {
        // Arrange
        var service = new BonusSyncHostedService(
            _authServiceMock.Object,
            null,
            _bonusTokenServiceMock.Object,
            _licenseManagerMock.Object,
            null,
            null,
            _loggerMock.Object);

        // Act
        service.Dispose();

        // Assert - イベント購読解除確認
        _authServiceMock.VerifyRemove(a => a.AuthStatusChanged -= It.IsAny<EventHandler<AuthStatusChangedEventArgs>>(), Times.Once);
    }

    [Fact]
    public async Task OnAuthStatusChanged_WhenLoggedIn_FetchesBonusTokens()
    {
        // Arrange
        var session = new AuthSession(
            AccessToken: "test-token",
            RefreshToken: "refresh-token",
            ExpiresAt: DateTime.UtcNow.AddHours(1),
            User: new UserInfo("user-id", "test@example.com", "Test User"));

        // [Gemini Review] ManualResetEventSlimで非同期完了を待機
        // [Issue #280+#281] NotifyBonusTokensLoaded呼び出しで待機完了
        var mre = new ManualResetEventSlim(false);

        _authServiceMock.Setup(a => a.GetCurrentSessionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        _bonusTokenServiceMock.Setup(b => b.FetchFromServerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BonusSyncResult { Success = true, TotalRemaining = 1000 });

        // NotifyBonusTokensLoaded呼び出しで待機完了（FetchFromServerAsyncの後に呼ばれる）
        _licenseManagerMock.Setup(l => l.NotifyBonusTokensLoaded())
            .Callback(() => mre.Set());

        EventHandler<AuthStatusChangedEventArgs>? capturedHandler = null;
        _authServiceMock.SetupAdd(a => a.AuthStatusChanged += It.IsAny<EventHandler<AuthStatusChangedEventArgs>>())
            .Callback<EventHandler<AuthStatusChangedEventArgs>>(h => capturedHandler = h);

        using var service = new BonusSyncHostedService(
            _authServiceMock.Object,
            null,
            _bonusTokenServiceMock.Object,
            _licenseManagerMock.Object,
            null,
            null,
            _loggerMock.Object);

        // Act - ログインイベント発火
        capturedHandler?.Invoke(this, new AuthStatusChangedEventArgs(true, session.User));

        // Assert - [Gemini Review] Task.Delayを排除、ManualResetEventSlimで確実に待機
        Assert.True(mre.Wait(TimeSpan.FromSeconds(5)), "NotifyBonusTokensLoaded was not called within the timeout.");
        _bonusTokenServiceMock.Verify(b => b.FetchFromServerAsync("test-token", It.IsAny<CancellationToken>()), Times.Once);
        // [Issue #280+#281] NotifyBonusTokensLoadedが呼ばれたことを確認
        _licenseManagerMock.Verify(l => l.NotifyBonusTokensLoaded(), Times.Once);
    }

    [Fact]
    public async Task OnAuthStatusChanged_WhenLoggedOut_SyncsConsumption()
    {
        // Arrange
        var session = new AuthSession(
            AccessToken: "test-token",
            RefreshToken: "refresh-token",
            ExpiresAt: DateTime.UtcNow.AddHours(1),
            User: new UserInfo("user-id", "test@example.com", "Test User"));

        // [Gemini Review] ManualResetEventSlimで非同期完了を待機
        var mre = new ManualResetEventSlim(false);

        _authServiceMock.Setup(a => a.GetCurrentSessionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        _bonusTokenServiceMock.Setup(b => b.HasPendingSync).Returns(true);
        _bonusTokenServiceMock.Setup(b => b.SyncToServerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BonusSyncResult { Success = true, TotalRemaining = 500 })
            .Callback(() => mre.Set());

        EventHandler<AuthStatusChangedEventArgs>? capturedHandler = null;
        _authServiceMock.SetupAdd(a => a.AuthStatusChanged += It.IsAny<EventHandler<AuthStatusChangedEventArgs>>())
            .Callback<EventHandler<AuthStatusChangedEventArgs>>(h => capturedHandler = h);

        using var service = new BonusSyncHostedService(
            _authServiceMock.Object,
            null,
            _bonusTokenServiceMock.Object,
            _licenseManagerMock.Object,
            null,
            null,
            _loggerMock.Object);

        // Act - ログアウトイベント発火
        capturedHandler?.Invoke(this, new AuthStatusChangedEventArgs(false, null));

        // Assert - [Gemini Review] Task.Delayを排除、ManualResetEventSlimで確実に待機
        Assert.True(mre.Wait(TimeSpan.FromSeconds(5)), "SyncToServerAsync was not called within the timeout.");
        _bonusTokenServiceMock.Verify(b => b.SyncToServerAsync("test-token", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnAuthStatusChanged_WhenNoBonusTokenService_DoesNotThrow()
    {
        // Arrange - IBonusTokenServiceがnullの場合
        EventHandler<AuthStatusChangedEventArgs>? capturedHandler = null;
        _authServiceMock.SetupAdd(a => a.AuthStatusChanged += It.IsAny<EventHandler<AuthStatusChangedEventArgs>>())
            .Callback<EventHandler<AuthStatusChangedEventArgs>>(h => capturedHandler = h);

        using var service = new BonusSyncHostedService(
            _authServiceMock.Object,
            null,
            null,  // IBonusTokenServiceがnull
            _licenseManagerMock.Object,
            null,
            null,
            _loggerMock.Object);

        var session = new AuthSession(
            AccessToken: "test-token",
            RefreshToken: "refresh-token",
            ExpiresAt: DateTime.UtcNow.AddHours(1),
            User: new UserInfo("user-id", "test@example.com", "Test User"));

        // Act - ログインイベント発火（例外が発生しないことを確認）
        var exception = await Record.ExceptionAsync(async () =>
        {
            capturedHandler?.Invoke(this, new AuthStatusChangedEventArgs(true, session.User));
            await Task.Delay(100); // 短い待機
        });

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task OnAuthStatusChanged_WhenNotLoggedIn_DoesNotFetch()
    {
        // Arrange - 未認証の場合
        _authServiceMock.Setup(a => a.GetCurrentSessionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuthSession?)null);

        EventHandler<AuthStatusChangedEventArgs>? capturedHandler = null;
        _authServiceMock.SetupAdd(a => a.AuthStatusChanged += It.IsAny<EventHandler<AuthStatusChangedEventArgs>>())
            .Callback<EventHandler<AuthStatusChangedEventArgs>>(h => capturedHandler = h);

        using var service = new BonusSyncHostedService(
            _authServiceMock.Object,
            null,
            _bonusTokenServiceMock.Object,
            _licenseManagerMock.Object,
            null,
            null,
            _loggerMock.Object);

        // Act - ログインイベント発火（セッションがnullを返す）
        capturedHandler?.Invoke(this, new AuthStatusChangedEventArgs(true, new UserInfo("id", "email", "name")));
        await Task.Delay(3000); // LoginFetchDelay + α

        // Assert - FetchFromServerAsyncは呼ばれない
        _bonusTokenServiceMock.Verify(b => b.FetchFromServerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        // NotifyBonusTokensLoadedも呼ばれない
        _licenseManagerMock.Verify(l => l.NotifyBonusTokensLoaded(), Times.Never);
    }

    #region [Issue #299] JWT Priority Tests

    /// <summary>
    /// [Issue #299] JWTが有効な場合、JWTが最優先で使用されることを確認
    /// </summary>
    [Fact]
    public async Task OnAuthStatusChanged_WhenJwtValid_UsesJwtToken()
    {
        // Arrange
        const string jwtToken = "valid.jwt.token";
        var session = new AuthSession(
            AccessToken: "supabase-token",
            RefreshToken: "refresh-token",
            ExpiresAt: DateTime.UtcNow.AddHours(1),
            User: new UserInfo("user-id", "test@example.com", "Test User"));

        var mre = new ManualResetEventSlim(false);

        // JWT有効
        _jwtTokenServiceMock.Setup(j => j.HasValidToken).Returns(true);
        _jwtTokenServiceMock.Setup(j => j.GetAccessTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(jwtToken);

        _authServiceMock.Setup(a => a.GetCurrentSessionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        _bonusTokenServiceMock.Setup(b => b.FetchFromServerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BonusSyncResult { Success = true, TotalRemaining = 1000 });

        _licenseManagerMock.Setup(l => l.NotifyBonusTokensLoaded())
            .Callback(() => mre.Set());

        EventHandler<AuthStatusChangedEventArgs>? capturedHandler = null;
        _authServiceMock.SetupAdd(a => a.AuthStatusChanged += It.IsAny<EventHandler<AuthStatusChangedEventArgs>>())
            .Callback<EventHandler<AuthStatusChangedEventArgs>>(h => capturedHandler = h);

        using var service = new BonusSyncHostedService(
            _authServiceMock.Object,
            null,
            _bonusTokenServiceMock.Object,
            _licenseManagerMock.Object,
            null,
            _jwtTokenServiceMock.Object,
            _loggerMock.Object);

        // Act
        capturedHandler?.Invoke(this, new AuthStatusChangedEventArgs(true, session.User));

        // Assert
        Assert.True(mre.Wait(TimeSpan.FromSeconds(5)), "NotifyBonusTokensLoaded was not called within the timeout.");
        // JWTがLicenseManagerに設定されたことを確認
        _licenseManagerMock.Verify(l => l.SetSessionToken(jwtToken), Times.AtLeastOnce);
        _jwtTokenServiceMock.Verify(j => j.GetAccessTokenAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    /// <summary>
    /// [Issue #299] JWTが無効な場合、Supabaseトークンにフォールバックすることを確認
    /// </summary>
    [Fact]
    public async Task OnAuthStatusChanged_WhenJwtInvalid_FallsBackToSupabaseToken()
    {
        // Arrange
        const string supabaseToken = "supabase-access-token";
        var session = new AuthSession(
            AccessToken: supabaseToken,
            RefreshToken: "refresh-token",
            ExpiresAt: DateTime.UtcNow.AddHours(1),
            User: new UserInfo("user-id", "test@example.com", "Test User"));

        var mre = new ManualResetEventSlim(false);

        // JWT無効
        _jwtTokenServiceMock.Setup(j => j.HasValidToken).Returns(false);

        _authServiceMock.Setup(a => a.GetCurrentSessionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        _bonusTokenServiceMock.Setup(b => b.FetchFromServerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BonusSyncResult { Success = true, TotalRemaining = 1000 });

        _licenseManagerMock.Setup(l => l.NotifyBonusTokensLoaded())
            .Callback(() => mre.Set());

        EventHandler<AuthStatusChangedEventArgs>? capturedHandler = null;
        _authServiceMock.SetupAdd(a => a.AuthStatusChanged += It.IsAny<EventHandler<AuthStatusChangedEventArgs>>())
            .Callback<EventHandler<AuthStatusChangedEventArgs>>(h => capturedHandler = h);

        using var service = new BonusSyncHostedService(
            _authServiceMock.Object,
            null,
            _bonusTokenServiceMock.Object,
            _licenseManagerMock.Object,
            null,
            _jwtTokenServiceMock.Object,
            _loggerMock.Object);

        // Act
        capturedHandler?.Invoke(this, new AuthStatusChangedEventArgs(true, session.User));

        // Assert
        Assert.True(mre.Wait(TimeSpan.FromSeconds(5)), "NotifyBonusTokensLoaded was not called within the timeout.");
        // SupabaseトークンがLicenseManagerに設定されたことを確認
        _licenseManagerMock.Verify(l => l.SetSessionToken(supabaseToken), Times.AtLeastOnce);
        // JWTのGetAccessTokenAsyncは呼ばれない
        _jwtTokenServiceMock.Verify(j => j.GetAccessTokenAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// [Issue #299] JWT取得が例外をスローした場合、フォールバックすることを確認
    /// </summary>
    [Fact]
    public async Task OnAuthStatusChanged_WhenJwtThrows_FallsBackToSupabaseToken()
    {
        // Arrange
        const string supabaseToken = "supabase-access-token";
        var session = new AuthSession(
            AccessToken: supabaseToken,
            RefreshToken: "refresh-token",
            ExpiresAt: DateTime.UtcNow.AddHours(1),
            User: new UserInfo("user-id", "test@example.com", "Test User"));

        var mre = new ManualResetEventSlim(false);

        // JWT有効だが取得時に例外
        _jwtTokenServiceMock.Setup(j => j.HasValidToken).Returns(true);
        _jwtTokenServiceMock.Setup(j => j.GetAccessTokenAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("JWT refresh failed"));

        _authServiceMock.Setup(a => a.GetCurrentSessionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        _bonusTokenServiceMock.Setup(b => b.FetchFromServerAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BonusSyncResult { Success = true, TotalRemaining = 1000 });

        _licenseManagerMock.Setup(l => l.NotifyBonusTokensLoaded())
            .Callback(() => mre.Set());

        EventHandler<AuthStatusChangedEventArgs>? capturedHandler = null;
        _authServiceMock.SetupAdd(a => a.AuthStatusChanged += It.IsAny<EventHandler<AuthStatusChangedEventArgs>>())
            .Callback<EventHandler<AuthStatusChangedEventArgs>>(h => capturedHandler = h);

        using var service = new BonusSyncHostedService(
            _authServiceMock.Object,
            null,
            _bonusTokenServiceMock.Object,
            _licenseManagerMock.Object,
            null,
            _jwtTokenServiceMock.Object,
            _loggerMock.Object);

        // Act
        capturedHandler?.Invoke(this, new AuthStatusChangedEventArgs(true, session.User));

        // Assert
        Assert.True(mre.Wait(TimeSpan.FromSeconds(5)), "NotifyBonusTokensLoaded was not called within the timeout.");
        // SupabaseトークンがLicenseManagerに設定されたことを確認（フォールバック）
        _licenseManagerMock.Verify(l => l.SetSessionToken(supabaseToken), Times.AtLeastOnce);
    }

    #endregion
}
