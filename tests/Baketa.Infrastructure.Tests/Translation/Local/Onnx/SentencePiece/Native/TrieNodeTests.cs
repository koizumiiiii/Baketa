using Xunit;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece.Native.Models;

namespace Baketa.Infrastructure.Tests.Translation.Local.Onnx.SentencePiece.Native;

/// <summary>
/// TrieNodeクラスの単体テスト
/// SentencePiece BPE仕様に従った最長一致検索をテスト
/// </summary>
public class TrieNodeTests
{
    [Fact]
    public void AddToken_Should_AddTokenCorrectly()
    {
        // Arrange
        var root = new TrieNode();
        
        // Act
        root.AddToken("hello", 1);
        root.AddToken("hell", 2);
        root.AddToken("he", 3);
        
        // Assert
        var result1 = root.FindLongestMatch("hello world".AsSpan());
        Assert.Equal((1, 5), result1); // "hello" が最長マッチ
        
        var result2 = root.FindLongestMatch("hell".AsSpan());
        Assert.Equal((2, 4), result2); // "hell" が完全マッチ
        
        var result3 = root.FindLongestMatch("he".AsSpan());
        Assert.Equal((3, 2), result3); // "he" が完全マッチ
    }
    
    [Fact]
    public void FindLongestMatch_Should_ReturnLongestMatch()
    {
        // Arrange
        var root = new TrieNode();
        root.AddToken("▁hello", 1);
        root.AddToken("▁hel", 2);
        root.AddToken("▁he", 3);
        root.AddToken("▁", 4);
        
        // Act
        var result = root.FindLongestMatch("▁hello world".AsSpan());
        
        // Assert
        Assert.Equal((1, 6), result); // "▁hello" が最長マッチ
    }
    
    [Fact]
    public void FindLongestMatch_Should_ReturnFirstInLexicalOrderForSameLength()
    {
        // Arrange
        var root = new TrieNode();
        root.AddToken("ab", 1);
        root.AddToken("ac", 2);
        
        // Act
        var result1 = root.FindLongestMatch("ab".AsSpan());
        var result2 = root.FindLongestMatch("ac".AsSpan());
        
        // Assert - 同じ長さの場合は辞書順で最初のもの
        Assert.Equal((1, 2), result1);
        Assert.Equal((2, 2), result2);
    }
    
    [Fact]
    public void FindLongestMatch_Should_ReturnUnkForUnknownText()
    {
        // Arrange
        var root = new TrieNode();
        root.AddToken("hello", 1);
        
        // Act
        var result = root.FindLongestMatch("xyz".AsSpan());
        
        // Assert
        Assert.Equal((1, 1), result); // UNKトークンで1文字進む
    }
    
    [Fact]
    public void FindLongestMatch_Should_HandleEmptyInput()
    {
        // Arrange
        var root = new TrieNode();
        root.AddToken("hello", 1);
        
        // Act
        var result = root.FindLongestMatch("".AsSpan());
        
        // Assert
        Assert.Equal((1, 1), result); // 空文字の場合もUNKトークン
    }
    
    [Theory]
    [InlineData("▁Hello ▁World", 6)]
    [InlineData("▁こんにちは", 6)]
    [InlineData("▁test123", 5)]
    public void FindLongestMatch_Should_HandleSentencePieceTokens(string input, int expectedLength)
    {
        // Arrange
        var root = new TrieNode();
        root.AddToken("▁Hello", 1);
        root.AddToken("▁World", 2);
        root.AddToken("▁こんにちは", 3);
        root.AddToken("▁test", 4);
        
        // Act
        var result = root.FindLongestMatch(input.AsSpan());
        
        // Assert
        Assert.Equal(expectedLength, result.length);
    }
}