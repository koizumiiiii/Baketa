using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece.Native;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Tests.Translation.Local.Onnx.SentencePiece;

/// <summary>
/// Native vs Real Tokenizer パフォーマンス比較ベンチマーク
/// 統合効果の定量的評価
/// </summary>
public class TokenizerPerformanceBenchmarks(ITestOutputHelper output) : IDisposable
{
    private readonly string _projectRoot = GetProjectRootDirectory();
    private bool _disposed;

    [Fact]
    public async Task CompareTokenizers_ProcessingSpeed_NativeShouldBeFaster()
    {
        // Arrange
        var modelPath = Path.Combine(_projectRoot, "Models", "SentencePiece", "opus-mt-ja-en.model");
        
        if (!File.Exists(modelPath))
        {
            output.WriteLine($"⚠️  Skipping test: Model file not found at {modelPath}");
            return;
        }

        var testTexts = GenerateTestDataset(500); // 500件のテキストでテスト
        
        output.WriteLine($"🏁 Tokenizer Performance Benchmark");
        output.WriteLine($"📊 Dataset: {testTexts.Count:N0} texts");
        output.WriteLine($"📝 Total characters: {testTexts.Sum(t => t.Length):N0}");
        output.WriteLine($"📂 Model: {Path.GetFileName(modelPath)}");
        output.WriteLine("");

        // Native Tokenizer テスト
        var nativeResults = await BenchmarkNativeTokenizer(modelPath, testTexts);
        
        // Real Tokenizer テスト
        var realResults = await BenchmarkRealTokenizer(modelPath, testTexts);

        // 結果比較とレポート
        GeneratePerformanceReport(nativeResults, realResults);
        
        // アサーション - Native が高速であることを期待
        nativeResults.AverageLatencyMs.Should().BeLessOrEqualTo(realResults.AverageLatencyMs * 1.2, 
            "Native tokenizer should be faster or comparable to Real tokenizer");
    }

    [Fact]
    public async Task CompareTokenizers_MemoryUsage_NativeShouldBeEfficient()
    {
        // Arrange
        var modelPath = Path.Combine(_projectRoot, "Models", "SentencePiece", "opus-mt-ja-en.model");
        
        if (!File.Exists(modelPath))
        {
            output.WriteLine($"⚠️  Skipping test: Model file not found at {modelPath}");
            return;
        }

        var testTexts = GenerateTestDataset(100); // メモリテスト用

        output.WriteLine($"💾 Memory Usage Benchmark");
        output.WriteLine($"📊 Dataset: {testTexts.Count:N0} texts");
        output.WriteLine("");

        // メモリ使用量比較
        var nativeMemory = await MeasureMemoryUsage("Native", () => 
            BenchmarkNativeTokenizerMemory(modelPath, testTexts));
            
        var realMemory = await MeasureMemoryUsage("Real", () => 
            BenchmarkRealTokenizerMemory(modelPath, testTexts));

        output.WriteLine($"📈 Memory Comparison:");
        output.WriteLine($"  Native: {nativeMemory:N0} bytes");
        output.WriteLine($"  Real:   {realMemory:N0} bytes");
        output.WriteLine($"  Ratio:  {(double)nativeMemory / realMemory:F2}x");

        // Native の方がメモリ効率が良いことを期待
        nativeMemory.Should().BeLessOrEqualTo((long)(realMemory * 1.5), 
            "Native tokenizer should be memory efficient");
    }

    [Fact]
    public async Task CompareTokenizers_Accuracy_ShouldProduceSimilarResults()
    {
        // Arrange
        var modelPath = Path.Combine(_projectRoot, "Models", "SentencePiece", "opus-mt-ja-en.model");
        
        if (!File.Exists(modelPath))
        {
            output.WriteLine($"⚠️  Skipping test: Model file not found at {modelPath}");
            return;
        }

        var testTexts = new[]
        {
            "こんにちは、世界！",
            "これは翻訳テストです。",
            "OPUS-MTトークナイザーの比較実験",
            "パフォーマンステスト実行中",
            "日本語と英語の翻訳精度確認"
        };

        output.WriteLine($"🎯 Accuracy Comparison Test");
        output.WriteLine($"📊 Test phrases: {testTexts.Length}");
        output.WriteLine("");

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

        // Native Tokenizer
        using var nativeTokenizer = await OpusMtNativeTokenizer.CreateAsync(modelPath);
        
        // Real Tokenizer
        var realLogger = loggerFactory.CreateLogger<RealSentencePieceTokenizer>();
        using var realTokenizer = new RealSentencePieceTokenizer(modelPath, realLogger);

        var results = new List<AccuracyResult>();

        foreach (var text in testTexts)
        {
            var nativeTokens = nativeTokenizer.Tokenize(text);
            var realTokens = realTokenizer.Tokenize(text);
            
            var nativeDecoded = nativeTokenizer.Decode(nativeTokens);
            var realDecoded = realTokenizer.Decode(realTokens);

            var result = new AccuracyResult
            {
                Input = text,
                NativeTokens = nativeTokens,
                RealTokens = realTokens,
                NativeDecoded = nativeDecoded,
                RealDecoded = realDecoded,
                TokenCountMatch = nativeTokens.Length == realTokens.Length,
                DecodingMatch = string.Equals(nativeDecoded, realDecoded, StringComparison.Ordinal)
            };
            
            results.Add(result);
            
            output.WriteLine($"📝 '{text}'");
            output.WriteLine($"  Native: {nativeTokens.Length} tokens → '{nativeDecoded}'");
            output.WriteLine($"  Real:   {realTokens.Length} tokens → '{realDecoded}'");
            output.WriteLine($"  Match:  Tokens={result.TokenCountMatch}, Decoded={result.DecodingMatch}");
            output.WriteLine("");
        }

        // 精度サマリー
        var tokenAccuracy = results.Count(r => r.TokenCountMatch) / (double)results.Count;
        var decodingAccuracy = results.Count(r => r.DecodingMatch) / (double)results.Count;
        
        output.WriteLine($"📊 Accuracy Summary:");
        output.WriteLine($"  Token Count Match: {tokenAccuracy:P1}");
        output.WriteLine($"  Decoding Match:    {decodingAccuracy:P1}");

        // 最低限の精度要求
        tokenAccuracy.Should().BeGreaterThan(0.6, "Token count should be reasonably similar");
    }

    private async Task<BenchmarkResult> BenchmarkNativeTokenizer(string modelPath, List<string> testTexts)
    {
        output.WriteLine($"🔥 Benchmarking Native Tokenizer...");
        
        using var tokenizer = await OpusMtNativeTokenizer.CreateAsync(modelPath);
        
        // ウォームアップ
        for (int i = 0; i < Math.Min(10, testTexts.Count); i++)
        {
            tokenizer.Tokenize(testTexts[i]);
        }

        // 実測定
        var sw = Stopwatch.StartNew();
        var totalTokens = 0;
        
        foreach (var text in testTexts)
        {
            var tokens = tokenizer.Tokenize(text);
            totalTokens += tokens.Length;
        }
        
        sw.Stop();

        var result = new BenchmarkResult
        {
            Implementation = "Native",
            TotalTexts = testTexts.Count,
            TotalTokens = totalTokens,
            TotalTimeMs = sw.ElapsedMilliseconds,
            AverageLatencyMs = (double)sw.ElapsedMilliseconds / testTexts.Count,
            TokensPerSecond = totalTokens / (sw.ElapsedMilliseconds / 1000.0)
        };

        output.WriteLine($"  ⏱️  Total time: {result.TotalTimeMs:N0}ms");
        output.WriteLine($"  📊 Avg latency: {result.AverageLatencyMs:F2}ms/text");
        output.WriteLine($"  🚀 Throughput: {result.TokensPerSecond:N0} tokens/sec");
        output.WriteLine("");

        return result;
    }

    private async Task<BenchmarkResult> BenchmarkRealTokenizer(string modelPath, List<string> testTexts)
    {
        await Task.Yield(); // 非同期メソッドの警告を解消
        
        output.WriteLine($"🔥 Benchmarking Real Tokenizer...");
        
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<RealSentencePieceTokenizer>();
        using var tokenizer = new RealSentencePieceTokenizer(modelPath, logger);
        
        // ウォームアップ
        for (int i = 0; i < Math.Min(10, testTexts.Count); i++)
        {
            tokenizer.Tokenize(testTexts[i]);
        }

        // 実測定
        var sw = Stopwatch.StartNew();
        var totalTokens = 0;
        
        foreach (var text in testTexts)
        {
            var tokens = tokenizer.Tokenize(text);
            totalTokens += tokens.Length;
        }
        
        sw.Stop();

        var result = new BenchmarkResult
        {
            Implementation = "Real",
            TotalTexts = testTexts.Count,
            TotalTokens = totalTokens,
            TotalTimeMs = sw.ElapsedMilliseconds,
            AverageLatencyMs = (double)sw.ElapsedMilliseconds / testTexts.Count,
            TokensPerSecond = totalTokens / (sw.ElapsedMilliseconds / 1000.0)
        };

        output.WriteLine($"  ⏱️  Total time: {result.TotalTimeMs:N0}ms");
        output.WriteLine($"  📊 Avg latency: {result.AverageLatencyMs:F2}ms/text");
        output.WriteLine($"  🚀 Throughput: {result.TokensPerSecond:N0} tokens/sec");
        output.WriteLine("");

        return result;
    }

    private async Task<long> BenchmarkNativeTokenizerMemory(string modelPath, List<string> testTexts)
    {
        using var tokenizer = await OpusMtNativeTokenizer.CreateAsync(modelPath);
        
        foreach (var text in testTexts)
        {
            tokenizer.Tokenize(text);
        }
        
        return GC.GetTotalMemory(true);
    }

    private Task<long> BenchmarkRealTokenizerMemory(string modelPath, List<string> testTexts)
    {
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<RealSentencePieceTokenizer>();
        using var tokenizer = new RealSentencePieceTokenizer(modelPath, logger);
        
        foreach (var text in testTexts)
        {
            tokenizer.Tokenize(text);
        }
        
        return Task.FromResult(GC.GetTotalMemory(true));
    }

    private async Task<long> MeasureMemoryUsage(string implementation, Func<Task<long>> operation)
    {
        // ガベージコレクション実行
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var beforeMemory = GC.GetTotalMemory(false);
        var afterMemory = await operation();
        
        return afterMemory - beforeMemory;
    }

    private void GeneratePerformanceReport(BenchmarkResult native, BenchmarkResult real)
    {
        output.WriteLine($"📊 Performance Comparison Report");
        output.WriteLine($"================================");
        output.WriteLine("");
        
        output.WriteLine($"⏱️  Processing Time:");
        output.WriteLine($"  Native: {native.TotalTimeMs:N0}ms");
        output.WriteLine($"  Real:   {real.TotalTimeMs:N0}ms");
        output.WriteLine($"  Ratio:  {(double)native.TotalTimeMs / real.TotalTimeMs:F2}x");
        output.WriteLine("");
        
        output.WriteLine($"📊 Average Latency:");
        output.WriteLine($"  Native: {native.AverageLatencyMs:F2}ms/text");
        output.WriteLine($"  Real:   {real.AverageLatencyMs:F2}ms/text");
        output.WriteLine($"  Improvement: {((real.AverageLatencyMs - native.AverageLatencyMs) / real.AverageLatencyMs * 100):F1}%");
        output.WriteLine("");
        
        output.WriteLine($"🚀 Throughput:");
        output.WriteLine($"  Native: {native.TokensPerSecond:N0} tokens/sec");
        output.WriteLine($"  Real:   {real.TokensPerSecond:N0} tokens/sec");
        output.WriteLine($"  Improvement: {((native.TokensPerSecond - real.TokensPerSecond) / real.TokensPerSecond * 100):F1}%");
        output.WriteLine("");

        // パフォーマンス判定
        var performanceGain = (real.AverageLatencyMs - native.AverageLatencyMs) / real.AverageLatencyMs;
        var status = performanceGain switch
        {
            > 0.2 => "🟢 Excellent (+20%)",
            > 0.1 => "🟡 Good (+10%)",
            > 0.0 => "🟠 Marginal",
            _ => "🔴 Slower"
        };
        
        output.WriteLine($"🎯 Performance Status: {status}");
    }

    private List<string> GenerateTestDataset(int count)
    {
        var templates = new[]
        {
            "こんにちは、{0}です。",
            "今日は{0}について話します。",
            "{0}の翻訳テストを実行中です。",
            "ゲーム内の{0}システムが更新されました。",
            "{0}機能の性能を測定しています。",
            "OPUS-MT{0}トークナイザーのベンチマーク",
            "{0}の処理速度を確認してください。",
            "翻訳エンジン{0}が正常に動作しています。",
            "{0}に関する詳細情報を表示します。",
            "パフォーマンステスト{0}を開始しました。"
        };

        var keywords = new[]
        {
            "システム", "機能", "テスト", "ベンチマーク", "性能",
            "翻訳", "トークナイザー", "エンジン", "処理", "実行",
            "確認", "測定", "比較", "統合", "最適化",
            "アルゴリズム", "データ", "結果", "効率", "速度"
        };

        var random = new Random(42); // 固定シード
        var texts = new List<string>();

        for (int i = 0; i < count; i++)
        {
            var template = templates[random.Next(templates.Length)];
            var keyword = keywords[random.Next(keywords.Length)];
            texts.Add(string.Format(CultureInfo.InvariantCulture, template, keyword));
        }

        return texts;
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

/// <summary>
/// ベンチマーク結果
/// </summary>
public class BenchmarkResult
{
    public string Implementation { get; set; } = string.Empty;
    public int TotalTexts { get; set; }
    public int TotalTokens { get; set; }
    public long TotalTimeMs { get; set; }
    public double AverageLatencyMs { get; set; }
    public double TokensPerSecond { get; set; }
}

/// <summary>
/// 精度比較結果
/// </summary>
public class AccuracyResult
{
    public string Input { get; set; } = string.Empty;
    public int[] NativeTokens { get; set; } = [];
    public int[] RealTokens { get; set; } = [];
    public string NativeDecoded { get; set; } = string.Empty;
    public string RealDecoded { get; set; } = string.Empty;
    public bool TokenCountMatch { get; set; }
    public bool DecodingMatch { get; set; }
}
