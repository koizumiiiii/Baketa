using System;
using System.IO;
using System.Threading.Tasks;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece.Native;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Tests.Translation.Local.Onnx.SentencePiece.Native;

/// <summary>
/// Trie木初期化デバッグテスト
/// </summary>
public class DebugTrieTests(ITestOutputHelper output)
{
    [Fact]
    public async Task DebugTrieInitialization_ShouldWork()
    {
        // Arrange
        var projectRoot = GetProjectRootDirectory();
        var modelPath = Path.Combine(projectRoot, "Models", "SentencePiece", "opus-mt-ja-en.model");
        
        if (!File.Exists(modelPath))
        {
            output.WriteLine($"⚠️  Skipping test: Model file not found at {modelPath}");
            return;
        }

        using var loggerFactory = LoggerFactory.Create(builder => 
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<OpusMtNativeTokenizer>();

        // Act
        using var tokenizer = await OpusMtNativeTokenizer.CreateAsync(modelPath);
        
        // Debug - 内部状態を確認
        output.WriteLine($"Tokenizer initialized: {tokenizer is not null}");
        
        // 簡単なトークン化テスト
        var testText = "こんにちは";
        var tokens = tokenizer.Tokenize(testText);
        var decoded = tokenizer.Decode(tokens) ?? string.Empty;
        
        output.WriteLine($"Input: '{testText}'");
        output.WriteLine($"Tokens: [{string.Join(", ", tokens)}]");
        output.WriteLine($"Decoded: '{decoded}'");
        
        // Assert
        tokens.Should().NotBeEmpty();
        decoded.Should().NotBeNull();
    }
    
    private static string GetProjectRootDirectory()
    {
        var currentDir = Directory.GetCurrentDirectory();
        while (currentDir != null && !File.Exists(Path.Combine(currentDir, "Baketa.sln")))
        {
            currentDir = Directory.GetParent(currentDir)?.FullName;
        }
        return currentDir ?? throw new DirectoryNotFoundException("Project root not found");
    }
}