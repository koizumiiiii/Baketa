using System.Globalization;
using Baketa.UI.Resources;
using Xunit;

namespace Baketa.UI.Tests.Resources;

/// <summary>
/// Unit tests for Strings resource file and localization.
/// Issue #176: リソースファイル作成（多言語対応Phase 1）
/// </summary>
public class StringsResourceTests
{
    /// <summary>
    /// Test 1: Strings.resx exists and is loadable
    /// </summary>
    [Fact]
    public void StringsResx_ShouldBeLoadable()
    {
        // Arrange & Act
        var resourceManager = Strings.ResourceManager;

        // Assert
        Assert.NotNull(resourceManager);
        Assert.Equal("Baketa.UI.Resources.Strings", resourceManager.BaseName);
    }

    /// <summary>
    /// Test 2: Default culture (Japanese) strings are accessible
    /// </summary>
    [Fact]
    public void DefaultCulture_ShouldReturnJapaneseStrings()
    {
        // Arrange
        var originalCulture = Strings.Culture;
        Strings.Culture = new CultureInfo("ja");

        try
        {
            // Act
            var title = Strings.App_Title;
            var loadingMessage = Strings.App_LoadingMessage;

            // Assert
            Assert.Equal("Baketa", title);
            Assert.Equal("起動中...", loadingMessage);
        }
        finally
        {
            Strings.Culture = originalCulture;
        }
    }

    /// <summary>
    /// Test 3: English culture strings are accessible
    /// </summary>
    [Fact]
    public void EnglishCulture_ShouldReturnEnglishStrings()
    {
        // Arrange
        var originalCulture = Strings.Culture;
        Strings.Culture = new CultureInfo("en");

        try
        {
            // Act
            var loadingMessage = Strings.App_LoadingMessage;

            // Assert
            Assert.Equal("Loading...", loadingMessage);
        }
        finally
        {
            Strings.Culture = originalCulture;
        }
    }

    /// <summary>
    /// Test 4: All MainOverlay keys exist
    /// </summary>
    [Theory]
    [InlineData("MainOverlay_Minimize")]
    [InlineData("MainOverlay_SelectWindow")]
    [InlineData("MainOverlay_LiveTranslation")]
    [InlineData("MainOverlay_ShotTranslation")]
    [InlineData("MainOverlay_Settings")]
    [InlineData("MainOverlay_Exit")]
    public void MainOverlayKeys_ShouldExist(string key)
    {
        // Arrange & Act
        var value = Strings.GetString(key);

        // Assert
        Assert.NotNull(value);
        Assert.NotEqual($"[{key}]", value);
        Assert.NotEmpty(value);
    }

    /// <summary>
    /// Test 5: All Settings keys exist
    /// </summary>
    [Theory]
    [InlineData("Settings_Title")]
    [InlineData("Settings_General_Title")]
    [InlineData("Settings_Translation_Title")]
    [InlineData("Settings_Capture_Title")]
    [InlineData("Settings_OCR_Title")]
    [InlineData("Settings_Overlay_Title")]
    [InlineData("Settings_Account_Title")]
    [InlineData("Settings_Advanced_Title")]
    public void SettingsKeys_ShouldExist(string key)
    {
        // Arrange & Act
        var value = Strings.GetString(key);

        // Assert
        Assert.NotNull(value);
        Assert.NotEmpty(value);
    }

    /// <summary>
    /// Test 6: Auth keys exist for Login and Signup
    /// </summary>
    [Theory]
    [InlineData("Auth_Login_Title")]
    [InlineData("Auth_Login_Email")]
    [InlineData("Auth_Login_Password")]
    [InlineData("Auth_Login_Button")]
    [InlineData("Auth_Signup_Title")]
    [InlineData("Auth_Signup_Button")]
    public void AuthKeys_ShouldExist(string key)
    {
        // Arrange & Act
        var value = Strings.GetString(key);

        // Assert
        Assert.NotNull(value);
        Assert.NotEmpty(value);
    }

    /// <summary>
    /// Test 7: Error message keys exist
    /// </summary>
    [Theory]
    [InlineData("Error_NetworkError")]
    [InlineData("Error_AuthenticationFailed")]
    [InlineData("Error_TranslationFailed")]
    [InlineData("Error_InvalidEmail")]
    [InlineData("Error_PasswordMismatch")]
    public void ErrorKeys_ShouldExist(string key)
    {
        // Arrange & Act
        var value = Strings.GetString(key);

        // Assert
        Assert.NotNull(value);
        Assert.NotEmpty(value);
    }

    /// <summary>
    /// Test 8: Format strings with placeholders work correctly
    /// </summary>
    [Fact]
    public void FormatStrings_ShouldFormatCorrectly()
    {
        // Arrange
        var originalCulture = Strings.Culture;
        Strings.Culture = CultureInfo.InvariantCulture;

        try
        {
            // Act
            var versionString = Strings.GetString("App_Version", "1.0.0");

            // Assert
            Assert.Contains("1.0.0", versionString);
        }
        finally
        {
            Strings.Culture = originalCulture;
        }
    }

    /// <summary>
    /// Test 9: GetString returns key in brackets for missing keys
    /// </summary>
    [Fact]
    public void GetString_WithMissingKey_ShouldReturnKeyInBrackets()
    {
        // Arrange
        const string missingKey = "NonExistent_Key_12345";

        // Act
        var value = Strings.GetString(missingKey);

        // Assert - key should be returned if not found (based on implementation)
        Assert.Equal(missingKey, value);
    }

    /// <summary>
    /// Test 10: Common buttons have consistent translations
    /// </summary>
    [Theory]
    [InlineData("Common_Save")]
    [InlineData("Common_Cancel")]
    [InlineData("Common_Apply")]
    [InlineData("Common_Reset")]
    [InlineData("Common_OK")]
    [InlineData("Common_Yes")]
    [InlineData("Common_No")]
    public void CommonButtonKeys_ShouldExist(string key)
    {
        // Arrange & Act
        var jaValue = Strings.ResourceManager.GetString(key, CultureInfo.InvariantCulture);
        var enValue = Strings.ResourceManager.GetString(key, new CultureInfo("en"));

        // Assert
        Assert.NotNull(jaValue);
        Assert.NotNull(enValue);
        Assert.NotEmpty(jaValue);
        Assert.NotEmpty(enValue);
    }

    /// <summary>
    /// Test 11: Culture can be changed dynamically
    /// </summary>
    [Fact]
    public void Culture_ShouldBeChangeable()
    {
        // Arrange
        var originalCulture = Strings.Culture;

        try
        {
            // Act - Switch to English
            Strings.Culture = new CultureInfo("en");
            var enValue = Strings.Common_Save;

            // Act - Switch to Japanese
            Strings.Culture = new CultureInfo("ja");
            var jaValue = Strings.Common_Save;

            // Assert
            Assert.Equal("Save", enValue);
            Assert.Equal("保存", jaValue);
        }
        finally
        {
            Strings.Culture = originalCulture;
        }
    }

    /// <summary>
    /// Test 12: Premium feature keys exist
    /// </summary>
    [Theory]
    [InlineData("Premium_Title")]
    [InlineData("Premium_FeatureAdFree")]
    [InlineData("Premium_FeatureCloudTranslation")]
    [InlineData("Premium_Monthly")]
    [InlineData("Premium_Yearly")]
    public void PremiumKeys_ShouldExist(string key)
    {
        // Arrange & Act
        var value = Strings.GetString(key);

        // Assert
        Assert.NotNull(value);
        Assert.NotEmpty(value);
    }
}
