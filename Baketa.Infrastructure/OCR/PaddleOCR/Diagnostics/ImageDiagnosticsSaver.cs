using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Imaging;

namespace Baketa.Infrastructure.OCR.PaddleOCR.Diagnostics;

/// <summary>
/// OCR診断用画像保存サービス
/// デバッグと問題分析のため画像とメタデータを保存
/// </summary>
public sealed class ImageDiagnosticsSaver : IDisposable
{
    private readonly string _outputDirectory;
    private readonly ILogger<ImageDiagnosticsSaver>? _logger;
    private readonly object _saveLock = new();
    private bool _disposed;
    
    // JsonSerializerOptionsをキャッシュして再利用
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ImageDiagnosticsSaver(string outputDirectory, ILogger<ImageDiagnosticsSaver>? logger = null)
    {
        _outputDirectory = outputDirectory ?? throw new ArgumentNullException(nameof(outputDirectory));
        _logger = logger;
        
        EnsureDirectoryExists();
    }

    /// <summary>
    /// 診断情報付きで画像を保存
    /// </summary>
    public async Task<string> SaveDiagnosticImageAsync(
        IImage image,
        string baseName,
        Dictionary<string, object>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(baseName);
        
        ObjectDisposedException.ThrowIf(_disposed, this);

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", System.Globalization.CultureInfo.InvariantCulture);
        var safeBaseName = SanitizeFileName(baseName);
        var imageFileName = $"{timestamp}_{safeBaseName}.png";
        var metadataFileName = $"{timestamp}_{safeBaseName}_metadata.json";

        var imagePath = Path.Combine(_outputDirectory, imageFileName);
        var metadataPath = Path.Combine(_outputDirectory, metadataFileName);

        try
        {
            lock (_saveLock)
            {
                EnsureDirectoryExists();
            }

            // 画像保存
            await SaveImageAsync(image, imagePath).ConfigureAwait(false);

            // メタデータ保存
            var fullMetadata = CreateMetadata(image, metadata);
            await SaveMetadataAsync(fullMetadata, metadataPath).ConfigureAwait(false);

            _logger?.LogDebug("診断画像保存完了: {ImagePath}", imagePath);
            return imageFileName;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "診断画像保存失敗: BaseName={BaseName}", baseName);
            throw;
        }
    }

    /// <summary>
    /// エラー発生時の画像を保存
    /// </summary>
    public async Task<string> SaveErrorImageAsync(
        IImage image,
        string operationId,
        Exception exception)
    {
        var metadata = new Dictionary<string, object>
        {
            ["ErrorType"] = exception.GetType().Name,
            ["ErrorMessage"] = exception.Message,
            ["StackTrace"] = exception.StackTrace ?? "",
            ["OperationId"] = operationId
        };

        return await SaveDiagnosticImageAsync(image, $"error_{operationId}", metadata)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 成功時の結果画像を保存（テキスト領域をハイライト）
    /// </summary>
    public async Task<string> SaveResultImageAsync(
        IImage originalImage,
        string operationId,
        IEnumerable<object> textRegions)
    {
        // TODO: テキスト領域をハイライトした画像の生成
        // 現在は元画像のみ保存
        
        var metadata = new Dictionary<string, object>
        {
            ["OperationId"] = operationId,
            ["TextRegionsCount"] = textRegions?.Count() ?? 0,
            ["ResultType"] = "Success"
        };

        return await SaveDiagnosticImageAsync(originalImage, $"result_{operationId}", metadata)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 古い診断ファイルをクリーンアップ
    /// </summary>
    public Task CleanupOldFilesAsync(TimeSpan maxAge)
    {
        return Task.Run(() =>
        {
            try
            {
                if (!Directory.Exists(_outputDirectory))
                    return;

                var cutoffTime = DateTime.Now - maxAge;
                var files = Directory.GetFiles(_outputDirectory, "*", SearchOption.TopDirectoryOnly);

                var deletedCount = 0;
                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.CreationTime < cutoffTime)
                    {
                        try
                        {
                            File.Delete(file);
                            deletedCount++;
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "古いファイル削除失敗: {FilePath}", file);
                        }
                    }
                }

                if (deletedCount > 0)
                {
                    _logger?.LogInformation("古い診断ファイルをクリーンアップ: {Count}個削除", deletedCount);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "診断ファイルクリーンアップ中にエラー");
            }
        });
    }

    /// <summary>
    /// 診断統計をファイルに出力
    /// </summary>
    public async Task SaveDiagnosticReportAsync(object diagnosticReport)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var reportPath = Path.Combine(_outputDirectory, $"diagnostic_report_{timestamp}.json");

        try
        {
            var json = JsonSerializer.Serialize(diagnosticReport, s_jsonOptions);

            await File.WriteAllTextAsync(reportPath, json).ConfigureAwait(false);
            _logger?.LogInformation("診断レポート保存完了: {ReportPath}", reportPath);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "診断レポート保存失敗");
        }
    }

    private async Task SaveImageAsync(IImage image, string imagePath)
    {
        try
        {
            // リフレクションを使用してToByteArrayAsyncメソッドを呼び出し
            var imageType = image.GetType();
            var toByteArrayMethod = imageType.GetMethod("ToByteArrayAsync");
            
            if (toByteArrayMethod != null)
            {
                if (toByteArrayMethod.Invoke(image, null) is Task<byte[]> task)
                {
                    var imageBytes = await task.ConfigureAwait(false);
                    await File.WriteAllBytesAsync(imagePath, imageBytes).ConfigureAwait(false);
                    return;
                }
            }

            // フォールバック: 基本情報のみテキストファイルとして保存
            var imageInfo = $"Image Type: {imageType.Name}\nWidth: {image.Width}\nHeight: {image.Height}\nTimestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            await File.WriteAllTextAsync(imagePath + ".txt", imageInfo).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "画像保存失敗、メタデータのみ保存: {ImagePath}", imagePath);
            
            // 最終フォールバック: メタデータのみ保存
            var imageInfo = $"Image Save Failed: {ex.Message}\nImage Type: {image.GetType().Name}\nWidth: {image.Width}\nHeight: {image.Height}\nTimestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            await File.WriteAllTextAsync(imagePath + ".error.txt", imageInfo).ConfigureAwait(false);
        }
    }

    private static async Task SaveMetadataAsync(Dictionary<string, object> metadata, string metadataPath)
    {
        var json = JsonSerializer.Serialize(metadata, s_jsonOptions);

        await File.WriteAllTextAsync(metadataPath, json).ConfigureAwait(false);
    }

    private static Dictionary<string, object> CreateMetadata(IImage image, Dictionary<string, object>? additionalMetadata)
    {
        var metadata = new Dictionary<string, object>
        {
            ["ImageWidth"] = image.Width,
            ["ImageHeight"] = image.Height,
            ["ImageType"] = image.GetType().Name,
            ["CaptureTime"] = DateTime.UtcNow.ToString("O"),
            ["FileFormat"] = "PNG"
        };

        if (additionalMetadata != null)
        {
            foreach (var kvp in additionalMetadata)
            {
                metadata[kvp.Key] = kvp.Value;
            }
        }

        return metadata;
    }

    private void EnsureDirectoryExists()
    {
        if (!Directory.Exists(_outputDirectory))
        {
            Directory.CreateDirectory(_outputDirectory);
            _logger?.LogDebug("診断出力ディレクトリ作成: {Directory}", _outputDirectory);
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        foreach (var invalidChar in invalidChars)
        {
            fileName = fileName.Replace(invalidChar, '_');
        }
        return fileName;
    }

    public void Dispose()
    {
        if (_disposed) return;

        // 自動クリーンアップ（7日間より古いファイルを削除）
        try
        {
            CleanupOldFilesAsync(TimeSpan.FromDays(7)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Dispose時の自動クリーンアップでエラー");
        }

        _disposed = true;
    }
}

/// <summary>
/// 画像診断保存の設定
/// </summary>
public class ImageDiagnosticsOptions
{
    public string OutputDirectory { get; set; } = "ocr_diagnostics";
    public bool SaveSuccessImages { get; set; } = true;
    public bool SaveErrorImages { get; set; } = true;
    public bool SaveMetadata { get; set; } = true;
    public TimeSpan CleanupMaxAge { get; set; } = TimeSpan.FromDays(7);
    public int MaxFilesPerDay { get; set; } = 1000;
}