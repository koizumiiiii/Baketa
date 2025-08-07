using System;
using System.IO;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation.Local.Onnx;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Tests.Translation;

/// <summary>
/// トークン詳細分析テスト
/// </summary>
public class TokenAnalysisTest
{
    private readonly ITestOutputHelper _output;

    public TokenAnalysisTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task AnalyzeTokenMappings()
    {
        // デバッグ用のロガーを作成
        var logger = new TestOutputLogger<AlphaOpusMtTranslationEngine>(_output);

        // モデルファイルパスの設定
        var modelsBaseDir = FindModelsDirectory();
        var onnxModelPath = Path.Combine(modelsBaseDir, "ONNX", "opus-mt-ja-en.onnx");
        var sentencePieceModelPath = Path.Combine(modelsBaseDir, "SentencePiece", "opus-mt-ja-en.model");

        // 言語ペア設定
        var languagePair = new LanguagePair
        {
            SourceLanguage = Language.Japanese,
            TargetLanguage = Language.English
        };

        var options = new AlphaOpusMtOptions
        {
            MaxSequenceLength = 256,
            MemoryLimitMb = 300,
            ThreadCount = 2,
            RepetitionPenalty = 1.2f  // Repetition Penalty有効
        };

        // エンジン作成
        var engine = new AlphaOpusMtTranslationEngine(
            onnxModelPath,
            sentencePieceModelPath,
            languagePair,
            options,
            logger);

        // 初期化
        var initResult = await engine.InitializeAsync();
        Assert.True(initResult, "エンジンの初期化に失敗");

        _output.WriteLine("=== トークン詳細分析 ===");

        // 1. 入力トークン分析
        var sourceTokenizer = engine.GetTokenizer();
        var targetTokenizer = engine.GetTargetTokenizer();

        var testText = "……複雑でよくわからない";
        var inputTokens = sourceTokenizer.Tokenize(testText);
        
        _output.WriteLine($"入力テキスト: '{testText}'");
        _output.WriteLine($"入力トークン: [{string.Join(", ", inputTokens)}]");
        
        _output.WriteLine("\n=== ソーストークナイザー（日本語）分析 ===");
        for (int i = 0; i < inputTokens.Length; i++)
        {
            var token = inputTokens[i];
            var decoded = sourceTokenizer.DecodeToken(token);
            _output.WriteLine($"トークン {token}: '{decoded}'");
        }

        // 2. 出力トークン分析
        var outputTokens = new int[] { 106, 43, 62, 15 }; // 観測された主要トークン
        
        _output.WriteLine("\n=== ターゲットトークナイザー（英語）分析 ===");
        foreach (var token in outputTokens)
        {
            var decoded = targetTokenizer.DecodeToken(token);
            _output.WriteLine($"トークン {token}: '{decoded}'");
        }

        // 3. 実際の翻訳実行
        var request = TranslationRequest.Create(
            testText,
            Language.Japanese,
            Language.English);

        var response = await engine.TranslateAsync(request);
        
        _output.WriteLine($"\n=== 翻訳結果 ===");
        _output.WriteLine($"入力: '{request.SourceText}'");
        _output.WriteLine($"出力: '{response.TranslatedText}'");
        _output.WriteLine($"成功: {response.IsSuccess}");

        // 4. 追加テストケース
        var additionalTests = new[]
        {
            "こんにちは",
            "ありがとう",
            "複雑",
            "わからない"
        };

        _output.WriteLine("\n=== 追加テストケース ===");
        foreach (var text in additionalTests)
        {
            var tokens = sourceTokenizer.Tokenize(text);
            _output.WriteLine($"'{text}' → [{string.Join(", ", tokens)}]");
            
            // 各トークンの詳細
            foreach (var token in tokens)
            {
                if (token != 0) // BOS/EOSを除く
                {
                    var decoded = sourceTokenizer.DecodeToken(token);
                    _output.WriteLine($"  {token}: '{decoded}'");
                }
            }
        }

        Assert.True(response.IsSuccess);
    }

    private static string FindModelsDirectory()
    {
        var candidatePaths = new[]
        {
            @"E:\dev\Baketa\Models",
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "..", "Models"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "Models"),
        };

        foreach (var path in candidatePaths)
        {
            var normalizedPath = Path.GetFullPath(path);
            if (Directory.Exists(normalizedPath))
            {
                return normalizedPath;
            }
        }

        throw new DirectoryNotFoundException($"Modelsディレクトリが見つかりません");
    }
}

