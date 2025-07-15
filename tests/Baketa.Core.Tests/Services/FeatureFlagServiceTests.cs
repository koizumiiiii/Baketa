using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Services;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Baketa.Core.Tests.Services;

public sealed class FeatureFlagServiceTests
{
    private readonly Mock<IOptionsMonitor<FeatureFlagSettings>> _optionsMonitorMock;
    private readonly Mock<ILogger<FeatureFlagService>> _loggerMock;
    private readonly FeatureFlagService _service;

    public FeatureFlagServiceTests()
    {
        _optionsMonitorMock = new Mock<IOptionsMonitor<FeatureFlagSettings>>();
        _loggerMock = new Mock<ILogger<FeatureFlagService>>();

        var defaultSettings = new FeatureFlagSettings();
        _optionsMonitorMock.Setup(x => x.CurrentValue).Returns(defaultSettings);

        _service = new FeatureFlagService(_optionsMonitorMock.Object, _loggerMock.Object);
    }

    [Fact]
    public void IsFeatureEnabled_WithValidFeatureName_ReturnsCorrectValue()
    {
        // Arrange
        var settings = new FeatureFlagSettings { EnableDebugFeatures = true };
        _optionsMonitorMock.Setup(x => x.CurrentValue).Returns(settings);
        var service = new FeatureFlagService(_optionsMonitorMock.Object, _loggerMock.Object);

        // Act & Assert
        Assert.True(service.IsFeatureEnabled("debug"));
        Assert.True(service.IsFeatureEnabled("debugging"));
        Assert.False(service.IsFeatureEnabled("authentication"));
        Assert.False(service.IsFeatureEnabled("unknown"));
    }

    [Fact]
    public void IsPropertyEnabled_WithValidProperty_ReturnsCorrectValue()
    {
        // Arrange
        var settings = new FeatureFlagSettings { EnableDebugFeatures = true };
        _optionsMonitorMock.Setup(x => x.CurrentValue).Returns(settings);
        var service = new FeatureFlagService(_optionsMonitorMock.Object, _loggerMock.Object);

        // Act & Assert
        Assert.True(service.IsPropertyEnabled(nameof(FeatureFlagSettings.EnableDebugFeatures)));
        Assert.False(service.IsPropertyEnabled(nameof(FeatureFlagSettings.EnableAuthenticationFeatures)));
        Assert.False(service.IsPropertyEnabled("NonExistentProperty"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PropertyAccessors_ReturnCorrectValues(bool value)
    {
        // Arrange
        var settings = new FeatureFlagSettings
        {
            EnableAuthenticationFeatures = value,
            EnableCloudTranslation = value,
            EnableAdvancedUIFeatures = value,
            EnableChineseOCR = value,
            EnableUsageStatistics = value,
            EnableDebugFeatures = value,
            EnableAutoUpdate = value,
            EnableFeedbackFeatures = value,
            EnableExperimentalFeatures = value,
            EnablePerformanceMonitoring = value
        };

        _optionsMonitorMock.Setup(x => x.CurrentValue).Returns(settings);
        var service = new FeatureFlagService(_optionsMonitorMock.Object, _loggerMock.Object);

        // Act & Assert
        Assert.Equal(value, service.IsAuthenticationEnabled);
        Assert.Equal(value, service.IsCloudTranslationEnabled);
        Assert.Equal(value, service.IsAdvancedUIEnabled);
        Assert.Equal(value, service.IsChineseOCREnabled);
        Assert.Equal(value, service.IsUsageStatisticsEnabled);
        Assert.Equal(value, service.IsDebugFeaturesEnabled);
        Assert.Equal(value, service.IsAutoUpdateEnabled);
        Assert.Equal(value, service.IsFeedbackEnabled);
        Assert.Equal(value, service.IsExperimentalFeaturesEnabled);
        Assert.Equal(value, service.IsPerformanceMonitoringEnabled);
    }

    [Fact]
    public void GetCurrentSettings_ReturnsCurrentSettings()
    {
        // Arrange
        var settings = new FeatureFlagSettings { EnableDebugFeatures = true };
        _optionsMonitorMock.Setup(x => x.CurrentValue).Returns(settings);
        var service = new FeatureFlagService(_optionsMonitorMock.Object, _loggerMock.Object);

        // Act
        var result = service.GetCurrentSettings();

        // Assert
        Assert.Equal(settings, result);
    }

    [Fact]
    public void FeatureFlagChanged_WhenSettingsChange_FiresEvent()
    {
        // Arrange
        var initialSettings = new FeatureFlagSettings { EnableDebugFeatures = false };
        var newSettings = new FeatureFlagSettings { EnableDebugFeatures = true };
        
        // OnChange拡張メソッドを直接モックする代わりに、テストダブルを使用
        var testOptionsMonitor = new TestOptionsMonitor<FeatureFlagSettings>(initialSettings);
        var service = new FeatureFlagService(testOptionsMonitor, _loggerMock.Object);

        var eventFired = false;
        FeatureFlagChangedEventArgs? eventArgs = null;

        service.FeatureFlagChanged += (sender, e) =>
        {
            eventFired = true;
            eventArgs = e;
        };

        // Act - テストダブルの設定変更メソッドを使用
        testOptionsMonitor.TriggerChange(newSettings);

        // Assert
        Assert.True(eventFired);
        Assert.NotNull(eventArgs);
        Assert.Equal(newSettings, eventArgs.NewSettings);
        Assert.Equal(initialSettings, eventArgs.PreviousSettings);
        Assert.Contains(nameof(FeatureFlagSettings.EnableDebugFeatures), eventArgs.ChangedProperties);
    }

    [Fact]
    public void Constructor_WithNullArguments_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new FeatureFlagService(null!, _loggerMock.Object));
        Assert.Throws<ArgumentNullException>(() => new FeatureFlagService(_optionsMonitorMock.Object, null!));
    }
}

/// <summary>
/// IOptionsMonitor&lt;T&gt;のテスト用実装
/// OnChange拡張メソッドをテスト可能にするためのテストダブル
/// </summary>
internal sealed class TestOptionsMonitor<T>(T initialValue) : IOptionsMonitor<T>
{
    private T CurrentValueInternal { get; set; } = initialValue;
    private readonly List<Action<T, string?>> _listeners = [];

    public T CurrentValue => CurrentValueInternal;

    public T Get(string? name) => CurrentValueInternal;

    public IDisposable? OnChange(Action<T, string?> listener)
    {
        _listeners.Add(listener);
        return new UnsubscribeAction(() => _listeners.Remove(listener));
    }

    /// <summary>
    /// テスト用: 設定変更を手動でトリガー
    /// </summary>
    /// <param name="newValue">新しい設定値</param>
    /// <param name="name">設定名（オプション）</param>
    public void TriggerChange(T newValue, string? name = null)
    {
        CurrentValueInternal = newValue;
        foreach (var listener in _listeners)
        {
            listener(newValue, name);
        }
    }

    private sealed class UnsubscribeAction(Action unsubscribe) : IDisposable
    {
        private bool Disposed { get; set; }

        public void Dispose()
        {
            if (!Disposed)
            {
                unsubscribe();
                Disposed = true;
            }
        }
    }
}