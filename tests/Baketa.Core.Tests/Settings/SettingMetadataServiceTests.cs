using System;
using System.Collections.Generic;
using System.Linq;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Baketa.Core.Tests.Settings;

/// <summary>
/// SettingMetadataServiceの単体テスト
/// </summary>
public class SettingMetadataServiceTests
{
    private readonly Mock<ILogger<SettingMetadataService>> _mockLogger;
    private readonly SettingMetadataService _service;

    public SettingMetadataServiceTests()
    {
        _mockLogger = new Mock<ILogger<SettingMetadataService>>();
        _service = new SettingMetadataService(_mockLogger.Object);
    }

    #region コンストラクタテスト

    [Fact]
    public void Constructor_WithValidLogger_ShouldSucceed()
    {
        // Arrange & Act
        var service = new SettingMetadataService(_mockLogger.Object);

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SettingMetadataService(null!));
    }

    #endregion

    #region GetMetadata(Type)テスト

    [Fact]
    public void GetMetadata_WithValidType_ShouldReturnMetadata()
    {
        // Arrange & Act
        var metadata = _service.GetMetadata<TestSettings>();

        // Assert
        Assert.NotNull(metadata);
        Assert.NotEmpty(metadata);
        Assert.Equal(3, metadata.Count);
    }

    [Fact]
    public void GetMetadata_WithNullType_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => _service.GetMetadata(null!));
    }

    [Fact]
    public void GetMetadata_WithTypeWithoutMetadata_ShouldReturnEmptyList()
    {
        // Arrange & Act
        var metadata = _service.GetMetadata<SettingsWithoutMetadata>();

        // Assert
        Assert.NotNull(metadata);
        Assert.Empty(metadata);
    }

    [Fact]
    public void GetMetadata_SameTypeTwice_ShouldReturnCachedResult()
    {
        // Arrange
        var metadata1 = _service.GetMetadata<TestSettings>();

        // Act
        var metadata2 = _service.GetMetadata<TestSettings>();

        // Assert
        Assert.Same(metadata1, metadata2);
    }

    #endregion

    #region GetMetadata<T>()テスト

    [Fact]
    public void GetMetadata_Generic_WithValidType_ShouldReturnMetadata()
    {
        // Arrange & Act
        var metadata = _service.GetMetadata<TestSettings>();

        // Assert
        Assert.NotNull(metadata);
        Assert.Equal(3, metadata.Count);
    }

    #endregion

    #region GetMetadataByLevelテスト

    [Fact]
    public void GetMetadataByLevel_WithBasicLevel_ShouldReturnBasicSettings()
    {
        // Arrange & Act
        var metadata = _service.GetMetadataByLevel<TestSettings>(SettingLevel.Basic);

        // Assert
        Assert.NotNull(metadata);
        Assert.Equal(2, metadata.Count);
        Assert.All(metadata, m => Assert.Equal(SettingLevel.Basic, m.Level));
    }

    [Fact]
    public void GetMetadataByLevel_WithAdvancedLevel_ShouldReturnAdvancedSettings()
    {
        // Arrange & Act
        var metadata = _service.GetMetadataByLevel<TestSettings>(SettingLevel.Advanced);

        // Assert
        Assert.NotNull(metadata);
        Assert.Single(metadata);
        Assert.Equal(SettingLevel.Advanced, metadata[0].Level);
    }

    [Fact]
    public void GetMetadataByLevel_WithDebugLevel_ShouldReturnEmptyList()
    {
        // Arrange & Act
        var metadata = _service.GetMetadataByLevel<TestSettings>(SettingLevel.Debug);

        // Assert
        Assert.NotNull(metadata);
        Assert.Empty(metadata);
    }

    #endregion

    #region GetMetadataByCategoryテスト

    [Fact]
    public void GetMetadataByCategory_WithValidCategory_ShouldReturnFilteredMetadata()
    {
        // Arrange & Act
        var metadata = _service.GetMetadataByCategory<TestSettings>("General");

        // Assert
        Assert.NotNull(metadata);
        Assert.Equal(2, metadata.Count);
        Assert.All(metadata, m => Assert.Equal("General", m.Category));
    }

    [Fact]
    public void GetMetadataByCategory_WithNonExistentCategory_ShouldReturnEmptyList()
    {
        // Arrange & Act
        var metadata = _service.GetMetadataByCategory<TestSettings>("NonExistent");

        // Assert
        Assert.NotNull(metadata);
        Assert.Empty(metadata);
    }

    [Fact]
    public void GetMetadataByCategory_WithNullCategory_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        // ArgumentException.ThrowIfNullOrEmptyではnullの場合ArgumentNullExceptionが投げられる
        Assert.Throws<ArgumentNullException>(() => _service.GetMetadataByCategory<TestSettings>(null!));
    }

    [Fact]
    public void GetMetadataByCategory_WithEmptyCategory_ShouldThrowArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => _service.GetMetadataByCategory<TestSettings>(string.Empty));
    }

    #endregion

    #region GetCategoriesテスト

    [Fact]
    public void GetCategories_ShouldReturnAllCategories()
    {
        // Arrange & Act
        var categories = _service.GetCategories<TestSettings>();

        // Assert
        Assert.NotNull(categories);
        Assert.Equal(2, categories.Count);
        Assert.Contains("General", categories);
        Assert.Contains("Advanced", categories);
    }

    [Fact]
    public void GetCategories_ShouldReturnSortedCategories()
    {
        // Arrange & Act
        var categories = _service.GetCategories<TestSettings>();

        // Assert
        Assert.Equal("Advanced", categories[0]);
        Assert.Equal("General", categories[1]);
    }

    #endregion

    #region ValidateValueテスト

    [Fact]
    public void ValidateValue_WithValidValue_ShouldReturnSuccess()
    {
        // Arrange
        var metadata = _service.GetMetadata<TestSettings>().First(m => m.Property.Name == "Name");

        // Act
        var result = _service.ValidateValue(metadata, "Valid Name");

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal("Valid Name", result.Value);
    }

    [Fact]
    public void ValidateValue_WithInvalidValue_ShouldReturnFailure()
    {
        // Arrange
        var metadata = _service.GetMetadata<TestSettings>().First(m => m.Property.Name == "Age");

        // Act
        var result = _service.ValidateValue(metadata, -1);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void ValidateValue_WithNullMetadata_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => _service.ValidateValue(null!, "value"));
    }

    #endregion

    #region ValidateSettingsテスト

    [Fact]
    public void ValidateSettings_WithValidSettings_ShouldReturnSuccessResults()
    {
        // Arrange
        var settings = new TestSettings
        {
            Name = "Valid Name",
            Age = 25,
            IsEnabled = true
        };

        // Act
        var results = _service.ValidateSettings(settings);

        // Assert
        Assert.NotNull(results);
        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.True(r.IsValid));
    }

    [Fact]
    public void ValidateSettings_WithInvalidSettings_ShouldReturnFailureResults()
    {
        // Arrange
        var settings = new TestSettings
        {
            Name = "Valid Name",
            Age = -1, // 無効な値
            IsEnabled = true
        };

        // Act
        var results = _service.ValidateSettings(settings);

        // Assert
        Assert.NotNull(results);
        Assert.Equal(3, results.Count);
        Assert.Contains(results, r => !r.IsValid);
    }

    [Fact]
    public void ValidateSettings_WithNullSettings_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => _service.ValidateSettings<TestSettings>(null!));
    }

    #endregion

    #region テスト用クラス

    /// <summary>
    /// テスト用設定クラス
    /// </summary>
    internal sealed class TestSettings
    {
        [SettingMetadata(SettingLevel.Basic, "General", "名前", Description = "ユーザーの名前")]
        public string Name { get; set; } = string.Empty;

        [SettingMetadata(SettingLevel.Basic, "General", "年齢", MinValue = 0, MaxValue = 120)]
        public int Age { get; set; } = 0;

        [SettingMetadata(SettingLevel.Advanced, "Advanced", "有効", Description = "機能を有効にするかどうか")]
        public bool IsEnabled { get; set; } = false;

        // メタデータ属性のないプロパティ
        public string NoMetadata { get; set; } = string.Empty;
    }

    /// <summary>
    /// メタデータ属性のない設定クラス
    /// </summary>
    internal sealed class SettingsWithoutMetadata
    {
        public string Property1 { get; set; } = string.Empty;
        public int Property2 { get; set; } = 0;
    }

    #endregion
}
