using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece;

/// <summary>
/// SentencePieceモデルファイルの管理
/// </summary>
public class SentencePieceModelManager
{
    private readonly SentencePieceOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SentencePieceModelManager> _logger;
    private readonly SemaphoreSlim _downloadSemaphore = new(1);
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="options">設定オプション</param>
    /// <param name="httpClientFactory">HTTPクライアントファクトリー</param>
    /// <param name="logger">ロガー</param>
    public SentencePieceModelManager(
        IOptions<SentencePieceOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<SentencePieceModelManager> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // モデルディレクトリの作成
        Directory.CreateDirectory(_options.ModelsDirectory);
        _logger.LogInformation("モデルディレクトリを確認しました: {Directory}", _options.ModelsDirectory);
    }

    /// <summary>
    /// モデルファイルのパスを取得（必要に応じてダウンロード）
    /// </summary>
    /// <param name="modelName">モデル名</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>モデルファイルのパス</returns>
    public async Task<string> GetModelPathAsync(string modelName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(modelName, nameof(modelName));

        var modelPath = Path.Combine(_options.ModelsDirectory, $"{modelName}.model");
        var metadataPath = Path.Combine(_options.ModelsDirectory, $"{modelName}.metadata.json");

        // モデルの存在とバージョンチェック
        if (File.Exists(modelPath) && await IsModelValidAsync(modelPath, metadataPath, cancellationToken))
        {
            await UpdateLastAccessedAsync(metadataPath);
            return modelPath;
        }

        // 同時ダウンロード防止
        await _downloadSemaphore.WaitAsync(cancellationToken);
        try
        {
            // 再チェック（他のスレッドがダウンロード済みの可能性）
            if (File.Exists(modelPath) && await IsModelValidAsync(modelPath, metadataPath, cancellationToken))
            {
                await UpdateLastAccessedAsync(metadataPath);
                return modelPath;
            }

            // ダウンロード実行
            await DownloadModelAsync(modelName, modelPath, metadataPath, cancellationToken);

            // 自動クリーンアップの実行
            if (_options.EnableAutoCleanup)
            {
                await CleanupOldModelsAsync();
            }
        }
        finally
        {
            _downloadSemaphore.Release();
        }

        return modelPath;
    }

    /// <summary>
    /// モデルファイルをダウンロード
    /// </summary>
    private async Task DownloadModelAsync(
        string modelName,
        string modelPath,
        string metadataPath,
        CancellationToken cancellationToken)
    {
        var url = string.Format(_options.DownloadUrl, modelName);
        var tempPath = $"{modelPath}.tmp";

        _logger.LogInformation("モデルのダウンロードを開始します: {ModelName} from {Url}", modelName, url);

        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(_options.DownloadTimeoutMinutes);

        Exception? lastException = null;
        for (int attempt = 1; attempt <= _options.MaxDownloadRetries; attempt++)
        {
            try
            {
                // プログレス付きダウンロード
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;

                using (var fileStream = File.Create(tempPath))
                using (var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken))
                {
                    await CopyWithProgressAsync(httpStream, fileStream, totalBytes, modelName, cancellationToken);
                }

                // チェックサム計算
                var checksum = _options.EnableChecksumValidation
                    ? await CalculateChecksumAsync(tempPath, cancellationToken)
                    : string.Empty;

                // メタデータ保存
                var metadata = new ModelMetadata
                {
                    ModelName = modelName,
                    DownloadedAt = DateTime.UtcNow,
                    Version = response.Headers.ETag?.Tag ?? "unknown",
                    Size = new FileInfo(tempPath).Length,
                    Checksum = checksum,
                    LastAccessedAt = DateTime.UtcNow,
                    SourceUrl = url
                };

                await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(metadata, _jsonOptions), cancellationToken);

                // アトミックな移動
                File.Move(tempPath, modelPath, true);

                _logger.LogInformation(
                    "モデルダウンロード完了: {ModelName} ({Size:N0} bytes, チェックサム: {Checksum})",
                    modelName, metadata.Size, checksum ?? "N/A");

                return;
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogWarning(ex,
                    "モデルダウンロード失敗 (試行 {Attempt}/{MaxAttempts}): {ModelName}",
                    attempt, _options.MaxDownloadRetries, modelName);

                // 一時ファイルのクリーンアップ
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }

                if (attempt < _options.MaxDownloadRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cancellationToken);
                }
            }
        }

        throw new InvalidOperationException(
            $"モデルのダウンロードに失敗しました: {modelName}",
            lastException);
    }

    /// <summary>
    /// プログレス付きでストリームをコピー
    /// </summary>
    private async Task CopyWithProgressAsync(
        Stream source,
        Stream destination,
        long totalBytes,
        string modelName,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920]; // 80KB buffer
        var totalBytesRead = 0L;
        var lastProgressReport = DateTime.UtcNow;
        int bytesRead;

        while ((bytesRead = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) != 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            totalBytesRead += bytesRead;

            // 1秒ごとに進捗報告
            if (DateTime.UtcNow - lastProgressReport > TimeSpan.FromSeconds(1))
            {
                var progress = totalBytes > 0
                    ? (double)totalBytesRead / totalBytes * 100
                    : 0;

                _logger.LogInformation(
                    "ダウンロード進捗 [{ModelName}]: {Progress:F1}% ({BytesRead:N0}/{TotalBytes:N0} bytes)",
                    modelName, progress, totalBytesRead, totalBytes > 0 ? totalBytes : totalBytesRead);

                lastProgressReport = DateTime.UtcNow;
            }
        }
    }

    /// <summary>
    /// SHA256チェックサムを計算
    /// </summary>
    private async Task<string> CalculateChecksumAsync(string filePath, CancellationToken cancellationToken)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    /// <summary>
    /// モデルの有効性を検証
    /// </summary>
    private async Task<bool> IsModelValidAsync(
        string modelPath,
        string metadataPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(metadataPath))
        {
            _logger.LogDebug("メタデータファイルが存在しません: {Path}", metadataPath);
            return false;
        }

        try
        {
            var json = await File.ReadAllTextAsync(metadataPath, cancellationToken);
            var metadata = JsonSerializer.Deserialize<ModelMetadata>(json);

            if (metadata == null)
            {
                _logger.LogWarning("メタデータの読み込みに失敗しました: {Path}", metadataPath);
                return false;
            }

            // 設定された日数以上古い場合は再ダウンロード
            if (metadata.DownloadedAt < DateTime.UtcNow.AddDays(-_options.ModelCacheDays))
            {
                _logger.LogInformation(
                    "モデルが古いため更新が必要: {ModelName} (ダウンロード日: {DownloadedAt})",
                    metadata.ModelName, metadata.DownloadedAt);
                return false;
            }

            // ファイルサイズチェック
            var actualSize = new FileInfo(modelPath).Length;
            if (actualSize != metadata.Size)
            {
                _logger.LogWarning(
                    "モデルファイルサイズ不一致: {Expected} != {Actual}",
                    metadata.Size, actualSize);
                return false;
            }

            // チェックサム検証（有効な場合）
            if (_options.EnableChecksumValidation && !string.IsNullOrEmpty(metadata.Checksum))
            {
                var actualChecksum = await CalculateChecksumAsync(modelPath, cancellationToken);
                if (actualChecksum != metadata.Checksum)
                {
                    _logger.LogWarning(
                        "モデルチェックサム不一致: {Expected} != {Actual}",
                        metadata.Checksum, actualChecksum);
                    return false;
                }
            }

            _logger.LogDebug("モデルは有効です: {ModelName}", metadata.ModelName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "モデルメタデータの検証エラー: {Path}", metadataPath);
            return false;
        }
    }

    /// <summary>
    /// 最終アクセス日時を更新
    /// </summary>
    private async Task UpdateLastAccessedAsync(string metadataPath)
    {
        try
        {
            var json = await File.ReadAllTextAsync(metadataPath);
            var metadata = JsonSerializer.Deserialize<ModelMetadata>(json);
            
            if (metadata != null)
            {
                metadata.LastAccessedAt = DateTime.UtcNow;
                await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(metadata, _jsonOptions));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "最終アクセス日時の更新に失敗しました: {Path}", metadataPath);
        }
    }

    /// <summary>
    /// 古いモデルをクリーンアップ
    /// </summary>
    private async Task CleanupOldModelsAsync()
    {
        try
        {
            var metadataFiles = Directory.GetFiles(_options.ModelsDirectory, "*.metadata.json");
            var cutoffDate = DateTime.UtcNow.AddDays(-_options.CleanupThresholdDays);

            foreach (var metadataPath in metadataFiles)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(metadataPath);
                    var metadata = JsonSerializer.Deserialize<ModelMetadata>(json);

                    if (metadata != null && metadata.LastAccessedAt < cutoffDate)
                    {
                        var modelPath = Path.ChangeExtension(metadataPath, ".model");
                        
                        if (File.Exists(modelPath))
                        {
                            File.Delete(modelPath);
                        }
                        
                        File.Delete(metadataPath);
                        
                        _logger.LogInformation(
                            "古いモデルを削除しました: {ModelName} (最終アクセス: {LastAccessed})",
                            metadata.ModelName, metadata.LastAccessedAt);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "モデルクリーンアップエラー: {Path}", metadataPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "モデルクリーンアップ中にエラーが発生しました");
        }
    }

    /// <summary>
    /// 利用可能なモデルの一覧を取得
    /// </summary>
    /// <returns>モデルメタデータのリスト</returns>
    public async Task<ModelMetadata[]> GetAvailableModelsAsync()
    {
        var models = new List<ModelMetadata>();
        var metadataFiles = Directory.GetFiles(_options.ModelsDirectory, "*.metadata.json");

        foreach (var metadataPath in metadataFiles)
        {
            try
            {
                var json = await File.ReadAllTextAsync(metadataPath);
                var metadata = JsonSerializer.Deserialize<ModelMetadata>(json);
                
                if (metadata != null)
                {
                    models.Add(metadata);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "メタデータ読み込みエラー: {Path}", metadataPath);
            }
        }

        return models.OrderBy(m => m.ModelName).ToArray();
    }
}
