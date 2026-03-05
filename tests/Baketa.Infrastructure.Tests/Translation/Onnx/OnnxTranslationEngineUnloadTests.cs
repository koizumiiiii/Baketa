using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Baketa.Infrastructure.Translation.Onnx;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.Translation.Onnx;

[SuppressMessage("Style", "CA1707:識別子にアンダースコアを含めることはできません", Justification = "xUnit規約に準拠するテストメソッド名")]
public class OnnxTranslationEngineUnloadTests : IDisposable
{
    private readonly OnnxTranslationEngine _sut;
    private readonly Mock<ILogger<OnnxTranslationEngine>> _loggerMock = new();

    public OnnxTranslationEngineUnloadTests()
    {
        // モデルディレクトリは存在しなくてもUnloadテストには影響なし
        _sut = new OnnxTranslationEngine(
            Path.Combine(Path.GetTempPath(), "nonexistent-onnx-models"),
            _loggerMock.Object,
            useKvCache: false);
    }

    [Fact]
    public async Task UnloadModelsAsync_WhenAlreadyUnloaded_DoesNothing()
    {
        // Arrange - 初期状態は未初期化（IsInitialized = false）

        // Act
        await _sut.UnloadModelsAsync();

        // Assert - エラーなく完了すること
        var isInitialized = GetIsInitialized();
        Assert.False(isInitialized);
    }

    [Fact]
    public async Task UnloadModelsAsync_WhenAlreadyUnloaded_LogsDebugMessage()
    {
        // Arrange - 初期状態は未初期化

        // Act
        await _sut.UnloadModelsAsync();

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("already unloaded")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task UnloadModelsAsync_WhenInitialized_ResetsIsInitialized()
    {
        // Arrange - IsInitialized を true に設定（リフレクション経由）
        SetIsInitialized(true);
        Assert.True(GetIsInitialized());

        // Act
        await _sut.UnloadModelsAsync();

        // Assert
        Assert.False(GetIsInitialized());
    }

    [Fact]
    public async Task UnloadModelsAsync_WhenInitialized_NullifiesTokenizer()
    {
        // Arrange
        SetIsInitialized(true);

        // Act
        await _sut.UnloadModelsAsync();

        // Assert
        var tokenizer = typeof(OnnxTranslationEngine)
            .GetField("_tokenizer", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(_sut);
        Assert.Null(tokenizer);
    }

    [Fact]
    public async Task UnloadModelsAsync_CanBeCalledMultipleTimes()
    {
        // Arrange
        SetIsInitialized(true);

        // Act - 複数回呼び出してもエラーにならない
        await _sut.UnloadModelsAsync();
        await _sut.UnloadModelsAsync();
        await _sut.UnloadModelsAsync();

        // Assert
        Assert.False(GetIsInitialized());
    }

    [Fact]
    public async Task UnloadModelsAsync_WhenInitialized_LogsInformationMessage()
    {
        // Arrange
        SetIsInitialized(true);

        // Act
        await _sut.UnloadModelsAsync();

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("ONNX models unloaded")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private bool GetIsInitialized()
    {
        return (bool)typeof(OnnxTranslationEngine)
            .BaseType! // TranslationEngineBase
            .GetProperty("IsInitialized", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(_sut)!;
    }

    private void SetIsInitialized(bool value)
    {
        typeof(OnnxTranslationEngine)
            .BaseType! // TranslationEngineBase
            .GetProperty("IsInitialized", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(_sut, value);
    }

    public void Dispose()
    {
        _sut.Dispose();
    }
}
