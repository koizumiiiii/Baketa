using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Translation;

using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

using System.Diagnostics.CodeAnalysis;

namespace Baketa.Infrastructure.Tests.Translation;

    [SuppressMessage("Style", "CA1707:識別子にアンダースコアを含めることはできません", Justification = "xUnit規約に準拠するテストメソッド名")]
[SuppressMessage("Style", "IDE0028:コレクションの初期化を簡素化できます", Justification = "プロジェクトのC#バージョンとの互換性を確保")]
    public class DefaultTranslationServiceTests : IDisposable
    {
        private readonly Mock<ILogger<DefaultTranslationService>> _loggerMock;
        private readonly Mock<ILogger<MockTranslationEngine>> _engineLoggerMock;
        private readonly Mock<IConfiguration> _configurationMock;
        private readonly MockTranslationEngine _mockEngine1;
        private readonly MockTranslationEngine _mockEngine2;
        private readonly DefaultTranslationService _service;
        
        public DefaultTranslationServiceTests()
        {
            _loggerMock = new Mock<ILogger<DefaultTranslationService>>();
            _engineLoggerMock = new Mock<ILogger<MockTranslationEngine>>();
            _configurationMock = new Mock<IConfiguration>();
            
            // テストデフォルトエンジン設定（最初のエンジンが選択される）
            _configurationMock.Setup(c => c["Translation:DefaultEngine"])
                .Returns("MockTranslationEngine");
            
            // エンジン1: 標準のモックエンジン
            _mockEngine1 = new MockTranslationEngine(_engineLoggerMock.Object);
            
            // エンジン2: カスタム名前のモックエンジン
            _mockEngine2 = new CustomNamedMockTranslationEngine(_engineLoggerMock.Object, "CustomMockEngine");
            
            // 翻訳サービスの作成
            _service = new DefaultTranslationService(
                _loggerMock.Object,
                new List<ITranslationEngine> { _mockEngine1, _mockEngine2 },
                _configurationMock.Object);
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
                    _mockEngine1.Dispose();
                    _mockEngine2.Dispose();
                }

                _disposed = true;
            }
        }
        
        [Fact]
        public void GetAvailableEngines_ReturnsAllRegisteredEngines()
        {
            // Act
            var engines = _service.GetAvailableEngines();
            
            // Assert
            Assert.NotNull(engines);
            Assert.Equal(2, engines.Count);
            Assert.Contains(engines, e => e.Name == "MockTranslationEngine");
            Assert.Contains(engines, e => e.Name == "CustomMockEngine");
        }
        
        [Fact]
        public void ActiveEngine_InitiallyFirstEngine()
        {
            // Act & Assert
            Assert.Same(_mockEngine1, _service.ActiveEngine);
        }
        
        [Fact]
        public async Task SetActiveEngineAsync_WithValidName_ChangesActiveEngine()
        {
            // Arrange
            var engineName = "CustomMockEngine";
            
            // Act
            var result = await _service.SetActiveEngineAsync(engineName).ConfigureAwait(true);
            
            // Assert
            Assert.True(result);
            Assert.Equal("CustomMockEngine", _service.ActiveEngine.Name);
        }
        
        [Fact]
        public async Task SetActiveEngineAsync_WithInvalidName_ReturnsFalse()
        {
            // Arrange
            var engineName = "NonExistentEngine";
            
            // Act
            var result = await _service.SetActiveEngineAsync(engineName).ConfigureAwait(true);
            
            // Assert
            Assert.False(result);
            Assert.Same(_mockEngine1, _service.ActiveEngine); // アクティブエンジンは変更されていない
        }
        
        [Fact]
        public async Task TranslateAsync_UsesActiveEngine()
        {
            // Arrange
            var text = "Hello";
            var sourceLang = Language.English;
            var targetLang = Language.Japanese;
            
            // Act
            var response = await _service.TranslateAsync(text, sourceLang, targetLang).ConfigureAwait(true);
            
            // Assert
            Assert.NotNull(response);
            Assert.True(response.IsSuccess);
            Assert.Equal("こんにちは", response.TranslatedText);
            Assert.Equal("MockTranslationEngine", response.EngineName);
        }
        
        [Fact]
        public async Task TranslateAsync_AfterChangingEngine_UsesNewActiveEngine()
        {
            // Arrange
            await _service.SetActiveEngineAsync("CustomMockEngine").ConfigureAwait(true);
            var text = "Hello";
            var sourceLang = Language.English;
            var targetLang = Language.Japanese;
            
            // Act
            var response = await _service.TranslateAsync(text, sourceLang, targetLang).ConfigureAwait(true);
            
            // Assert
            Assert.NotNull(response);
            Assert.True(response.IsSuccess);
            Assert.Equal("CustomMockEngine", response.EngineName);
        }
        
        [Fact]
        public async Task TranslateBatchAsync_ProcessesAllTexts()
        {
            // Arrange
            var texts = new List<string> { "Hello", "Thank you", "Goodbye" };
            var sourceLang = Language.English;
            var targetLang = Language.Japanese;
            
            // Act
            var responses = await _service.TranslateBatchAsync(texts, sourceLang, targetLang).ConfigureAwait(true);
            
            // Assert
            Assert.NotNull(responses);
            Assert.Equal(3, responses.Count);
            Assert.All(responses, r => Assert.True(r.IsSuccess));
            Assert.Equal("こんにちは", responses[0].TranslatedText);
            Assert.Equal("ありがとう", responses[1].TranslatedText);
            Assert.Equal("さようなら", responses[2].TranslatedText);
        }
    }
