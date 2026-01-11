using Baketa.Core.Abstractions.License;
using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.License.Events;
using Baketa.Core.License.Models;
using Baketa.Core.Settings;
using Baketa.Infrastructure.Translation.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.Translation.Services;

/// <summary>
/// CloudTranslationAvailabilityServiceのユニットテスト
/// Issue #273: Cloud翻訳設定の自動同期システム
/// </summary>
public class CloudTranslationAvailabilityServiceTests : IDisposable
{
    private readonly Mock<ILicenseManager> _mockLicenseManager;
    private readonly Mock<IUnifiedSettingsService> _mockUnifiedSettingsService;
    private readonly Mock<ILogger<CloudTranslationAvailabilityService>> _mockLogger;
    private TranslationSettings _translationSettings;
    private CloudTranslationAvailabilityService? _service;

    public CloudTranslationAvailabilityServiceTests()
    {
        _mockLicenseManager = new Mock<ILicenseManager>();
        _mockUnifiedSettingsService = new Mock<IUnifiedSettingsService>();
        _mockLogger = new Mock<ILogger<CloudTranslationAvailabilityService>>();

        _translationSettings = new TranslationSettings
        {
            EnableCloudAiTranslation = false,
            UseLocalEngine = true,  // [Issue #280+#281] ローカル翻訳がデフォルト
            DefaultEngine = TranslationEngine.NLLB200
        };

        // GetTranslationSettingsのモック設定
        _mockUnifiedSettingsService.Setup(x => x.GetTranslationSettings()).Returns(() => _translationSettings);

        // デフォルト: Freeプラン（Cloud翻訳未対応）
        _mockLicenseManager.Setup(x => x.IsFeatureAvailable(FeatureType.CloudAiTranslation)).Returns(false);
    }

    public void Dispose()
    {
        _service?.Dispose();
    }

    private CloudTranslationAvailabilityService CreateService()
    {
        _service = new CloudTranslationAvailabilityService(
            _mockLicenseManager.Object,
            _mockUnifiedSettingsService.Object,
            _mockLogger.Object);
        return _service;
    }

    /// <summary>
    /// SettingsChangedイベントを発火させるヘルパーメソッド
    /// </summary>
    private void RaiseSettingsChanged(TranslationSettings newSettings)
    {
        _translationSettings = newSettings;
        _mockUnifiedSettingsService.Raise(
            x => x.SettingsChanged += null,
            _mockUnifiedSettingsService.Object,
            new SettingsChangedEventArgs("Translation", SettingsType.Translation));
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithFreeUserAndCloudDisabled_InitializesCorrectly()
    {
        // Arrange
        _mockLicenseManager.Setup(x => x.IsFeatureAvailable(FeatureType.CloudAiTranslation)).Returns(false);
        _translationSettings.EnableCloudAiTranslation = false;
        _translationSettings.UseLocalEngine = true;  // [Issue #280+#281]

        // Act
        var service = CreateService();

        // Assert
        Assert.False(service.IsEntitled);
        Assert.False(service.IsPreferred);
        Assert.False(service.IsEffectivelyEnabled);
    }

    [Fact]
    public void Constructor_WithProUserAndCloudEnabled_InitializesCorrectly()
    {
        // Arrange
        _mockLicenseManager.Setup(x => x.IsFeatureAvailable(FeatureType.CloudAiTranslation)).Returns(true);
        _translationSettings.EnableCloudAiTranslation = true;
        _translationSettings.UseLocalEngine = false;  // [Issue #280+#281]

        // Act
        var service = CreateService();

        // Assert
        Assert.True(service.IsEntitled);
        Assert.True(service.IsPreferred);
        Assert.True(service.IsEffectivelyEnabled);
    }

    [Fact]
    public void Constructor_WithProUserButCloudDisabled_IsEffectivelyDisabled()
    {
        // Arrange
        _mockLicenseManager.Setup(x => x.IsFeatureAvailable(FeatureType.CloudAiTranslation)).Returns(true);
        _translationSettings.EnableCloudAiTranslation = false;
        _translationSettings.UseLocalEngine = true;  // [Issue #280+#281]

        // Act
        var service = CreateService();

        // Assert
        Assert.True(service.IsEntitled);
        Assert.False(service.IsPreferred);
        Assert.False(service.IsEffectivelyEnabled);
    }

    [Fact]
    public void Constructor_WithFreeUserButCloudEnabled_IsEffectivelyDisabled()
    {
        // Arrange
        _mockLicenseManager.Setup(x => x.IsFeatureAvailable(FeatureType.CloudAiTranslation)).Returns(false);
        _translationSettings.EnableCloudAiTranslation = true;
        _translationSettings.UseLocalEngine = false;  // [Issue #280+#281]

        // Act
        var service = CreateService();

        // Assert
        Assert.False(service.IsEntitled);
        Assert.True(service.IsPreferred);
        Assert.False(service.IsEffectivelyEnabled);
    }

    [Fact]
    public void Constructor_WithNullLicenseManager_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new CloudTranslationAvailabilityService(
                null!,
                _mockUnifiedSettingsService.Object,
                _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullUnifiedSettingsService_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new CloudTranslationAvailabilityService(
                _mockLicenseManager.Object,
                null!,
                _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new CloudTranslationAvailabilityService(
                _mockLicenseManager.Object,
                _mockUnifiedSettingsService.Object,
                null!));
    }

    #endregion

    #region RefreshStateAsync Tests

    [Fact]
    public async Task RefreshStateAsync_ShouldCallLicenseManagerRefresh()
    {
        // Arrange
        var service = CreateService();

        // Act
        await service.RefreshStateAsync();

        // Assert
        _mockLicenseManager.Verify(x => x.RefreshStateAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshStateAsync_WhenEntitlementChanges_FiresEvent()
    {
        // Arrange
        var service = CreateService();
        var eventFired = false;
        CloudTranslationAvailabilityChangedEventArgs? receivedArgs = null;

        service.AvailabilityChanged += (sender, args) =>
        {
            eventFired = true;
            receivedArgs = args;
        };

        // シミュレート: RefreshStateAsync後にEntitledがtrueになる
        _translationSettings.EnableCloudAiTranslation = true;
        _translationSettings.UseLocalEngine = false;  // [Issue #280+#281]
        _mockLicenseManager.Setup(x => x.IsFeatureAvailable(FeatureType.CloudAiTranslation)).Returns(true);

        // Act
        await service.RefreshStateAsync();

        // Assert
        Assert.True(eventFired);
        Assert.NotNull(receivedArgs);
        Assert.False(receivedArgs.WasEnabled);
        Assert.True(receivedArgs.IsEnabled);
        Assert.Equal(CloudTranslationChangeReason.Initialization, receivedArgs.Reason);
    }

    #endregion

    #region SetPreferredAsync Tests

    [Fact]
    public async Task SetPreferredAsync_WhenChangingToTrue_UpdatesSettings()
    {
        // Arrange
        _mockLicenseManager.Setup(x => x.IsFeatureAvailable(FeatureType.CloudAiTranslation)).Returns(true);
        var service = CreateService();

        // Act
        await service.SetPreferredAsync(true);

        // Assert - [Issue #280+#281] UseLocalEngine も確認
        _mockUnifiedSettingsService.Verify(
            x => x.UpdateTranslationSettingsAsync(
                It.Is<TranslationSettings>(s =>
                    s.EnableCloudAiTranslation == true &&
                    s.UseLocalEngine == false &&
                    s.DefaultEngine == TranslationEngine.Gemini),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SetPreferredAsync_WhenChangingToFalse_SetsNLLB200Engine()
    {
        // Arrange
        _mockLicenseManager.Setup(x => x.IsFeatureAvailable(FeatureType.CloudAiTranslation)).Returns(true);
        _translationSettings.EnableCloudAiTranslation = true;
        _translationSettings.UseLocalEngine = false;  // [Issue #280+#281]
        var service = CreateService();

        // Act
        await service.SetPreferredAsync(false);

        // Assert - [Issue #280+#281] UseLocalEngine も確認
        _mockUnifiedSettingsService.Verify(
            x => x.UpdateTranslationSettingsAsync(
                It.Is<TranslationSettings>(s =>
                    s.EnableCloudAiTranslation == false &&
                    s.UseLocalEngine == true &&
                    s.DefaultEngine == TranslationEngine.NLLB200),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SetPreferredAsync_WhenValueUnchanged_DoesNotUpdateSettings()
    {
        // Arrange
        var service = CreateService();

        // Act
        await service.SetPreferredAsync(false); // 既にfalse

        // Assert
        _mockUnifiedSettingsService.Verify(
            x => x.UpdateTranslationSettingsAsync(It.IsAny<TranslationSettings>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SetPreferredAsync_WhenEntitledUserEnablesCloud_FiresEvent()
    {
        // Arrange
        _mockLicenseManager.Setup(x => x.IsFeatureAvailable(FeatureType.CloudAiTranslation)).Returns(true);
        var service = CreateService();
        var eventFired = false;
        CloudTranslationAvailabilityChangedEventArgs? receivedArgs = null;

        service.AvailabilityChanged += (sender, args) =>
        {
            eventFired = true;
            receivedArgs = args;
        };

        // Act
        await service.SetPreferredAsync(true);

        // Assert
        Assert.True(eventFired);
        Assert.NotNull(receivedArgs);
        Assert.False(receivedArgs.WasEnabled);
        Assert.True(receivedArgs.IsEnabled);
        Assert.Equal(CloudTranslationChangeReason.UserPreferenceChanged, receivedArgs.Reason);
    }

    #endregion

    #region License State Change Tests

    [Fact]
    public void OnLicenseStateChanged_WhenUpgradedToPro_AutoEnablesCloudTranslation()
    {
        // Arrange
        var service = CreateService();

        var oldState = new LicenseState { CurrentPlan = PlanType.Free };
        var newState = new LicenseState { CurrentPlan = PlanType.Pro };
        var eventArgs = new LicenseStateChangedEventArgs(oldState, newState, LicenseChangeReason.PlanUpgrade);

        // シミュレート: アップグレード後にEntitledがtrueになる
        _mockLicenseManager.Setup(x => x.IsFeatureAvailable(FeatureType.CloudAiTranslation)).Returns(true);

        // Act
        _mockLicenseManager.Raise(x => x.StateChanged += null, _mockLicenseManager.Object, eventArgs);

        // Assert: 自動的にSetPreferredAsync(true)が呼ばれる
        _mockUnifiedSettingsService.Verify(
            x => x.UpdateTranslationSettingsAsync(
                It.Is<TranslationSettings>(s => s.EnableCloudAiTranslation == true),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void OnLicenseStateChanged_WhenDowngradedToFree_DoesNotAutoDisable()
    {
        // Arrange
        _mockLicenseManager.Setup(x => x.IsFeatureAvailable(FeatureType.CloudAiTranslation)).Returns(true);
        _translationSettings.EnableCloudAiTranslation = true;
        _translationSettings.UseLocalEngine = false;  // [Issue #280+#281]
        var service = CreateService();

        var oldState = new LicenseState { CurrentPlan = PlanType.Pro };
        var newState = new LicenseState { CurrentPlan = PlanType.Free };
        var eventArgs = new LicenseStateChangedEventArgs(oldState, newState, LicenseChangeReason.PlanDowngrade);

        // シミュレート: ダウングレード後にEntitledがfalseになる
        _mockLicenseManager.Setup(x => x.IsFeatureAvailable(FeatureType.CloudAiTranslation)).Returns(false);

        CloudTranslationAvailabilityChangedEventArgs? receivedArgs = null;
        service.AvailabilityChanged += (sender, args) => receivedArgs = args;

        // Act
        _mockLicenseManager.Raise(x => x.StateChanged += null, _mockLicenseManager.Object, eventArgs);

        // Assert: イベントは発火するが、設定は自動的に無効化されない（ユーザーの選択を尊重）
        Assert.NotNull(receivedArgs);
        Assert.True(receivedArgs.WasEnabled);
        Assert.False(receivedArgs.IsEnabled);
        Assert.Equal(CloudTranslationChangeReason.PlanDowngrade, receivedArgs.Reason);

        // SetPreferredAsync(false)は呼ばれない（ダウングレード時は自動無効化しない）
        _mockUnifiedSettingsService.Verify(
            x => x.UpdateTranslationSettingsAsync(
                It.Is<TranslationSettings>(s => s.EnableCloudAiTranslation == false),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void OnLicenseStateChanged_WhenPromotionApplied_AutoEnablesAndFiresEvent()
    {
        // Arrange
        var service = CreateService();

        var oldState = new LicenseState { CurrentPlan = PlanType.Free };
        var newState = new LicenseState { CurrentPlan = PlanType.Pro };
        var eventArgs = new LicenseStateChangedEventArgs(oldState, newState, LicenseChangeReason.PromotionApplied);

        _mockLicenseManager.Setup(x => x.IsFeatureAvailable(FeatureType.CloudAiTranslation)).Returns(true);

        CloudTranslationAvailabilityChangedEventArgs? receivedArgs = null;
        service.AvailabilityChanged += (sender, args) => receivedArgs = args;

        // Act
        _mockLicenseManager.Raise(x => x.StateChanged += null, _mockLicenseManager.Object, eventArgs);

        // Assert: プロモーション適用時、自動的にCloud翻訳が有効化される
        // 最終的なイベント理由は UserPreferenceChanged（SetPreferredAsync経由での有効化のため）
        Assert.NotNull(receivedArgs);
        Assert.True(receivedArgs.IsEnabled);
        Assert.True(receivedArgs.IsEntitled);
        Assert.True(receivedArgs.IsPreferred);
        // 注: 理由は UserPreferenceChanged になる（自動有効化はSetPreferredAsync経由）
        Assert.Equal(CloudTranslationChangeReason.UserPreferenceChanged, receivedArgs.Reason);
    }

    #endregion

    #region Settings Change Tests

    [Fact]
    public void OnSettingsChanged_WhenPreferenceChanges_FiresEvent()
    {
        // Arrange
        _mockLicenseManager.Setup(x => x.IsFeatureAvailable(FeatureType.CloudAiTranslation)).Returns(true);
        var service = CreateService();

        CloudTranslationAvailabilityChangedEventArgs? receivedArgs = null;
        service.AvailabilityChanged += (sender, args) => receivedArgs = args;

        // Act: 設定変更をシミュレート - [Issue #280+#281] UseLocalEngineで判定
        var newSettings = new TranslationSettings { EnableCloudAiTranslation = true, UseLocalEngine = false };
        RaiseSettingsChanged(newSettings);

        // Assert
        Assert.NotNull(receivedArgs);
        Assert.False(receivedArgs.WasEnabled);
        Assert.True(receivedArgs.IsEnabled);
        Assert.Equal(CloudTranslationChangeReason.UserPreferenceChanged, receivedArgs.Reason);
    }

    [Fact]
    public void OnSettingsChanged_WhenPreferenceUnchanged_DoesNotFireEvent()
    {
        // Arrange
        var service = CreateService();

        var eventFired = false;
        service.AvailabilityChanged += (sender, args) => eventFired = true;

        // Act: 同じ設定値で変更通知 - [Issue #280+#281] UseLocalEngine = true (デフォルト)
        var sameSettings = new TranslationSettings { EnableCloudAiTranslation = false, UseLocalEngine = true };
        RaiseSettingsChanged(sameSettings);

        // Assert
        Assert.False(eventFired);
    }

    [Fact]
    public void OnSettingsChanged_WhenNonTranslationSettings_DoesNotProcess()
    {
        // Arrange
        _mockLicenseManager.Setup(x => x.IsFeatureAvailable(FeatureType.CloudAiTranslation)).Returns(true);
        _translationSettings.EnableCloudAiTranslation = true;
        _translationSettings.UseLocalEngine = false;  // [Issue #280+#281]
        var service = CreateService();

        var eventFired = false;
        service.AvailabilityChanged += (sender, args) => eventFired = true;

        // Act: OCR設定変更（Translation以外）
        _mockUnifiedSettingsService.Raise(
            x => x.SettingsChanged += null,
            _mockUnifiedSettingsService.Object,
            new SettingsChangedEventArgs("Ocr", SettingsType.Ocr));

        // Assert: Translationタイプ以外は処理されない
        Assert.False(eventFired);
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_UnsubscribesFromEvents()
    {
        // Arrange
        var service = CreateService();

        // Act
        service.Dispose();

        // Assert: Disposeが例外なく完了することを確認
        service.Dispose(); // 2回目のDisposeも安全
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert: 複数回Disposeしても例外が発生しない
        service.Dispose();
        service.Dispose();
        service.Dispose();
    }

    #endregion

    #region Integration Scenario Tests

    [Fact]
    public async Task Scenario_FreeUserUpgradesToPro_CloudTranslationAutoEnabled()
    {
        // Arrange: Freeユーザーとして開始
        var service = CreateService();
        Assert.False(service.IsEffectivelyEnabled);

        // Act: Proプランにアップグレード
        var oldState = new LicenseState { CurrentPlan = PlanType.Free };
        var newState = new LicenseState { CurrentPlan = PlanType.Pro };
        var eventArgs = new LicenseStateChangedEventArgs(oldState, newState, LicenseChangeReason.PlanUpgrade);

        _mockLicenseManager.Setup(x => x.IsFeatureAvailable(FeatureType.CloudAiTranslation)).Returns(true);
        _mockLicenseManager.Raise(x => x.StateChanged += null, _mockLicenseManager.Object, eventArgs);

        // Assert: Cloud翻訳が自動有効化される
        Assert.True(service.IsEntitled);
        _mockUnifiedSettingsService.Verify(
            x => x.UpdateTranslationSettingsAsync(
                It.Is<TranslationSettings>(s => s.EnableCloudAiTranslation == true),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Scenario_ProUserDisablesCloud_ThenReEnables()
    {
        // Arrange: Proユーザーとして開始（Cloud有効）
        _mockLicenseManager.Setup(x => x.IsFeatureAvailable(FeatureType.CloudAiTranslation)).Returns(true);
        _translationSettings.EnableCloudAiTranslation = true;
        _translationSettings.UseLocalEngine = false;  // [Issue #280+#281]
        var service = CreateService();
        Assert.True(service.IsEffectivelyEnabled);

        // Act 1: Cloud翻訳を無効化
        await service.SetPreferredAsync(false);
        Assert.False(service.IsPreferred);

        // Act 2: Cloud翻訳を再有効化
        await service.SetPreferredAsync(true);

        // Assert - [Issue #280+#281] UseLocalEngine も確認
        _mockUnifiedSettingsService.Verify(
            x => x.UpdateTranslationSettingsAsync(
                It.Is<TranslationSettings>(s => s.EnableCloudAiTranslation == true && s.UseLocalEngine == false && s.DefaultEngine == TranslationEngine.Gemini),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void Scenario_UserTogglesLocalEngine_SettingsChangeDetected()
    {
        // Arrange: Proユーザーとして開始
        _mockLicenseManager.Setup(x => x.IsFeatureAvailable(FeatureType.CloudAiTranslation)).Returns(true);
        var service = CreateService();

        CloudTranslationAvailabilityChangedEventArgs? receivedArgs = null;
        service.AvailabilityChanged += (sender, args) => receivedArgs = args;

        // Act: UIでローカルエンジンからAIエンジンに切り替え（SettingsChangedイベント経由）
        // [Issue #280+#281] UseLocalEngineで判定されるようになった
        var newSettings = new TranslationSettings { EnableCloudAiTranslation = true, UseLocalEngine = false };
        RaiseSettingsChanged(newSettings);

        // Assert: 設定変更が検出され、イベントが発火
        Assert.NotNull(receivedArgs);
        Assert.True(receivedArgs.IsEnabled);
        Assert.True(service.IsPreferred);
        Assert.True(service.IsEffectivelyEnabled);
    }

    #endregion
}
