using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;

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
    /// バイト配列を受け取ってROI画像を保存
    /// </summary>
    public async Task SaveResultImageAsync(
        byte[] imageBytes,
        string filePath,
        string operationId)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(operationId);
        
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        try
        {
            // ディレクトリが存在しない場合は作成
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            await File.WriteAllBytesAsync(filePath, imageBytes).ConfigureAwait(false);
            _logger?.LogTrace("ROI画像保存完了: {FilePath}, 操作ID: {OperationId}", filePath, operationId);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "ROI画像保存失敗: {FilePath}, 操作ID: {OperationId}", filePath, operationId);
            throw;
        }
    }
    
    /// <summary>
    /// 検出されたテキスト領域を赤枠で囲んだ全体画像を保存
    /// </summary>
    public async Task SaveAnnotatedFullImageAsync(
        byte[] originalImageBytes,
        IEnumerable<OcrTextRegion> textRegions,
        string filePath,
        string operationId)
    {
        ArgumentNullException.ThrowIfNull(originalImageBytes);
        ArgumentNullException.ThrowIfNull(textRegions);
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(operationId);
        
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        try
        {
            // ディレクトリが存在しない場合は作成
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            // 元画像に赤枠を描画
            var annotatedImageBytes = await CreateAnnotatedImageAsync(originalImageBytes, textRegions).ConfigureAwait(false);
            
            await File.WriteAllBytesAsync(filePath, annotatedImageBytes).ConfigureAwait(false);
            _logger?.LogTrace("赤枠付きROI画像保存完了: {FilePath}, 操作ID: {OperationId}", filePath, operationId);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "赤枠付きROI画像保存失敗: {FilePath}, 操作ID: {OperationId}", filePath, operationId);
            throw;
        }
    }
    
    /// <summary>
    /// 画像に検出されたテキスト領域を赤枠で囲んだ注釈画像を作成
    /// </summary>
    private async Task<byte[]> CreateAnnotatedImageAsync(byte[] originalImageBytes, IEnumerable<OcrTextRegion> textRegions)
    {
        return await Task.Run(() =>
        {
            using var memoryStream = new MemoryStream(originalImageBytes);
            using var originalBitmap = new System.Drawing.Bitmap(memoryStream);
            using var annotatedBitmap = new System.Drawing.Bitmap(originalBitmap.Width, originalBitmap.Height);
            using var graphics = System.Drawing.Graphics.FromImage(annotatedBitmap);
            
            // 高品質描画設定
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            
            // 元画像を描画
            graphics.DrawImage(originalBitmap, 0, 0);
            
            // テキスト領域に赤枠を描画
            using var redPen = new System.Drawing.Pen(System.Drawing.Color.Red, 3.0f);
            using var textBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Red);
            using var backgroundBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(200, 255, 255, 255));
            using var font = new System.Drawing.Font("Arial", 12, System.Drawing.FontStyle.Bold);
            
            foreach (var region in textRegions)
            {
                // 赤い境界線を描画
                graphics.DrawRectangle(redPen, region.Bounds);
                
                // 信頼度とテキスト情報を描画
                var confidence = $"{region.Confidence:F2}";
                var displayText = string.IsNullOrWhiteSpace(region.Text) ? "?" : 
                                 region.Text.Length > 10 ? region.Text[..10] + "..." : region.Text;
                var label = $"{confidence} | {displayText}";
                
                var textSize = graphics.MeasureString(label, font);
                var textRect = new System.Drawing.RectangleF(
                    region.Bounds.X, 
                    Math.Max(0, region.Bounds.Y - textSize.Height - 2), 
                    textSize.Width + 4, 
                    textSize.Height + 2);
                
                // 背景を描画
                graphics.FillRectangle(backgroundBrush, textRect);
                
                // テキストを描画
                graphics.DrawString(label, font, textBrush, textRect.X + 2, textRect.Y + 1);
            }
            
            // 注釈付き画像をバイト配列に変換
            using var outputStream = new MemoryStream();
            annotatedBitmap.Save(outputStream, System.Drawing.Imaging.ImageFormat.Png);
            return outputStream.ToArray();
        }).ConfigureAwait(false);
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