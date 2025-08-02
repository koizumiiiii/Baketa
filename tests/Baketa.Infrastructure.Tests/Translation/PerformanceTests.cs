using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece.Native;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Tests.Translation;

/// <summary>
/// OPUS-MT Native Tokenizer パフォーマンステスト
/// 大量テキスト処理での性能測定と最適化ポイント特定
/// </summary>
public class PerformanceTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _projectRoot = GetProjectRootDirectory();
    private bool _disposed;

    [Fact]
    public async Task OpusMtNativeTokenizer_BulkProcessing_ShouldMeetPerformanceRequirements()
    {
        // Arrange
        var modelPath = Path.Combine(_projectRoot, "Models", "SentencePiece", "opus-mt-ja-en.model");
        
        if (!File.Exists(modelPath))
        {
            output.WriteLine($"⚠️  Skipping test: Model file not found at {modelPath}");
            return;
        }

        // 大量テキストデータの準備（実際のゲーム環境を模擬）
        var testTexts = GenerateGameTextDataset(1000); // 1000件のテキスト
        
        output.WriteLine($"🚀 Bulk processing performance test");
        output.WriteLine($"📊 Dataset size: {testTexts.Count:N0} texts");
        output.WriteLine($"📂 Model: {Path.GetFileName(modelPath)}");

        using var tokenizer = await OpusMtNativeTokenizer.CreateAsync(modelPath);
        
        // Warm-up run (JIT最適化)
        output.WriteLine($"🔥 Warm-up run...");
        for (int i = 0; i < Math.Min(10, testTexts.Count); i++)
        {
            tokenizer.Tokenize(testTexts[i]);
        }

        // Act - 性能測定
        var stopwatch = Stopwatch.StartNew();
        var results = new List<PerformanceResult>();
        
        foreach (var text in testTexts)
        {
            var textStopwatch = Stopwatch.StartNew();
            
            var tokens = tokenizer.Tokenize(text);
            var decoded = tokenizer.Decode(tokens);
            
            textStopwatch.Stop();
            
            results.Add(new PerformanceResult
            {
                Text = text,
                TextLength = text.Length,
                TokenCount = tokens.Length,
                ProcessingTimeMs = textStopwatch.Elapsed.TotalMilliseconds,
                TokensPerSecond = tokens.Length / Math.Max(textStopwatch.Elapsed.TotalSeconds, 0.001)
            });
        }
        
        stopwatch.Stop();

        // Assert & Analysis
        var totalTime = stopwatch.Elapsed.TotalMilliseconds;
        var avgTimePerText = totalTime / testTexts.Count;
        var textsPerSecond = testTexts.Count / stopwatch.Elapsed.TotalSeconds;
        var totalTokens = results.Sum(r => r.TokenCount);
        var tokensPerSecond = totalTokens / stopwatch.Elapsed.TotalSeconds;

        output.WriteLine($"\n📈 Performance Results:");
        output.WriteLine($"  Total processing time: {totalTime:F2} ms");
        output.WriteLine($"  Average time per text: {avgTimePerText:F3} ms");
        output.WriteLine($"  Texts per second: {textsPerSecond:F1}");
        output.WriteLine($"  Total tokens processed: {totalTokens:N0}");
        output.WriteLine($"  Tokens per second: {tokensPerSecond:F0}");

        // Performance requirements
        avgTimePerText.Should().BeLessThan(10.0, "Average processing time should be under 10ms per text");
        textsPerSecond.Should().BeGreaterThan(100, "Should process at least 100 texts per second");
        tokensPerSecond.Should().BeGreaterThan(1000, "Should process at least 1000 tokens per second");

        // Detailed analysis
        AnalyzePerformanceCharacteristics(results);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(500)]
    [InlineData(1000)]
    [InlineData(2000)]
    public async Task OpusMtNativeTokenizer_ScalabilityTest_ShouldScaleLinearly(int textCount)
    {
        // Arrange
        var modelPath = Path.Combine(_projectRoot, "Models", "SentencePiece", "opus-mt-ja-en.model");
        
        if (!File.Exists(modelPath))
        {
            output.WriteLine($"⚠️  Skipping test: Model file not found at {modelPath}");
            return;
        }

        var testTexts = GenerateGameTextDataset(textCount);
        
        output.WriteLine($"📊 Scalability test: {textCount:N0} texts");

        using var tokenizer = await OpusMtNativeTokenizer.CreateAsync(modelPath);

        // Act
        var stopwatch = Stopwatch.StartNew();
        
        foreach (var text in testTexts)
        {
            tokenizer.Tokenize(text);
        }
        
        stopwatch.Stop();

        // Assert
        var totalTime = stopwatch.Elapsed.TotalMilliseconds;
        var avgTimePerText = totalTime / textCount;
        var textsPerSecond = textCount / stopwatch.Elapsed.TotalSeconds;

        output.WriteLine($"  Total time: {totalTime:F2} ms");
        output.WriteLine($"  Avg per text: {avgTimePerText:F3} ms");
        output.WriteLine($"  Texts/sec: {textsPerSecond:F1}");

        // Scalability assertions
        avgTimePerText.Should().BeLessThan(5.0, $"Average time should remain low even with {textCount} texts");
        textsPerSecond.Should().BeGreaterThan(200, $"Throughput should remain high with {textCount} texts");
    }

    [Fact]
    public async Task OpusMtNativeTokenizer_MemoryUsage_ShouldRemainStable()
    {
        // Arrange
        var modelPath = Path.Combine(_projectRoot, "Models", "SentencePiece", "opus-mt-ja-en.model");
        
        if (!File.Exists(modelPath))
        {
            output.WriteLine($"⚠️  Skipping test: Model file not found at {modelPath}");
            return;
        }

        var testTexts = GenerateGameTextDataset(500);
        
        output.WriteLine($"🧠 Memory usage stability test");

        using var tokenizer = await OpusMtNativeTokenizer.CreateAsync(modelPath);

        // Measure memory before processing
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var initialMemory = GC.GetTotalMemory(false);
        output.WriteLine($"  Initial memory: {initialMemory / 1024 / 1024:F2} MB");

        // Act - Process texts multiple times
        for (int iteration = 0; iteration < 5; iteration++)
        {
            foreach (var text in testTexts)
            {
                var tokens = tokenizer.Tokenize(text);
                var decoded = tokenizer.Decode(tokens);
            }
            
            var currentMemory = GC.GetTotalMemory(false);
            output.WriteLine($"  After iteration {iteration + 1}: {currentMemory / 1024 / 1024:F2} MB");
        }

        // Force garbage collection and measure final memory
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var finalMemory = GC.GetTotalMemory(false);
        var memoryIncrease = finalMemory - initialMemory;
        
        output.WriteLine($"  Final memory: {finalMemory / 1024 / 1024:F2} MB");
        output.WriteLine($"  Memory increase: {memoryIncrease / 1024 / 1024:F2} MB");

        // Assert
        memoryIncrease.Should().BeLessThan(50 * 1024 * 1024, "Memory increase should be less than 50MB");
    }

    [Fact]
    public async Task OpusMtNativeTokenizer_ConcurrentAccess_ShouldBeThreadSafe()
    {
        // Arrange
        var modelPath = Path.Combine(_projectRoot, "Models", "SentencePiece", "opus-mt-ja-en.model");
        
        if (!File.Exists(modelPath))
        {
            output.WriteLine($"⚠️  Skipping test: Model file not found at {modelPath}");
            return;
        }

        var testTexts = GenerateGameTextDataset(100);
        
        output.WriteLine($"🔄 Concurrent access test");

        using var tokenizer = await OpusMtNativeTokenizer.CreateAsync(modelPath);

        // Act - Concurrent processing
        var tasks = Enumerable.Range(0, 10).Select(async threadId =>
        {
            await Task.Yield(); // 非同期処理を追加
            
            var threadStopwatch = Stopwatch.StartNew();
            var results = new List<int[]>();
            
            foreach (var text in testTexts)
            {
                var tokens = tokenizer.Tokenize(text);
                results.Add(tokens);
            }
            
            threadStopwatch.Stop();
            
            return new
            {
                ThreadId = threadId,
                ProcessingTime = threadStopwatch.Elapsed.TotalMilliseconds,
                Results = results
            };
        }).ToArray();

        var allResults = await Task.WhenAll(tasks);

        // Assert
        allResults.Should().HaveCount(10, "All threads should complete successfully");
        
        foreach (var result in allResults)
        {
            result.Results.Should().HaveCount(testTexts.Count, $"Thread {result.ThreadId} should process all texts");
            result.ProcessingTime.Should().BeLessThan(5000, $"Thread {result.ThreadId} should complete within 5 seconds");
            
            output.WriteLine($"  Thread {result.ThreadId}: {result.ProcessingTime:F2} ms");
        }

        // Verify consistency
        var firstThreadResults = allResults[0].Results;
        foreach (var otherResult in allResults.Skip(1))
        {
            for (int i = 0; i < firstThreadResults.Count; i++)
            {
                otherResult.Results[i].Should().BeEquivalentTo(firstThreadResults[i], 
                    $"Results should be consistent across threads for text {i}");
            }
        }
        
        output.WriteLine($"✅ All threads produced consistent results");
    }

    [Fact]
    public void RealSentencePieceTokenizer_PerformanceComparison_WithNativeTokenizer()
    {
        // Arrange
        var modelPath = Path.Combine(_projectRoot, "Models", "SentencePiece", "opus-mt-ja-en.model");
        
        if (!File.Exists(modelPath))
        {
            output.WriteLine($"⚠️  Skipping test: Model file not found at {modelPath}");
            return;
        }

        var testTexts = GenerateGameTextDataset(100).Take(50).ToList(); // 小さめのデータセット
        var logger = NullLogger<RealSentencePieceTokenizer>.Instance;
        
        output.WriteLine($"⚖️  Performance comparison: Real vs Native");

        using var realTokenizer = new RealSentencePieceTokenizer(modelPath, logger);

        // Measure RealSentencePieceTokenizer performance
        var realStopwatch = Stopwatch.StartNew();
        foreach (var text in testTexts)
        {
            realTokenizer.Tokenize(text);
        }
        realStopwatch.Stop();

        var realTime = realStopwatch.Elapsed.TotalMilliseconds;
        var realTextsPerSecond = testTexts.Count / realStopwatch.Elapsed.TotalSeconds;

        output.WriteLine($"  RealSentencePieceTokenizer:");
        output.WriteLine($"    Total time: {realTime:F2} ms");
        output.WriteLine($"    Texts/sec: {realTextsPerSecond:F1}");

        // Performance should be reasonable even with fallback implementation
        realTime.Should().BeLessThan(1000, "RealSentencePieceTokenizer should complete within 1 second");
        realTextsPerSecond.Should().BeGreaterThan(50, "Should process at least 50 texts per second");
    }

    private List<string> GenerateGameTextDataset(int count)
    {
        var gameTexts = new[]
        {
            // 短いテキスト
            "HP回復",
            "レベルアップ",
            "アイテム入手",
            "戦闘開始",
            "ミッション完了",
            "セーブしました",
            
            // 中程度のテキスト
            "こんにちは、冒険者よ！",
            "新しいクエストが利用可能です。",
            "あなたのレベルが上がりました！",
            "アイテムをインベントリに追加しました。",
            "この街へようこそ！何かお手伝いできることはありますか？",
            "危険な敵が近づいています。戦闘の準備をしてください。",
            
            // 長いテキスト
            "遠い昔、この大陸には平和が訪れていました。しかし、闇の勢力が復活し、世界は再び混沌に包まれようとしています。",
            "あなたは選ばれし勇者として、この世界を救う使命を負っています。長い旅が始まりますが、仲間たちと共に立ち向かいましょう。",
            "古代の魔法書には、失われた文明の秘密が記されています。この知識を使って、強大な敵を打ち倒すことができるでしょう。",
            "クエストを完了すると、経験値とゴールドが獲得できます。また、まれに強力な装備品も手に入れることができるかもしれません。",
            
            // 特殊文字・記号を含むテキスト
            "HP: 100/100",
            "経験値: 1,250 XP",
            "ゴールド: ￥50,000",
            "攻撃力+25%アップ！",
            "レア装備【神剣エクスカリバー】を入手しました！",
            "クリティカルヒット！ダメージ×2.5倍！",
            
            // 英語混じりのテキスト
            "Newゲームを開始しますか？",
            "Statusウィンドウを開きました",
            "Saveデータを読み込み中...",
            "Battle Systemが初期化されました",
            "Quest Logを確認してください",
            "Game Overです。Continue しますか？"
        };

        var random = new Random(42); // 固定シードで再現可能性を確保
        var result = new List<string>();
        
        for (int i = 0; i < count; i++)
        {
            // テキストをランダムに選択し、バリエーションを作成
            var baseText = gameTexts[random.Next(gameTexts.Length)];
            
            // 20%の確率でテキストを繰り返し
            if (random.NextDouble() < 0.2)
            {
                var repeatCount = random.Next(2, 4);
                baseText = string.Join(" ", Enumerable.Repeat(baseText, repeatCount));
            }
            
            result.Add(baseText);
        }
        
        return result;
    }

    private void AnalyzePerformanceCharacteristics(List<PerformanceResult> results)
    {
        output.WriteLine($"\n🔍 Performance Analysis:");
        
        // Text length vs processing time correlation
        var shortTexts = results.Where(r => r.TextLength <= 10).ToList();
        var mediumTexts = results.Where(r => r.TextLength > 10 && r.TextLength <= 50).ToList();
        var longTexts = results.Where(r => r.TextLength > 50).ToList();
        
        if (shortTexts.Count > 0)
        {
            var avgShortTime = shortTexts.Average(r => r.ProcessingTimeMs);
            output.WriteLine($"  Short texts (≤10 chars): {avgShortTime:F3} ms avg");
        }
        
        if (mediumTexts.Count > 0)
        {
            var avgMediumTime = mediumTexts.Average(r => r.ProcessingTimeMs);
            output.WriteLine($"  Medium texts (11-50 chars): {avgMediumTime:F3} ms avg");
        }
        
        if (longTexts.Count > 0)
        {
            var avgLongTime = longTexts.Average(r => r.ProcessingTimeMs);
            output.WriteLine($"  Long texts (>50 chars): {avgLongTime:F3} ms avg");
        }

        // Token processing rate
        var avgTokensPerSecond = results.Average(r => r.TokensPerSecond);
        var maxTokensPerSecond = results.Max(r => r.TokensPerSecond);
        var minTokensPerSecond = results.Min(r => r.TokensPerSecond);
        
        output.WriteLine($"  Token processing rate:");
        output.WriteLine($"    Average: {avgTokensPerSecond:F0} tokens/sec");
        output.WriteLine($"    Max: {maxTokensPerSecond:F0} tokens/sec");
        output.WriteLine($"    Min: {minTokensPerSecond:F0} tokens/sec");

        // Identify potential bottlenecks
        var slowestTexts = results.OrderByDescending(r => r.ProcessingTimeMs).Take(5).ToList();
        if (slowestTexts.Count > 0)
        {
            output.WriteLine($"  Slowest processing times:");
            foreach (var slow in slowestTexts)
            {
                output.WriteLine($"    {slow.ProcessingTimeMs:F3} ms: \"{slow.Text[..Math.Min(50, slow.Text.Length)]}...\"");
            }
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

    public void Dispose()
    {
        if (_disposed)
            return;
            
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private sealed record PerformanceResult
    {
        public string Text { get; init; } = string.Empty;
        public int TextLength { get; init; }
        public int TokenCount { get; init; }
        public double ProcessingTimeMs { get; init; }
        public double TokensPerSecond { get; init; }
    }
}
