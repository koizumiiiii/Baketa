using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation.Onnx;
using Language = Baketa.Core.Models.Translation.Language;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Integration.Tests.Translation;

/// <summary>
/// OnnxTranslationEngine の E2E テスト用フィクスチャ
/// モデルを1回だけ読み込み、全テストで共有する
/// </summary>
public class OnnxTranslationFixture : IAsyncLifetime, IDisposable
{
    public OnnxTranslationEngine? Engine { get; private set; }
    public bool IsAvailable { get; private set; }
    public string SkipReason { get; private set; } = string.Empty;

    private readonly ILoggerFactory _loggerFactory;

    public OnnxTranslationFixture()
    {
        _loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
    }

    public async Task InitializeAsync()
    {
        var modelDir = FindModelDirectory();
        if (modelDir == null)
        {
            IsAvailable = false;
            SkipReason = "NLLB-200 ONNX model not found. Place models in Models/nllb-200-onnx-int8/";
            return;
        }

        var logger = _loggerFactory.CreateLogger<OnnxTranslationEngine>();
        Engine = new OnnxTranslationEngine(modelDir, logger);

        var initialized = await Engine.InitializeAsync();
        if (!initialized)
        {
            IsAvailable = false;
            SkipReason = "OnnxTranslationEngine initialization failed (model files may be incomplete)";
            Engine = null;
            return;
        }

        IsAvailable = true;
    }

    public Task DisposeAsync()
    {
        Engine?.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _loggerFactory.Dispose();
        GC.SuppressFinalize(this);
    }

    private static string? FindModelDirectory()
    {
        // BaseDirectory → 上位ディレクトリを順にたどって Models/nllb-200-onnx-int8/ を探す
        var searchPaths = new[]
        {
            AppDomain.CurrentDomain.BaseDirectory,
            Directory.GetCurrentDirectory(),
        };

        foreach (var basePath in searchPaths)
        {
            var dir = basePath;
            for (var i = 0; i < 8; i++)
            {
                var candidate = Path.Combine(dir, "Models", "nllb-200-onnx-int8");
                if (Directory.Exists(candidate) && HasRequiredModelFiles(candidate))
                    return candidate;

                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
        }

        return null;
    }

    private static bool HasRequiredModelFiles(string dir)
    {
        // encoder_model*.onnx が存在すればモデルディレクトリとみなす
        return Directory.GetFiles(dir, "encoder_model*.onnx").Length > 0;
    }
}

[Collection("OnnxTranslation")]
[SuppressMessage("Style", "CA1707:識別子にアンダースコアを含めることはできません", Justification = "xUnit規約に準拠するテストメソッド名")]
public class OnnxTranslationE2ETests : IClassFixture<OnnxTranslationFixture>
{
    private readonly OnnxTranslationFixture _fixture;
    private readonly ITestOutputHelper _output;

    public OnnxTranslationE2ETests(OnnxTranslationFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private bool SkipIfModelUnavailable()
    {
        if (!_fixture.IsAvailable)
        {
            _output.WriteLine($"[SKIP] {_fixture.SkipReason}");
            return true;
        }
        return false;
    }

    [Fact]
    [Trait("Category", "E2E")]
    public async Task TranslateAsync_JapaneseToEnglish_ReturnsValidTranslation()
    {
        if (SkipIfModelUnavailable()) return;

        // Arrange
        var request = new TranslationRequest
        {
            SourceText = "こんにちは",
            SourceLanguage = Language.Japanese,
            TargetLanguage = Language.English,
        };

        // Act
        var response = await _fixture.Engine!.TranslateAsync(request);

        // Assert
        _output.WriteLine($"Input: {request.SourceText}");
        _output.WriteLine($"Output: {response.TranslatedText}");
        _output.WriteLine($"IsSuccess: {response.IsSuccess}");

        Assert.True(response.IsSuccess, $"Translation failed: {response.Error?.Message}");
        Assert.False(string.IsNullOrWhiteSpace(response.TranslatedText));
        // 英語文字（ASCII）が含まれていることを確認
        Assert.Matches("[a-zA-Z]", response.TranslatedText);
    }

    [Theory]
    [Trait("Category", "E2E")]
    [InlineData("ja", "Hello")]
    [InlineData("ko", "Hello")]
    [InlineData("zh-CN", "Hello")]
    [InlineData("zh-TW", "Hello")]
    [InlineData("fr", "Hello")]
    [InlineData("de", "Hello")]
    [InlineData("it", "Hello")]
    [InlineData("es", "Hello")]
    [InlineData("pt", "Hello")]
    public async Task TranslateAsync_EnglishToTargetLanguage_Succeeds(string targetCode, string sourceText)
    {
        if (SkipIfModelUnavailable()) return;

        // Arrange
        var request = new TranslationRequest
        {
            SourceText = sourceText,
            SourceLanguage = Language.English,
            TargetLanguage = Language.FromCode(targetCode),
        };

        // Act
        var response = await _fixture.Engine!.TranslateAsync(request);

        // Assert
        _output.WriteLine($"en → {targetCode}: \"{sourceText}\" → \"{response.TranslatedText}\"");

        Assert.True(response.IsSuccess, $"Translation en→{targetCode} failed: {response.Error?.Message}");
        Assert.False(string.IsNullOrWhiteSpace(response.TranslatedText));
        // 翻訳結果が原文と異なることを確認
        Assert.NotEqual(sourceText, response.TranslatedText);
    }

    [Theory]
    [Trait("Category", "E2E")]
    [InlineData("en", "こんにちは")]
    [InlineData("ko", "こんにちは")]
    [InlineData("zh-CN", "こんにちは")]
    [InlineData("zh-TW", "こんにちは")]
    [InlineData("fr", "こんにちは")]
    [InlineData("de", "こんにちは")]
    [InlineData("it", "こんにちは")]
    [InlineData("es", "こんにちは")]
    [InlineData("pt", "こんにちは")]
    public async Task TranslateAsync_JapaneseToTargetLanguage_Succeeds(string targetCode, string sourceText)
    {
        if (SkipIfModelUnavailable()) return;

        // Arrange
        var request = new TranslationRequest
        {
            SourceText = sourceText,
            SourceLanguage = Language.Japanese,
            TargetLanguage = Language.FromCode(targetCode),
        };

        // Act
        var response = await _fixture.Engine!.TranslateAsync(request);

        // Assert
        _output.WriteLine($"ja → {targetCode}: \"{sourceText}\" → \"{response.TranslatedText}\"");

        Assert.True(response.IsSuccess, $"Translation ja→{targetCode} failed: {response.Error?.Message}");
        Assert.False(string.IsNullOrWhiteSpace(response.TranslatedText));
        // 翻訳結果が原文（日本語）と異なることを確認
        Assert.NotEqual(sourceText, response.TranslatedText);
    }

    [Theory]
    [Trait("Category", "E2E")]
    [InlineData("ko", "zh-CN", "안녕하세요")]
    [InlineData("zh-CN", "ko", "你好")]
    [InlineData("ko", "zh-TW", "안녕하세요")]
    [InlineData("zh-TW", "ko", "你好")]
    [InlineData("zh-CN", "zh-TW", "你好世界")]
    [InlineData("zh-TW", "zh-CN", "你好世界")]
    public async Task TranslateAsync_CjkCrossLanguage_Succeeds(string srcCode, string tgtCode, string text)
    {
        if (SkipIfModelUnavailable()) return;

        // Arrange
        var request = new TranslationRequest
        {
            SourceText = text,
            SourceLanguage = Language.FromCode(srcCode),
            TargetLanguage = Language.FromCode(tgtCode),
        };

        // Act
        var response = await _fixture.Engine!.TranslateAsync(request);

        // Assert
        _output.WriteLine($"{srcCode} → {tgtCode}: \"{text}\" → \"{response.TranslatedText}\"");

        Assert.True(response.IsSuccess, $"Translation {srcCode}→{tgtCode} failed: {response.Error?.Message}");
        Assert.False(string.IsNullOrWhiteSpace(response.TranslatedText));
        Assert.NotEqual(text, response.TranslatedText);
    }

    [Theory]
    [Trait("Category", "E2E")]
    [InlineData("fr", "de", "Bonjour le monde")]
    [InlineData("de", "fr", "Hallo Welt")]
    [InlineData("de", "es", "Guten Morgen")]
    [InlineData("es", "pt", "Buenos días")]
    [InlineData("it", "fr", "Buongiorno")]
    [InlineData("pt", "it", "Bom dia")]
    public async Task TranslateAsync_EuropeanCrossLanguage_Succeeds(string srcCode, string tgtCode, string text)
    {
        if (SkipIfModelUnavailable()) return;

        // Arrange
        var request = new TranslationRequest
        {
            SourceText = text,
            SourceLanguage = Language.FromCode(srcCode),
            TargetLanguage = Language.FromCode(tgtCode),
        };

        // Act
        var response = await _fixture.Engine!.TranslateAsync(request);

        // Assert
        _output.WriteLine($"{srcCode} → {tgtCode}: \"{text}\" → \"{response.TranslatedText}\"");

        Assert.True(response.IsSuccess, $"Translation {srcCode}→{tgtCode} failed: {response.Error?.Message}");
        Assert.False(string.IsNullOrWhiteSpace(response.TranslatedText));
        Assert.NotEqual(text, response.TranslatedText);
    }

    [Theory]
    [Trait("Category", "E2E")]
    [InlineData("ko", "fr", "안녕하세요")]
    [InlineData("zh-CN", "de", "你好世界")]
    [InlineData("fr", "ko", "Bonjour")]
    [InlineData("de", "zh-CN", "Hallo")]
    public async Task TranslateAsync_CjkEuropeanCross_Succeeds(string srcCode, string tgtCode, string text)
    {
        if (SkipIfModelUnavailable()) return;

        // Arrange
        var request = new TranslationRequest
        {
            SourceText = text,
            SourceLanguage = Language.FromCode(srcCode),
            TargetLanguage = Language.FromCode(tgtCode),
        };

        // Act
        var response = await _fixture.Engine!.TranslateAsync(request);

        // Assert
        _output.WriteLine($"{srcCode} → {tgtCode}: \"{text}\" → \"{response.TranslatedText}\"");

        Assert.True(response.IsSuccess, $"Translation {srcCode}→{tgtCode} failed: {response.Error?.Message}");
        Assert.False(string.IsNullOrWhiteSpace(response.TranslatedText));
        Assert.NotEqual(text, response.TranslatedText);
    }

    [Theory]
    [Trait("Category", "E2E")]
    [InlineData("ja", "en", "この村は昔、竜に守られていた。しかし竜は姿を消し、魔物が現れるようになった。勇者よ、竜を探してくれないか。")]
    [InlineData("en", "ja", "The kingdom has fallen into darkness. Monsters roam the land freely. Only a brave hero can restore the light. Will you accept this quest?")]
    [InlineData("en", "ko", "Welcome to the ancient library. Here you will find scrolls of forgotten knowledge. Choose wisely, for each scroll holds great power.")]
    [InlineData("en", "zh-CN", "The enchanted forest is dangerous at night. Stay on the path and do not trust the whispers. Your sword will protect you.")]
    [InlineData("en", "fr", "The castle gates are sealed by ancient magic. To enter, you must collect three sacred crystals hidden across the realm.")]
    [InlineData("en", "de", "The blacksmith can forge powerful weapons from rare materials. Bring him dragon scales and he will craft a legendary sword.")]
    [InlineData("ja", "ko", "この村は昔、竜に守られていた。しかし竜は姿を消し、魔物が現れるようになった。勇者よ、竜を探してくれないか。")]
    [InlineData("ja", "zh-CN", "魔王の城は北の山脈の奥深くにある。道中には多くの魔物が待ち受けている。仲間を集め、装備を整えて出発せよ。")]
    [InlineData("ja", "fr", "この剣は伝説の鍛冶師が作った。竜の鱗と古代の魔石を融合させた最強の武器だ。使いこなせるのは選ばれし者だけ。")]
    [InlineData("ja", "de", "冒険者ギルドへようこそ。ここでは依頼を受けて報酬を得ることができる。まずはランクの低い依頼から始めるといい。")]
    [InlineData("ja", "es", "古の図書館には忘れ去られた知識が眠っている。賢者の書を見つけることができれば、失われた魔法を習得できるだろう。")]
    public async Task TranslateAsync_LongText_Succeeds(string srcCode, string tgtCode, string text)
    {
        if (SkipIfModelUnavailable()) return;

        // Arrange
        var request = new TranslationRequest
        {
            SourceText = text,
            SourceLanguage = Language.FromCode(srcCode),
            TargetLanguage = Language.FromCode(tgtCode),
        };

        // Act
        var response = await _fixture.Engine!.TranslateAsync(request);

        // Assert
        _output.WriteLine($"{srcCode} → {tgtCode} (long text):");
        _output.WriteLine($"  Input ({text.Length} chars): \"{text}\"");
        _output.WriteLine($"  Output ({response.TranslatedText?.Length ?? 0} chars): \"{response.TranslatedText}\"");

        Assert.True(response.IsSuccess, $"Translation {srcCode}→{tgtCode} failed: {response.Error?.Message}");
        Assert.False(string.IsNullOrWhiteSpace(response.TranslatedText));
        Assert.NotEqual(text, response.TranslatedText);
        // 長文翻訳は原文の20%以上の長さを期待（極端に短い翻訳は異常）
        Assert.True(response.TranslatedText!.Length >= text.Length / 5,
            $"Translated text is too short ({response.TranslatedText.Length} chars vs {text.Length} chars original)");
    }

    [Fact]
    [Trait("Category", "E2E")]
    public async Task TranslateAsync_WithEmptyText_HandlesGracefully()
    {
        if (SkipIfModelUnavailable()) return;

        // Arrange
        var request = new TranslationRequest
        {
            SourceText = "",
            SourceLanguage = Language.English,
            TargetLanguage = Language.Japanese,
        };

        // Act
        var response = await _fixture.Engine!.TranslateAsync(request);

        // Assert
        _output.WriteLine($"IsSuccess: {response.IsSuccess}, Error: {response.Error?.Message}");
        // 空文字列は失敗またはそのまま返されるべき（クラッシュしないこと）
        Assert.NotNull(response);
    }

    [Fact]
    [Trait("Category", "E2E")]
    public async Task TranslateAsync_ShortText_CompletesWithinTimeout()
    {
        if (SkipIfModelUnavailable()) return;

        // Arrange
        var request = new TranslationRequest
        {
            SourceText = "Good morning",
            SourceLanguage = Language.English,
            TargetLanguage = Language.Japanese,
        };

        // Act
        var sw = Stopwatch.StartNew();
        var response = await _fixture.Engine!.TranslateAsync(request);
        sw.Stop();

        // Assert
        _output.WriteLine($"Translation completed in {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"Result: {response.TranslatedText}");

        Assert.True(response.IsSuccess, $"Translation failed: {response.Error?.Message}");
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30),
            $"Translation took {sw.ElapsedMilliseconds}ms, exceeding 30s timeout");
    }
}

/// <summary>
/// テストコレクション定義: ONNX翻訳テストの並列実行を抑制
/// </summary>
[CollectionDefinition("OnnxTranslation", DisableParallelization = true)]
[SuppressMessage("Naming", "CA1711:識別子は、不適切なサフィックスを含むことはできません", Justification = "xUnit CollectionDefinition規約")]
public class OnnxTranslationTestCollection : ICollectionFixture<OnnxTranslationFixture>
{
}
