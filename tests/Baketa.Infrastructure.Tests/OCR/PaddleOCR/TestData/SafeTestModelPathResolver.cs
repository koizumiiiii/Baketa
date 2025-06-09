using Baketa.Infrastructure.OCR.PaddleOCR.Models;
using System.IO;

namespace Baketa.Infrastructure.Tests.OCR.PaddleOCR.TestData;

/// <summary>
/// テスト用の安全なモデルパスリゾルバー
/// 実際のファイルシステム操作を行わず、テスト環境で安全に動作します
/// </summary>
public class SafeTestModelPathResolver : IModelPathResolver
{
    private readonly string _testBaseDirectory;

    public SafeTestModelPathResolver(string testBaseDirectory)
    {
        _testBaseDirectory = testBaseDirectory ?? throw new ArgumentNullException(nameof(testBaseDirectory));
    }

    public string GetModelsRootDirectory()
    {
        return Path.Combine(_testBaseDirectory, "Models");
    }

    public string GetDetectionModelsDirectory()
    {
        return Path.Combine(_testBaseDirectory, "Models", "detection");
    }

    public string GetRecognitionModelsDirectory(string language)
    {
        ArgumentException.ThrowIfNullOrEmpty(language);
        return Path.Combine(_testBaseDirectory, "Models", "recognition", language);
    }

    public string GetClassificationModelsDirectory()
    {
        return Path.Combine(_testBaseDirectory, "Models", "classification");
    }

    public string GetDetectionModelPath(string modelName)
    {
        ArgumentException.ThrowIfNullOrEmpty(modelName);
        return Path.Combine(GetDetectionModelsDirectory(), $"{modelName}.onnx");
    }

    public string GetRecognitionModelPath(string language, string modelName)
    {
        ArgumentException.ThrowIfNullOrEmpty(language);
        ArgumentException.ThrowIfNullOrEmpty(modelName);
        return Path.Combine(GetRecognitionModelsDirectory(language), $"{modelName}.onnx");
    }

    public string GetClassificationModelPath(string modelName)
    {
        ArgumentException.ThrowIfNullOrEmpty(modelName);
        return Path.Combine(GetClassificationModelsDirectory(), $"{modelName}.onnx");
    }

    public bool FileExists(string filePath)
    {
        // テスト用では常にファイルが存在しないとみなす（安全性のため）
        return false;
    }

    public void EnsureDirectoryExists(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(directoryPath);
        
        try
        {
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // テスト環境ではアクセス権限エラーを無視
        }
        catch (IOException)
        {
            // テスト環境ではI/Oエラーを無視
        }
        catch (ArgumentException)
        {
            // テスト環境では引数エラーを無視
        }
    }
}
