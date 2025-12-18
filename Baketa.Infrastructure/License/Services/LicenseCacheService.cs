using System.IO;
using System.Text.Json;
using Baketa.Core.Abstractions.License;
using Baketa.Core.License.Models;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.License.Services;

/// <summary>
/// ライセンス状態のローカルキャッシュサービス実装
/// JSONファイルベースの永続化を提供
/// </summary>
public sealed class LicenseCacheService : ILicenseCacheService, IDisposable
{
    private readonly ILogger<LicenseCacheService> _logger;
    private readonly LicenseSettings _settings;
    private readonly string _cacheDirectory;
    private readonly string _cacheFilePath;
    private readonly string _pendingConsumptionsFilePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _disposed;

    // インメモリキャッシュ
    private LicenseCacheEntry? _cachedEntry;
    private List<PendingTokenConsumption> _pendingConsumptions = [];
    private readonly object _memoryLock = new();

    /// <summary>
    /// LicenseCacheServiceを初期化
    /// </summary>
    public LicenseCacheService(
        ILogger<LicenseCacheService> logger,
        IOptions<LicenseSettings> settings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));

        // キャッシュディレクトリ設定
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _cacheDirectory = Path.Combine(userProfile, ".baketa", "license");
        _cacheFilePath = Path.Combine(_cacheDirectory, "license-cache.json");
        _pendingConsumptionsFilePath = Path.Combine(_cacheDirectory, "pending-consumptions.json");

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        // キャッシュディレクトリを作成
        EnsureCacheDirectoryExists();

        // 起動時にキャッシュを読み込み
        _ = Task.Run(LoadCacheFromFileAsync);
    }

    /// <inheritdoc/>
    public async Task<LicenseState?> GetCachedStateAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_memoryLock)
        {
            if (_cachedEntry is null || _cachedEntry.UserId != userId)
            {
                return null;
            }

            // 有効期限チェック
            if (_cachedEntry.ExpiresAt <= DateTime.UtcNow)
            {
                _logger.LogDebug("ライセンスキャッシュが期限切れです: UserId={UserId}", userId);
                return null;
            }

            return _cachedEntry.State;
        }
    }

    /// <inheritdoc/>
    public async Task SetCachedStateAsync(
        string userId,
        LicenseState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(state);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var entry = new LicenseCacheEntry
        {
            UserId = userId,
            State = state,
            CachedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(_settings.CacheExpirationMinutes)
        };

        lock (_memoryLock)
        {
            _cachedEntry = entry;
        }

        // ファイルに永続化
        await SaveCacheToFileAsync(entry, cancellationToken).ConfigureAwait(false);

        _logger.LogDebug(
            "ライセンスキャッシュを保存: UserId={UserId}, Plan={Plan}, ExpiresAt={ExpiresAt}",
            userId, state.CurrentPlan, entry.ExpiresAt);
    }

    /// <inheritdoc/>
    public async Task ClearCacheAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_memoryLock)
        {
            if (_cachedEntry?.UserId == userId)
            {
                _cachedEntry = null;
            }
        }

        // ファイルを削除
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(_cacheFilePath))
            {
                File.Delete(_cacheFilePath);
                _logger.LogInformation("ライセンスキャッシュを削除しました: UserId={UserId}", userId);
            }
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<bool> IsCacheValidAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_memoryLock)
        {
            if (_cachedEntry is null || _cachedEntry.UserId != userId)
            {
                return false;
            }

            // 通常の有効期限チェック
            if (_cachedEntry.ExpiresAt <= DateTime.UtcNow)
            {
                return false;
            }

            // オフライン許容期間のチェック（キャッシュ日時からの経過時間）
            var maxOfflinePeriod = TimeSpan.FromHours(_settings.OfflineGracePeriodHours);
            if (DateTime.UtcNow - _cachedEntry.CachedAt > maxOfflinePeriod)
            {
                _logger.LogWarning(
                    "オフライン許容期間を超過しました: UserId={UserId}, CachedAt={CachedAt}",
                    userId, _cachedEntry.CachedAt);
                return false;
            }

            return true;
        }
    }

    /// <inheritdoc/>
    public async Task<LicenseState?> UpdateTokenUsageAsync(
        string userId,
        long tokensConsumed,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_memoryLock)
        {
            if (_cachedEntry is null || _cachedEntry.UserId != userId)
            {
                _logger.LogWarning(
                    "トークン使用量更新失敗: キャッシュが存在しません UserId={UserId}", userId);
                return null;
            }

            // 楽観的更新: ローカルでトークン使用量を加算
            var updatedState = _cachedEntry.State.WithTokensConsumed(tokensConsumed);
            _cachedEntry = _cachedEntry with { State = updatedState };

            _logger.LogDebug(
                "トークン使用量をローカル更新: UserId={UserId}, Consumed={Consumed}, NewTotal={NewTotal}",
                userId, tokensConsumed, updatedState.CloudAiTokensUsed);

            return updatedState;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PendingTokenConsumption>> GetPendingConsumptionsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_memoryLock)
        {
            return _pendingConsumptions
                .Where(p => p.UserId == userId)
                .ToList()
                .AsReadOnly();
        }
    }

    /// <inheritdoc/>
    public async Task AddPendingConsumptionAsync(
        PendingTokenConsumption consumption,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(consumption);
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_memoryLock)
        {
            // 重複チェック（IdempotencyKey）
            if (_pendingConsumptions.Any(p => p.IdempotencyKey == consumption.IdempotencyKey))
            {
                _logger.LogDebug(
                    "重複した消費記録をスキップ: IdempotencyKey={Key}",
                    consumption.IdempotencyKey);
                return;
            }

            _pendingConsumptions.Add(consumption);
        }

        // ファイルに永続化
        await SavePendingConsumptionsToFileAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogDebug(
            "未同期消費記録を追加: UserId={UserId}, Tokens={Tokens}, Key={Key}",
            consumption.UserId, consumption.TokenCount, consumption.IdempotencyKey);
    }

    /// <inheritdoc/>
    public async Task RemoveSyncedConsumptionsAsync(
        IEnumerable<string> idempotencyKeys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(idempotencyKeys);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var keysSet = idempotencyKeys.ToHashSet(StringComparer.Ordinal);
        if (keysSet.Count == 0)
        {
            return;
        }

        int removedCount;
        lock (_memoryLock)
        {
            var originalCount = _pendingConsumptions.Count;
            _pendingConsumptions = _pendingConsumptions
                .Where(p => !keysSet.Contains(p.IdempotencyKey))
                .ToList();
            removedCount = originalCount - _pendingConsumptions.Count;
        }

        if (removedCount > 0)
        {
            // ファイルに永続化
            await SavePendingConsumptionsToFileAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "同期済み消費記録を削除: Count={Count}", removedCount);
        }
    }

    /// <summary>
    /// キャッシュをファイルから読み込む
    /// </summary>
    private async Task LoadCacheFromFileAsync()
    {
        try
        {
            await _fileLock.WaitAsync().ConfigureAwait(false);
            try
            {
                // ライセンスキャッシュの読み込み
                if (File.Exists(_cacheFilePath))
                {
                    var json = await File.ReadAllTextAsync(_cacheFilePath).ConfigureAwait(false);
                    var entry = JsonSerializer.Deserialize<LicenseCacheEntry>(json, _jsonOptions);

                    if (entry is not null)
                    {
                        lock (_memoryLock)
                        {
                            _cachedEntry = entry;
                        }
                        _logger.LogDebug("ライセンスキャッシュをファイルから読み込み: UserId={UserId}", entry.UserId);
                    }
                }

                // 未同期消費記録の読み込み
                if (File.Exists(_pendingConsumptionsFilePath))
                {
                    var json = await File.ReadAllTextAsync(_pendingConsumptionsFilePath).ConfigureAwait(false);
                    var consumptions = JsonSerializer.Deserialize<List<PendingTokenConsumption>>(json, _jsonOptions);

                    if (consumptions is not null)
                    {
                        lock (_memoryLock)
                        {
                            _pendingConsumptions = consumptions;
                        }
                        _logger.LogDebug("未同期消費記録を読み込み: Count={Count}", consumptions.Count);
                    }
                }
            }
            finally
            {
                _fileLock.Release();
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "キャッシュファイルのパースに失敗しました。キャッシュをクリアします");
            await ClearCorruptedCacheAsync().ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "キャッシュファイルの読み込みに失敗しました");
        }
    }

    /// <summary>
    /// キャッシュをファイルに保存
    /// </summary>
    private async Task SaveCacheToFileAsync(LicenseCacheEntry entry, CancellationToken cancellationToken)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var json = JsonSerializer.Serialize(entry, _jsonOptions);
            await WriteFileWithRetryAsync(_cacheFilePath, json, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// 未同期消費記録をファイルに保存
    /// </summary>
    private async Task SavePendingConsumptionsToFileAsync(CancellationToken cancellationToken)
    {
        List<PendingTokenConsumption> consumptionsToSave;
        lock (_memoryLock)
        {
            consumptionsToSave = [.. _pendingConsumptions];
        }

        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var json = JsonSerializer.Serialize(consumptionsToSave, _jsonOptions);
            await WriteFileWithRetryAsync(_pendingConsumptionsFilePath, json, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// リトライ付きアトミックファイル書き込み
    /// 一時ファイルに書き込み後、File.Moveでアトミックに置き換え（データ損失防止）
    /// </summary>
    private async Task WriteFileWithRetryAsync(string filePath, string content, CancellationToken cancellationToken)
    {
        const int maxRetries = 3;
        const int retryDelayMs = 50;

        var tempPath = filePath + ".tmp";

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                // 1. 一時ファイルに書き込み
                await using (var stream = new FileStream(
                    tempPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None))
                await using (var writer = new StreamWriter(stream))
                {
                    await writer.WriteAsync(content).ConfigureAwait(false);
                }

                // 2. アトミックにファイルを置き換え
                File.Move(tempPath, filePath, overwrite: true);

                return;
            }
            catch (IOException ex) when (attempt < maxRetries - 1)
            {
                _logger.LogWarning(
                    ex,
                    "ファイル保存リトライ {Attempt}/{MaxRetries}: {FilePath}",
                    attempt + 1, maxRetries, filePath);

                // 一時ファイルが残っている場合は削除
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch { /* クリーンアップ失敗は無視 */ }

                await Task.Delay(retryDelayMs, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// 破損したキャッシュをクリア
    /// </summary>
    private async Task ClearCorruptedCacheAsync()
    {
        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (File.Exists(_cacheFilePath))
            {
                var backupPath = $"{_cacheFilePath}.corrupt.{DateTime.Now:yyyyMMdd_HHmmss}";
                File.Move(_cacheFilePath, backupPath);
                _logger.LogWarning("破損したキャッシュをバックアップしました: {BackupPath}", backupPath);
            }

            lock (_memoryLock)
            {
                _cachedEntry = null;
            }
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// キャッシュディレクトリの存在を確認
    /// </summary>
    private void EnsureCacheDirectoryExists()
    {
        try
        {
            if (!Directory.Exists(_cacheDirectory))
            {
                Directory.CreateDirectory(_cacheDirectory);
                _logger.LogInformation("ライセンスキャッシュディレクトリを作成: {Directory}", _cacheDirectory);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "キャッシュディレクトリの作成に失敗しました");
            throw;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _fileLock.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// ライセンスキャッシュエントリ（永続化用）
/// </summary>
internal sealed record LicenseCacheEntry
{
    /// <summary>
    /// ユーザーID
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// キャッシュされたライセンス状態
    /// </summary>
    public required LicenseState State { get; init; }

    /// <summary>
    /// キャッシュ日時
    /// </summary>
    public required DateTime CachedAt { get; init; }

    /// <summary>
    /// 有効期限
    /// </summary>
    public required DateTime ExpiresAt { get; init; }
}
