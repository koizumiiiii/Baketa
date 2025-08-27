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
        
        // 🔍 [ULTRADEBUG] 保存処理開始時の詳細ログ
        Console.WriteLine($"🔍 [ROI-SAVE-START] 操作ID: {operationId}");
        Console.WriteLine($"🔍 [ROI-SAVE-START] ファイルパス: {filePath}");
        Console.WriteLine($"🔍 [ROI-SAVE-START] バイト配列サイズ: {imageBytes.Length:N0} bytes ({imageBytes.Length / 1024.0:F2} KB)");
        Console.WriteLine($"🔍 [ROI-SAVE-START] バイト配列ハッシュ: {imageBytes.Take(16).Select(b => b.ToString("X2")).Aggregate((a, b) => a + b)}...");
        
        try
        {
            // ディレクトリが存在しない場合は作成
            var directory = Path.GetDirectoryName(filePath);
            Console.WriteLine($"🔍 [ROI-DIR] ディレクトリパス: {directory}");
            
            if (!string.IsNullOrEmpty(directory))
            {
                Console.WriteLine($"🔍 [ROI-DIR] ディレクトリ存在チェック: {Directory.Exists(directory)}");
                
                if (!Directory.Exists(directory))
                {
                    Console.WriteLine($"🔍 [ROI-DIR] ディレクトリを作成中...");
                    Directory.CreateDirectory(directory);
                    Console.WriteLine($"🔍 [ROI-DIR] ディレクトリ作成完了: {Directory.Exists(directory)}");
                }
            }
            
            Console.WriteLine($"🔍 [ROI-FILE] ファイル書き込み開始...");
            var writeStart = DateTime.Now;
            
            try
            {
                // ファイル書き込み詳細モニタリング
                using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    Console.WriteLine($"🔍 [ROI-STREAM] FileStream作成成功 - パス: {filePath}");
                    Console.WriteLine($"🔍 [ROI-STREAM] FileStream設定 - Mode=Create, Access=Write, Share=None");
                    
                    await fileStream.WriteAsync(imageBytes, 0, imageBytes.Length).ConfigureAwait(false);
                    Console.WriteLine($"🔍 [ROI-STREAM] WriteAsync完了 - 書き込みバイト数: {imageBytes.Length:N0}");
                    
                    await fileStream.FlushAsync().ConfigureAwait(false);
                    Console.WriteLine($"🔍 [ROI-STREAM] FlushAsync完了");
                    
                    Console.WriteLine($"🔍 [ROI-STREAM] FileStream詳細 - CanRead={fileStream.CanRead}, CanWrite={fileStream.CanWrite}, Position={fileStream.Position}, Length={fileStream.Length}");
                }
                
                Console.WriteLine($"🔍 [ROI-STREAM] FileStreamクローズ完了 - using文終了");
            }
            catch (Exception streamEx)
            {
                Console.WriteLine($"💥 [ROI-STREAM] FileStream操作エラー: {streamEx.GetType().Name} - {streamEx.Message}");
                throw;
            }
            
            var writeEnd = DateTime.Now;
            Console.WriteLine($"🔍 [ROI-FILE] ファイル書き込み完了 - 経過時間: {(writeEnd - writeStart).TotalMilliseconds:F2}ms");
            
            // 即座のファイル状態確認
            Console.WriteLine($"🔍 [ROI-IMMEDIATE] 即座の確認開始...");
            var immediateExists = File.Exists(filePath);
            var immediateSize = immediateExists ? new FileInfo(filePath).Length : 0;
            Console.WriteLine($"🔍 [ROI-IMMEDIATE] 書き込み直後の存在: {immediateExists}");
            Console.WriteLine($"🔍 [ROI-IMMEDIATE] 書き込み直後のサイズ: {immediateSize:N0} bytes");
            
            // 100ms待機後の再確認
            Console.WriteLine($"🔍 [ROI-WAIT] 100ms待機中...");
            await Task.Delay(100).ConfigureAwait(false);
            
            var delayedExists = File.Exists(filePath);
            var delayedSize = delayedExists ? new FileInfo(filePath).Length : 0;
            Console.WriteLine($"🔍 [ROI-WAIT] 100ms後の存在: {delayedExists}");
            Console.WriteLine($"🔍 [ROI-WAIT] 100ms後のサイズ: {delayedSize:N0} bytes");
            
            // ファイル詳細情報
            if (immediateExists)
            {
                var fileInfo = new FileInfo(filePath);
                Console.WriteLine($"🔍 [ROI-DETAILS] 作成時刻: {fileInfo.CreationTime:yyyy-MM-dd HH:mm:ss.fff}");
                Console.WriteLine($"🔍 [ROI-DETAILS] 更新時刻: {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss.fff}");
                Console.WriteLine($"🔍 [ROI-DETAILS] アクセス時刻: {fileInfo.LastAccessTime:yyyy-MM-dd HH:mm:ss.fff}");
                Console.WriteLine($"🔍 [ROI-DETAILS] 読み取り専用: {fileInfo.IsReadOnly}");
                Console.WriteLine($"🔍 [ROI-DETAILS] 属性: {fileInfo.Attributes}");
            }
            
            // ディレクトリ全体のファイル数確認
            var parentDirectory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(parentDirectory) && Directory.Exists(parentDirectory))
            {
                var allFiles = Directory.GetFiles(parentDirectory, "*.png").Length;
                Console.WriteLine($"🔍 [ROI-DIR-COUNT] ディレクトリ内PNGファイル数: {allFiles}");
            }
            
            // 最終確認用
            var fileExists = delayedExists;
            var fileSize = delayedSize;
            Console.WriteLine($"🔍 [ROI-VERIFY] 最終存在確認: {fileExists}");
            Console.WriteLine($"🔍 [ROI-VERIFY] 最終ファイルサイズ: {fileSize:N0} bytes");
            
            _logger?.LogTrace("ROI画像保存完了: {FilePath}, 操作ID: {OperationId}", filePath, operationId);
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or UnauthorizedAccessException)
        {
            Console.WriteLine($"💥 [ROI-ERROR] ディレクトリ/権限エラー: {ex.GetType().Name} - {ex.Message}");
            _logger?.LogWarning("ROI画像保存失敗: {Path}, 理由: {Reason}", 
                filePath, ex.GetType().Name);
            
            // フォールバック先への保存を試行
            Console.WriteLine($"🔄 [ROI-FALLBACK] フォールバック保存を試行中...");
            await TrySaveToFallbackLocationAsync(imageBytes, filePath, operationId).ConfigureAwait(false);
        }
        catch (IOException ioEx) when (ioEx.Message.Contains("being used by another process"))
        {
            Console.WriteLine($"💥 [ROI-ERROR] ファイルロックエラー: {ioEx.Message}");
            _logger?.LogWarning("ファイルロック検出 - リトライ試行: {FilePath}", filePath);
            
            // 短時間待機してリトライ
            await Task.Delay(100).ConfigureAwait(false);
            Console.WriteLine($"🔄 [ROI-RETRY] リトライ保存を試行中...");
            await RetryImageSaveAsync(imageBytes, filePath, operationId, maxRetries: 3).ConfigureAwait(false);
        }
        catch (OutOfMemoryException memEx)
        {
            Console.WriteLine($"💥 [ROI-ERROR] メモリ不足エラー: サイズ={imageBytes.Length / 1024}KB - {memEx.Message}");
            _logger?.LogError(memEx, "メモリ不足でROI画像保存失敗: サイズ={ImageSize}KB", imageBytes.Length / 1024);
            
            // 圧縮して再試行
            Console.WriteLine($"🔄 [ROI-COMPRESS] 圧縮保存を試行中...");
            await SaveCompressedImageAsync(imageBytes, filePath, operationId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"💥 [ROI-ERROR] 予期しないエラー: {ex.GetType().Name} - {ex.Message}");
            Console.WriteLine($"💥 [ROI-ERROR] スタックトレース: {ex.StackTrace}");
            _logger?.LogError(ex, "予期しないROI画像保存エラー: {FilePath}, 操作ID: {OperationId}", filePath, operationId);
            
            // 最終フォールバック: メタデータのみ保存
            Console.WriteLine($"🔄 [ROI-METADATA] エラーメタデータ保存を試行中...");
            await SaveErrorMetadataAsync(filePath, operationId, ex).ConfigureAwait(false);
            throw;
        }
    }
    
    /// <summary>
    /// 検出されたテキスト領域を赤枠で囲んだ全体画像を保存
    /// 🎯 [COORDINATE_FIX] 低解像度画像用にTextRegion.Boundsをそのまま使用
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
        
        // 🔍 [ROI_DEBUG] 詳細デバッグログ開始
        Console.WriteLine($"🔍 [ROI_DEBUG] SaveAnnotatedFullImageAsync開始");
        Console.WriteLine($"🔍 [ROI_DEBUG] FilePath: {filePath}");
        Console.WriteLine($"🔍 [ROI_DEBUG] OperationId: {operationId}");
        Console.WriteLine($"🔍 [ROI_DEBUG] OriginalImageBytes.Length: {originalImageBytes.Length}");
        Console.WriteLine($"🔍 [ROI_DEBUG] TextRegions.Count: {textRegions.Count()}");
        
        System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_batch_ocr.txt", 
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 [ROI_DEBUG] SaveAnnotatedFullImageAsync開始 - FilePath: {filePath}{Environment.NewLine}");
        
        try
        {
            // ディレクトリが存在しない場合は作成
            var directory = Path.GetDirectoryName(filePath);
            Console.WriteLine($"🔍 [ROI_DEBUG] Directory: {directory}");
            
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Console.WriteLine($"🔍 [ROI_DEBUG] ディレクトリ作成中: {directory}");
                Directory.CreateDirectory(directory);
                Console.WriteLine($"🔍 [ROI_DEBUG] ディレクトリ作成完了");
            }
            else
            {
                Console.WriteLine($"🔍 [ROI_DEBUG] ディレクトリは既に存在: {Directory.Exists(directory)}");
            }
            
            // 🎯 [COORDINATE_FIX] 低解像度画像用に座標調整してから赤枠を描画
            Console.WriteLine($"🔍 [ROI_DEBUG] CreateAnnotatedImageAsync開始");
            var annotatedImageBytes = await CreateAnnotatedImageAsync(originalImageBytes, textRegions).ConfigureAwait(false);
            Console.WriteLine($"🔍 [ROI_DEBUG] CreateAnnotatedImageAsync完了 - AnnotatedImageBytes.Length: {annotatedImageBytes.Length}");
            
            Console.WriteLine($"🔍 [ROI_DEBUG] File.WriteAllBytesAsync開始");
            await File.WriteAllBytesAsync(filePath, annotatedImageBytes).ConfigureAwait(false);
            Console.WriteLine($"🔍 [ROI_DEBUG] File.WriteAllBytesAsync完了");
            
            // ファイル存在確認
            var fileExists = File.Exists(filePath);
            var fileSize = fileExists ? new FileInfo(filePath).Length : 0;
            Console.WriteLine($"🔍 [ROI_DEBUG] ファイル存在確認: {fileExists}, サイズ: {fileSize}");
            
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_batch_ocr.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ✅ [ROI_SUCCESS] ROI画像保存成功 - FilePath: {filePath}, Size: {fileSize}{Environment.NewLine}");
            
            _logger?.LogTrace("赤枠付きROI画像保存完了: {FilePath}, 操作ID: {OperationId}", filePath, operationId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ [ROI_ERROR] SaveAnnotatedFullImageAsync例外発生: {ex.Message}");
            Console.WriteLine($"❌ [ROI_ERROR] StackTrace: {ex.StackTrace}");
            
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_batch_ocr.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ [ROI_ERROR] ROI画像保存失敗: {ex.Message}{Environment.NewLine}");
            
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
            // 🔧 [GDI_FIX] スレッドセーフなBitmapコピー作成
            using var memoryStream = new MemoryStream(originalImageBytes);
            using var originalBitmap = new System.Drawing.Bitmap(memoryStream);
            
            // 🔧 [THREAD_SAFE] 元画像の完全なコピーを作成（並行アクセス競合を回避）
            using var safeOriginalCopy = new System.Drawing.Bitmap(originalBitmap);
            using var annotatedBitmap = new System.Drawing.Bitmap(originalBitmap.Width, originalBitmap.Height);
            using var graphics = System.Drawing.Graphics.FromImage(annotatedBitmap);
            
            // 高品質描画設定
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            
            // 🔧 [SAFE_DRAW] スレッドセーフなコピーから描画
            graphics.DrawImage(safeOriginalCopy, 0, 0);
            
            // 描画リソース準備
            using var redPen = new System.Drawing.Pen(System.Drawing.Color.Red, 3.0f);
            using var textBrush = new System.Drawing.SolidBrush(System.Drawing.Color.Red);
            using var backgroundBrush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(200, 255, 255, 255));
            using var font = new System.Drawing.Font("Arial", 12, System.Drawing.FontStyle.Bold);
            
            // 🛡️ [THREAD_SAFETY_FINAL_FIX] 完全に順次処理に変更してGDI+スレッドセーフティ問題を解決
            var regionTasks = new List<dynamic>();
            foreach (var region in textRegions)
            {
                var confidence = $"{region.Confidence:F2}";
                var displayText = string.IsNullOrWhiteSpace(region.Text) ? "?" : 
                                 region.Text.Length > 10 ? region.Text[..10] + "..." : region.Text;
                var label = $"{confidence} | {displayText}";
                
                // テキストサイズ計算（順次実行でスレッドセーフティ確保）
                var textSize = graphics.MeasureString(label, font);
                var textRect = new System.Drawing.RectangleF(
                    region.Bounds.X, 
                    Math.Max(0, region.Bounds.Y - textSize.Height - 2), 
                    textSize.Width + 4, 
                    textSize.Height + 2);
                
                regionTasks.Add(new { Region = region, Label = label, TextRect = textRect });
            }

            // 🎯 [COORDINATE_FIX] 描画は順次実行（GDI+のスレッドセーフティ問題対応）
            // ROI診断画像は低解像度画像なので、TextRegion.Boundsをそのまま使用（スケール変換不要）
            foreach (var item in regionTasks)
            {
                // 🔧 [LOW_RES_COORDINATE] 低解像度画像用の座標をそのまま使用
                // TextRegion.Bounds は既にROI座標系なので変換せずに直接描画
                var roiBounds = item.Region.Bounds;
                Console.WriteLine($"🎯 [ROI_DRAW] 描画座標: ({roiBounds.X},{roiBounds.Y}) サイズ:({roiBounds.Width}x{roiBounds.Height}) テキスト:'{item.Region.Text}'");
                
                // 赤い境界線を描画（ROI座標系をそのまま使用）
                graphics.DrawRectangle(redPen, roiBounds);
                
                // 背景を描画
                graphics.FillRectangle(backgroundBrush, item.TextRect);
                
                // テキストを描画
                graphics.DrawString(item.Label, font, textBrush, item.TextRect.X + 2, item.TextRect.Y + 1);
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

    internal async Task SaveImageAsync(IImage image, string imagePath)
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

    /// <summary>
    /// フォールバック先への画像保存試行
    /// </summary>
    private async Task TrySaveToFallbackLocationAsync(byte[] imageBytes, string originalPath, string operationId)
    {
        var fallbackLocations = new[]
        {
            Path.Combine(Path.GetTempPath(), "Baketa", "ROI", "Fallback"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Baketa", "ROI", "Fallback"),
            Path.Combine(Path.GetDirectoryName(originalPath) ?? string.Empty, "Fallback")
        };

        Console.WriteLine($"🔄 [FALLBACK] フォールバック保存開始: {fallbackLocations.Length}箇所を試行");

        foreach (var fallbackDir in fallbackLocations)
        {
            try
            {
                Console.WriteLine($"🔄 [FALLBACK] 試行中: {fallbackDir}");
                Directory.CreateDirectory(fallbackDir);
                var fallbackPath = Path.Combine(fallbackDir, $"fallback_{operationId}_{Path.GetFileName(originalPath)}");
                
                Console.WriteLine($"🔄 [FALLBACK] ファイル書き込み: {fallbackPath}");
                await File.WriteAllBytesAsync(fallbackPath, imageBytes).ConfigureAwait(false);
                
                // 書き込み確認
                var fallbackExists = File.Exists(fallbackPath);
                var fallbackSize = fallbackExists ? new FileInfo(fallbackPath).Length : 0;
                Console.WriteLine($"✅ [FALLBACK] 保存成功: 存在={fallbackExists}, サイズ={fallbackSize:N0}bytes");
                
                _logger?.LogInformation("フォールバック保存成功: {FallbackPath}", fallbackPath);
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💥 [FALLBACK] 失敗: {fallbackDir} - {ex.GetType().Name}: {ex.Message}");
                _logger?.LogTrace("フォールバック保存失敗: {FallbackDir} - {Error}", fallbackDir, ex.Message);
            }
        }
        
        Console.WriteLine($"💥 [FALLBACK] 全てのフォールバック保存が失敗: {operationId}");
        _logger?.LogWarning("全てのフォールバック保存が失敗: {OperationId}", operationId);
    }

    /// <summary>
    /// リトライ機能付き画像保存
    /// </summary>
    private async Task RetryImageSaveAsync(byte[] imageBytes, string filePath, string operationId, int maxRetries)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await File.WriteAllBytesAsync(filePath, imageBytes).ConfigureAwait(false);
                _logger?.LogInformation("リトライ保存成功: {FilePath} (試行: {Attempt}/{MaxRetries})", 
                    filePath, attempt, maxRetries);
                return;
            }
            catch (IOException) when (attempt < maxRetries)
            {
                var delay = attempt * 200; // 200ms, 400ms, 600ms...
                _logger?.LogDebug("リトライ待機: {Delay}ms (試行: {Attempt}/{MaxRetries})", delay, attempt, maxRetries);
                await Task.Delay(delay).ConfigureAwait(false);
            }
        }
        
        _logger?.LogError("最大リトライ回数に達しました: {FilePath} (試行回数: {MaxRetries})", filePath, maxRetries);
        throw new IOException($"ファイル保存に{maxRetries}回失敗しました: {filePath}");
    }

    /// <summary>
    /// 圧縮画像保存
    /// </summary>
    private async Task SaveCompressedImageAsync(byte[] imageBytes, string filePath, string operationId)
    {
        try
        {
            using var originalStream = new MemoryStream(imageBytes);
            using var originalBitmap = new System.Drawing.Bitmap(originalStream);
            using var compressedStream = new MemoryStream();
            
            // JPEG形式で品質50%に圧縮
            var jpegEncoder = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders()
                .First(codec => codec.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);
            
            var encoderParams = new System.Drawing.Imaging.EncoderParameters(1);
            encoderParams.Param[0] = new System.Drawing.Imaging.EncoderParameter(
                System.Drawing.Imaging.Encoder.Quality, 50L);
            
            originalBitmap.Save(compressedStream, jpegEncoder, encoderParams);
            var compressedBytes = compressedStream.ToArray();
            
            var compressedPath = Path.ChangeExtension(filePath, ".jpg");
            await File.WriteAllBytesAsync(compressedPath, compressedBytes).ConfigureAwait(false);
            
            _logger?.LogInformation("圧縮画像保存成功: {CompressedPath} (元サイズ: {OriginalSize}KB → 圧縮後: {CompressedSize}KB)", 
                compressedPath, imageBytes.Length / 1024, compressedBytes.Length / 1024);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "圧縮画像保存も失敗: {OperationId}", operationId);
            
            // 最後の手段：テキスト情報のみ保存
            await SaveErrorMetadataAsync(filePath, operationId, ex).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// エラー時のメタデータ保存
    /// </summary>
    private async Task SaveErrorMetadataAsync(string originalPath, string operationId, Exception exception)
    {
        try
        {
            var errorMetadata = new Dictionary<string, object>
            {
                ["OperationId"] = operationId,
                ["OriginalPath"] = originalPath,
                ["ErrorType"] = exception.GetType().Name,
                ["ErrorMessage"] = exception.Message,
                ["Timestamp"] = DateTime.UtcNow.ToString("O"),
                ["MachineName"] = Environment.MachineName,
                ["ProcessId"] = Environment.ProcessId
            };

            var metadataJson = JsonSerializer.Serialize(errorMetadata, s_jsonOptions);
            var errorMetadataPath = Path.ChangeExtension(originalPath, ".error.json");
            
            await File.WriteAllTextAsync(errorMetadataPath, metadataJson).ConfigureAwait(false);
            _logger?.LogInformation("エラーメタデータ保存完了: {ErrorMetadataPath}", errorMetadataPath);
        }
        catch (Exception metaEx)
        {
            _logger?.LogError(metaEx, "エラーメタデータ保存も失敗: {OperationId}", operationId);
        }
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