using System.IO;
using Baketa.Infrastructure.OCR.PaddleOCR.Models;

namespace Baketa.Infrastructure.Tests.OCR.PaddleOCR.TestData;

/// <summary>
/// テスト用の安全なモデルパスリゾルバー
/// 実際のファイルシステム操作を行わず、テスト環境で安全に動作します
/// </summary>
/// <param name="testBaseDirectory">テスト用ベースディレクトリパス</param>
public class SafeTestModelPathResolver(string testBaseDirectory) : IModelPathResolver
{
    private readonly string _testBaseDirectory = testBaseDirectory ?? throw new ArgumentNullException(nameof(testBaseDirectory));

    public string GetModelsRootDirectory()
    {
        return Path.Combine(_testBaseDirectory, "Models");
    }

    public string GetDetectionModelsDirectory()
    {
        return Path.Combine(_testBaseDirectory, "Models", "detection");
    }

    public string GetRecognitionModelsDirectory(string languageCode)
    {
        ArgumentException.ThrowIfNullOrEmpty(languageCode);
        return Path.Combine(_testBaseDirectory, "Models", "recognition", languageCode);
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

    public string GetRecognitionModelPath(string languageCode, string modelName)
    {
        ArgumentException.ThrowIfNullOrEmpty(languageCode);
        ArgumentException.ThrowIfNullOrEmpty(modelName);
        return Path.Combine(GetRecognitionModelsDirectory(languageCode), $"{modelName}.onnx");
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
