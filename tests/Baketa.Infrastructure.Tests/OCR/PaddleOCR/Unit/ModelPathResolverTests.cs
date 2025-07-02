using Xunit;
using Baketa.Infrastructure.OCR.PaddleOCR.Models;
using System.IO;

namespace Baketa.Infrastructure.Tests.OCR.PaddleOCR.Unit;

/// <summary>
/// ModelPathResolver関連の単体テスト
/// Phase 4: テストと検証 - モデル管理テスト
/// </summary>
public class ModelPathResolverTests : IDisposable
{
    private readonly string _testBaseDirectory;
    private readonly DefaultModelPathResolver _pathResolver;
    private bool _disposed;

    public ModelPathResolverTests()
    {
        _testBaseDirectory = Path.Combine(Path.GetTempPath(), "BaketaModelPathTest", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testBaseDirectory);
        _pathResolver = new DefaultModelPathResolver(_testBaseDirectory);
    }

    #region コンストラクタテスト

    [Fact]
    public void Constructor_ValidBaseDirectory_CreatesInstance()
    {
        // Act & Assert
        Assert.NotNull(_pathResolver);
    }

    [Theory]
    [InlineData(null)]
    public void Constructor_InvalidBaseDirectory_ThrowsArgumentNullException(string? baseDirectory)
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new DefaultModelPathResolver(baseDirectory!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_InvalidBaseDirectory_ThrowsArgumentException(string baseDirectory)
    {
        // Root cause solution: Avoid network paths in tests
        // Use local invalid paths instead of network paths to prevent access issues
        
        // Act & Assert
        Assert.Throws<ArgumentException>(() => new DefaultModelPathResolver(baseDirectory));
    }

    #endregion

    #region ディレクトリパス解決テスト

    [Fact]
    public void GetModelsRootDirectory_ReturnsCorrectPath()
    {
        // Act
        var modelsPath = _pathResolver.GetModelsRootDirectory();

        // Assert
        var expectedPath = Path.Combine(_testBaseDirectory, "Models");
        Assert.Equal(expectedPath, modelsPath);
    }

    [Fact]
    public void GetDetectionModelsDirectory_ReturnsCorrectPath()
    {
        // Act
        var detectionPath = _pathResolver.GetDetectionModelsDirectory();

        // Assert
        var expectedPath = Path.Combine(_testBaseDirectory, "Models", "detection");
        Assert.Equal(expectedPath, detectionPath);
    }

    [Theory]
    [InlineData("eng")]
    [InlineData("jpn")]
    [InlineData("chs")]
    [InlineData("cht")]
    public void GetRecognitionModelsDirectory_ValidLanguageCodes_ReturnsCorrectPaths(string languageCode)
    {
        // Act
        var recognitionPath = _pathResolver.GetRecognitionModelsDirectory(languageCode);

        // Assert
        var expectedPath = Path.Combine(_testBaseDirectory, "Models", "recognition", languageCode);
        Assert.Equal(expectedPath, recognitionPath);
    }

    [Theory]
    [InlineData(null)]
    public void GetRecognitionModelsDirectory_InvalidLanguageCode_ThrowsArgumentNullException(string? languageCode)
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _pathResolver.GetRecognitionModelsDirectory(languageCode!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GetRecognitionModelsDirectory_InvalidLanguageCode_ThrowsArgumentException(string languageCode)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _pathResolver.GetRecognitionModelsDirectory(languageCode));
    }

    #endregion

    #region モデルファイルパス解決テスト

    [Theory]
    [InlineData("det_db_standard")]
    [InlineData("det_db_lite")]
    [InlineData("custom_detection_model")]
    public void GetDetectionModelPath_ValidModelNames_ReturnsCorrectPaths(string modelName)
    {
        // Act
        var modelPath = _pathResolver.GetDetectionModelPath(modelName);

        // Assert
        var expectedPath = Path.Combine(_testBaseDirectory, "Models", "detection", $"{modelName}.onnx");
        Assert.Equal(expectedPath, modelPath);
    }

    [Theory]
    [InlineData(null)]
    public void GetDetectionModelPath_InvalidModelName_ThrowsArgumentNullException(string? modelName)
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _pathResolver.GetDetectionModelPath(modelName!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GetDetectionModelPath_InvalidModelName_ThrowsArgumentException(string modelName)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _pathResolver.GetDetectionModelPath(modelName));
    }

    [Theory]
    [InlineData("eng", "rec_english_standard")]
    [InlineData("jpn", "rec_japan_standard")]
    [InlineData("chs", "rec_chinese_simplified")]
    [InlineData("cht", "rec_chinese_traditional")]
    public void GetRecognitionModelPath_ValidParameters_ReturnsCorrectPaths(string languageCode, string modelName)
    {
        // Act
        var modelPath = _pathResolver.GetRecognitionModelPath(languageCode, modelName);

        // Assert
        var expectedPath = Path.Combine(_testBaseDirectory, "Models", "recognition", languageCode, $"{modelName}.onnx");
        Assert.Equal(expectedPath, modelPath);
    }

    [Theory]
    [InlineData(null, "rec_english_standard")]
    [InlineData("eng", null)]
    public void GetRecognitionModelPath_InvalidParameters_ThrowsArgumentNullException(string? languageCode, string? modelName)
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _pathResolver.GetRecognitionModelPath(languageCode!, modelName!));
    }

    [Theory]
    [InlineData("", "rec_english_standard")]
    [InlineData("   ", "rec_english_standard")]
    [InlineData("eng", "")]
    [InlineData("eng", "   ")]
    public void GetRecognitionModelPath_InvalidParameters_ThrowsArgumentException(string languageCode, string modelName)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _pathResolver.GetRecognitionModelPath(languageCode, modelName));
    }

    [Theory]
    [InlineData("cls_standard")]
    [InlineData("cls_lite")]
    [InlineData("custom_classification_model")]
    public void GetClassificationModelPath_ValidModelNames_ReturnsCorrectPaths(string modelName)
    {
        // Act
        var modelPath = _pathResolver.GetClassificationModelPath(modelName);

        // Assert
        var expectedPath = Path.Combine(_testBaseDirectory, "Models", "classification", $"{modelName}.onnx");
        Assert.Equal(expectedPath, modelPath);
    }

    [Theory]
    [InlineData(null)]
    public void GetClassificationModelPath_InvalidModelName_ThrowsArgumentNullException(string? modelName)
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _pathResolver.GetClassificationModelPath(modelName!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GetClassificationModelPath_InvalidModelName_ThrowsArgumentException(string modelName)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _pathResolver.GetClassificationModelPath(modelName));
    }

    #endregion

    #region パス正規化テスト

    [Fact]
    public void AllPaths_UseCorrectDirectorySeparators()
    {
        // Act
        var paths = new[]
        {
            _pathResolver.GetModelsRootDirectory(),
            _pathResolver.GetDetectionModelsDirectory(),
            _pathResolver.GetRecognitionModelsDirectory("eng"),
            _pathResolver.GetDetectionModelPath("det_db_standard"),
            _pathResolver.GetRecognitionModelPath("eng", "rec_english_standard"),
            _pathResolver.GetClassificationModelPath("cls_standard")
        };

        // Assert
        foreach (var path in paths)
        {
            Assert.False(path.Contains('/', StringComparison.Ordinal), $"Path should not contain forward slashes: {path}");
            Assert.True(Path.IsPathRooted(path), $"Path should be rooted: {path}");
        }
    }

    [Fact]
    public void AllPaths_StartWithBaseDirectory()
    {
        // Act
        var paths = new[]
        {
            _pathResolver.GetModelsRootDirectory(),
            _pathResolver.GetDetectionModelsDirectory(),
            _pathResolver.GetRecognitionModelsDirectory("eng"),
            _pathResolver.GetDetectionModelPath("det_db_standard"),
            _pathResolver.GetRecognitionModelPath("eng", "rec_english_standard"),
            _pathResolver.GetClassificationModelPath("cls_standard")
        };

        // Assert
        foreach (var path in paths)
        {
            Assert.StartsWith(_testBaseDirectory, path, StringComparison.OrdinalIgnoreCase);
        }
    }

    #endregion

    #region ファイル拡張子テスト

    [Theory]
    [InlineData("det_db_standard")]
    [InlineData("custom_model")]
    public void GetDetectionModelPath_AlwaysReturnsOnnxExtension(string modelName)
    {
        // Act
        var modelPath = _pathResolver.GetDetectionModelPath(modelName);

        // Assert
        Assert.EndsWith(".onnx", modelPath, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("eng", "rec_english_standard")]
    [InlineData("jpn", "custom_model")]
    public void GetRecognitionModelPath_AlwaysReturnsOnnxExtension(string languageCode, string modelName)
    {
        // Act
        var modelPath = _pathResolver.GetRecognitionModelPath(languageCode, modelName);

        // Assert
        Assert.EndsWith(".onnx", modelPath, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("cls_standard")]
    [InlineData("custom_classifier")]
    public void GetClassificationModelPath_AlwaysReturnsOnnxExtension(string modelName)
    {
        // Act
        var modelPath = _pathResolver.GetClassificationModelPath(modelName);

        // Assert
        Assert.EndsWith(".onnx", modelPath, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region 階層構造テスト

    [Fact]
    public void DirectoryHierarchy_FollowsExpectedStructure()
    {
        // Act
        var modelsRoot = _pathResolver.GetModelsRootDirectory();
        var detectionDir = _pathResolver.GetDetectionModelsDirectory();
        var recognitionEngDir = _pathResolver.GetRecognitionModelsDirectory("eng");
        var recognitionJpnDir = _pathResolver.GetRecognitionModelsDirectory("jpn");

        // Assert - 階層関係の確認
        Assert.True(detectionDir.StartsWith(modelsRoot, StringComparison.OrdinalIgnoreCase), "Detection directory should be under models root");
        Assert.True(recognitionEngDir.StartsWith(modelsRoot, StringComparison.OrdinalIgnoreCase), "Recognition ENG directory should be under models root");
        Assert.True(recognitionJpnDir.StartsWith(modelsRoot, StringComparison.OrdinalIgnoreCase), "Recognition JPN directory should be under models root");

        // Assert - ディレクトリ名の確認
        Assert.EndsWith("detection", detectionDir, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("recognition", "eng"), recognitionEngDir, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("recognition", "jpn"), recognitionJpnDir, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ModelFilePaths_AreUnderCorrectDirectories()
    {
        // Act
        var detectionModelPath = _pathResolver.GetDetectionModelPath("det_db_standard");
        var recognitionModelPath = _pathResolver.GetRecognitionModelPath("eng", "rec_english_standard");
        var classificationModelPath = _pathResolver.GetClassificationModelPath("cls_standard");

        var detectionDir = _pathResolver.GetDetectionModelsDirectory();
        var recognitionDir = _pathResolver.GetRecognitionModelsDirectory("eng");

        // Assert
        Assert.True(detectionModelPath.StartsWith(detectionDir, StringComparison.OrdinalIgnoreCase), "Detection model should be under detection directory");
        Assert.True(recognitionModelPath.StartsWith(recognitionDir, StringComparison.OrdinalIgnoreCase), "Recognition model should be under recognition directory");
        Assert.True(classificationModelPath.StartsWith(_pathResolver.GetModelsRootDirectory(), StringComparison.OrdinalIgnoreCase), "Classification model should be under models root");
    }

    #endregion

    #region エッジケーステスト

    [Fact]
    public void GetRecognitionModelsDirectory_CaseInsensitive_HandlesCorrectly()
    {
        // Act
        var lowerCasePath = _pathResolver.GetRecognitionModelsDirectory("eng");
        var upperCasePath = _pathResolver.GetRecognitionModelsDirectory("ENG");

        // Assert
        // 実装では大文字小文字をそのまま使用するため、異なるパスになる
        Assert.NotEqual(lowerCasePath, upperCasePath);
        Assert.EndsWith("eng", lowerCasePath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("ENG", upperCasePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ModelPaths_WithSpecialCharacters_HandledCorrectly()
    {
        // Arrange
        const string specialModelName = "model_with-special.chars_123";
        const string specialLanguage = "zh-cn";

        // Act & Assert - 特殊文字を含む名前でも例外が発生しないことを確認
        var detectionPath = _pathResolver.GetDetectionModelPath(specialModelName);
        var recognitionPath = _pathResolver.GetRecognitionModelPath(specialLanguage, specialModelName);

        Assert.Contains(specialModelName, detectionPath, StringComparison.Ordinal);
        Assert.Contains(specialLanguage, recognitionPath, StringComparison.Ordinal);
        Assert.Contains(specialModelName, recognitionPath, StringComparison.Ordinal);
    }

    [Fact]
    public void BaseDirectory_WithTrailingSlash_HandledCorrectly()
    {
        // Arrange
        var baseDirectoryWithSlash = _testBaseDirectory + Path.DirectorySeparatorChar;
        var pathResolverWithSlash = new DefaultModelPathResolver(baseDirectoryWithSlash);

        // Act
        var modelsPath = pathResolverWithSlash.GetModelsRootDirectory();
        var detectionPath = pathResolverWithSlash.GetDetectionModelsDirectory();

        // Assert
        Assert.StartsWith(_testBaseDirectory, modelsPath, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(_testBaseDirectory, detectionPath, StringComparison.OrdinalIgnoreCase);
        // 重複したディレクトリセパレータがないことを確認
        Assert.DoesNotContain($"{Path.DirectorySeparatorChar}{Path.DirectorySeparatorChar}", modelsPath, StringComparison.Ordinal);
        Assert.DoesNotContain($"{Path.DirectorySeparatorChar}{Path.DirectorySeparatorChar}", detectionPath, StringComparison.Ordinal);
    }

    #endregion

    #region IDisposable実装

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // テスト用ディレクトリのクリーンアップ
                try
                {
                    if (Directory.Exists(_testBaseDirectory))
                    {
                        Directory.Delete(_testBaseDirectory, true);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // アクセス権限エラーは無視
                }
                catch (DirectoryNotFoundException)
                {
                    // ディレクトリが既に削除されている場合は無視
                }
                catch (IOException)
                {
                    // I/Oエラーは無視
                }
            }
            
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    #endregion
}
