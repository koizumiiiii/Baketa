using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Baketa.Core.Utilities;
using FluentAssertions;

namespace Baketa.Core.Tests.Utilities;

/// <summary>
/// BaketaExceptionHandlerのテストクラス
/// </summary>
public class BaketaExceptionHandlerTests
{
    [Fact]
    public async Task HandleWithFallbackAsync_PrimarySucceeds_ReturnsPrimaryResult()
    {
        // Arrange
        const string expectedResult = "プライマリ成功";
        
        // Act
        var result = await BaketaExceptionHandler.HandleWithFallbackAsync(
            primary: () => Task.FromResult(expectedResult),
            fallback: () => Task.FromResult("フォールバック")
        );

        // Assert
        result.Should().Be(expectedResult);
    }

    [Fact]
    public async Task HandleWithFallbackAsync_PrimaryFails_ReturnsFallbackResult()
    {
        // Arrange
        const string expectedResult = "フォールバック成功";
        
        // Act
        var result = await BaketaExceptionHandler.HandleWithFallbackAsync(
            primary: () => Task.FromException<string>(new InvalidOperationException("プライマリ失敗")),
            fallback: () => Task.FromResult(expectedResult)
        );

        // Assert
        result.Should().Be(expectedResult);
    }

    [Fact]
    public async Task HandleWithFallbackAsync_PrimaryFailsWithOnError_ExecutesOnError()
    {
        // Arrange
        var errorCalled = false;
        Exception? capturedError = null;

        // Act
        var result = await BaketaExceptionHandler.HandleWithFallbackAsync(
            primary: () => Task.FromException<string>(new TimeoutException("タイムアウト")),
            fallback: () => Task.FromResult("フォールバック"),
            onError: (ex) =>
            {
                errorCalled = true;
                capturedError = ex;
                return Task.CompletedTask;
            }
        );

        // Assert
        result.Should().Be("フォールバック");
        errorCalled.Should().BeTrue();
        capturedError.Should().BeOfType<TimeoutException>();
        capturedError!.Message.Should().Be("タイムアウト");
    }

    [Fact]
    public async Task HandleWithFallbackAsync_NullPrimary_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            BaketaExceptionHandler.HandleWithFallbackAsync<string>(
                primary: null!,
                fallback: () => Task.FromResult("フォールバック")
            )
        );
    }

    [Fact]
    public async Task HandleWithFallbackAsync_NullFallback_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            BaketaExceptionHandler.HandleWithFallbackAsync<string>(
                primary: () => Task.FromResult("プライマリ"),
                fallback: null!
            )
        );
    }

    [Fact]
    public async Task HandleWithMultipleFallbacksAsync_FirstStrategySucceeds_ReturnsFirstResult()
    {
        // Arrange
        var strategies = new List<Func<Task<string>>>
        {
            () => Task.FromResult("戦略1成功"),
            () => Task.FromResult("戦略2"),
            () => Task.FromResult("戦略3")
        };

        // Act
        var result = await BaketaExceptionHandler.HandleWithMultipleFallbacksAsync(strategies);

        // Assert
        result.Should().Be("戦略1成功");
    }

    [Fact]
    public async Task HandleWithMultipleFallbacksAsync_FirstFails_ReturnsSecondResult()
    {
        // Arrange
        var strategies = new List<Func<Task<string>>>
        {
            () => Task.FromException<string>(new InvalidOperationException("戦略1失敗")),
            () => Task.FromResult("戦略2成功"),
            () => Task.FromResult("戦略3")
        };

        // Act
        var result = await BaketaExceptionHandler.HandleWithMultipleFallbacksAsync(strategies);

        // Assert
        result.Should().Be("戦略2成功");
    }

    [Fact]
    public async Task HandleWithMultipleFallbacksAsync_AllFail_ThrowsAggregateException()
    {
        // Arrange
        var strategies = new List<Func<Task<string>>>
        {
            () => Task.FromException<string>(new InvalidOperationException("戦略1失敗")),
            () => Task.FromException<string>(new TimeoutException("戦略2失敗")),
            () => Task.FromException<string>(new ArgumentException("戦略3失敗"))
        };

        // Act & Assert
        var exception = await Assert.ThrowsAsync<AggregateException>(() =>
            BaketaExceptionHandler.HandleWithMultipleFallbacksAsync(strategies)
        );
        
        exception.Message.Should().StartWith("All fallback strategies failed");
        exception.InnerException.Should().BeOfType<ArgumentException>();
        exception.InnerException!.Message.Should().Be("戦略3失敗");
    }

    [Fact]
    public async Task HandleWithMultipleFallbacksAsync_EmptyStrategies_ThrowsArgumentException()
    {
        // Arrange
        var strategies = new List<Func<Task<string>>>();

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            BaketaExceptionHandler.HandleWithMultipleFallbacksAsync(strategies)
        );
        
        exception.ParamName.Should().Be("strategies");
        exception.Message.Should().StartWith("At least one strategy must be provided");
    }

    [Fact]
    public async Task HandleWithMultipleFallbacksAsync_WithOnError_ExecutesOnErrorForEachFailure()
    {
        // Arrange
        var errorMessages = new List<string>();
        var strategies = new List<Func<Task<string>>>
        {
            () => Task.FromException<string>(new InvalidOperationException("戦略1失敗")),
            () => Task.FromException<string>(new TimeoutException("戦略2失敗")),
            () => Task.FromResult("戦略3成功")
        };

        // Act
        var result = await BaketaExceptionHandler.HandleWithMultipleFallbacksAsync(
            strategies,
            onError: (ex, message) =>
            {
                errorMessages.Add($"{ex.GetType().Name}: {message}");
                return Task.CompletedTask;
            }
        );

        // Assert
        result.Should().Be("戦略3成功");
        errorMessages.Should().HaveCount(2);
        errorMessages[0].Should().Be("InvalidOperationException: Strategy 1 failed, trying next...");
        errorMessages[1].Should().Be("TimeoutException: Strategy 2 failed, trying next...");
    }

    [Fact]
    public void HandleWithFallback_SynchronousVersion_PrimarySucceeds_ReturnsPrimaryResult()
    {
        // Arrange
        const string expectedResult = "プライマリ成功";
        
        // Act
        var result = BaketaExceptionHandler.HandleWithFallback(
            primary: () => expectedResult,
            fallback: () => "フォールバック"
        );

        // Assert
        result.Should().Be(expectedResult);
    }

    [Fact]
    public void HandleWithFallback_SynchronousVersion_PrimaryFails_ReturnsFallbackResult()
    {
        // Arrange
        const string expectedResult = "フォールバック成功";
        
        // Act
        var result = BaketaExceptionHandler.HandleWithFallback(
            primary: () => throw new InvalidOperationException("プライマリ失敗"),
            fallback: () => expectedResult
        );

        // Assert
        result.Should().Be(expectedResult);
    }

    [Theory]
    [InlineData(typeof(TimeoutException), "テスト処理", "テスト処理中にタイムアウトが発生しました。処理に時間がかかっています。")]
    [InlineData(typeof(UnauthorizedAccessException), "ファイル操作", "ファイル操作中にアクセス権限エラーが発生しました。")]
    // NetworkInformationExceptionはパブリックコンストラクタがないため除外
    [InlineData(typeof(System.IO.IOException), "データ保存", "データ保存中にファイル操作エラーが発生しました。")]
    [InlineData(typeof(ArgumentException), "設定読み込み", "設定読み込み中に不正な設定値が検出されました。設定を確認してください。")]
    [InlineData(typeof(InvalidOperationException), "翻訳処理", "翻訳処理中に操作エラーが発生しました。アプリケーションを再起動してください。")]
    [InlineData(typeof(NotSupportedException), "画像処理", "画像処理中に予期しないエラーが発生しました。")]
    public void GetUserFriendlyErrorMessage_VariousExceptionTypes_ReturnsExpectedMessage(Type exceptionType, string context, string expectedMessage)
    {
        // Arrange
        var exception = (Exception)Activator.CreateInstance(exceptionType, "テストメッセージ")!;

        // Act
        var result = BaketaExceptionHandler.GetUserFriendlyErrorMessage(exception, context);

        // Assert
        result.Should().Be(expectedMessage);
    }

    [Fact]
    public void GetUserFriendlyErrorMessage_NullException_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            BaketaExceptionHandler.GetUserFriendlyErrorMessage(null!, "テストコンテキスト")
        );
    }

    [Fact]
    public async Task TryTranslationEnginesAsync_FirstEngineSucceeds_ReturnsFirstResult()
    {
        // Arrange
        var engines = new List<Func<string, string, string, Task<string>>>
        {
            (text, source, target) => Task.FromResult($"エンジン1: {text}を{source}から{target}に翻訳"),
            (text, source, target) => Task.FromResult($"エンジン2: {text}を{source}から{target}に翻訳")
        };

        // Act
        var result = await BaketaExceptionHandler.TryTranslationEnginesAsync(
            engines, "Hello", "en", "ja"
        );

        // Assert
        result.Should().Be("エンジン1: Helloをenからjaに翻訳");
    }

    [Fact]
    public async Task TryTranslationEnginesAsync_FirstEngineFails_ReturnsSecondResult()
    {
        // Arrange
        var engines = new List<Func<string, string, string, Task<string>>>
        {
            (text, source, target) => Task.FromException<string>(new TimeoutException("エンジン1タイムアウト")),
            (text, source, target) => Task.FromResult($"エンジン2: {text}を{source}から{target}に翻訳")
        };

        // Act
        var result = await BaketaExceptionHandler.TryTranslationEnginesAsync(
            engines, "Hello", "en", "ja"
        );

        // Assert
        result.Should().Be("エンジン2: Helloをenからjaに翻訳");
    }

    [Fact]
    public async Task TryTranslationEnginesAsync_NoEngines_ReturnsOriginalText()
    {
        // Arrange
        var engines = new List<Func<string, string, string, Task<string>>>();
        const string originalText = "変換前のテキスト";

        // Act
        var result = await BaketaExceptionHandler.TryTranslationEnginesAsync(
            engines, originalText, "en", "ja"
        );

        // Assert
        result.Should().Be(originalText);
    }

    [Fact]
    public async Task TryTranslationEnginesAsync_AllEnginesFail_ThrowsAggregateException()
    {
        // Arrange
        var engines = new List<Func<string, string, string, Task<string>>>
        {
            (text, source, target) => Task.FromException<string>(new TimeoutException("エンジン1失敗")),
            (text, source, target) => Task.FromException<string>(new InvalidOperationException("エンジン2失敗"))
        };

        // Act & Assert
        await Assert.ThrowsAsync<AggregateException>(() =>
            BaketaExceptionHandler.TryTranslationEnginesAsync(engines, "Hello", "en", "ja")
        );
    }

    [Fact]
    public async Task TryTranslationEnginesAsync_NullText_ThrowsArgumentNullException()
    {
        // Arrange
        var engines = new List<Func<string, string, string, Task<string>>>
        {
            (text, source, target) => Task.FromResult($"翻訳: {text}")
        };

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            BaketaExceptionHandler.TryTranslationEnginesAsync(engines, null!, "en", "ja")
        );
    }

    [Fact]
    public async Task TryTranslationEnginesAsync_NullEngineList_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            BaketaExceptionHandler.TryTranslationEnginesAsync(null!, "Hello", "en", "ja")
        );
    }
}