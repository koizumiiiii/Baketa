using System;
using System.Collections.Generic;
using System.Linq;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece;
using Xunit;

namespace Baketa.Infrastructure.Tests.Translation.Local.Onnx.SentencePiece;

/// <summary>
/// ModelMetadataクラスのテスト
/// </summary>
public class ModelMetadataTests
{
    [Fact]
    public void Constructor_DefaultValues_AreCorrect()
    {
        // Act
        var metadata = new ModelMetadata();

        // Assert
        Assert.Equal(string.Empty, metadata.ModelName);
        Assert.Equal(string.Empty, metadata.Version);
        Assert.Equal(string.Empty, metadata.Checksum);
        Assert.Null(metadata.SourceUrl);
        Assert.Equal(string.Empty, metadata.Description);
        Assert.Equal(string.Empty, metadata.SourceLanguage);
        Assert.Equal(string.Empty, metadata.TargetLanguage);
        Assert.Equal("SentencePiece", metadata.ModelType);
        Assert.Equal(0, metadata.Size);
        Assert.Equal(default, metadata.DownloadedAt);
        Assert.Equal(default, metadata.LastAccessedAt);
        Assert.NotNull(metadata.CustomMetadata);
        Assert.Empty(metadata.CustomMetadata);
        Assert.Null(metadata.ExpiresAt);
    }

    [Fact]
    public void IsValid_WithinCacheDays_ReturnsTrue()
    {
        // Arrange
        var metadata = new ModelMetadata
        {
            DownloadedAt = DateTime.UtcNow.AddDays(-10)
        };

        // Act
        var isValid = metadata.IsValid(cacheDays: 30);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void IsValid_ExceedsCacheDays_ReturnsFalse()
    {
        // Arrange
        var metadata = new ModelMetadata
        {
            DownloadedAt = DateTime.UtcNow.AddDays(-40)
        };

        // Act
        var isValid = metadata.IsValid(cacheDays: 30);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void IsValid_WithExpiresAt_UsesExpirationDate()
    {
        // Arrange
        var metadata = new ModelMetadata
        {
            DownloadedAt = DateTime.UtcNow.AddDays(-40), // キャッシュ期限を超過
            ExpiresAt = DateTime.UtcNow.AddDays(10) // 有効期限は未来
        };

        // Act
        var isValid = metadata.IsValid(cacheDays: 30);

        // Assert
        Assert.True(isValid); // ExpiresAtが優先されるため、true
    }

    [Fact]
    public void IsValid_WithExpiredExpiresAt_ReturnsFalse()
    {
        // Arrange
        var metadata = new ModelMetadata
        {
            DownloadedAt = DateTime.UtcNow.AddDays(-10), // キャッシュ期限内
            ExpiresAt = DateTime.UtcNow.AddDays(-5) // 有効期限が過去
        };

        // Act
        var isValid = metadata.IsValid(cacheDays: 30);

        // Assert
        Assert.False(isValid); // ExpiresAtが過去のため、false
    }

    [Fact]
    public void Clone_CreatesDeepCopy()
    {
        // Arrange
        var original = new ModelMetadata
        {
            ModelName = "test-model",
            DownloadedAt = DateTime.UtcNow,
            Version = "1.0.0",
            Size = 1024,
            Checksum = "abc123",
            LastAccessedAt = DateTime.UtcNow,
            SourceUrl = new Uri("https://example.com/model.bin"),
            Description = "Test model",
            SourceLanguage = "ja",
            TargetLanguage = "en",
            ModelType = "TestType",
            CustomMetadata = new Dictionary<string, object> { { "key1", "value1" } },
            ExpiresAt = DateTime.UtcNow.AddDays(30)
        };

        // Act
        var clone = original.Clone();

        // Assert
        Assert.NotSame(original, clone);
        Assert.Equal(original.ModelName, clone.ModelName);
        Assert.Equal(original.DownloadedAt, clone.DownloadedAt);
        Assert.Equal(original.Version, clone.Version);
        Assert.Equal(original.Size, clone.Size);
        Assert.Equal(original.Checksum, clone.Checksum);
        Assert.Equal(original.LastAccessedAt, clone.LastAccessedAt);
        Assert.Equal(original.SourceUrl, clone.SourceUrl);
        Assert.Equal(original.Description, clone.Description);
        Assert.Equal(original.SourceLanguage, clone.SourceLanguage);
        Assert.Equal(original.TargetLanguage, clone.TargetLanguage);
        Assert.Equal(original.ModelType, clone.ModelType);
        Assert.Equal(original.ExpiresAt, clone.ExpiresAt);
        
        // CustomMetadataは深いコピーであることを確認
        Assert.NotSame(original.CustomMetadata, clone.CustomMetadata);
        Assert.Equal(original.CustomMetadata.Count, clone.CustomMetadata.Count);
        Assert.Equal(original.CustomMetadata["key1"], clone.CustomMetadata["key1"]);
    }

    [Theory]
    [InlineData("", "", "")]
    [InlineData("ja", "", "")]
    [InlineData("", "en", "")]
    [InlineData("ja", "en", "ja-en")]
    [InlineData("zh-CN", "en", "zh-CN-en")]
    public void LanguagePair_ReturnsCorrectFormat(string sourceLanguage, string targetLanguage, string expected)
    {
        // Arrange
        var metadata = new ModelMetadata
        {
            SourceLanguage = sourceLanguage,
            TargetLanguage = targetLanguage
        };

        // Act
        var languagePair = metadata.LanguagePair;

        // Assert
        Assert.Equal(expected, languagePair);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(1572864, "1.5 MB")]
    [InlineData(1073741824, "1.0 GB")]
    [InlineData(2147483648, "2.0 GB")]
    public void FormattedSize_ReturnsCorrectFormat(long size, string expected)
    {
        // Arrange
        var metadata = new ModelMetadata { Size = size };

        // Act
        var formattedSize = metadata.FormattedSize;

        // Assert
        Assert.Equal(expected, formattedSize);
    }

    [Fact]
    public void GetTimeAgo_ReturnsCorrectFormat()
    {
        // Arrange & Act & Assert
        var metadata = new ModelMetadata { LastAccessedAt = DateTime.UtcNow.AddMinutes(-30) };
        Assert.True(metadata.GetTimeAgo().Contains("minutes ago", StringComparison.Ordinal));

        metadata.LastAccessedAt = DateTime.UtcNow.AddHours(-5);
        Assert.True(metadata.GetTimeAgo().Contains("hours ago", StringComparison.Ordinal));

        metadata.LastAccessedAt = DateTime.UtcNow.AddDays(-3);
        Assert.True(metadata.GetTimeAgo().Contains("days ago", StringComparison.Ordinal));

        metadata.LastAccessedAt = DateTime.UtcNow.AddDays(-14);
        Assert.True(metadata.GetTimeAgo().Contains("weeks ago", StringComparison.Ordinal));

        metadata.LastAccessedAt = DateTime.UtcNow.AddDays(-60);
        Assert.True(metadata.GetTimeAgo().Contains("months ago", StringComparison.Ordinal));

        metadata.LastAccessedAt = DateTime.UtcNow.AddDays(-400);
        Assert.True(metadata.GetTimeAgo().Contains("years ago", StringComparison.Ordinal));
    }

    [Fact]
    public void ToString_ReturnsCorrectFormat()
    {
        // Arrange
        var metadata = new ModelMetadata
        {
            ModelName = "test-model",
            SourceLanguage = "ja",
            TargetLanguage = "en",
            Size = 1024,
            Version = "1.0.0"
        };

        // Act
        var toString = metadata.ToString();

        // Assert
        Assert.True(toString.Contains("test-model", StringComparison.Ordinal));
        Assert.True(toString.Contains("ja-en", StringComparison.Ordinal));
        Assert.True(toString.Contains("1.0 KB", StringComparison.Ordinal));
        Assert.True(toString.Contains("1.0.0", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ValidMetadata_ReturnsValid()
    {
        // Arrange
        var metadata = new ModelMetadata
        {
            ModelName = "valid-model",
            Size = 1024,
            DownloadedAt = DateTime.UtcNow.AddMinutes(-10),
            SourceUrl = new Uri("https://example.com/model.bin")
        };

        // Act
        var result = metadata.Validate();

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_InvalidMetadata_ReturnsErrors()
    {
        // Arrange
        var futureDate = DateTime.UtcNow.AddHours(1); // 確実に未来の日時（1時間後）
        var metadata = new ModelMetadata
        {
            ModelName = "", // 無効: 空文字
            Size = -1, // 無効: 負の値
            DownloadedAt = futureDate, // 無効: 未来の日時
            SourceUrl = new Uri("relative-path", UriKind.Relative) // 無効: 相対パスのURL
        };

        // Act
        var result = metadata.Validate();

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
        
        // 期待されるエラーメッセージを直接チェック
        Assert.Contains("ModelName is required", result.Errors);
        Assert.Contains("Size must be greater than 0", result.Errors);
        Assert.Contains("DownloadedAt cannot be in the future", result.Errors);
        Assert.Contains("SourceUrl must be an absolute URL if provided", result.Errors);
    }

    [Fact]
    public void Validate_DefaultDownloadedAt_ReturnsError()
    {
        // Arrange - DownloadedAtがdefault(DateTime)の場合のテスト
        var metadata = new ModelMetadata
        {
            ModelName = "valid-model",
            Size = 1024,
            DownloadedAt = default // 無効: default値
        };

        // Act
        var result = metadata.Validate();

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("DownloadedAt must be set", result.Errors);
    }

    [Fact]
    public void Validate_NullSourceUrl_IsValid()
    {
        // Arrange
        var metadata = new ModelMetadata
        {
            ModelName = "valid-model",
            Size = 1024,
            DownloadedAt = DateTime.UtcNow.AddMinutes(-10),
            SourceUrl = null // nullは有効
        };

        // Act
        var result = metadata.Validate();

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidationResult_GetErrorMessage_ReturnsJoinedErrors()
    {
        // Arrange
        var result = new ValidationResult
        {
            IsValid = false,
            Errors = new List<string> { "Error 1", "Error 2", "Error 3" }.AsReadOnly()
        };

        // Act
        var errorMessage = result.GetErrorMessage();

        // Assert
        Assert.Equal("Error 1; Error 2; Error 3", errorMessage);
    }

    [Fact]
    public void ValidationResult_EmptyErrors_ReturnsEmptyString()
    {
        // Arrange
        var result = new ValidationResult
        {
            IsValid = true,
            Errors = new List<string>().AsReadOnly()
        };

        // Act
        var errorMessage = result.GetErrorMessage();

        // Assert
        Assert.Equal(string.Empty, errorMessage);
    }

    [Fact]
    public void CustomMetadata_CanStoreVariousTypes()
    {
        // Arrange
        var metadata = new ModelMetadata();

        // Act
        metadata.CustomMetadata["string"] = "text";
        metadata.CustomMetadata["int"] = 42;
        metadata.CustomMetadata["bool"] = true;
        metadata.CustomMetadata["double"] = 3.14;
        metadata.CustomMetadata["datetime"] = DateTime.UtcNow;

        // Assert
        Assert.Equal("text", metadata.CustomMetadata["string"]);
        Assert.Equal(42, metadata.CustomMetadata["int"]);
        Assert.Equal(true, metadata.CustomMetadata["bool"]);
        Assert.Equal(3.14, metadata.CustomMetadata["double"]);
        Assert.IsType<DateTime>(metadata.CustomMetadata["datetime"]);
    }

    [Fact]
    public void ExpiresAt_Property_WorksCorrectly()
    {
        // Arrange
        var metadata = new ModelMetadata();
        var expirationDate = DateTime.UtcNow.AddDays(30);

        // Act
        metadata.ExpiresAt = expirationDate;

        // Assert
        Assert.Equal(expirationDate, metadata.ExpiresAt);

        // Act - null assignment
        metadata.ExpiresAt = null;

        // Assert
        Assert.Null(metadata.ExpiresAt);
    }

    [Theory]
    [InlineData("test-model", "ja", "en", 1024, "1.0.0")]
    [InlineData("another-model", "zh", "fr", 2048, "2.1.3")]
    public void Properties_CanBeSetAndRetrieved(string modelName, string sourceLang, string targetLang, long size, string version)
    {
        // Arrange
        var metadata = new ModelMetadata();

        // Act
        metadata.ModelName = modelName;
        metadata.SourceLanguage = sourceLang;
        metadata.TargetLanguage = targetLang;
        metadata.Size = size;
        metadata.Version = version;

        // Assert
        Assert.Equal(modelName, metadata.ModelName);
        Assert.Equal(sourceLang, metadata.SourceLanguage);
        Assert.Equal(targetLang, metadata.TargetLanguage);
        Assert.Equal(size, metadata.Size);
        Assert.Equal(version, metadata.Version);
    }
}
