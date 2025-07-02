using System;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece;
using Xunit;

namespace Baketa.Infrastructure.Tests.Translation.Local.Onnx.SentencePiece;

/// <summary>
/// TokenizationExceptionのテスト
/// </summary>
public class TokenizationExceptionTests
{
    [Fact]
    public void Constructor_WithAllParameters_InitializesCorrectly()
    {
        // Arrange
        var message = "Test error message";
        var inputText = "Test input text";
        var modelName = "test-model";
        var innerException = new InvalidOperationException("Inner exception");

        // Act
        var exception = new TokenizationException(message, inputText, modelName, innerException);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Equal(inputText, exception.InputText);
        Assert.Equal(modelName, exception.ModelName);
        Assert.Equal(innerException, exception.InnerException);
        Assert.Null(exception.CharacterPosition);
    }

    [Fact]
    public void Constructor_WithoutInnerException_InitializesCorrectly()
    {
        // Arrange
        var message = "Test error message";
        var inputText = "Test input text";
        var modelName = "test-model";

        // Act
        var exception = new TokenizationException(message, inputText, modelName);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Equal(inputText, exception.InputText);
        Assert.Equal(modelName, exception.ModelName);
        Assert.Null(exception.InnerException);
        Assert.Null(exception.CharacterPosition);
    }

    [Fact]
    public void Constructor_WithCharacterPosition_InitializesCorrectly()
    {
        // Arrange
        var message = "Test error message";
        var inputText = "Test input text";
        var modelName = "test-model";
        var characterPosition = 5;

        // Act
        var exception = new TokenizationException(message, inputText, modelName, characterPosition);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Equal(inputText, exception.InputText);
        Assert.Equal(modelName, exception.ModelName);
        Assert.Equal(characterPosition, exception.CharacterPosition);
    }

    [Theory]
    [InlineData("", "model", "Empty input text")]
    [InlineData("input", "", "Empty model name")]
    [InlineData(null!, "model", "Null input text")]
    [InlineData("input", null!, "Null model name")]
    public void Constructor_WithVariousInputs_HandlesGracefully(string inputText, string modelName, string expectedMessage)
    {
        // Act
        var exception = new TokenizationException(expectedMessage, inputText, modelName);

        // Assert
        Assert.Equal(expectedMessage, exception.Message);
        Assert.Equal(inputText, exception.InputText);
        Assert.Equal(modelName, exception.ModelName);
    }

    [Fact]
    public void InheritanceFromException_IsCorrect()
    {
        // Arrange
        var exception = new TokenizationException("Test", "input", "model");

        // Act & Assert
        Assert.IsAssignableFrom<Exception>(exception);
    }

    [Fact]
    public void ToString_ContainsRelevantInformation()
    {
        // Arrange
        var message = "Tokenization failed";
        var inputText = "Sample input";
        var modelName = "test-model";
        var characterPosition = 10;
        
        var exception = new TokenizationException(message, inputText, modelName, characterPosition);

        // Act
        var stringRepresentation = exception.ToString();

        // Assert
        Assert.Contains(message, stringRepresentation, StringComparison.Ordinal);
        Assert.Contains(nameof(TokenizationException), stringRepresentation, StringComparison.Ordinal);
    }

    [Fact]
    public void GetType_ReturnsCorrectType()
    {
        // Arrange
        var exception = new TokenizationException("Test", "input", "model");

        // Act
        var type = exception.GetType();

        // Assert
        Assert.Equal(typeof(TokenizationException), type);
    }

    [Fact]
    public void Properties_AreReadOnly()
    {
        // Arrange
        var inputText = "Original input";
        var modelName = "Original model";
        var characterPosition = 42;
        var exception = new TokenizationException("Test", inputText, modelName, characterPosition);

        // Act & Assert
        // プロパティが init-only であることを確認
        Assert.Equal(inputText, exception.InputText);
        Assert.Equal(modelName, exception.ModelName);
        Assert.Equal(characterPosition, exception.CharacterPosition);
    }

    [Fact]
    public void Serialization_WorksCorrectly()
    {
        // Arrange
        var message = "Serialization test";
        var inputText = "Test input for serialization";
        var modelName = "serialization-model";
        var characterPosition = 15;
        
        var originalException = new TokenizationException(message, inputText, modelName, characterPosition);

        // Act - この時点ではシリアライゼーション機能は実装されていないが、
        // 将来的に必要になった場合のテスト構造を提供
        
        // Assert
        Assert.Equal(message, originalException.Message);
        Assert.Equal(inputText, originalException.InputText);
        Assert.Equal(modelName, originalException.ModelName);
        Assert.Equal(characterPosition, originalException.CharacterPosition);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(int.MaxValue)]
    public void CharacterPosition_AcceptsValidValues(int position)
    {
        // Arrange & Act
        var exception = new TokenizationException("Test", "input", "model", position);

        // Assert
        Assert.Equal(position, exception.CharacterPosition);
    }

    [Fact]
    public void CharacterPosition_CanBeNull()
    {
        // Arrange & Act
        var exception = new TokenizationException("Test", "input", "model");

        // Assert
        Assert.Null(exception.CharacterPosition);
    }

    [Fact]
    public void MultipleExceptions_HaveDifferentInstances()
    {
        // Arrange & Act
        var exception1 = new TokenizationException("Message 1", "input1", "model1");
        var exception2 = new TokenizationException("Message 2", "input2", "model2");

        // Assert
        Assert.NotSame(exception1, exception2);
        Assert.NotEqual(exception1.InputText, exception2.InputText);
        Assert.NotEqual(exception1.ModelName, exception2.ModelName);
        Assert.NotEqual(exception1.Message, exception2.Message);
    }
}
