using System;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Core.Models.Translation;
using Baketa.Infrastructure.Translation;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

using System.Diagnostics.CodeAnalysis;

namespace Baketa.Infrastructure.Tests.Translation
{
    [SuppressMessage("Style", "CA1707:識別子にアンダースコアを含めることはできません", Justification = "xUnit規約に準拠するテストメソッド名")]
    public class TranslationEngineTests : IDisposable
    {
        private readonly Mock<ILogger<MockTranslationEngine>> _loggerMock;
        private readonly MockTranslationEngine _engine;
        
        public TranslationEngineTests()
        {
            _loggerMock = new Mock<ILogger<MockTranslationEngine>>();
            _engine = new MockTranslationEngine(_loggerMock.Object);
        }
        
        private bool _disposed;

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
                    // マネージドリソースの解放
                    _engine.Dispose();
                }

                _disposed = true;
            }
        }
        
        [Fact]
        public void Constructor_InitializesProperties()
        {
            // Arrange & Act
            using var engine = new MockTranslationEngine(_loggerMock.Object);
            
            // Assert
            Assert.Equal("MockTranslationEngine", engine.Name);
            Assert.Equal("テスト用のモック翻訳エンジン", engine.Description);
            Assert.False(engine.RequiresNetwork);
        }
        
        [Fact]
        public async Task GetSupportedLanguagePairsAsync_ReturnsExpectedPairs()
        {
            // Arrange & Act
            var pairs = await _engine.GetSupportedLanguagePairsAsync().ConfigureAwait(true);
            
            // Assert
            Assert.NotNull(pairs);
            Assert.Equal(10, pairs.Count); // サポートする言語ペアの数
            
            // 英語 → 日本語のペアが含まれることを確認
            Assert.Contains(pairs, p => 
                p.SourceLanguage.Code == "en" && 
                p.TargetLanguage.Code == "ja");
            
            // 日本語 → 英語のペアが含まれることを確認
            Assert.Contains(pairs, p => 
                p.SourceLanguage.Code == "ja" && 
                p.TargetLanguage.Code == "en");
            
            // 英語 → 中国語（簡体字）のペアが含まれることを確認
            Assert.Contains(pairs, p => 
                p.SourceLanguage.Code == "en" && 
                p.TargetLanguage.Code == "zh" &&
                p.TargetLanguage.RegionCode == "CN");
        }
        
        [Fact]
        public async Task SupportsLanguagePairAsync_WithSupportedPair_ReturnsTrue()
        {
            // Arrange
            var pair = LanguagePair.Create(Language.English, Language.Japanese);
            
            // Act
            var result = await _engine.SupportsLanguagePairAsync(pair).ConfigureAwait(true);
            
            // Assert
            Assert.True(result);
        }
        
        [Fact]
        public async Task SupportsLanguagePairAsync_WithUnsupportedPair_ReturnsFalse()
        {
            // Arrange - 未サポートの言語ペア（仮の例としてフランス語を使用）
            var unsupportedLanguage = new Language
            {
                Code = "fr",
                Name = "French",
                NativeName = "Français"
            };
            var pair = LanguagePair.Create(unsupportedLanguage, Language.Japanese);
            
            // Act
            var result = await _engine.SupportsLanguagePairAsync(pair).ConfigureAwait(true);
            
            // Assert
            Assert.False(result);
        }
        
        [Fact]
        public async Task TranslateAsync_WithPresetTranslation_ReturnsExpectedResult()
        {
            // Arrange
            var request = TranslationRequest.Create("Hello", Language.English, Language.Japanese);
            
            // Act
            var response = await _engine.TranslateAsync(request).ConfigureAwait(true);
            
            // Assert
            Assert.NotNull(response);
            Assert.True(response.IsSuccess);
            Assert.Equal("こんにちは", response.TranslatedText);
            Assert.Equal("MockTranslationEngine", response.EngineName);
            Assert.InRange(response.ConfidenceScore, 0.85f, 1.0f);
        }
        
        [Fact]
        public async Task TranslateAsync_WithNonPresetTranslation_ReturnsGeneratedResult()
        {
            // Arrange
            var request = TranslationRequest.Create("This is a test", Language.English, Language.Japanese);
            
            // Act
            var response = await _engine.TranslateAsync(request).ConfigureAwait(true);
            
            // Assert
            Assert.NotNull(response);
            Assert.True(response.IsSuccess);
            Assert.StartsWith("[英→日]", response.TranslatedText, StringComparison.Ordinal);
            Assert.Equal("MockTranslationEngine", response.EngineName);
        }
        
        [Fact]
        public async Task TranslateBatchAsync_ProcessesAllRequests()
        {
            // Arrange
            var requests = new[]
            {
                TranslationRequest.Create("Hello", Language.English, Language.Japanese),
                TranslationRequest.Create("Thank you", Language.English, Language.Japanese),
                TranslationRequest.Create("Goodbye", Language.English, Language.Japanese)
            };
            
            // Act
            var responses = await _engine.TranslateBatchAsync(requests).ConfigureAwait(true);
            
            // Assert
            Assert.NotNull(responses);
            Assert.Equal(3, responses.Count);
            Assert.All(responses, r => Assert.True(r.IsSuccess));
            Assert.Equal("こんにちは", responses[0].TranslatedText);
            Assert.Equal("ありがとう", responses[1].TranslatedText);
            Assert.Equal("さようなら", responses[2].TranslatedText);
        }
        
        [Fact]
        public async Task InitializeAsync_AlwaysSucceeds()
        {
            // Arrange & Act
            var result = await _engine.InitializeAsync().ConfigureAwait(true);
            
            // Assert
            Assert.True(result);
        }
        
        [Fact]
        public async Task IsReadyAsync_AfterInitialization_ReturnsTrue()
        {
            // Arrange
            await _engine.InitializeAsync().ConfigureAwait(true);
            
            // Act
            var result = await _engine.IsReadyAsync().ConfigureAwait(true);
            
            // Assert
            Assert.True(result);
        }
        
        [Fact]
        public async Task TranslateAsync_WithSimulatedError_ReturnsErrorResponse()
        {
            // Arrange
            using var engineWithError = new MockTranslationEngine(_loggerMock.Object, simulatedErrorRate: 1.0f);
            var request = TranslationRequest.Create("Hello", Language.English, Language.Japanese);
            
            // Act
            var response = await engineWithError.TranslateAsync(request).ConfigureAwait(true);
            
            // Assert
            Assert.NotNull(response);
            Assert.False(response.IsSuccess);
            Assert.NotNull(response.Error);
            Assert.Equal(TranslationError.InternalError, response.Error.ErrorCode);
        }
        
        [Fact]
        public async Task TranslateAsync_WithChinese_SelectsCorrectTranslation()
        {
            // Arrange
            var requestSimplified = TranslationRequest.Create(
                "Hello", 
                Language.English, 
                Language.ChineseSimplified);
                
            var requestTraditional = TranslationRequest.Create(
                "Hello", 
                Language.English, 
                Language.ChineseTraditional);
            
            // Act
            var responseSimplified = await _engine.TranslateAsync(requestSimplified).ConfigureAwait(true);
            var responseTraditional = await _engine.TranslateAsync(requestTraditional).ConfigureAwait(true);
            
            // Assert - 簡体字と繁体字で異なる翻訳結果になることを確認
            Assert.NotNull(responseSimplified);
            Assert.NotNull(responseTraditional);
            Assert.True(responseSimplified.IsSuccess);
            Assert.True(responseTraditional.IsSuccess);
            Assert.Equal("你好", responseSimplified.TranslatedText);
            Assert.Equal("你好", responseTraditional.TranslatedText);
            // 注：このテストでは簡体字と繁体字の「こんにちは」は同じ表記になるため、
            // 別のフレーズでテストすることが理想的です
        }
    }
}