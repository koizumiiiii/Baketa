using System;
using System.IO;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation.Local.Onnx;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece.Native;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Tests.Translation;

/// <summary>
/// 実運用環境でのOPUS-MT Native Tokenizer統合テスト
/// 実際の翻訳パイプラインでの動作確認
/// </summary>
public class ProductionIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _projectRoot;
    private readonly ServiceProvider _serviceProvider;
    private bool _disposed;

    public ProductionIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _projectRoot = GetProjectRootDirectory();
        
        // DI コンテナの設定
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public void OpusMtModelsDirectory_ShouldExist()
    {
        // Arrange
        var modelsDir = Path.Combine(_projectRoot, "Models", "SentencePiece");
        
        // Act & Assert
        Directory.Exists(modelsDir).Should().BeTrue($"Models directory should exist at: {modelsDir}");
        
        var modelFiles = Directory.GetFiles(modelsDir, "*.model");
        modelFiles.Should().NotBeEmpty("At least one OPUS-MT model file should exist");
        
        _output.WriteLine($"📁 Models directory: {modelsDir}");
        _output.WriteLine($"🔍 Found {modelFiles.Length} model files:");
        
        foreach (var file in modelFiles)
        {
            var fileInfo = new FileInfo(file);
            _output.WriteLine($"  ✓ {Path.GetFileName(file)} ({fileInfo.Length:N0} bytes)");
        }
    }

    [Fact]
    public async Task OpusMtNativeTokenizer_ShouldWorkInProductionScenario()
    {
        // Arrange
        var modelPath = Path.Combine(_projectRoot, "Models", "SentencePiece", "opus-mt-ja-en.model");
        
        if (!File.Exists(modelPath))
        {
            _output.WriteLine($"⚠️  Skipping test: Model file not found at {modelPath}");
            return;
        }

        var logger = _serviceProvider.GetRequiredService<ILogger<OpusMtNativeTokenizer>>();
        
        // 実際のゲームから抽出されるようなテキストサンプル
        var testTexts = new[]
        {
            "こんにちは、世界！",           // 基本的な日本語
            "レベルアップしました",         // ゲーム用語
            "HPが回復しました",           // 英語混じり
            "アイテムを入手しました",       // カタカナ
            "戦闘開始",                  // 短いテキスト
            "あなたの冒険が始まります。新しい世界へようこそ！" // 長いテキスト
        };

        _output.WriteLine($"🚀 Production scenario test with OPUS-MT Native Tokenizer");
        _output.WriteLine($"📂 Model: {Path.GetFileName(modelPath)}");

        // Act & Assert
        using var tokenizer = await OpusMtNativeTokenizer.CreateAsync(modelPath);
        
        tokenizer.Should().NotBeNull();
        tokenizer.IsInitialized.Should().BeTrue();
        
        _output.WriteLine($"✅ Tokenizer initialized successfully");
        _output.WriteLine($"   VocabularySize: {tokenizer.VocabularySize:N0}");

        foreach (var text in testTexts)
        {
            try
            {
                var startTime = DateTime.UtcNow;
                
                // トークン化実行
                var tokens = tokenizer.Tokenize(text);
                var decoded = tokenizer.Decode(tokens);
                
                var elapsed = DateTime.UtcNow - startTime;
                
                // 基本的な検証
                tokens.Should().NotBeNull();
                decoded.Should().NotBeNull();
                
                if (!string.IsNullOrEmpty(text))
                {
                    tokens.Length.Should().BeGreaterThan(0, "Non-empty text should produce tokens");
                }
                
                _output.WriteLine($"🧪 '{text}' → [{string.Join(", ", tokens)}] ({elapsed.TotalMilliseconds:F2}ms)");
                _output.WriteLine($"   Decoded: '{decoded}'");
                
                // パフォーマンス確認（100ms以下が目標）
                elapsed.TotalMilliseconds.Should().BeLessThan(100, 
                    $"Tokenization should be fast for production use: {text}");
                
            }
            catch (Exception ex)
            {
                _output.WriteLine($"❌ Failed to process '{text}': {ex.Message}");
                throw;
            }
        }
    }

    [Fact]
    public void RealSentencePieceTokenizer_ShouldWorkWithFallback()
    {
        // Arrange
        var modelPath = Path.Combine(_projectRoot, "Models", "SentencePiece", "opus-mt-ja-en.model");
        
        if (!File.Exists(modelPath))
        {
            _output.WriteLine($"⚠️  Skipping test: Model file not found at {modelPath}");
            return;
        }

        var logger = _serviceProvider.GetRequiredService<ILogger<RealSentencePieceTokenizer>>();
        var testText = "こんにちは、世界！";
        
        _output.WriteLine($"🔧 Testing RealSentencePieceTokenizer fallback behavior");

        // Act
        using var tokenizer = new RealSentencePieceTokenizer(modelPath, logger);
        
        // Assert
        tokenizer.Should().NotBeNull();
        tokenizer.ModelPath.Should().Be(modelPath);
        
        var normalized = tokenizer.NormalizeText(testText);
        var tokens = tokenizer.Tokenize(testText);
        var decoded = tokenizer.Decode(tokens);
        
        normalized.Should().NotBeNullOrEmpty();
        tokens.Should().NotBeNull();
        decoded.Should().NotBeNull();
        
        _output.WriteLine($"📝 Input: '{testText}'");
        _output.WriteLine($"🔄 Normalized: '{normalized}'");
        _output.WriteLine($"🔢 Tokens: [{string.Join(", ", tokens)}]");
        _output.WriteLine($"📤 Decoded: '{decoded}'");
        _output.WriteLine($"✅ Fallback implementation working correctly");
        
        // SentencePiece正規化の確認
        normalized.Should().StartWith("▁", "SentencePiece normalization should add prefix space symbol");
    }

    [Fact]
    public void SentencePieceTokenizerFactory_ShouldCreateCorrectImplementation()
    {
        // Arrange
        var modelPath = Path.Combine(_projectRoot, "Models", "SentencePiece", "opus-mt-ja-en.model");
        
        if (!File.Exists(modelPath))
        {
            _output.WriteLine($"⚠️  Skipping test: Model file not found at {modelPath}");
            return;
        }

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        
        _output.WriteLine($"🏭 Testing SentencePieceTokenizerFactory in production scenario");

        // Act
        var nativeTokenizer = SentencePieceTokenizerFactory.Create(
            modelPath, "production-test-native", loggerFactory, useNative: true);
        
        var fallbackTokenizer = SentencePieceTokenizerFactory.Create(
            modelPath, "production-test-fallback", loggerFactory, useTemporary: true);

        // Assert
        nativeTokenizer.Should().NotBeNull();
        fallbackTokenizer.Should().NotBeNull();
        
        nativeTokenizer.Should().BeAssignableTo<ITokenizer>();
        fallbackTokenizer.Should().BeAssignableTo<ITokenizer>();
        
        _output.WriteLine($"✅ Native tokenizer type: {nativeTokenizer.GetType().Name}");
        _output.WriteLine($"✅ Fallback tokenizer type: {fallbackTokenizer.GetType().Name}");
        
        // テストテキストでの動作確認
        var testText = "プロダクションテスト";
        
        var nativeTokens = nativeTokenizer.Tokenize(testText);
        nativeTokens.Should().NotBeNull();
        
        _output.WriteLine($"📝 Test text: '{testText}'");
        _output.WriteLine($"🔢 Native tokens: [{string.Join(", ", nativeTokens)}]");
        
        // Fallback tokenizer (TemporarySentencePieceTokenizer) は初期化が必要
        try
        {
            var fallbackTokens = fallbackTokenizer.Tokenize(testText);
            fallbackTokens.Should().NotBeNull();
            _output.WriteLine($"🔢 Fallback tokens: [{string.Join(", ", fallbackTokens)}]");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("初期化"))
        {
            _output.WriteLine($"⚠️  Fallback tokenizer requires initialization: {ex.Message}");
            _output.WriteLine($"ℹ️  This is expected behavior for TemporarySentencePieceTokenizer");
        }
        
        // Cleanup
        if (nativeTokenizer is IDisposable nativeDisposable)
            nativeDisposable.Dispose();
        if (fallbackTokenizer is IDisposable fallbackDisposable)
            fallbackDisposable.Dispose();
    }

    [Theory]
    [InlineData("")]
    [InlineData("こんにちは")]
    [InlineData("Hello, World!")]
    [InlineData("非常に長いテキストの例として、この文章を使用して実際のゲーム環境でよく見られるような状況をシミュレートします。")]
    public async Task OpusMtNativeTokenizer_ShouldHandleEdgeCases(string input)
    {
        // Arrange
        var modelPath = Path.Combine(_projectRoot, "Models", "SentencePiece", "opus-mt-ja-en.model");
        
        if (!File.Exists(modelPath))
        {
            _output.WriteLine($"⚠️  Skipping test: Model file not found at {modelPath}");
            return;
        }

        _output.WriteLine($"🧪 Edge case test: '{input}' (length: {input.Length})");

        // Act & Assert
        using var tokenizer = await OpusMtNativeTokenizer.CreateAsync(modelPath);
        
        var action = () =>
        {
            var tokens = tokenizer.Tokenize(input);
            var decoded = tokenizer.Decode(tokens);
            return (tokens, decoded);
        };
        
        action.Should().NotThrow("Tokenizer should handle edge cases gracefully");
        
        var (tokens, decoded) = action();
        
        tokens.Should().NotBeNull();
        decoded.Should().NotBeNull();
        
        _output.WriteLine($"🔢 Tokens: [{string.Join(", ", tokens)}] (count: {tokens.Length})");
        _output.WriteLine($"📤 Decoded: '{decoded}'");
        _output.WriteLine($"✅ Edge case handled successfully");
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

    public void Dispose()
    {
        if (_disposed)
            return;
            
        _serviceProvider?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}