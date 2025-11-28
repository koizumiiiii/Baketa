using System;
using Baketa.Core.Abstractions.Auth;
using Baketa.Infrastructure.Auth;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.Auth;

/// <summary>
/// PasswordStrengthValidatorの単体テスト
/// パスワード強度チェック、ブラックリスト検証、強度インジケーター機能をテスト
/// </summary>
public sealed class PasswordStrengthValidatorTests
{
    private readonly Mock<ILogger<PasswordStrengthValidator>> _mockLogger;
    private readonly PasswordStrengthValidator _validator;

    public PasswordStrengthValidatorTests()
    {
        _mockLogger = new Mock<ILogger<PasswordStrengthValidator>>();
        _validator = new PasswordStrengthValidator(_mockLogger.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullLogger_DoesNotThrow()
    {
        // Act & Assert - ロガーはオプションなのでnullでも問題ない
        var validator = new PasswordStrengthValidator(null);
        validator.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithValidLogger_InitializesCorrectly()
    {
        // Arrange & Act
        var validator = new PasswordStrengthValidator(_mockLogger.Object);

        // Assert
        validator.Should().NotBeNull();
    }

    #endregion

    #region ValidatePassword - Null/Empty Tests

    [Fact]
    public void ValidatePassword_WithNull_ReturnsInvalidResult()
    {
        // Act
        var result = _validator.ValidatePassword(null!);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Strength.Should().Be(PasswordStrength.Weak);
        result.Errors.Should().Contain("パスワードを入力してください");
    }

    [Fact]
    public void ValidatePassword_WithEmptyString_ReturnsInvalidResult()
    {
        // Act
        var result = _validator.ValidatePassword(string.Empty);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("パスワードを入力してください");
    }

    [Fact]
    public void ValidatePassword_WithWhitespace_ReturnsInvalidResult()
    {
        // Act
        var result = _validator.ValidatePassword("   ");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("パスワードを入力してください");
    }

    #endregion

    #region ValidatePassword - Length Tests

    [Theory]
    [InlineData("Abc1")]       // 4文字
    [InlineData("Abc12")]      // 5文字
    [InlineData("Abc123")]     // 6文字
    [InlineData("Abc1234")]    // 7文字
    public void ValidatePassword_WithTooShortPassword_ReturnsInvalidResult(string password)
    {
        // Act
        var result = _validator.ValidatePassword(password);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("8文字以上"));
    }

    [Fact]
    public void ValidatePassword_WithExactly8Characters_PassesLengthCheck()
    {
        // Arrange - 8文字で3カテゴリを満たす
        var password = "Abcdef1!";

        // Act
        var result = _validator.ValidatePassword(password);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().NotContain(e => e.Contains("8文字以上"));
    }

    #endregion

    #region ValidatePassword - Category Tests

    [Theory]
    [InlineData("abcdefgh")]       // 小文字のみ (1種類)
    [InlineData("ABCDEFGH")]       // 大文字のみ (1種類)
    [InlineData("12345678")]       // 数字のみ (1種類)
    [InlineData("!@#$%^&*")]       // 記号のみ (1種類)
    public void ValidatePassword_WithOnlyOneCategory_ReturnsInvalidResult(string password)
    {
        // Act
        var result = _validator.ValidatePassword(password);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("3種類以上"));
        result.CategoryCount.Should().Be(1);
    }

    [Theory]
    [InlineData("Abcdefgh")]       // 大文字+小文字 (2種類)
    [InlineData("abcdefg1")]       // 小文字+数字 (2種類)
    [InlineData("ABCDEFG1")]       // 大文字+数字 (2種類)
    [InlineData("abcdefg!")]       // 小文字+記号 (2種類)
    public void ValidatePassword_WithOnlyTwoCategories_ReturnsInvalidResult(string password)
    {
        // Act
        var result = _validator.ValidatePassword(password);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("3種類以上"));
        result.CategoryCount.Should().Be(2);
    }

    [Theory]
    [InlineData("Abcdefg1")]       // 大文字+小文字+数字 (3種類)
    [InlineData("Abcdefg!")]       // 大文字+小文字+記号 (3種類)
    [InlineData("abcdef1!")]       // 小文字+数字+記号 (3種類)
    [InlineData("ABCDEF1!")]       // 大文字+数字+記号 (3種類)
    public void ValidatePassword_WithThreeCategories_ReturnsValidResult(string password)
    {
        // Act
        var result = _validator.ValidatePassword(password);

        // Assert
        result.IsValid.Should().BeTrue();
        result.CategoryCount.Should().Be(3);
    }

    [Theory]
    [InlineData("Abcdef1!")]       // 全4種類
    [InlineData("TestPass1@")]     // 全4種類
    [InlineData("Xy9$abcd")]       // 全4種類
    public void ValidatePassword_WithAllFourCategories_ReturnsValidResultWithCategoryCount4(string password)
    {
        // Act
        var result = _validator.ValidatePassword(password);

        // Assert
        result.IsValid.Should().BeTrue();
        result.CategoryCount.Should().Be(4);
    }

    #endregion

    #region ValidatePassword - Blacklist Tests

    [Theory]
    [InlineData("password")]
    [InlineData("Password")]
    [InlineData("PASSWORD")]
    [InlineData("password1")]
    [InlineData("Password123")]
    [InlineData("12345678")]
    [InlineData("qwertyui")]
    [InlineData("admin123")]
    [InlineData("test1234")]
    public void ValidatePassword_WithBlacklistedPassword_ReturnsInvalidResult(string password)
    {
        // Act
        var result = _validator.ValidatePassword(password);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("一般的すぎるパスワード"));
    }

    [Fact]
    public void ValidatePassword_WithUniquePassword_DoesNotFailBlacklistCheck()
    {
        // Arrange - ユニークなパスワード（3種類以上含む）
        var password = "MyUniq1!";

        // Act
        var result = _validator.ValidatePassword(password);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().NotContain(e => e.Contains("一般的すぎるパスワード"));
    }

    #endregion

    #region GetPasswordStrength Tests

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetPasswordStrength_WithEmptyPassword_ReturnsWeak(string? password)
    {
        // Act
        var strength = _validator.GetPasswordStrength(password!);

        // Assert
        strength.Should().Be(PasswordStrength.Weak);
    }

    [Theory]
    [InlineData("abc")]           // 短すぎる
    [InlineData("abcdefg")]       // 1カテゴリのみ
    [InlineData("password")]      // ブラックリスト
    public void GetPasswordStrength_WithWeakPassword_ReturnsWeak(string password)
    {
        // Act
        var strength = _validator.GetPasswordStrength(password);

        // Assert
        strength.Should().Be(PasswordStrength.Weak);
    }

    [Theory]
    [InlineData("Abcdefg1")]       // 8文字、3カテゴリ → Medium
    [InlineData("MyPass12")]       // 8文字、3カテゴリ → Medium
    public void GetPasswordStrength_WithMediumPassword_ReturnsMedium(string password)
    {
        // Act
        var strength = _validator.GetPasswordStrength(password);

        // Assert
        strength.Should().Be(PasswordStrength.Medium);
    }

    [Theory]
    [InlineData("Xyz9$bcdwqr1")]     // 12文字、4カテゴリ → Strong
    [InlineData("MySecure123!")]     // 12文字、4カテゴリ → Strong
    [InlineData("UniquePass1@")]     // 12文字、4カテゴリ → Strong
    public void GetPasswordStrength_WithStrongPassword_ReturnsStrong(string password)
    {
        // Act
        var strength = _validator.GetPasswordStrength(password);

        // Assert
        strength.Should().Be(PasswordStrength.Strong);
    }

    #endregion

    #region IsBlacklistedPassword Tests

    [Fact]
    public void IsBlacklistedPassword_WithNull_ReturnsFalse()
    {
        // Act
        var result = _validator.IsBlacklistedPassword(null!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsBlacklistedPassword_WithEmpty_ReturnsFalse()
    {
        // Act
        var result = _validator.IsBlacklistedPassword(string.Empty);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("password")]
    [InlineData("Password")]
    [InlineData("12345678")]
    [InlineData("qwertyui")]
    [InlineData("admin123")]
    public void IsBlacklistedPassword_WithCommonPassword_ReturnsTrue(string password)
    {
        // Act
        var result = _validator.IsBlacklistedPassword(password);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("MyUniquePass1")]
    [InlineData("Xyz987654")]
    [InlineData("SecretKey99")]
    public void IsBlacklistedPassword_WithUniquePassword_ReturnsFalse(string password)
    {
        // Act
        var result = _validator.IsBlacklistedPassword(password);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region GetStrengthMessage Tests

    [Fact]
    public void GetStrengthMessage_WithWeak_ReturnsCorrectMessage()
    {
        // Act
        var message = _validator.GetStrengthMessage(PasswordStrength.Weak);

        // Assert
        message.Should().Be("弱い");
    }

    [Fact]
    public void GetStrengthMessage_WithMedium_ReturnsCorrectMessage()
    {
        // Act
        var message = _validator.GetStrengthMessage(PasswordStrength.Medium);

        // Assert
        message.Should().Be("普通");
    }

    [Fact]
    public void GetStrengthMessage_WithStrong_ReturnsCorrectMessage()
    {
        // Act
        var message = _validator.GetStrengthMessage(PasswordStrength.Strong);

        // Assert
        message.Should().Be("強い");
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void ValidatePassword_WithUnicodeCharacters_HandlesCorrectly()
    {
        // Arrange - 日本語を含むパスワード（文字数としてはカウントされるが、カテゴリとしては記号扱い）
        var password = "日本語Pass1!";

        // Act
        var result = _validator.ValidatePassword(password);

        // Assert
        result.Should().NotBeNull();
        // 日本語文字は記号としてカウントされる可能性がある
    }

    [Fact]
    public void ValidatePassword_WithVeryLongPassword_HandlesCorrectly()
    {
        // Arrange - 非常に長いパスワード（ブラックリストに含まれない文字列）
        var password = "Xyz9$unique" + new string('w', 100);

        // Act
        var result = _validator.ValidatePassword(password);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Strength.Should().Be(PasswordStrength.Strong);
    }

    [Fact]
    public void ValidatePassword_WithSpecialSymbols_RecognizesAsSymbolCategory()
    {
        // Arrange - 様々な記号を含むパスワード（ブラックリストに含まれない）
        var password = "Xyz9@#$%^&*()";

        // Act
        var result = _validator.ValidatePassword(password);

        // Assert
        result.IsValid.Should().BeTrue();
        result.CategoryCount.Should().Be(4);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void FullValidation_WithRealisticStrongPassword_ReturnsExpectedResult()
    {
        // Arrange - 現実的な強いパスワード
        var password = "MySecure@Pass2024";

        // Act
        var result = _validator.ValidatePassword(password);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Strength.Should().Be(PasswordStrength.Strong);
        result.CategoryCount.Should().Be(4);
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void FullValidation_WithRealisticMediumPassword_ReturnsExpectedResult()
    {
        // Arrange - 現実的な中程度のパスワード
        var password = "MyPass123";

        // Act
        var result = _validator.ValidatePassword(password);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Strength.Should().Be(PasswordStrength.Medium);
        result.CategoryCount.Should().Be(3);
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void FullValidation_WithMultipleIssues_ReturnsAllErrors()
    {
        // Arrange - 複数の問題を持つパスワード（短い、カテゴリ不足）
        var password = "abc";

        // Act
        var result = _validator.ValidatePassword(password);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCountGreaterThan(1);
        result.Errors.Should().Contain(e => e.Contains("8文字以上"));
        result.Errors.Should().Contain(e => e.Contains("3種類以上"));
    }

    #endregion
}
