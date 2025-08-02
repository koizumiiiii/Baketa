using System;
using System.IO;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece.Native;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Tests.Translation.Local.Onnx.SentencePiece.Native;

/// <summary>
/// Native Tokenizer Factory統合テスト
/// </summary>
public class NativeTokenizerFactoryTests(ITestOutputHelper output)
{
    [Fact]
    public void OpusMtNativeTokenizer_ShouldCreateWithValidModel()
    {
        // Arrange
        var mockModelPath = "test.model";
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<OpusMtNativeTokenizer>();

        // Act & Assert - モデルファイルが存在しない場合でもトークナイザーは作成される
        var tokenizer = new OpusMtNativeTokenizer(mockModelPath, "Test Tokenizer", logger);
        
        tokenizer.Should().NotBeNull();
        tokenizer.Name.Should().Be("Test Tokenizer");
        output.WriteLine($"Created tokenizer: {tokenizer.Name}");

        tokenizer.Dispose();
    }

    [Fact]
    public void RealSentencePieceTokenizer_ShouldCreateWithValidModel()
    {
        // Arrange
        var mockModelPath = "test.model";
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<RealSentencePieceTokenizer>();

        // Act
        var tokenizer = new RealSentencePieceTokenizer(mockModelPath, logger);

        // Assert
        tokenizer.Should().NotBeNull();
        tokenizer.Name.Should().Contain("SentencePiece Tokenizer");
        
        output.WriteLine($"Real tokenizer type: {tokenizer.GetType().Name}");
        output.WriteLine($"Tokenizer name: {tokenizer.Name}");

        tokenizer.Dispose();
    }

    [Fact]
    public void ITokenizer_InterfaceCompatibility_ShouldWork()
    {
        // Arrange
        var mockModelPath = "test.model";
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<OpusMtNativeTokenizer>();

        // Act
        var concreteTokenizer = new OpusMtNativeTokenizer(mockModelPath, "Interface Test", logger);

        // Assert - ITokenizerインターフェース経由でアクセス可能
        concreteTokenizer.Should().NotBeNull();
        concreteTokenizer.Name.Should().Be("Interface Test");
        concreteTokenizer.VocabularySize.Should().BeGreaterOrEqualTo(0);
        
        // 基本的なトークナイザー機能をテスト（インターフェース経由）
#pragma warning disable CA1859 // インターフェース互換性をテストするため意図的に使用
        ITokenizer tokenizer = concreteTokenizer;
#pragma warning restore CA1859
        var tokens = tokenizer.Tokenize("test");
        var decoded = tokenizer.Decode(tokens);
        
        tokens.Should().NotBeNull();
        decoded.Should().NotBeNull();
        
        output.WriteLine($"Interface compatible tokenizer: {concreteTokenizer.GetType().Name}");

        // Disposeは具象型で実行
        concreteTokenizer.Dispose();
    }
}
