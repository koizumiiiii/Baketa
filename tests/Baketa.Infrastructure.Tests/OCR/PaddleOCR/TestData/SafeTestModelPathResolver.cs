using Baketa.Infrastructure.OCR.PaddleOCR.Models;
using System.IO;

namespace Baketa.Infrastructure.Tests.OCR.PaddleOCR.TestData;

/// <summary>
/// テスト用の安全なModelPathResolver
/// 実際のファイルシステムにアクセスせず、テスト専用のパスを提供します
/// </summary>
public class SafeTestModelPathResolver : IModelPathResolver
{
    private readonly string _baseDirectory;

    public SafeTestModelPathResolver(string baseDirectory)
    {
        _baseDirectory = baseDirectory ?? throw new ArgumentNullException(nameof(baseDirectory));
    }

    public string GetModelsRootDirectory()
    {
        return Path.Combine(_baseDirectory, "Models");
    }

    public string GetDetectionModelsDirectory()
    {
        return Path.Combine(GetModelsRootDirectory(), "detection");
    }

    public string GetRecognitionModelsDirectory(string language)
    {
        return Path.Combine(GetModelsRootDirectory(), "recognition", language);
    }

    public string GetDetectionModelPath(string modelName)
    {
        return Path.Combine(GetDetectionModelsDirectory(), $"{modelName}.onnx");
    }

    public string GetRecognitionModelPath(string language, string modelName)
    {
        return Path.Combine(GetRecognitionModelsDirectory(language), $"{modelName}.onnx");
    }

    public string GetClassificationModelPath(string modelName)
    {
        return Path.Combine(GetModelsRootDirectory(), "classification", $"{modelName}.onnx");
    }

    public string GetTempDirectory()
    {
        return Path.Combine(_baseDirectory, "Temp");
    }

    public bool FileExists(string path)
    {
        // テスト用：常にfalseを返してモデルダウンロードをスキップ
        return false;
    }

    public void EnsureDirectoryExists(string directoryPath)
    {
        // 安全なテスト専用ディレクトリのみを作成
        if (string.IsNullOrWhiteSpace(directoryPath))
            return;

        // ネットワークパスを検出して拒否
        if (directoryPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            throw new ArgumentException($"無効なネットワークパス: {directoryPath}", nameof(directoryPath));
        }

        // テストベースディレクトリ以下のパスのみ許可
        if (!directoryPath.StartsWith(_baseDirectory, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"テストベースディレクトリ外のパス: {directoryPath}", nameof(directoryPath));
        }

        try
        {
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"ディレクトリの作成に失敗: {directoryPath}", ex);
        }
    }
}
