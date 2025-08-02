using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece.Native;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece.Native.Optimizations;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Tests.Translation;

/// <summary>
/// OPUS-MT Native Tokenizer メモリ使用量最適化テスト
/// 最適化前後のメモリ効率とパフォーマンス比較検証
/// </summary>
public class MemoryOptimizationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _projectRoot;
    private bool _disposed;

    public MemoryOptimizationTests(ITestOutputHelper output)
    {
        _output = output;
        _projectRoot = GetProjectRootDirectory();
    }

    [Fact]
    public async Task MemoryOptimizedTokenizer_ShouldUseSignificantlyLessMemory()
    {
        // Arrange
        var modelPath = Path.Combine(_projectRoot, "Models", "SentencePiece", "opus-mt-ja-en.model");
        
        if (!File.Exists(modelPath))
        {
            _output.WriteLine($"⚠️  Skipping test: Model file not found at {modelPath}");
            return;
        }

        _output.WriteLine($"🧠 Memory optimization comparison test");
        _output.WriteLine($"📂 Model: {Path.GetFileName(modelPath)}");

        // 初期メモリ測定
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var initialMemory = GC.GetTotalMemory(false);

        // Act & Assert - 通常のトークナイザー
        _output.WriteLine($"\n📊 Standard OpusMtNativeTokenizer:");
        using var standardTokenizer = await OpusMtNativeTokenizer.CreateAsync(modelPath);
        
        GC.Collect();
        var standardMemory = GC.GetTotalMemory(false) - initialMemory;
        _output.WriteLine($"  Memory usage: {standardMemory / 1024:F1} KB");

        // テストデータでの処理
        var testTexts = GenerateTestDataset(100);
        var standardStopwatch = Stopwatch.StartNew();
        
        foreach (var text in testTexts)
        {
            var tokens = standardTokenizer.Tokenize(text);
            var decoded = standardTokenizer.Decode(tokens);
        }
        
        standardStopwatch.Stop();
        var standardTime = standardStopwatch.Elapsed.TotalMilliseconds;
        _output.WriteLine($"  Processing time: {standardTime:F2} ms");

        // メモリ最適化版トークナイザー
        _output.WriteLine($"\n🚀 MemoryOptimizedOpusMtTokenizer:");
        using var optimizedTokenizer = await MemoryOptimizedOpusMtTokenizer.CreateOptimizedAsync(modelPath);
        
        GC.Collect();
        var optimizedMemory = GC.GetTotalMemory(false) - initialMemory - standardMemory;
        _output.WriteLine($"  Memory usage: {optimizedMemory / 1024:F1} KB");
        
        var memoryStats = optimizedTokenizer.MemoryStatistics;
        _output.WriteLine($"  Memory breakdown: {memoryStats}");

        var optimizedStopwatch = Stopwatch.StartNew();
        
        foreach (var text in testTexts)
        {
            var tokens = optimizedTokenizer.Tokenize(text);
            var decoded = optimizedTokenizer.Decode(tokens);
        }
        
        optimizedStopwatch.Stop();
        var optimizedTime = optimizedStopwatch.Elapsed.TotalMilliseconds;
        _output.WriteLine($"  Processing time: {optimizedTime:F2} ms");

        // メモリ効率の検証
        var memoryReduction = ((double)(standardMemory - optimizedMemory) / standardMemory) * 100;
        var speedRatio = optimizedTime / standardTime;
        
        _output.WriteLine($"\n📈 Optimization Results:");
        _output.WriteLine($"  Memory reduction: {memoryReduction:F1}% ({standardMemory / 1024:F1} KB → {optimizedMemory / 1024:F1} KB)");
        _output.WriteLine($"  Speed ratio: {speedRatio:F2}x (optimized/standard)");

        // Assert
        optimizedMemory.Should().BeLessThan(standardMemory, "Optimized version should use less memory");
        memoryReduction.Should().BeGreaterThan(10, "Should achieve at least 10% memory reduction");
        speedRatio.Should().BeLessThan(2.0, "Optimized version should not be significantly slower");
    }

    [Fact]
    public async Task MemoryOptimizedTokenizer_ConsistencyWithStandardVersion()
    {
        // Arrange
        var modelPath = Path.Combine(_projectRoot, "Models", "SentencePiece", "opus-mt-ja-en.model");
        
        if (!File.Exists(modelPath))
        {
            _output.WriteLine($"⚠️  Skipping test: Model file not found at {modelPath}");
            return;
        }

        _output.WriteLine($"🔍 Consistency verification between standard and optimized versions");

        using var standardTokenizer = await OpusMtNativeTokenizer.CreateAsync(modelPath);
        using var optimizedTokenizer = await MemoryOptimizedOpusMtTokenizer.CreateOptimizedAsync(modelPath);

        var testTexts = new[]
        {
            "こんにちは、世界！",
            "レベルアップしました",
            "HPが回復しました",
            "アイテムを入手しました",
            "戦闘開始",
            "あなたの冒険が始まります。新しい世界へようこそ！",
            "", // 空文字
            "ABC123", // 英数字
            "🎮🎯🚀", // 絵文字
            "　全角スペース　", // 全角文字
        };

        // Act & Assert
        for (int i = 0; i < testTexts.Length; i++)
        {
            var text = testTexts[i];
            _output.WriteLine($"Testing text {i}: '{text}'");

            var standardTokens = standardTokenizer.Tokenize(text);
            var optimizedTokens = optimizedTokenizer.Tokenize(text);

            var standardDecoded = standardTokenizer.Decode(standardTokens);
            var optimizedDecoded = optimizedTokenizer.Decode(optimizedTokens);

            // トークン化結果の一致確認
            optimizedTokens.Should().BeEquivalentTo(standardTokens, 
                $"Tokenization should be consistent for text: '{text}'");

            // デコード結果の一致確認
            optimizedDecoded.Should().Be(standardDecoded, 
                $"Decoding should be consistent for text: '{text}'");

            _output.WriteLine($"  ✅ Tokens: [{string.Join(", ", standardTokens)}]");
            _output.WriteLine($"  ✅ Decoded: '{standardDecoded}'");
        }

        _output.WriteLine($"✅ All consistency tests passed");
    }

    [Fact]
    public async Task StringInternPool_ShouldReduceMemoryUsage()
    {
        // Arrange
        var modelPath = Path.Combine(_projectRoot, "Models", "SentencePiece", "opus-mt-ja-en.model");
        
        if (!File.Exists(modelPath))
        {
            _output.WriteLine($"⚠️  Skipping test: Model file not found at {modelPath}");
            return;
        }

        _output.WriteLine($"🔤 StringInternPool memory optimization test");

        // Act
        using var optimizedTokenizer = await MemoryOptimizedOpusMtTokenizer.CreateOptimizedAsync(modelPath);
        var stats = optimizedTokenizer.MemoryStatistics;

        // Assert
        stats.InternedStringCount.Should().BeGreaterThan(0, "Should have interned strings");
        stats.InternPoolMemory.Should().BeGreaterThan(0, "Intern pool should consume memory");
        
        // インターン化効果の確認（実際の語彙数より少ないメモリ使用量）
        var avgStringLength = 5; // 推定平均文字列長
        var estimatedRawMemory = stats.VocabularyCount * avgStringLength * sizeof(char) * 3; // 語彙+逆引き+オーバーヘッド
        
        stats.InternPoolMemory.Should().BeLessThan(estimatedRawMemory, 
            "Intern pool should use less memory than raw string storage");

        _output.WriteLine($"📊 StringIntern Statistics:");
        _output.WriteLine($"  Vocabulary entries: {stats.VocabularyCount:N0}");
        _output.WriteLine($"  Interned strings: {stats.InternedStringCount:N0}");
        _output.WriteLine($"  Intern pool memory: {stats.InternPoolMemory / 1024:F1} KB");
        _output.WriteLine($"  Estimated raw memory: {estimatedRawMemory / 1024:F1} KB");
        _output.WriteLine($"  Memory efficiency: {(1.0 - (double)stats.InternPoolMemory / estimatedRawMemory) * 100:F1}%");
    }

    [Fact]
    public async Task OptimizedTrieNode_ShouldProvideEfficientLookup()
    {
        // Arrange
        var modelPath = Path.Combine(_projectRoot, "Models", "SentencePiece", "opus-mt-ja-en.model");
        
        if (!File.Exists(modelPath))
        {
            _output.WriteLine($"⚠️  Skipping test: Model file not found at {modelPath}");
            return;
        }

        _output.WriteLine($"🌳 OptimizedTrieNode performance test");

        using var optimizedTokenizer = await MemoryOptimizedOpusMtTokenizer.CreateOptimizedAsync(modelPath);
        var stats = optimizedTokenizer.MemoryStatistics;

        // 高頻度テキストでのルックアップ性能測定
        var testTexts = Enumerable.Repeat("こんにちは世界", 1000).ToArray();
        
        var stopwatch = Stopwatch.StartNew();
        foreach (var text in testTexts)
        {
            optimizedTokenizer.Tokenize(text);
        }
        stopwatch.Stop();

        var avgLookupTime = stopwatch.Elapsed.TotalMilliseconds / testTexts.Length;

        // Assert
        stats.TrieNodeCount.Should().BeGreaterThan(0, "Should have trie nodes");
        avgLookupTime.Should().BeLessThan(0.1, "Average lookup should be under 0.1ms");

        _output.WriteLine($"🌳 Trie Performance:");
        _output.WriteLine($"  Total nodes: {stats.TrieNodeCount:N0}");
        _output.WriteLine($"  Trie memory: {stats.TrieMemory / 1024:F1} KB");
        _output.WriteLine($"  Average lookup time: {avgLookupTime:F4} ms");
        _output.WriteLine($"  Lookups per second: {1000 / avgLookupTime:F0}");
    }

    [Fact]
    public async Task MemoryOptimization_ShouldMaintainPerformance()
    {
        // Arrange
        var modelPath = Path.Combine(_projectRoot, "Models", "SentencePiece", "opus-mt-ja-en.model");
        
        if (!File.Exists(modelPath))
        {
            _output.WriteLine($"⚠️  Skipping test: Model file not found at {modelPath}");
            return;
        }

        _output.WriteLine($"⚡ Memory optimization performance impact test");

        using var optimizedTokenizer = await MemoryOptimizedOpusMtTokenizer.CreateOptimizedAsync(modelPath);
        
        var testDataset = GenerateTestDataset(500);

        // 最適化前のベースライン測定
        var baselineStopwatch = Stopwatch.StartNew();
        foreach (var text in testDataset)
        {
            var tokens = optimizedTokenizer.Tokenize(text);
            var decoded = optimizedTokenizer.Decode(tokens);
        }
        baselineStopwatch.Stop();
        var baselineTime = baselineStopwatch.Elapsed.TotalMilliseconds;

        // メモリ最適化実行
        _output.WriteLine($"🔧 Executing memory optimization...");
        optimizedTokenizer.OptimizeMemory();

        // 最適化後の性能測定
        var optimizedStopwatch = Stopwatch.StartNew();
        foreach (var text in testDataset)
        {
            var tokens = optimizedTokenizer.Tokenize(text);
            var decoded = optimizedTokenizer.Decode(tokens);
        }
        optimizedStopwatch.Stop();
        var optimizedTime = optimizedStopwatch.Elapsed.TotalMilliseconds;

        var performanceRatio = optimizedTime / baselineTime;

        // Assert
        performanceRatio.Should().BeLessThan(1.2, "Performance should not degrade by more than 20%");

        _output.WriteLine($"📊 Performance Impact:");
        _output.WriteLine($"  Baseline time: {baselineTime:F2} ms");
        _output.WriteLine($"  Optimized time: {optimizedTime:F2} ms");
        _output.WriteLine($"  Performance ratio: {performanceRatio:F3}x");
        _output.WriteLine($"  Impact: {(performanceRatio - 1) * 100:+F1}%");

        var memoryReport = optimizedTokenizer.GetMemoryReport();
        _output.WriteLine($"📋 Final memory state: {memoryReport}");
    }

    private static string[] GenerateTestDataset(int count)
    {
        var baseTexts = new[]
        {
            "こんにちは",
            "レベルアップ",
            "アイテム入手",
            "戦闘開始",
            "HP回復",
            "ミッション完了",
            "新しいクエストが利用可能です",
            "あなたのレベルが上がりました",
            "強力な装備を入手しました",
            "ボスを倒しました"
        };

        var random = new Random(42);
        var result = new string[count];
        
        for (int i = 0; i < count; i++)
        {
            result[i] = baseTexts[random.Next(baseTexts.Length)];
        }
        
        return result;
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
            
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}