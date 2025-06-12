using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Baketa.Core.Settings;

namespace Baketa.Core.Tests.Settings;

/// <summary>
/// SettingValidationResultの単体テスト
/// </summary>
public class SettingValidationResultTests
{
    #region コンストラクタテスト

    [Fact]
    public void Constructor_WithValidParameters_ShouldSucceed()
    {
        // Arrange
        var metadata = CreateTestMetadata();

        // Act
        var result = new SettingValidationResult(metadata, "test", true, null, null);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(metadata, result.Metadata);
        Assert.Equal("test", result.Value);
        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.WarningMessage);
    }

    [Fact]
    public void Constructor_WithNullMetadata_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new SettingValidationResult(null!, "test", true, null, null));
    }

    #endregion

    #region ファクトリーメソッドテスト

    [Fact]
    public void Success_WithValidParameters_ShouldCreateSuccessResult()
    {
        // Arrange
        var metadata = CreateTestMetadata();

        // Act
        var result = SettingValidationResult.Success(metadata, "test", "warning");

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(metadata, result.Metadata);
        Assert.Equal("test", result.Value);
        Assert.Null(result.ErrorMessage);
        Assert.Equal("warning", result.WarningMessage);
    }

    [Fact]
    public void Success_WithoutWarning_ShouldCreateSuccessResultWithoutWarning()
    {
        // Arrange
        var metadata = CreateTestMetadata();

        // Act
        var result = SettingValidationResult.Success(metadata, "test");

        // Assert
        Assert.True(result.IsValid);
        Assert.Null(result.WarningMessage);
    }

    [Fact]
    public void Failure_WithValidParameters_ShouldCreateFailureResult()
    {
        // Arrange
        var metadata = CreateTestMetadata();

        // Act
        var result = SettingValidationResult.Failure(metadata, "test", "error message");

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(metadata, result.Metadata);
        Assert.Equal("test", result.Value);
        Assert.Equal("error message", result.ErrorMessage);
        Assert.Null(result.WarningMessage);
    }

    [Fact]
    public void Failure_WithNullErrorMessage_ShouldThrowArgumentException()
    {
        // Arrange
        var metadata = CreateTestMetadata();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => 
            SettingValidationResult.Failure(metadata, "test", null!));
    }

    [Fact]
    public void Failure_WithEmptyErrorMessage_ShouldThrowArgumentException()
    {
        // Arrange
        var metadata = CreateTestMetadata();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => 
            SettingValidationResult.Failure(metadata, "test", string.Empty));
    }

    #endregion

    #region ToStringテスト

    [Fact]
    public void ToString_ForSuccessResult_ShouldReturnValidFormat()
    {
        // Arrange
        var metadata = CreateTestMetadata();
        var result = SettingValidationResult.Success(metadata, "test");

        // Act
        var stringResult = result.ToString();

        // Assert
        Assert.Contains("Valid", stringResult);
        Assert.Contains("Test Setting", stringResult);
    }

    [Fact]
    public void ToString_ForFailureResult_ShouldReturnValidFormat()
    {
        // Arrange
        var metadata = CreateTestMetadata();
        var result = SettingValidationResult.Failure(metadata, "test", "error");

        // Act
        var stringResult = result.ToString();

        // Assert
        Assert.Contains("Invalid", stringResult);
        Assert.Contains("Test Setting", stringResult);
        Assert.Contains("error", stringResult);
    }

    #endregion

    #region ヘルパーメソッド

    private static SettingMetadata CreateTestMetadata()
    {
        var property = typeof(TestSettings).GetProperty(nameof(TestSettings.TestProperty))!;
        var attribute = new SettingMetadataAttribute(SettingLevel.Basic, "Test", "Test Setting");
        return new SettingMetadata(property, attribute);
    }

    private sealed class TestSettings
    {
        public string TestProperty { get; set; } = string.Empty;
    }

    #endregion
}

/// <summary>
/// SettingsValidationResultの単体テスト
/// </summary>
public class SettingsValidationResultTests
{
    #region コンストラクタテスト

    [Fact]
    public void Constructor_WithValidParameters_ShouldSucceed()
    {
        // Arrange
        var validationResults = new List<SettingValidationResult>
        {
            SettingValidationResult.Success(CreateTestMetadata(), "test1"),
            SettingValidationResult.Failure(CreateTestMetadata(), "test2", "error")
        };

        // Act
        var result = new SettingsValidationResult(validationResults);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Results.Count);
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Constructor_WithNullResults_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SettingsValidationResult(null!));
    }

    #endregion

    #region プロパティテスト

    [Fact]
    public void IsValid_WithAllValidResults_ShouldReturnTrue()
    {
        // Arrange
        var validationResults = new List<SettingValidationResult>
        {
            SettingValidationResult.Success(CreateTestMetadata(), "test1"),
            SettingValidationResult.Success(CreateTestMetadata(), "test2")
        };

        // Act
        var result = new SettingsValidationResult(validationResults);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void IsValid_WithSomeInvalidResults_ShouldReturnFalse()
    {
        // Arrange
        var validationResults = new List<SettingValidationResult>
        {
            SettingValidationResult.Success(CreateTestMetadata(), "test1"),
            SettingValidationResult.Failure(CreateTestMetadata(), "test2", "error")
        };

        // Act
        var result = new SettingsValidationResult(validationResults);

        // Assert
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Errors_ShouldReturnOnlyErrorMessages()
    {
        // Arrange
        var validationResults = new List<SettingValidationResult>
        {
            SettingValidationResult.Success(CreateTestMetadata(), "test1", "warning"),
            SettingValidationResult.Failure(CreateTestMetadata(), "test2", "error1"),
            SettingValidationResult.Failure(CreateTestMetadata(), "test3", "error2")
        };

        // Act
        var result = new SettingsValidationResult(validationResults);

        // Assert
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "error1");
        Assert.Contains(result.Errors, e => e.ErrorMessage == "error2");
    }

    [Fact]
    public void Warnings_ShouldReturnOnlyWarningMessages()
    {
        // Arrange
        var validationResults = new List<SettingValidationResult>
        {
            SettingValidationResult.Success(CreateTestMetadata(), "test1", "warning1"),
            SettingValidationResult.Success(CreateTestMetadata(), "test2", "warning2"),
            SettingValidationResult.Failure(CreateTestMetadata(), "test3", "error")
        };

        // Act
        var result = new SettingsValidationResult(validationResults);

        // Assert
        Assert.Equal(2, result.Warnings.Count);
        Assert.Contains(result.Warnings, w => w.WarningMessage == "warning1");
        Assert.Contains(result.Warnings, w => w.WarningMessage == "warning2");
    }

    #endregion

    #region ファクトリーメソッドテスト

    [Fact]
    public void CreateSuccess_ShouldReturnValidResult()
    {
        // Act
        var result = SettingsValidationResult.CreateSuccess();

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Results);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void CreateFailure_WithErrorMessage_ShouldReturnInvalidResult()
    {
        // Act
        var result = SettingsValidationResult.CreateFailure("Test error");

        // Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Test error");
    }

    [Fact]
    public void CreateFailure_WithNullErrorMessage_ShouldThrowArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => SettingsValidationResult.CreateFailure(null!));
    }

    #endregion

    #region ヘルパーメソッド

    private static SettingMetadata CreateTestMetadata()
    {
        var property = typeof(TestSettings).GetProperty(nameof(TestSettings.TestProperty))!;
        var attribute = new SettingMetadataAttribute(SettingLevel.Basic, "Test", "Test Setting");
        return new SettingMetadata(property, attribute);
    }

    private sealed class TestSettings
    {
        public string TestProperty { get; set; } = string.Empty;
    }

    #endregion
}

/// <summary>
/// SettingMetadataの単体テスト
/// </summary>
public class SettingMetadataTests
{
    #region コンストラクタテスト

    [Fact]
    public void Constructor_WithValidParameters_ShouldSucceed()
    {
        // Arrange
        var property = typeof(TestSettings).GetProperty(nameof(TestSettings.StringProperty))!;
        var attribute = new SettingMetadataAttribute(SettingLevel.Basic, "Test", "Test Setting");

        // Act
        var metadata = new SettingMetadata(property, attribute);

        // Assert
        Assert.NotNull(metadata);
        Assert.Equal(property, metadata.Property);
        Assert.Equal(SettingLevel.Basic, metadata.Level);
        Assert.Equal("Test", metadata.Category);
        Assert.Equal("Test Setting", metadata.DisplayName);
    }

    [Fact]
    public void Constructor_WithNullProperty_ShouldThrowArgumentNullException()
    {
        // Arrange
        var attribute = new SettingMetadataAttribute(SettingLevel.Basic, "Test", "Test Setting");

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SettingMetadata(null!, attribute));
    }

    [Fact]
    public void Constructor_WithNullAttribute_ShouldThrowArgumentNullException()
    {
        // Arrange
        var property = typeof(TestSettings).GetProperty(nameof(TestSettings.StringProperty))!;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SettingMetadata(property, null!));
    }

    #endregion

    #region IsValidValueテスト

    [Fact]
    public void IsValidValue_WithValidStringValue_ShouldReturnTrue()
    {
        // Arrange
        var metadata = CreateStringMetadata();

        // Act
        var isValid = metadata.IsValidValue("test");

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void IsValidValue_WithInvalidTypeValue_ShouldReturnFalse()
    {
        // Arrange
        var metadata = CreateStringMetadata();

        // Act
        var isValid = metadata.IsValidValue(123);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void IsValidValue_WithIntegerInRange_ShouldReturnTrue()
    {
        // Arrange
        var metadata = CreateIntegerMetadata(0, 100);

        // Act
        var isValid = metadata.IsValidValue(50);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void IsValidValue_WithIntegerOutOfRange_ShouldReturnFalse()
    {
        // Arrange
        var metadata = CreateIntegerMetadata(0, 100);

        // Act
        var isValid = metadata.IsValidValue(150);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void IsValidValue_WithValidEnumValue_ShouldReturnTrue()
    {
        // Arrange
        var metadata = CreateEnumMetadata(TestEnum.Value1, TestEnum.Value2);

        // Act
        var isValid = metadata.IsValidValue(TestEnum.Value1);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void IsValidValue_WithInvalidEnumValue_ShouldReturnFalse()
    {
        // Arrange
        var metadata = CreateEnumMetadata(TestEnum.Value1, TestEnum.Value2);

        // Act
        var isValid = metadata.IsValidValue(TestEnum.Value3);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void IsValidValue_WithNullValue_ShouldReturnTrue()
    {
        // Arrange
        var metadata = CreateStringMetadata();

        // Act
        var isValid = metadata.IsValidValue(null);

        // Assert
        Assert.True(isValid); // null値は一般的に許可される
    }

    #endregion

    #region GetValue/SetValueテスト

    [Fact]
    public void GetValue_WithValidInstance_ShouldReturnPropertyValue()
    {
        // Arrange
        var metadata = CreateStringMetadata();
        var instance = new TestSettings { StringProperty = "test value" };

        // Act
        var value = metadata.GetValue(instance);

        // Assert
        Assert.Equal("test value", value);
    }

    [Fact]
    public void SetValue_WithValidValue_ShouldUpdateProperty()
    {
        // Arrange
        var metadata = CreateStringMetadata();
        var instance = new TestSettings();

        // Act
        metadata.SetValue(instance, "new value");

        // Assert
        Assert.Equal("new value", instance.StringProperty);
    }

    [Fact]
    public void GetValue_WithNullInstance_ShouldThrowArgumentNullException()
    {
        // Arrange
        var metadata = CreateStringMetadata();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => metadata.GetValue(null!));
    }

    [Fact]
    public void SetValue_WithNullInstance_ShouldThrowArgumentNullException()
    {
        // Arrange
        var metadata = CreateStringMetadata();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => metadata.SetValue(null!, "value"));
    }

    #endregion

    #region ヘルパーメソッド

    private static SettingMetadata CreateStringMetadata()
    {
        var property = typeof(TestSettings).GetProperty(nameof(TestSettings.StringProperty))!;
        var attribute = new SettingMetadataAttribute(SettingLevel.Basic, "Test", "String Property");
        return new SettingMetadata(property, attribute);
    }

    private static SettingMetadata CreateIntegerMetadata(int minValue, int maxValue)
    {
        var property = typeof(TestSettings).GetProperty(nameof(TestSettings.IntProperty))!;
        var attribute = new SettingMetadataAttribute(SettingLevel.Basic, "Test", "Int Property")
        {
            MinValue = minValue,
            MaxValue = maxValue
        };
        return new SettingMetadata(property, attribute);
    }

    private static SettingMetadata CreateEnumMetadata(params TestEnum[] validValues)
    {
        var property = typeof(TestSettings).GetProperty(nameof(TestSettings.EnumProperty))!;
        var attribute = new SettingMetadataAttribute(SettingLevel.Basic, "Test", "Enum Property")
        {
            ValidValues = [.. validValues.Cast<object>()]
        };
        return new SettingMetadata(property, attribute);
    }

    #endregion

    #region テスト用クラス

    private sealed class TestSettings
    {
        public string StringProperty { get; set; } = string.Empty;
        public int IntProperty { get; set; }
        public TestEnum EnumProperty { get; set; }
    }

    private enum TestEnum
    {
        Value1,
        Value2,
        Value3
    }

    #endregion
}
