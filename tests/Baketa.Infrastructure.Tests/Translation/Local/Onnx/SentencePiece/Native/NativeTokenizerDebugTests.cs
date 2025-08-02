using System;
using System.IO;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece.Native;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Tests.Translation.Local.Onnx.SentencePiece.Native;

/// <summary>
/// Native Tokenizerのデバッグテスト
/// </summary>
public class NativeTokenizerDebugTests(ITestOutputHelper output)
{
    [Fact]
    public void Debug_NativeTokenizer_BasicOperation()
    {
        // Arrange
        var projectRoot = GetProjectRootDirectory();
        var modelPath = Path.Combine(projectRoot, "Models", "SentencePiece", "helsinki-opus-mt-ja-en.model");
        
        if (!File.Exists(modelPath))
        {
            output.WriteLine($"⚠️  Skipping test: Model not found at {modelPath}");
            return;
        }

        output.WriteLine($"📂 Model path: {modelPath}");
        output.WriteLine($"📁 Model exists: {File.Exists(modelPath)}");
        output.WriteLine($"📄 Model size: {new FileInfo(modelPath).Length / 1024} KB");

        try
        {
            // Act - Native Tokenizer初期化
            using var tokenizer = new OpusMtNativeTokenizer(modelPath);
            
            output.WriteLine($"✅ Tokenizer initialized successfully");
            output.WriteLine($"🔧 Tokenizer ID: {tokenizer.TokenizerId}");

            // テストテキスト
            var testTexts = new[]
            {
                "hello",
                "こんにちは",
                "hello world",
                "これは",
                "a"
            };

            foreach (var text in testTexts)
            {
                output.WriteLine($"📝 Testing: '{text}'");
                
                try
                {
                    var tokens = tokenizer.Tokenize(text);
                    output.WriteLine($"   ✅ Tokens: [{string.Join(", ", tokens)}] (count: {tokens.Length})");
                    
                    if (tokens.Length > 0)
                    {
                        var decoded = tokenizer.Decode(tokens);
                        output.WriteLine($"   🔄 Decoded: '{decoded}'");
                    }
                }
                catch (Exception ex)
                {
                    output.WriteLine($"   ❌ Error: {ex.Message}");
                }
                
                output.WriteLine("");
            }

            // 特殊トークンID確認
            try
            {
                var bosId = tokenizer.GetSpecialTokenId("BOS");
                var eosId = tokenizer.GetSpecialTokenId("EOS");
                var unkId = tokenizer.GetSpecialTokenId("UNK");
                var padId = tokenizer.GetSpecialTokenId("PAD");
                
                output.WriteLine($"🎯 Special tokens: BOS={bosId}, EOS={eosId}, UNK={unkId}, PAD={padId}");
            }
            catch (Exception ex)
            {
                output.WriteLine($"❌ Special token error: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            output.WriteLine($"❌ Tokenizer initialization failed: {ex.Message}");
            output.WriteLine($"📋 Stack trace: {ex.StackTrace}");
            throw;
        }
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