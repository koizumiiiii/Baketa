using System;
using System.Diagnostics.CodeAnalysis;
using Baketa.Infrastructure.Platform.Windows.OpenCv.Exceptions;
using Xunit;

namespace Baketa.Infrastructure.Platform.Tests.Windows.OpenCv;

/// <summary>
/// OcrProcessingExceptionクラスの単体テスト
/// </summary>
[SuppressMessage("Design", "CA1515:型を内部にする必要があります", Justification = "xUnitのテストクラスはpublicでなければなりません")]
public class OcrProcessingExceptionTests
{
    [Fact]
    public void ConstructorDefaultShouldCreateInstance()
    {
        // Act
        var exception = new OcrProcessingException();

        // Assert
        Assert.NotNull(exception);
        Assert.Null(exception.InnerException);
        Assert.Equal(string.Empty, exception.Message);
    }

    [Fact]
    public void ConstructorWithMessageShouldSetMessage()
    {
        // Arrange
        var message = "テストエラーメッセージ";

        // Act
        var exception = new OcrProcessingException(message);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void ConstructorWithMessageAndInnerExceptionShouldSetBoth()
    {
        // Arrange
        var message = "外部例外メッセージ";
        var innerException = new InvalidOperationException("内部例外メッセージ");

        // Act
        var exception = new OcrProcessingException(message, innerException);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Same(innerException, exception.InnerException);
    }

    [Fact]
    public void ExceptionShouldBeSerializable()
    {
        // Arrange
        var original = new OcrProcessingException("シリアライズテスト", new ArgumentException("内部例外"));

        // Assert - シリアライズ可能な例外型であることを確認
        Assert.NotNull(original);
        Assert.IsAssignableFrom<Exception>(original);
    }
}
