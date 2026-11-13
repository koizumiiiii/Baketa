using Baketa.Core.Translation.Common;
using Xunit;

namespace Baketa.Core.Tests.Translation.Common;

public class TranslationTextNormalizerTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("already  spaced", "already  spaced")]
    [InlineData("Shiori\n\"I had a lot of fun, but... I'd rather not step on a\nmanhole for a while. Ehehe.\"", "Shiori \"I had a lot of fun, but... I'd rather not step on a manhole for a while. Ehehe.\"")]
    [InlineData("こんにちは\n世界", "こんにちは世界")]
    [InlineData("你好\n世界", "你好世界")]
    [InlineData("안녕하세요\n세계", "안녕하세요 세계")]
    [InlineData("Привет\nмир", "Привет мир")]
    [InlineData("日本語\nEnglish", "日本語English")]
    [InlineData("English\n日本語", "English日本語")]
    [InlineData("hello\n！", "hello！")]
    [InlineData("ภาษา\nไทย", "ภาษาไทย")]
    [InlineData("𠮷\n野", "𠮷野")]
    [InlineData("word\n𠮷", "word𠮷")]
    [InlineData("\U0001B000\nABC|\U0001B100\nABC|\U0001AFF0\nABC|\U0001B150\nABC", "\U0001B000ABC|\U0001B100ABC|\U0001AFF0ABC|\U0001B150ABC")]
    [InlineData("ABC\n\U0001B000|ABC\n\U0001B100|ABC\n\U0001AFF0|ABC\n\U0001B150", "ABC\U0001B000|ABC\U0001B100|ABC\U0001AFF0|ABC\U0001B150")]
    [InlineData("葛\U000E0100\nABC", "葛\U000E0100ABC")]
    [InlineData("葛\U000E01EF\nABC", "葛\U000E01EFABC")]
    [InlineData("葛\uFE00\nABC", "葛\uFE00ABC")]
    [InlineData("葛\uFE0F\nABC", "葛\uFE0FABC")]
    [InlineData("\U00020BB7\nABC", "\U00020BB7ABC")]
    [InlineData("\U00020BB7\U000E0100\nABC", "\U00020BB7\U000E0100ABC")]
    [InlineData("葛\u0301\u200D\nABC", "葛\u0301\u200DABC")]
    [InlineData("葛\u200C\nABC", "葛\u200CABC")]
    [InlineData("ABC\n\u200D葛\U000E0100", "ABC\u200D葛\U000E0100")]
    [InlineData("ABC\nDEF", "ABC DEF")]
    [InlineData("cafe\u0301\nABC", "cafe\u0301 ABC")]
    [InlineData("A\uFE0F\nBC", "A\uFE0F BC")]
    [InlineData("葛\U000E0100 \nABC", "葛\U000E0100 ABC")]
    [InlineData("\uFE0F\nABC", "\uFE0F ABC")]
    [InlineData("a \nmanhole", "a manhole")]
    [InlineData("a\n manhole", "a manhole")]
    [InlineData("a  \n  manhole", "a manhole")]
    [InlineData("a\r\nmanhole", "a manhole")]
    [InlineData("a\rmanhole", "a manhole")]
    [InlineData("a\n\n\r\nmanhole", "a manhole")]
    [InlineData("日本\n\n語", "日本語")]
    [InlineData("a\n \t\nmanhole", "a manhole")]
    [InlineData("exam-\nple", "exam-ple")]
    [InlineData("exam- \nple", "exam- ple")]
    [InlineData("123\n456", "123 456")]
    [InlineData("ＨＥＬＬＯ\nＷＯＲＬＤ", "ＨＥＬＬＯ ＷＯＲＬＤ")]
    [InlineData("ｈｅｌｌｏ\nｗｏｒｌｄ", "ｈｅｌｌｏ ｗｏｒｌｄ")]
    [InlineData("１２３\n４５６", "１２３ ４５６")]
    [InlineData("０\n９", "０ ９")]
    [InlineData("Ａ\nＺ", "Ａ Ｚ")]
    [InlineData("ａ\nｚ", "ａ ｚ")]
    [InlineData("残り\n３個", "残り３個")]
    [InlineData("３\n個", "３個")]
    [InlineData("hello!\n\"world\"", "hello! \"world\"")]
    [InlineData("\r\nhello\n", "hello")]
    [InlineData("\r\n\n", "")]
    [InlineData("a\u2028b", "a b")]
    public void JoinWrappedLines_WrappedText_PreservesWordBoundaries(string? input, string expected)
    {
        // Act
        var actual = TranslationTextNormalizer.JoinWrappedLines(input);

        // Assert
        Assert.Equal(expected, actual);
        Assert.Equal(actual, TranslationTextNormalizer.JoinWrappedLines(actual));
    }
}
