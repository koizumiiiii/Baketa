using System.Text.RegularExpressions;
using Baketa.Core.Constants;
using Xunit;

namespace Baketa.Core.Tests.Constants;

/// <summary>
/// ValidationPatternsの単体テスト
/// プロモーションコードのBase32 Crockfordパターン検証
/// </summary>
public class ValidationPatternsTests
{
    private static readonly Regex PromotionCodeRegex = new(ValidationPatterns.PromotionCode);

    #region Valid Promotion Codes

    [Theory]
    [InlineData("BAKETA-12345678")]  // 数字のみ
    [InlineData("BAKETA-ABCDEFGH")]  // A-H（Iを含まない）
    [InlineData("BAKETA-JKMNPQRS")]  // J,K,M,N,P,Q,R,S（Lを含まない）
    [InlineData("BAKETA-TVWXYZ01")]  // T,V-Z,数字（Uを含まない）
    [InlineData("BAKETA-XZ8P4R9N")]  // 実際のコード例
    [InlineData("BAKETA-66092008")]  // 数字のみの実際のコード例
    public void PromotionCode_ValidCodes_ShouldMatch(string code)
    {
        // Act
        var isMatch = PromotionCodeRegex.IsMatch(code);

        // Assert
        Assert.True(isMatch, $"Valid code '{code}' should match the pattern");
    }

    #endregion

    #region Invalid Promotion Codes - Excluded Characters (I, L, O, U)

    [Theory]
    [InlineData("BAKETA-IAAAAAAA")]  // I は除外
    [InlineData("BAKETA-AAAAAAAI")]  // I は除外
    [InlineData("BAKETA-LAAAAAAA")]  // L は除外
    [InlineData("BAKETA-AAAAAABL")]  // L は除外
    [InlineData("BAKETA-OAAAAAAA")]  // O は除外
    [InlineData("BAKETA-AAAAAAO0")]  // O は除外（0と紛らわしい）
    [InlineData("BAKETA-UAAAAAAA")]  // U は除外
    [InlineData("BAKETA-AAAAAATU")]  // U は除外
    public void PromotionCode_ExcludedCharacters_ShouldNotMatch(string code)
    {
        // Act
        var isMatch = PromotionCodeRegex.IsMatch(code);

        // Assert
        Assert.False(isMatch, $"Code '{code}' with excluded character should not match");
    }

    #endregion

    #region Invalid Promotion Codes - Wrong Format

    [Theory]
    [InlineData("BAKETA-XXXX-XXXX")]  // 古いフォーマット（ハイフン付き）
    [InlineData("BAKETA-1234567")]    // 7文字（短すぎ）
    [InlineData("BAKETA-123456789")]  // 9文字（長すぎ）
    [InlineData("BAKETA12345678")]    // ハイフンなし
    [InlineData("BAKETA-abcdefgh")]   // 小文字
    [InlineData("baketa-ABCDEFGH")]   // プレフィックスが小文字
    [InlineData("OTHER-12345678")]    // 異なるプレフィックス
    [InlineData("12345678")]          // プレフィックスなし
    [InlineData("")]                  // 空文字
    [InlineData("BAKETA-")]           // コード部分なし
    [InlineData("-12345678")]         // プレフィックスなし
    public void PromotionCode_InvalidFormat_ShouldNotMatch(string code)
    {
        // Act
        var isMatch = PromotionCodeRegex.IsMatch(code);

        // Assert
        Assert.False(isMatch, $"Invalid format code '{code}' should not match");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void PromotionCode_AllValidCharacters_ShouldMatch()
    {
        // Base32 Crockford valid characters: 0-9, A-H, J-K, M-N, P-T, V-Z (32 chars)
        // Total: 10 digits + 22 letters = 32 characters
        var validChars = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        // Test first 8 characters
        var code1 = $"BAKETA-{validChars.Substring(0, 8)}";
        Assert.True(PromotionCodeRegex.IsMatch(code1), $"Code '{code1}' should match");

        // Test middle 8 characters
        var code2 = $"BAKETA-{validChars.Substring(8, 8)}";
        Assert.True(PromotionCodeRegex.IsMatch(code2), $"Code '{code2}' should match");

        // Test last 8 characters (with wrap)
        var code3 = $"BAKETA-{validChars.Substring(24, 8)}";
        Assert.True(PromotionCodeRegex.IsMatch(code3), $"Code '{code3}' should match");
    }

    [Fact]
    public void PromotionCode_PatternExcludesCorrectCharacters()
    {
        // Verify that exactly I, L, O, U are excluded
        var excludedChars = new[] { 'I', 'L', 'O', 'U' };
        var validChars = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        foreach (var ch in excludedChars)
        {
            Assert.DoesNotContain(ch, validChars);

            var testCode = $"BAKETA-{ch}AAAAAAA";
            Assert.False(PromotionCodeRegex.IsMatch(testCode),
                $"Character '{ch}' should be excluded from valid codes");
        }
    }

    #endregion
}
