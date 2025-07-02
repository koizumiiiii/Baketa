using Microsoft.Extensions.Logging;
using System.IO;

namespace Baketa.Infrastructure.OCR.PaddleOCR.Models;

/// <summary>
/// デフォルトのモデルパス解決実装
/// </summary>
public class DefaultModelPathResolver : IModelPathResolver
{
    private readonly string _baseDirectory;
    private readonly ILogger<DefaultModelPathResolver>? _logger;

    public DefaultModelPathResolver(string baseDirectory, ILogger<DefaultModelPathResolver>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory, nameof(baseDirectory));
            
        _baseDirectory = baseDirectory;
        _logger = logger;
        
        _logger?.LogDebug("DefaultModelPathResolver initialized with base directory: {BaseDirectory}", _baseDirectory);
    }

    /// <inheritdoc />
    public string GetModelsRootDirectory() 
        => Path.Combine(_baseDirectory, "Models");

    /// <inheritdoc />
    public string GetDetectionModelsDirectory() 
        => Path.Combine(GetModelsRootDirectory(), "detection");

    /// <inheritdoc />
    public string GetRecognitionModelsDirectory(string languageCode) 
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(languageCode, nameof(languageCode));
            
        return Path.Combine(GetModelsRootDirectory(), "recognition", languageCode);
    }

    /// <inheritdoc />
    public string GetDetectionModelPath(string modelName) 
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName, nameof(modelName));
            
        return Path.Combine(GetDetectionModelsDirectory(), $"{modelName}.onnx");
    }

    /// <inheritdoc />
    public string GetRecognitionModelPath(string languageCode, string modelName) 
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(languageCode, nameof(languageCode));
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName, nameof(modelName));
            
        return Path.Combine(GetRecognitionModelsDirectory(languageCode), $"{modelName}.onnx");
    }

    /// <inheritdoc />
    public string GetClassificationModelPath(string modelName) 
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName, nameof(modelName));
            
        return Path.Combine(GetModelsRootDirectory(), "classification", $"{modelName}.onnx");
    }

    /// <inheritdoc />
    public bool FileExists(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath, nameof(filePath));
        
        var exists = File.Exists(filePath);
        _logger?.LogDebug("File existence check for {FilePath}: {Exists}", filePath, exists);
        
        return exists;
    }

    /// <inheritdoc />
    public void EnsureDirectoryExists(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath, nameof(directoryPath));
        
        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
            _logger?.LogInformation("Created directory: {DirectoryPath}", directoryPath);
        }
        else
        {
            _logger?.LogDebug("Directory already exists: {DirectoryPath}", directoryPath);
        }
    }
}
