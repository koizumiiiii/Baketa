using System;
using System.IO;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation.Local.Onnx;
using Baketa.Infrastructure.Translation.Local.Onnx.Chinese;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Tests.Translation.Local.Onnx.Chinese;

/// <summary>
/// 中国語翻訳機能の統合テスト
/// </summary>
public class ChineseTranslationIntegrationTests(ITestOutputHelper output) : IDisposable
{
    private readonly ITestOutputHelper _output = output ?? throw new ArgumentNullException(nameof(output));
    private readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(builder =>
            builder.AddProvider(new XunitLoggerProvider(output)));
    private bool _disposed;

    [Fact]
    public void ChineseLanguageProcessor_BasicFunctionality_WorksCorrectly()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<ChineseLanguageProcessor>();
        var processor = new ChineseLanguageProcessor(logger);

        // Act & Assert - プレフィックス取得
        Assert.Equal(">>cmn_Hans<<", processor.GetOpusPrefix("zh-CN"));
        Assert.Equal(">>cmn_Hant<<", processor.GetOpusPrefix("zh-TW"));
        Assert.Equal(">>yue<<", processor.GetOpusPrefix("yue"));
        Assert.Equal(string.Empty, processor.GetOpusPrefix("en"));

        // Act & Assert - 中国語判定
        Assert.True(processor.IsChineseLanguageCode("zh-CN"));
        Assert.True(processor.IsChineseLanguageCode("zh-TW"));
        Assert.True(processor.IsChineseLanguageCode("cmn"));
        Assert.False(processor.IsChineseLanguageCode("en"));
        Assert.False(processor.IsChineseLanguageCode("ja"));

        // Act & Assert - プレフィックス追加
        var simplifiedLang = Language.ChineseSimplified;
        var traditionalLang = Language.ChineseTraditional;
        
        Assert.Equal(">>cmn_Hans<< Hello", processor.AddPrefixToText("Hello", simplifiedLang));
        Assert.Equal(">>cmn_Hant<< Hello", processor.AddPrefixToText("Hello", traditionalLang));
        
        // 既存プレフィックスがある場合は追加しない
        Assert.Equal(">>existing<< Hello", processor.AddPrefixToText(">>existing<< Hello", simplifiedLang));
    }

    [Theory]
    [InlineData("简体中文测试", ChineseScriptType.Simplified)]
    [InlineData("繁體中文測試", ChineseScriptType.Traditional)]
    [InlineData("Hello World", ChineseScriptType.Unknown)]
    [InlineData("", ChineseScriptType.Unknown)]
    public void ChineseLanguageProcessor_ScriptDetection_WorksCorrectly(string text, ChineseScriptType expected)
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<ChineseLanguageProcessor>();
        var processor = new ChineseLanguageProcessor(logger);

        // Act
        var result = processor.DetectScriptType(text);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ChineseLanguageExtensions_LanguageDetection_WorksCorrectly()
    {
        // Arrange & Act & Assert
        Assert.True(Language.ChineseSimplified.IsChinese());
        Assert.True(Language.ChineseTraditional.IsChinese());
        Assert.False(Language.English.IsChinese());
        Assert.False(Language.Japanese.IsChinese());

        Assert.True(Language.ChineseSimplified.IsSimplifiedChinese());
        Assert.False(Language.ChineseSimplified.IsTraditionalChinese());
        
        Assert.True(Language.ChineseTraditional.IsTraditionalChinese());
        Assert.False(Language.ChineseTraditional.IsSimplifiedChinese());

        // 文字体系の説明
        Assert.Equal("簡体字", Language.ChineseSimplified.GetChineseScriptDescription());
        Assert.Equal("繁体字", Language.ChineseTraditional.GetChineseScriptDescription());
        Assert.Equal("非中国語", Language.English.GetChineseScriptDescription());
    }

    [Fact]
    public void ChineseLanguageExtensions_SupportedLanguages_ReturnsCorrectList()
    {
        // Act
        var supportedLanguages = ChineseLanguageExtensions.GetSupportedChineseLanguages();

        // Assert
        Assert.NotNull(supportedLanguages);
        Assert.NotEmpty(supportedLanguages);
        Assert.Contains(supportedLanguages, lang => lang.Code == "zh-CN");
        Assert.Contains(supportedLanguages, lang => lang.Code == "zh-TW");
        Assert.Contains(supportedLanguages, lang => lang.Code == "zh-Hans");
        Assert.Contains(supportedLanguages, lang => lang.Code == "zh-Hant");
        Assert.Contains(supportedLanguages, lang => lang.Code == "yue");

        _output.WriteLine($"サポートされている中国語言語数: {supportedLanguages.Count}");
        foreach (var lang in supportedLanguages)
        {
            _output.WriteLine($"- {lang.Code}: {lang.DisplayName} ({lang.NativeName})");
        }
    }

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("zh-TW")]
    [InlineData("zh-Hans")]
    [InlineData("zh-Hant")]
    [InlineData("yue")]
    [InlineData("cmn")]
    public void ChineseLanguageExtensions_GetLanguageByCode_ReturnsValidLanguage(string languageCode)
    {
        // Act
        var language = ChineseLanguageExtensions.GetChineseLanguageByCode(languageCode);

        // Assert
        Assert.NotNull(language);
        Assert.True(language.IsChinese());
        
        _output.WriteLine($"言語コード '{languageCode}' -> {language.DisplayName}");
    }

    [Theory]
    [InlineData("我爱学习中文")]    // 簡体字
    [InlineData("我愛學習中文")]    // 繁体字
    [InlineData("你好世界")]        // 共通文字
    [InlineData("Hello World")]    // 非中国語
    public void ChineseLanguageExtensions_GetRecommendedLanguage_ReturnsValidLanguage(string text)
    {
        // Act
        var recommendedLanguage = ChineseLanguageExtensions.GetRecommendedChineseLanguage(text);

        // Assert
        Assert.NotNull(recommendedLanguage);
        Assert.True(recommendedLanguage.IsChinese());
        
        _output.WriteLine($"テキスト '{text}' -> 推奨言語: {recommendedLanguage.GetChineseScriptDescription()}");
    }

    [Theory]
    [InlineData('中', true)]
    [InlineData('国', true)]
    [InlineData('語', true)]
    [InlineData('A', false)]
    [InlineData('あ', false)]
    [InlineData('1', false)]
    public void ChineseLanguageProcessor_IsChineseCharacter_WorksCorrectly(char character, bool expected)
    {
        // Act
        var result = ChineseLanguageProcessor.IsChineseCharacter(character);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ChineseTranslation_EndToEndWorkflow_SimulatesCorrectly()
    {
        // Arrange - 実際のファイルがないため、ワークフローのシミュレーション
        var logger = _loggerFactory.CreateLogger<ChineseLanguageProcessor>();
        var processor = new ChineseLanguageProcessor(logger);

        var testScenarios = new[]
        {
            new { Text = "Hello world", TargetLang = "zh-CN", ExpectedPrefix = ">>cmn_Hans<<" },
            new { Text = "Good morning", TargetLang = "zh-TW", ExpectedPrefix = ">>cmn_Hant<<" },
            new { Text = "How are you", TargetLang = "yue", ExpectedPrefix = ">>yue<<" },
            new { Text = "Nice to meet you", TargetLang = "en", ExpectedPrefix = "" }
        };

        foreach (var scenario in testScenarios)
        {
            // Act
            var targetLanguage = new Language { Code = scenario.TargetLang, DisplayName = "Test" };
            var processedText = processor.AddPrefixToText(scenario.Text, targetLanguage);

            // Assert
            if (string.IsNullOrEmpty(scenario.ExpectedPrefix))
            {
                Assert.Equal(scenario.Text, processedText);
            }
            else
            {
                Assert.Equal($"{scenario.ExpectedPrefix} {scenario.Text}", processedText);
            }

            _output.WriteLine($"シナリオ - 言語: {scenario.TargetLang}, テキスト: '{scenario.Text}' -> '{processedText}'");
        }
    }

    [Fact]
    public void ChineseTranslation_ErrorHandling_WorksCorrectly()
    {
        // Arrange
        var logger = _loggerFactory.CreateLogger<ChineseLanguageProcessor>();

        // Act & Assert - null logger
        Assert.Throws<ArgumentNullException>(() => new ChineseLanguageProcessor(null!));

        // Act & Assert - null引数の処理
        var processor = new ChineseLanguageProcessor(logger);
        
        Assert.Equal(string.Empty, processor.GetOpusPrefix((string)null!));
        Assert.Equal(string.Empty, processor.GetOpusPrefix((Language)null!));
        Assert.Equal(string.Empty, processor.AddPrefixToText(null!, Language.ChineseSimplified));
        Assert.Equal(string.Empty, processor.AddPrefixToText("test", null!));
        
        Assert.Equal(ChineseScriptType.Unknown, processor.DetectScriptType(null!));
        Assert.Equal(ChineseScriptType.Unknown, processor.DetectScriptType(string.Empty));
        
        Assert.False(processor.IsChineseLanguageCode(null!));
        Assert.False(processor.IsChineseLanguageCode(string.Empty));
        Assert.False(processor.IsChineseLanguageCode("   "));
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _loggerFactory?.Dispose();
            }
            _disposed = true;
        }
    }
}

/// <summary>
/// Xunit用のロガープロバイダー
/// </summary>
public sealed class XunitLoggerProvider(ITestOutputHelper output) : ILoggerProvider
{
    private readonly ITestOutputHelper _output = output ?? throw new ArgumentNullException(nameof(output));
    private bool _disposed;

    public ILogger CreateLogger(string categoryName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new XunitLogger(_output, categoryName);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}

/// <summary>
/// Xunit用のロガー
/// </summary>
public sealed class XunitLogger(ITestOutputHelper output, string categoryName) : ILogger
{
    private readonly ITestOutputHelper _output = output ?? throw new ArgumentNullException(nameof(output));
    private readonly string _categoryName = categoryName ?? throw new ArgumentNullException(nameof(categoryName));

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        
        try
        {
            var message = formatter(state, exception);
            _output.WriteLine($"[{logLevel}] {_categoryName}: {message}");
            
            if (exception != null)
            {
                _output.WriteLine($"Exception: {exception}");
            }
        }
#pragma warning disable CA1031 // テストログ出力では全ての例外をキャッチする必要がある
        catch
        {
            // テスト環境でのログ書き込みエラーは無視
        }
#pragma warning restore CA1031
    }
}
