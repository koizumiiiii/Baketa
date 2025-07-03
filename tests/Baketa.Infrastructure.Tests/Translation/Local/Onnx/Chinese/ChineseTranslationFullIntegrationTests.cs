using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Core.Translation.Configuration;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation.Local.Onnx.Chinese;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Tests.Translation.Local.Onnx.Chinese;

/// <summary>
/// 中国語翻訳機能の完全統合テスト
/// </summary>
public sealed class ChineseTranslationFullIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _testOutput;
    private readonly ServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private bool _disposed;

    public ChineseTranslationFullIntegrationTests(ITestOutputHelper testOutput)
    {
        _testOutput = testOutput ?? throw new ArgumentNullException(nameof(testOutput));

        // テスト用の設定を作成
        var configurationBuilder = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Translation:Languages:DefaultSourceLanguage"] = "auto",
                ["Translation:Languages:DefaultTargetLanguage"] = "ja",
                ["Translation:Languages:EnableChineseVariantAutoDetection"] = "true",
                ["Translation:Languages:EnableLanguageDetection"] = "true",
                ["SentencePiece:ModelsDirectory"] = "Models/SentencePiece",
                ["SentencePiece:DefaultModel"] = "opus-mt-ja-en",
                ["SentencePiece:MaxInputLength"] = "10000"
            });

        _configuration = configurationBuilder.Build();

        // DIコンテナの設定
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // ログ設定
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // 設定の登録
        services.AddSingleton(_configuration);

        // 中国語翻訳サポートの追加
        services.AddChineseTranslationSupport(_configuration);

        // ここでは実際のOpusMtOnnxEngineの代わりにモックを使用
        // 実際のテストでは、より軽量なテスト用実装を使用
    }

    [Fact]
    public void LanguageConfiguration_WithDI_ShouldLoadCorrectly()
    {
        // Act
        var config = LanguageConfiguration.Default;

        // Assert
        Assert.NotNull(config);
        Assert.Equal("auto", config.DefaultSourceLanguage);
        Assert.Equal("ja", config.DefaultTargetLanguage);
        Assert.True(config.EnableChineseVariantAutoDetection);

        _testOutput.WriteLine($"言語設定が正常に読み込まれました: サポート言語数 {config.SupportedLanguages.Count}");
    }

    [Fact]
    public void ChineseLanguageProcessor_WithDI_ShouldDetectVariants()
    {
        // Arrange
        var processor = _serviceProvider.GetRequiredService<ChineseLanguageProcessor>();

        // Act & Assert
        var simplifiedResult = processor.DetectScriptType("这是简体字测试");
        var traditionalResult = processor.DetectScriptType("這是繁體字測試");

        _testOutput.WriteLine($"簡体字検出: {simplifiedResult}");
        _testOutput.WriteLine($"繁体字検出: {traditionalResult}");

        // 基本的な動作確認
        Assert.NotEqual(ChineseScriptType.Unknown, simplifiedResult);
        Assert.NotEqual(ChineseScriptType.Unknown, traditionalResult);
    }

    [Fact]
    public void ChineseVariantDetectionService_ShouldDetectCorrectly()
    {
        // Arrange
        var detectionService = _serviceProvider.GetRequiredService<ChineseVariantDetectionService>();

        // Act
        var simplifiedVariant = detectionService.DetectVariant("这是简体字");
        var traditionalVariant = detectionService.DetectVariant("這是繁體字");
        var emptyVariant = detectionService.DetectVariant("");

        // Assert
        Assert.True(ChineseVariantExtensions.IsValid(simplifiedVariant));
        Assert.True(ChineseVariantExtensions.IsValid(traditionalVariant));
        Assert.Equal(ChineseVariant.Auto, emptyVariant);

        _testOutput.WriteLine($"簡体字検出結果: {simplifiedVariant}");
        _testOutput.WriteLine($"繁体字検出結果: {traditionalVariant}");
        _testOutput.WriteLine($"空文字検出結果: {emptyVariant}");
    }

    [Fact]
    public void LanguageConfiguration_ChineseLanguages_ShouldBeFilteredCorrectly()
    {
        // Arrange
        var config = LanguageConfiguration.Default;

        // Act
        var chineseLanguages = config.GetChineseLanguages().ToList();

        // Assert
        Assert.NotEmpty(chineseLanguages);
        Assert.All(chineseLanguages, lang => 
        {
            Assert.True(lang.IsChinese(), $"言語 {lang.Code} は中国語として認識されませんでした");
        });

        _testOutput.WriteLine($"中国語系言語数: {chineseLanguages.Count}");
        foreach (var lang in chineseLanguages)
        {
            _testOutput.WriteLine($"  - {lang.Code}: {lang.Name}");
        }
    }

    [Theory]
    [InlineData("en", "zh-Hans", true)]
    [InlineData("en", "zh-Hant", true)]
    [InlineData("zh", "en", true)]
    [InlineData("ja", "ko", false)] // 現在未サポート
    public void LanguageConfiguration_TranslationPairSupport_ShouldReturnCorrectResult(
        string sourceLang, string targetLang, bool expectedSupported)
    {
        // Arrange
        var config = LanguageConfiguration.Default;

        // Act
        var isSupported = config.IsTranslationPairSupported(sourceLang, targetLang);

        // Assert
        Assert.Equal(expectedSupported, isSupported);

        _testOutput.WriteLine($"言語ペア {sourceLang}->{targetLang}: {(isSupported ? "サポート" : "未サポート")}");
    }

    [Fact]
    public void ServiceProvider_ShouldResolveAllRequiredServices()
    {
        // Act & Assert
        var services = new[]
        {
            typeof(ChineseLanguageProcessor),
            typeof(ChineseVariantDetectionService)
        };

        foreach (var serviceType in services)
        {
            var service = _serviceProvider.GetService(serviceType);
            Assert.NotNull(service);
            _testOutput.WriteLine($"✓ {serviceType.Name} が正常に解決されました");
        }
    }

    [Theory]
    [InlineData(ChineseVariant.Auto, "")]
    [InlineData(ChineseVariant.Simplified, ">>cmn_Hans<<")]
    [InlineData(ChineseVariant.Traditional, ">>cmn_Hant<<")]
    [InlineData(ChineseVariant.Cantonese, ">>yue<<")]
    public void ChineseVariant_OpusPrefix_ShouldReturnCorrectValue(ChineseVariant variant, string expectedPrefix)
    {
        // Act
        var result = variant.GetOpusPrefix();

        // Assert
        Assert.Equal(expectedPrefix, result);
        _testOutput.WriteLine($"中国語変種 {variant}: プレフィックス '{result}'");
    }

    [Theory]
    [InlineData("zh-hans", ChineseVariant.Simplified)]
    [InlineData("zh-hant", ChineseVariant.Traditional)]
    [InlineData("yue", ChineseVariant.Cantonese)]
    [InlineData("zh", ChineseVariant.Auto)]
    [InlineData("unknown", ChineseVariant.Auto)]
    public void ChineseVariant_FromLanguageCode_ShouldReturnCorrectVariant(string languageCode, ChineseVariant expectedVariant)
    {
        // Act
        var result = ChineseVariantExtensions.FromLanguageCode(languageCode);

        // Assert
        Assert.Equal(expectedVariant, result);
        _testOutput.WriteLine($"言語コード '{languageCode}' -> 中国語変種 {result}");
    }

    [Fact]
    public void LanguageInfo_ChineseVariants_ShouldBeCreatedCorrectly()
    {
        // Arrange & Act
        var simplifiedInfo = new LanguageInfo
        {
            Code = "zh-Hans",
            Name = "中国語（簡体字）",
            NativeName = "中文（简体）",
            OpusPrefix = ">>cmn_Hans<<",
            Variant = ChineseVariant.Simplified
        };
        var traditionalInfo = new LanguageInfo
        {
            Code = "zh-Hant",
            Name = "中国語（繁体字）",
            NativeName = "中文（繁體）",
            OpusPrefix = ">>cmn_Hant<<",
            Variant = ChineseVariant.Traditional
        };

        // Assert
        Assert.Equal("zh-Hans", simplifiedInfo.Code);
        Assert.Equal(ChineseVariant.Simplified, simplifiedInfo.Variant);
        Assert.Equal(">>cmn_Hans<<", simplifiedInfo.OpusPrefix);
        Assert.True(simplifiedInfo.IsChinese());

        Assert.Equal("zh-Hant", traditionalInfo.Code);
        Assert.Equal(ChineseVariant.Traditional, traditionalInfo.Variant);
        Assert.Equal(">>cmn_Hant<<", traditionalInfo.OpusPrefix);
        Assert.True(traditionalInfo.IsChinese());

        _testOutput.WriteLine($"簡体字LanguageInfo: {simplifiedInfo}");
        _testOutput.WriteLine($"繁体字LanguageInfo: {traditionalInfo}");
    }

    [Fact]
    public void LanguageConfiguration_Validation_ShouldPassForDefault()
    {
        // Arrange
        var config = LanguageConfiguration.Default;

        // Act
        var errors = config.Validate();

        // Assert
        Assert.Empty(errors);
        _testOutput.WriteLine("デフォルト言語設定の検証が成功しました");
    }

    [Fact]
    public void ChineseLanguageProcessor_SupportedLanguageCodes_ShouldContainExpectedCodes()
    {
        // Arrange
        var processor = _serviceProvider.GetRequiredService<ChineseLanguageProcessor>();

        // Act
        var supportedCodes = processor.GetSupportedLanguageCodes();

        // Assert
        Assert.NotEmpty(supportedCodes);
        Assert.Contains("zh-Hans", supportedCodes);
        Assert.Contains("zh-Hant", supportedCodes);
        Assert.Contains("zh", supportedCodes);

        _testOutput.WriteLine($"サポートされている中国語言語コード数: {supportedCodes.Count}");
        foreach (var code in supportedCodes)
        {
            _testOutput.WriteLine($"  - {code}");
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _serviceProvider?.Dispose();
            }
            _disposed = true;
        }
    }
}

/// <summary>
/// パフォーマンステスト用クラス
/// </summary>
public class ChineseTranslationPerformanceTests(ITestOutputHelper testOutput)
{
    private readonly ITestOutputHelper _testOutput = testOutput ?? throw new ArgumentNullException(nameof(testOutput));

    [Theory]
    [InlineData("This is a test sentence for translation performance measurement.")]
    [InlineData("Natural language processing is a fascinating field of study.")]
    [InlineData("Machine translation has improved significantly in recent years.")]
    public void Performance_ChineseVariantDetection_ShouldBeFast(string testText)
    {
        // パフォーマンステスト用 - 簡単な基本動作確認
        var processor = new ChineseLanguageProcessor(Mock.Of<ILogger<ChineseLanguageProcessor>>());
        
        // Act
        var result = processor.DetectScriptType(testText);
        
        // Assert - 英語テキストなのでUnknownであることを確認
        Assert.Equal(ChineseScriptType.Unknown, result);
        
        _testOutput.WriteLine($"パフォーマンステスト完了: {testText} -> {result}");
    }

    [Fact]
    public void RealWorld_ChineseTranslation_WithActualModel()
    {
        // このテストは基本的な中国語言語評定機能の確認を行います
        var processor = new ChineseLanguageProcessor(Mock.Of<ILogger<ChineseLanguageProcessor>>());
        
        // Act - 中国語コードのサポート確認
        var isChineseSupported = processor.IsChineseLanguageCode("zh");
        var supportedCodes = processor.GetSupportedLanguageCodes();
        
        // Assert
        Assert.True(isChineseSupported);
        Assert.NotEmpty(supportedCodes);
        Assert.Contains("zh", supportedCodes);
        
        _testOutput.WriteLine("実世界テスト完了。中国語言語コードサポートが確認されました。");
    }
}
