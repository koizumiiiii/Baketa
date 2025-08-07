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
/// 簡単な翻訳デバッグテスト
/// </summary>
public class SimpleTranslationDebugTest
{
    private readonly ITestOutputHelper _output;

    public SimpleTranslationDebugTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task DebugSimpleTranslation()
    {
        // デバッグ用のロガーを作成（XUnit Test Output向け）
        var logger = new TestOutputLogger<AlphaOpusMtTranslationEngine>(_output);

        // モデルファイルパスの設定
        var modelsBaseDir = FindModelsDirectory();
        var onnxModelPath = Path.Combine(modelsBaseDir, "ONNX", "opus-mt-ja-en.onnx");
        var sentencePieceModelPath = Path.Combine(modelsBaseDir, "SentencePiece", "opus-mt-ja-en.model");

        _output.WriteLine($"ONNX Model: {onnxModelPath}");
        _output.WriteLine($"SentencePiece Model: {sentencePieceModelPath}");

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
            RepetitionPenalty = 1.2f // Repetition Penalty有効化
        };

        // エンジン作成（ソーストークナイザーパス指定）
        var engine = new AlphaOpusMtTranslationEngine(
            onnxModelPath,
            sentencePieceModelPath, // ソーストークナイザーパス
            languagePair,
            options,
            logger);

        // 初期化
        var initResult = await engine.InitializeAsync();
        Assert.True(initResult, "エンジンの初期化に失敗");

        // 実際の日本語テキストでテスト（Source/Target語彙分離効果確認）
        var testText = "……複雑でよくわからない"; // 複雑なテストケース
        var request = TranslationRequest.Create(
            testText,
            Language.Japanese,
            Language.English);

        _output.WriteLine($"=== テスト開始: '{testText}' ===");

        var response = await engine.TranslateAsync(request);

        _output.WriteLine($"成功: {response.IsSuccess}");
        _output.WriteLine($"入力: '{request.SourceText}'");
        _output.WriteLine($"結果: '{response.TranslatedText}'");

        if (!response.IsSuccess && response.Error != null)
        {
            _output.WriteLine($"エラー: {response.Error.Message}");
        }
        else
        {
            // 翻訳結果の詳細確認
            var isProperTranslation = !string.IsNullOrEmpty(response.TranslatedText) 
                && response.TranslatedText != request.SourceText 
                && !response.TranslatedText.Contains("っ たち");
            
            _output.WriteLine($"正常な翻訳かどうか: {isProperTranslation}");
            _output.WriteLine($"結果の長さ: {response.TranslatedText?.Length ?? 0}");
            
            if (response.TranslatedText?.Contains("っ たち") == true)
            {
                _output.WriteLine("⚠️ 語彙混同による意味不明な出力が検出されました");
            }
            
            if (response.TranslatedText == request.SourceText)
            {
                _output.WriteLine("⚠️ 翻訳されずにそのまま出力されています");
            }
            
            if (string.IsNullOrEmpty(response.TranslatedText))
            {
                _output.WriteLine("⚠️ 空の翻訳結果です");
            }
        }

        Assert.True(response.IsSuccess, "翻訳処理自体は成功する必要があります");
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

/// <summary>
/// XUnit Test Output向けロガー
/// </summary>
public class TestOutputLogger<T> : ILogger<T>
{
    private readonly ITestOutputHelper _output;

    public TestOutputLogger(ITestOutputHelper output)
    {
        _output = output;
    }

    IDisposable ILogger.BeginScope<TState>(TState state) => new NoOpDisposable();

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        _output.WriteLine($"[{logLevel}] {formatter(state, exception)}");
        if (exception != null)
        {
            _output.WriteLine($"Exception: {exception}");
        }
    }

    private sealed class NoOpDisposable : IDisposable
    {
        public void Dispose() { }
    }
}