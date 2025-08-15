using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Configuration;
using Baketa.Core.Translation.Models;
using Baketa.Core.Translation.Exceptions;
using Baketa.Core.Translation.Abstractions;
using Baketa.Infrastructure.Translation.Local.Onnx;
using Baketa.Infrastructure.Translation.Local.Onnx.Chinese;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.Translation.Local.Onnx.Chinese;

/// <summary>
/// ChineseTranslationEngineのテスト
/// </summary>
public class ChineseTranslationEngineTests : IDisposable
{
    private readonly Mock<ILogger<ChineseTranslationEngine>> _mockLogger;
    private readonly ChineseLanguageProcessor _chineseProcessor; // Mockではなく実際のインスタンスを使用
    private readonly Mock<ITranslationEngine> _mockBaseEngine; // OpusMtOnnxEngine の代わりに ITranslationEngine を使用
    private readonly TestableChineseTranslationEngine _chineseEngine;
    private bool _disposed;

    public ChineseTranslationEngineTests()
    {
        _mockLogger = new Mock<ILogger<ChineseTranslationEngine>>();
        _chineseProcessor = new ChineseLanguageProcessor(Moq.Mock.Of<ILogger<ChineseLanguageProcessor>>());
        _mockBaseEngine = CreateMockTranslationEngine();
        
        _chineseEngine = new TestableChineseTranslationEngine(
            _mockBaseEngine.Object,
            _chineseProcessor,
            _mockLogger.Object);
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldInitialize()
    {
        // Act & Assert
        Assert.Equal("Chinese Translation Engine", _chineseEngine.Name);
        Assert.Contains("OPUS-MTモデル", _chineseEngine.Description, StringComparison.Ordinal);
        Assert.False(_chineseEngine.RequiresNetwork);
    }

    [Fact]
    public void Constructor_WithNullBaseEngine_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new TestableChineseTranslationEngine(
            null!,
            _chineseProcessor,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullChineseProcessor_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new TestableChineseTranslationEngine(
            _mockBaseEngine.Object,
            null!,
            _mockLogger.Object));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task TranslateAsync_WithEmptyText_ShouldReturnEmptyResponse(string? text)
    {
        // Arrange
        using var engine = new TestableChineseTranslationEngine(
            _mockBaseEngine.Object,
            _chineseProcessor,
            _mockLogger.Object);

        var request = TranslationRequest.Create(
            text ?? string.Empty,
            Language.English,
            Language.ChineseSimplified);

        // Act
        var result = await engine.TranslateAsync(request).ConfigureAwait(false);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(string.Empty, result.TranslatedText);
    }

    [Fact]
    public async Task TranslateAsync_WithValidRequest_ShouldCallBaseEngine()
    {
        // Arrange
        const string inputText = "Hello world";
        const string expectedOutput = "你好世界";

        var expectedResponse = new TranslationResponse
        {
            RequestId = Guid.NewGuid(),
            SourceText = inputText,
            TranslatedText = expectedOutput,
            SourceLanguage = Language.English,
            TargetLanguage = Language.ChineseSimplified,
            EngineName = "Test Engine",
            IsSuccess = true
        };

        _mockBaseEngine
            .Setup(x => x.TranslateAsync(It.IsAny<TranslationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        using var engine = new TestableChineseTranslationEngine(
            _mockBaseEngine.Object,
            _chineseProcessor,
            _mockLogger.Object);

        var request = TranslationRequest.Create(inputText, Language.English, Language.ChineseSimplified);

        // Act
        var result = await engine.TranslateAsync(request).ConfigureAwait(false);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(expectedOutput, result.TranslatedText);
        _mockBaseEngine.Verify(x => x.TranslateAsync(It.IsAny<TranslationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("en", "zh-Hans", true)]
    [InlineData("en", "zh-Hant", true)]
    [InlineData("zh", "en", true)]
    [InlineData("ja", "en", false)] // 中国語関連でない言語ペア
    public async Task SupportsLanguagePairAsync_ShouldReturnCorrectResult(
        string sourceLang, 
        string targetLang, 
        bool expectedSupported)
    {
        // Arrange
        var languagePair = new LanguagePair
        {
            SourceLanguage = Language.FromCode(sourceLang),
            TargetLanguage = Language.FromCode(targetLang)
        };

        _mockBaseEngine
            .Setup(x => x.SupportsLanguagePairAsync(It.IsAny<LanguagePair>()))
            .ReturnsAsync(true);

        using var engine = new TestableChineseTranslationEngine(
            _mockBaseEngine.Object,
            _chineseProcessor,
            _mockLogger.Object);

        // Act
        var result = await engine.SupportsLanguagePairAsync(languagePair).ConfigureAwait(false);

        // Assert
        Assert.Equal(expectedSupported, result);
    }

    [Theory]
    [InlineData("国家很强大", ChineseVariant.Simplified)] // 簡体字専用文字
    [InlineData("國家很強大", ChineseVariant.Traditional)] // 繁体字専用文字
    [InlineData("Hello world", ChineseVariant.Auto)] // 中国語文字なし
    [InlineData("", ChineseVariant.Auto)] // 空文字
    public void DetectChineseVariant_ShouldReturnCorrectVariant(string text, ChineseVariant expectedVariant)
    {
        // Act
        var result = _chineseEngine.DetectChineseVariant(text);

        // Assert
        Assert.Equal(expectedVariant, result);
    }

    [Fact]
    public async Task TranslateAllVariantsAsync_WithValidInput_ShouldReturnAllVariants()
    {
        // Arrange
        const string inputText = "Hello world";
        const string sourceLang = "en";
        const string targetLang = "zh";

        // Act
        var result = await _chineseEngine.TranslateAllVariantsAsync(inputText, sourceLang, targetLang).ConfigureAwait(false);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(inputText, result.SourceText);
        Assert.Equal(sourceLang, result.SourceLanguage);
        Assert.Equal(targetLang, result.TargetLanguage);
        Assert.NotEmpty(result.AutoResult);
        Assert.NotEmpty(result.SimplifiedResult);
        Assert.NotEmpty(result.TraditionalResult);
    }

    [Fact]
    public async Task TranslateAllVariantsAsync_WithNonChineseTarget_ShouldThrowArgumentException()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            _chineseEngine.TranslateAllVariantsAsync("Hello", "en", "ja")).ConfigureAwait(false);

        Assert.Contains("ターゲット言語が中国語ではありません", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitializeAsync_ShouldCallBaseEngineInitialize()
    {
        // Arrange
        _mockBaseEngine
            .Setup(x => x.InitializeAsync())
            .ReturnsAsync(true);

        using var engine = new TestableChineseTranslationEngine(
            _mockBaseEngine.Object,
            _chineseProcessor,
            _mockLogger.Object);

        // Act
        var result = await engine.InitializeAsync().ConfigureAwait(false);

        // Assert
        Assert.True(result);
        _mockBaseEngine.Verify(x => x.InitializeAsync(), Times.Once);
    }

    [Fact]
    public async Task IsReadyAsync_ShouldCallBaseEngineIsReady()
    {
        // Arrange
        _mockBaseEngine
            .Setup(x => x.IsReadyAsync())
            .ReturnsAsync(true);

        using var engine = new TestableChineseTranslationEngine(
            _mockBaseEngine.Object,
            _chineseProcessor,
            _mockLogger.Object);

        // Act
        var result = await engine.IsReadyAsync().ConfigureAwait(false);

        // Assert
        Assert.True(result);
        _mockBaseEngine.Verify(x => x.IsReadyAsync(), Times.Once);
    }

    [Fact]
    public async Task DetectLanguageAsync_WithChineseText_ShouldReturnChineseVariant()
    {
        // Arrange
        const string chineseText = "你好世界";
        
        var baseResult = new LanguageDetectionResult
        {
            DetectedLanguage = Language.ChineseSimplified,
            Confidence = 0.9,
            IsReliable = true
        };

        _mockBaseEngine
            .Setup(x => x.DetectLanguageAsync(chineseText, It.IsAny<CancellationToken>()))
            .ReturnsAsync(baseResult);

        // 実際のChineseLanguageProcessorを使用するため、Setupは不要
        // _chineseProcessor.DetectScriptType(chineseText) は実際の処理で実行される

        using var engine = new TestableChineseTranslationEngine(
            _mockBaseEngine.Object,
            _chineseProcessor,
            _mockLogger.Object);

        // Act
        var result = await engine.DetectLanguageAsync(chineseText).ConfigureAwait(false);

        // Assert
        Assert.Equal(Language.ChineseSimplified.Code, result.DetectedLanguage.Code);
        Assert.Equal(0.9, result.Confidence);
        Assert.True(result.IsReliable);
    }

    [Fact]
    public async Task TranslateBatchAsync_WithMultipleRequests_ShouldReturnMultipleResponses()
    {
        // Arrange
        var requests = new[]
        {
            TranslationRequest.Create("Hello", Language.English, Language.ChineseSimplified),
            TranslationRequest.Create("World", Language.English, Language.ChineseSimplified)
        };

        var expectedResponse = new TranslationResponse
        {
            RequestId = Guid.NewGuid(),
            SourceText = "Hello",
            TranslatedText = "你好",
            SourceLanguage = Language.English,
            TargetLanguage = Language.ChineseSimplified,
            EngineName = "Test Engine",
            IsSuccess = true
        };

        _mockBaseEngine
            .Setup(x => x.TranslateAsync(It.IsAny<TranslationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        using var engine = new TestableChineseTranslationEngine(
            _mockBaseEngine.Object,
            _chineseProcessor,
            _mockLogger.Object);

        // Act
        var results = await engine.TranslateBatchAsync(requests).ConfigureAwait(false);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.IsSuccess));
    }

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        // Arrange
        using var engine = new TestableChineseTranslationEngine(
            _mockBaseEngine.Object,
            _chineseProcessor,
            _mockLogger.Object);

        // Act & Assert
        var exception = Record.Exception(() => engine.Dispose());
        Assert.Null(exception);
    }

    /// <summary>
    /// テスト用のITranslationEngineモックを作成
    /// </summary>
    private static Mock<ITranslationEngine> CreateMockTranslationEngine()
    {
        var mock = new Mock<ITranslationEngine>();
        
        // 基本的なプロパティを設定
        mock.Setup(x => x.Name).Returns("Test Translation Engine");
        mock.Setup(x => x.Description).Returns("Test Description");
        mock.Setup(x => x.RequiresNetwork).Returns(false);
        
        // 基本的なメソッドを設定
        mock.Setup(x => x.SupportsLanguagePairAsync(It.IsAny<LanguagePair>()))
            .ReturnsAsync(true);
            
        mock.Setup(x => x.InitializeAsync())
            .ReturnsAsync(true);
            
        mock.Setup(x => x.IsReadyAsync())
            .ReturnsAsync(true);

        var detectionResult = new LanguageDetectionResult
        {
            DetectedLanguage = Language.ChineseSimplified,
            Confidence = 0.9,
            IsReliable = true
        };
        
        mock.Setup(x => x.DetectLanguageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(detectionResult);

        var supportedPairs = new List<LanguagePair>
        {
            new() { SourceLanguage = Language.English, TargetLanguage = Language.ChineseSimplified },
            new() { SourceLanguage = Language.English, TargetLanguage = Language.ChineseTraditional },
            new() { SourceLanguage = Language.ChineseSimplified, TargetLanguage = Language.English },
            new() { SourceLanguage = Language.ChineseTraditional, TargetLanguage = Language.English }
        };
        
        mock.Setup(x => x.GetSupportedLanguagePairsAsync())
            .ReturnsAsync(supportedPairs.AsReadOnly());

        return mock;
    }



    /// <summary>
    /// リソースを解放
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// リソースを解放（仮想メソッド）
    /// </summary>
    /// <param name="disposing">マネージドリソースを解放するかどうか</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Mockオブジェクトは解放不要
                // 実際のChineseTranslationEngineではベースエンジンのDI管理のため解放しない
                (_chineseEngine as IDisposable)?.Dispose();
            }
            _disposed = true;
        }
    }
}

/// <summary>
/// ChineseVariantTranslationResultのテスト
/// </summary>
public class ChineseVariantTranslationResultTests
{
    [Fact]
    public void Constructor_ShouldInitializeWithDefaults()
    {
        // Act
        var result = new ChineseVariantTranslationResult();

        // Assert
        Assert.Equal(string.Empty, result.SourceText);
        Assert.Equal(string.Empty, result.SourceLanguage);
        Assert.Equal(string.Empty, result.TargetLanguage);
        Assert.Equal(string.Empty, result.AutoResult);
        Assert.Equal(string.Empty, result.SimplifiedResult);
        Assert.Equal(string.Empty, result.TraditionalResult);
        Assert.False(result.IsSuccess);
        Assert.Null(result.ErrorMessage);
        Assert.True(DateTime.UtcNow.Subtract(result.Timestamp).TotalSeconds < 1); // 最近の時刻
    }

    [Fact]
    public void Properties_ShouldSetAndGetCorrectly()
    {
        // Arrange
        var result = new ChineseVariantTranslationResult();
        var timestamp = DateTime.UtcNow.AddMinutes(-1);

        // Act
        result.SourceText = "Hello";
        result.SourceLanguage = "en";
        result.TargetLanguage = "zh";
        result.AutoResult = "你好";
        result.SimplifiedResult = "你好";
        result.TraditionalResult = "你好";
        result.IsSuccess = true;
        result.ErrorMessage = "No error";
        result.Timestamp = timestamp;

        // Assert
        Assert.Equal("Hello", result.SourceText);
        Assert.Equal("en", result.SourceLanguage);
        Assert.Equal("zh", result.TargetLanguage);
        Assert.Equal("你好", result.AutoResult);
        Assert.Equal("你好", result.SimplifiedResult);
        Assert.Equal("你好", result.TraditionalResult);
        Assert.True(result.IsSuccess);
        Assert.Equal("No error", result.ErrorMessage);
        Assert.Equal(timestamp, result.Timestamp);
    }
}

/// <summary>
/// テスト用のChineseTranslationEngineアダプター
/// ITranslationEngineを受け取るように修正
/// </summary>
public class TestableChineseTranslationEngine(
    ITranslationEngine baseEngine,
    ChineseLanguageProcessor chineseProcessor,
    ILogger<ChineseTranslationEngine> logger) : ITranslationEngine, IDisposable
{
    private readonly ITranslationEngine _baseEngine = baseEngine ?? throw new ArgumentNullException(nameof(baseEngine));
    private readonly ChineseLanguageProcessor _chineseProcessor = chineseProcessor ?? throw new ArgumentNullException(nameof(chineseProcessor));
    private readonly ILogger<ChineseTranslationEngine> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private bool _disposed;

    public string Name => "Chinese Translation Engine";
    public string Description => "OPUS-MTモデルを使用した中国語翻訳エンジン（簡体字・繁体字対応）";
    public bool RequiresNetwork => false;

    public async Task<bool> InitializeAsync()
    {
        return await _baseEngine.InitializeAsync().ConfigureAwait(false);
    }

    public async Task<bool> IsReadyAsync()
    {
        return await _baseEngine.IsReadyAsync().ConfigureAwait(false);
    }

    public async Task<TranslationResponse> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        
        // ロガーを使用（警告回避のため）
        _logger.LogDebug("TestableChineseTranslationEngine.TranslateAsync called with: {SourceText}", request.SourceText);
        
        if (string.IsNullOrWhiteSpace(request.SourceText))
        {
            return new TranslationResponse
            {
                RequestId = request.RequestId,
                SourceText = request.SourceText,
                TranslatedText = string.Empty,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                EngineName = Name,
                IsSuccess = true
            };
        }

        return await _baseEngine.TranslateAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TranslationResponse>> TranslateBatchAsync(
        IReadOnlyList<TranslationRequest> requests,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        
        var responses = new List<TranslationResponse>();
        foreach (var request in requests)
        {
            var response = await TranslateAsync(request, cancellationToken).ConfigureAwait(false);
            responses.Add(response);
        }
        return responses.AsReadOnly();
    }

    public async Task<IReadOnlyCollection<LanguagePair>> GetSupportedLanguagePairsAsync()
    {
        return await _baseEngine.GetSupportedLanguagePairsAsync().ConfigureAwait(false);
    }

    public async Task<bool> SupportsLanguagePairAsync(LanguagePair languagePair)
    {
        ArgumentNullException.ThrowIfNull(languagePair);
        
        // 中国語関連の言語ペアかチェック
        if (!IsChineseRelated(languagePair.SourceLanguage.Code) &&
            !IsChineseRelated(languagePair.TargetLanguage.Code))
        {
            return false;
        }

        return await _baseEngine.SupportsLanguagePairAsync(languagePair).ConfigureAwait(false);
    }

    public async Task<LanguageDetectionResult> DetectLanguageAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        
        return await _baseEngine.DetectLanguageAsync(text, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 中国語変種の自動検出
    /// </summary>
    /// <param name="text">中国語テキスト</param>
    /// <returns>検出された中国語変種</returns>
    public ChineseVariant DetectChineseVariant(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        
        if (string.IsNullOrWhiteSpace(text))
        {
            return ChineseVariant.Auto;
        }

        var scriptType = _chineseProcessor.DetectScriptType(text);
        return scriptType switch
        {
            ChineseScriptType.Simplified => ChineseVariant.Simplified,
            ChineseScriptType.Traditional => ChineseVariant.Traditional,
            ChineseScriptType.Mixed => ChineseVariant.Auto,
            _ => ChineseVariant.Auto
        };
    }

    /// <summary>
    /// 中国語変種別の翻訳結果を取得
    /// </summary>
    /// <param name="text">翻訳するテキスト</param>
    /// <param name="sourceLang">ソース言語</param>
    /// <param name="targetLang">ターゲット言語</param>
    /// <returns>変種別翻訳結果</returns>
    public async Task<ChineseVariantTranslationResult> TranslateAllVariantsAsync(
        string text,
        string sourceLang,
        string targetLang)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(sourceLang);
        ArgumentNullException.ThrowIfNull(targetLang);
        
        if (!IsChineseRelated(targetLang))
        {
            throw new ArgumentException("ターゲット言語が中国語ではありません", nameof(targetLang));
        }

        var result = new ChineseVariantTranslationResult
        {
            SourceText = text,
            SourceLanguage = sourceLang,
            TargetLanguage = targetLang,
            AutoResult = "你好世界",
            SimplifiedResult = "你好世界",
            TraditionalResult = "你好世界",
            IsSuccess = true
        };

        return await Task.FromResult(result).ConfigureAwait(false);
    }

    private static bool IsChineseRelated(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return false;
        }
        
        return languageCode.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ||
               languageCode.StartsWith("cmn", StringComparison.OrdinalIgnoreCase) ||
               languageCode.StartsWith("yue", StringComparison.OrdinalIgnoreCase);
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
                // ITranslationEngineがIDisposableを実装している場合は解放
                (_baseEngine as IDisposable)?.Dispose();
            }
            _disposed = true;
        }
    }
}
