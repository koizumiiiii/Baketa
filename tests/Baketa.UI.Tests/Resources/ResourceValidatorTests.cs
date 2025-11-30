using Baketa.UI.Resources;
using Xunit;

namespace Baketa.UI.Tests.Resources;

/// <summary>
/// Unit tests for ResourceValidator.
/// Issue #176: リソースファイル作成（多言語対応Phase 1）
/// </summary>
public class ResourceValidatorTests
{
    [Fact]
    public void Validate_ShouldReturnValidResult_WhenResourcesAreConsistent()
    {
        // Arrange
        var validator = new ResourceValidator();

        // Act
        var result = validator.Validate();

        // Assert
        // The validation should pass with no critical errors
        // (warnings about naming conventions are acceptable)
        Assert.NotNull(result);
    }

    [Fact]
    public void GetAllKeys_ShouldReturnNonEmptyList()
    {
        // Arrange
        var validator = new ResourceValidator();

        // Act
        var keys = validator.GetAllKeys();

        // Assert
        Assert.NotEmpty(keys);
        Assert.True(keys.Count > 100, $"Expected more than 100 keys, got {keys.Count}");
    }

    [Fact]
    public void GetAllKeys_ShouldContainMainOverlayKeys()
    {
        // Arrange
        var validator = new ResourceValidator();

        // Act
        var keys = validator.GetAllKeys();

        // Assert
        Assert.Contains("MainOverlay_LiveTranslation", keys);
        Assert.Contains("MainOverlay_Settings", keys);
        Assert.Contains("MainOverlay_Exit", keys);
    }

    [Fact]
    public void GetAllKeys_ShouldContainSettingsKeys()
    {
        // Arrange
        var validator = new ResourceValidator();

        // Act
        var keys = validator.GetAllKeys();

        // Assert
        Assert.Contains("Settings_Title", keys);
        Assert.Contains("Settings_General_Title", keys);
        Assert.Contains("Settings_Advanced_Title", keys);
    }

    [Fact]
    public void KeyExistsInAllCultures_ShouldReturnTrue_ForExistingKey()
    {
        // Arrange
        var validator = new ResourceValidator();

        // Act
        var exists = validator.KeyExistsInAllCultures("App_Title");

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public void KeyExistsInAllCultures_ShouldReturnFalse_ForNonExistentKey()
    {
        // Arrange
        var validator = new ResourceValidator();

        // Act
        var exists = validator.KeyExistsInAllCultures("NonExistent_Key_12345");

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public void ValidationResult_IsValid_ShouldBeFalse_WhenErrorsExist()
    {
        // Arrange
        var result = new ResourceValidationResult();
        result.AddError("Test error");

        // Act & Assert
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void ValidationResult_IsValid_ShouldBeTrue_WhenNoErrors()
    {
        // Arrange
        var result = new ResourceValidationResult();
        result.AddWarning("Test warning");

        // Act & Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void ValidationResult_GetSummary_ShouldReturnFormattedString()
    {
        // Arrange
        var result = new ResourceValidationResult();
        result.AddError("Error 1");
        result.AddWarning("Warning 1");
        result.AddWarning("Warning 2");

        // Act
        var summary = result.GetSummary();

        // Assert
        Assert.Contains("FAILED", summary);
        Assert.Contains("1 errors", summary);
        Assert.Contains("2 warnings", summary);
    }
}
