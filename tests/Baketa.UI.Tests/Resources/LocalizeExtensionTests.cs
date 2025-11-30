using System.Globalization;
using Avalonia.Data;
using Baketa.UI.Extensions;
using Baketa.UI.Resources;
using Baketa.UI.Services;
using Xunit;

namespace Baketa.UI.Tests.Resources;

/// <summary>
/// Unit tests for LocalizeExtension and LocalizationManager.
/// Issue #176: リソースファイル作成（多言語対応Phase 1）
/// Issue #177: 言語切替機能（多言語対応Phase 2）
/// </summary>
public class LocalizeExtensionTests
{
    [Fact]
    public void LocalizeExtension_WithValidKey_ShouldReturnBinding()
    {
        // Arrange
        var extension = new LocalizeExtension("App_Title");

        // Act
        var result = extension.ProvideValue(null!);

        // Assert
        Assert.IsType<Binding>(result);
        var binding = (Binding)result;
        Assert.Equal("[App_Title]", binding.Path);
        Assert.Same(LocalizationManager.Instance, binding.Source);
    }

    [Fact]
    public void LocalizeExtension_WithEmptyKey_ShouldReturnMissingKeyMessage()
    {
        // Arrange
        var extension = new LocalizeExtension();

        // Act
        var result = extension.ProvideValue(null!);

        // Assert
        Assert.Equal("[Missing Key]", result);
    }

    [Fact]
    public void LocalizeExtension_Constructor_ShouldSetKey()
    {
        // Arrange & Act
        var extension = new LocalizeExtension("Test_Key");

        // Assert
        Assert.Equal("Test_Key", extension.Key);
    }

    [Fact]
    public void LocalizeExtension_DefaultConstructor_ShouldHaveEmptyKey()
    {
        // Arrange & Act
        var extension = new LocalizeExtension();

        // Assert
        Assert.Equal(string.Empty, extension.Key);
    }
}

/// <summary>
/// Unit tests for LocalizationManager.
/// Issue #177: 言語切替機能（多言語対応Phase 2）
/// </summary>
public class LocalizationManagerTests
{
    [Fact]
    public void LocalizationManager_Instance_ShouldReturnSameSingleton()
    {
        // Act
        var instance1 = LocalizationManager.Instance;
        var instance2 = LocalizationManager.Instance;

        // Assert
        Assert.Same(instance1, instance2);
    }

    [Fact]
    public void LocalizationManager_Indexer_WithValidKey_ShouldReturnLocalizedString()
    {
        // Arrange
        var originalCulture = Strings.Culture;
        Strings.Culture = CultureInfo.InvariantCulture;

        try
        {
            // Act
            var result = LocalizationManager.Instance["App_Title"];

            // Assert
            Assert.Equal("Baketa", result);
        }
        finally
        {
            Strings.Culture = originalCulture;
        }
    }

    [Fact]
    public void LocalizationManager_Indexer_WithMissingKey_ShouldReturnKeyInBrackets()
    {
        // Act
        var result = LocalizationManager.Instance["NonExistent_Key"];

        // Assert
        Assert.Equal("[NonExistent_Key]", result);
    }

    [Fact]
    public void LocalizationManager_Indexer_WithEmptyKey_ShouldReturnMissingKeyMessage()
    {
        // Act
        var result = LocalizationManager.Instance[string.Empty];

        // Assert
        Assert.Equal("[Missing Key]", result);
    }

    [Fact]
    public void LocalizationManager_Indexer_WithNullKey_ShouldReturnMissingKeyMessage()
    {
        // Act
        var result = LocalizationManager.Instance[null!];

        // Assert
        Assert.Equal("[Missing Key]", result);
    }

    [Fact]
    public void LocalizationManager_WithEnglishCulture_ShouldReturnEnglishString()
    {
        // Arrange
        var originalCulture = Strings.Culture;
        Strings.Culture = new CultureInfo("en");

        try
        {
            // Act
            var result = LocalizationManager.Instance["Common_Save"];

            // Assert
            Assert.Equal("Save", result);
        }
        finally
        {
            Strings.Culture = originalCulture;
        }
    }

    [Fact]
    public void LocalizationManager_WithJapaneseCulture_ShouldReturnJapaneseString()
    {
        // Arrange
        var originalCulture = Strings.Culture;
        Strings.Culture = new CultureInfo("ja");

        try
        {
            // Act
            var result = LocalizationManager.Instance["Common_Save"];

            // Assert
            Assert.Equal("保存", result);
        }
        finally
        {
            Strings.Culture = originalCulture;
        }
    }

    [Fact]
    public void LocalizationManager_GetString_WithFormat_ShouldFormatString()
    {
        // Arrange
        var originalCulture = Strings.Culture;
        Strings.Culture = new CultureInfo("en");

        try
        {
            // Act - Using App_Version key that has a format specifier {0}
            var result = LocalizationManager.Instance.GetString("App_Version", "1.0.0");

            // Assert - Should format the string with the version
            Assert.NotNull(result);
            Assert.Contains("1.0.0", result);
            Assert.Contains("Version", result);
        }
        finally
        {
            Strings.Culture = originalCulture;
        }
    }
}
