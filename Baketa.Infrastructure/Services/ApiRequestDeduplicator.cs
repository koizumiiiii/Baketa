using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Services;

/// <summary>
/// API重複呼び出し削減マネージャー
/// Issue #299: 複数HostedServiceの並列起動による重複API呼び出しを1回に集約
/// </summary>
/// <remarks>
/// ConcurrentDictionary + TaskCompletionSource パターンで実装。
/// 同一キーのリクエストが同時に発生した場合、最初の1回のみ実行し、
/// 他のリクエストはその結果を待機して共有する。
/// </remarks>
public interface IApiRequestDeduplicator
{
    /// <summary>
    /// 指定されたキーでリクエストを実行（重複排除付き）
    /// </summary>
    /// <typeparam name="T">結果の型</typeparam>
    /// <param name="key">重複排除キー（例: "bonus-tokens-status:userId"）</param>
    /// <param name="factory">実際のAPI呼び出しを行うファクトリ関数</param>
    /// <param name="cacheDuration">キャッシュ有効期間（デフォルト: 30秒）</param>
    /// <returns>API呼び出し結果</returns>
    Task<T?> ExecuteOnceAsync<T>(
        string key,
        Func<Task<T?>> factory,
        TimeSpan? cacheDuration = null) where T : class;

    /// <summary>
    /// 指定されたプレフィックスで始まるキャッシュを無効化
    /// </summary>
    /// <param name="prefix">キープレフィックス（例: "bonus-tokens"）</param>
    void InvalidateByPrefix(string prefix);

    /// <summary>
    /// 全キャッシュを無効化（ログイン/ログアウト時に使用）
    /// </summary>
    void InvalidateAll();

    /// <summary>
    /// [Issue #299] レート制限状態を取得
    /// </summary>
    /// <returns>true: レート制限中、false: 正常</returns>
    bool IsRateLimited { get; }

    /// <summary>
    /// [Issue #299] レート制限解除までの残り時間
    /// </summary>
    TimeSpan? RateLimitRemainingTime { get; }
}

/// <summary>
/// エンドポイントごとのキャッシュ期間設定
/// Geminiレビューに基づき最適化
/// </summary>
public static class ApiCacheDurations
{
    /// <summary>ボーナストークン状態: 90秒（トークン残量は頻繁に変化しない）</summary>
    public static readonly TimeSpan BonusTokens = TimeSpan.FromSeconds(90);

    /// <summary>クォータ状態: 45秒（翻訳ごとに変化するがリアルタイム性不要）</summary>
    public static readonly TimeSpan QuotaStatus = TimeSpan.FromSeconds(45);

    /// <summary>プロモーション状態: 5分（ほぼ変化しない）</summary>
    public static readonly TimeSpan PromotionStatus = TimeSpan.FromMinutes(5);

    /// <summary>同意状態: 10分（ユーザーアクションでのみ変化）</summary>
    public static readonly TimeSpan ConsentStatus = TimeSpan.FromMinutes(10);

    /// <summary>デフォルト: 30秒</summary>
    public static readonly TimeSpan Default = TimeSpan.FromSeconds(30);
}

/// <summary>
/// API重複呼び出し削減マネージャー実装
/// </summary>
/// <remarks>
/// [Issue #299] キルスイッチ機能追加:
/// 1分間に30回以上のAPIリクエストを検出すると、1分間すべてのリクエストをブロック。
/// これにより、無限ループやバグによる過剰なAPI呼び出しを防止。
/// </remarks>
public sealed class ApiRequestDeduplicator : IApiRequestDeduplicator, IDisposable
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly SemaphoreSlim _cleanupLock = new(1, 1);
    private readonly ILogger<ApiRequestDeduplicator> _logger;

    private DateTime _lastCleanup = DateTime.UtcNow;
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);

    // [Issue #299] キルスイッチ（レート制限）関連フィールド
    /// <summary>1分間の最大リクエスト数</summary>
    private const int MaxRequestsPerMinute = 30;

    /// <summary>レート制限発動時のブロック期間</summary>
    private static readonly TimeSpan BlockDuration = TimeSpan.FromMinutes(1);

    /// <summary>リクエストタイムスタンプ（スライディングウィンドウ）</summary>
    private readonly ConcurrentQueue<DateTime> _requestTimestamps = new();

    /// <summary>ブロック解除時刻（nullの場合はブロックなし）</summary>
    private DateTime? _blockedUntil;

    /// <summary>レート制限チェック用ロック</summary>
    private readonly object _rateLimitLock = new();

    private bool _disposed;

    public ApiRequestDeduplicator(ILogger<ApiRequestDeduplicator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogDebug("[Issue #299] ApiRequestDeduplicator initialized (KillSwitch: {MaxRequests}/min)", MaxRequestsPerMinute);
    }

    /// <inheritdoc/>
    public bool IsRateLimited
    {
        get
        {
            lock (_rateLimitLock)
            {
                return _blockedUntil.HasValue && DateTime.UtcNow < _blockedUntil.Value;
            }
        }
    }

    /// <inheritdoc/>
    public TimeSpan? RateLimitRemainingTime
    {
        get
        {
            lock (_rateLimitLock)
            {
                if (!_blockedUntil.HasValue) return null;
                var remaining = _blockedUntil.Value - DateTime.UtcNow;
                return remaining > TimeSpan.Zero ? remaining : null;
            }
        }
    }

    /// <inheritdoc/>
    public async Task<T?> ExecuteOnceAsync<T>(
        string key,
        Func<Task<T?>> factory,
        TimeSpan? cacheDuration = null) where T : class
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(factory);

        // [Issue #299] キルスイッチ: レート制限中はリクエストをブロック
        if (CheckAndUpdateRateLimit(key, isActualRequest: false))
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var duration = cacheDuration ?? ApiCacheDurations.Default;

        // 期限切れエントリのクリーンアップ（非同期で実行）
        _ = CleanupExpiredEntriesAsync();

        // [Gemini Review] 無限ループ防止: 最大再試行回数を設定
        const int MaxRetries = 3;
        var retryCount = 0;

        while (retryCount < MaxRetries)
        {
            // キャッシュヒットチェック
            if (_cache.TryGetValue(key, out var cached) && cached.ExpiresAt > now)
            {
                try
                {
                    var result = await cached.Task.ConfigureAwait(false);
                    _logger.LogDebug(
                        "[Issue #299] Cache hit: Key={Key}, ExpiresIn={ExpiresIn}s",
                        key,
                        (cached.ExpiresAt - now).TotalSeconds);
                    return result as T;
                }
                catch
                {
                    // 失敗したタスクは再実行を許可
                    _cache.TryRemove(key, out _);
                    retryCount++;
                    continue;
                }
            }

            // 新規エントリ作成
            // RunContinuationsAsynchronously: SetResult呼び出しスレッドで継続が実行されるのを防ぐ
            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var newEntry = new CacheEntry(tcs.Task, now.Add(duration));

            // AddOrUpdate で確実に処理（レースコンディション回避）
            var actualEntry = _cache.AddOrUpdate(
                key,
                newEntry,
                (_, existing) => existing.ExpiresAt > now ? existing : newEntry);

            if (ReferenceEquals(actualEntry.Task, tcs.Task))
            {
                // このスレッドが実行者
                // [Issue #299] 実際のAPI呼び出し時のみリクエストを記録
                if (CheckAndUpdateRateLimit(key, isActualRequest: true))
                {
                    _cache.TryRemove(key, out _);
                    tcs.SetCanceled();
                    return null;
                }

                _logger.LogDebug("[Issue #299] Executing: Key={Key}", key);
                try
                {
                    var result = await factory().ConfigureAwait(false);
                    tcs.SetResult(result);
                    _logger.LogDebug(
                        "[Issue #299] Executed and cached: Key={Key}, CacheDuration={Duration}s",
                        key,
                        duration.TotalSeconds);
                    return result;
                }
                catch (Exception ex)
                {
                    _cache.TryRemove(key, out _);
                    tcs.SetException(ex);
                    _logger.LogDebug(ex, "[Issue #299] Execution failed: Key={Key}", key);
                    throw;
                }
            }
            else
            {
                // 別スレッドが実行中 → 結果を待つ
                _logger.LogDebug("[Issue #299] Waiting for pending request: Key={Key}", key);
                try
                {
                    var result = await actualEntry.Task.ConfigureAwait(false);
                    return result as T;
                }
                catch
                {
                    // 失敗した場合は再試行
                    retryCount++;
                    continue;
                }
            }
        }

        // 最大再試行回数超過
        _logger.LogWarning(
            "[Issue #299] Max retries exceeded: Key={Key}, MaxRetries={MaxRetries}",
            key, MaxRetries);
        return null;
    }

    /// <inheritdoc/>
    public void InvalidateByPrefix(string prefix)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);

        var keysToRemove = _cache.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();

        var removedCount = 0;
        foreach (var key in keysToRemove)
        {
            if (_cache.TryRemove(key, out _))
            {
                removedCount++;
            }
        }

        if (removedCount > 0)
        {
            _logger.LogDebug(
                "[Issue #299] Invalidated by prefix: Prefix={Prefix}, Count={Count}",
                prefix,
                removedCount);
        }
    }

    /// <inheritdoc/>
    public void InvalidateAll()
    {
        var count = _cache.Count;
        _cache.Clear();

        if (count > 0)
        {
            _logger.LogDebug("[Issue #299] Invalidated all cache: Count={Count}", count);
        }
    }

    /// <summary>
    /// 期限切れエントリのクリーンアップ
    /// </summary>
    private async Task CleanupExpiredEntriesAsync()
    {
        var now = DateTime.UtcNow;
        if (now - _lastCleanup < CleanupInterval)
            return;

        if (!await _cleanupLock.WaitAsync(0).ConfigureAwait(false))
            return;

        try
        {
            _lastCleanup = now;
            var expiredKeys = _cache
                .Where(kvp => kvp.Value.ExpiresAt <= now)
                .Select(kvp => kvp.Key)
                .ToList();

            var removedCount = 0;
            foreach (var key in expiredKeys)
            {
                if (_cache.TryRemove(key, out _))
                {
                    removedCount++;
                }
            }

            if (removedCount > 0)
            {
                _logger.LogDebug(
                    "[Issue #299] Cleanup expired entries: Count={Count}",
                    removedCount);
            }
        }
        finally
        {
            _cleanupLock.Release();
        }
    }

    /// <summary>
    /// [Issue #299] レート制限チェックと更新
    /// </summary>
    /// <param name="key">リクエストキー（ログ用）</param>
    /// <param name="isActualRequest">true: 実際のAPI呼び出し（カウント対象）、false: ブロックチェックのみ</param>
    /// <returns>true: ブロック中（リクエスト禁止）、false: 許可</returns>
    private bool CheckAndUpdateRateLimit(string key, bool isActualRequest)
    {
        var now = DateTime.UtcNow;

        lock (_rateLimitLock)
        {
            // ブロック中かチェック
            if (_blockedUntil.HasValue)
            {
                if (now < _blockedUntil.Value)
                {
                    var remaining = _blockedUntil.Value - now;
                    _logger.LogWarning(
                        "[Issue #299] RATE LIMITED - Request blocked: Key={Key}, RemainingBlock={RemainingSeconds}s",
                        key,
                        remaining.TotalSeconds);
                    return true;
                }

                // ブロック期間終了 → 解除
                _blockedUntil = null;
                _logger.LogInformation("[Issue #299] Rate limit block expired, resuming normal operation");
            }

            // 実際のAPI呼び出し時のみカウント
            if (!isActualRequest)
            {
                return false;
            }

            // 古いタイムスタンプを削除（1分以上前）
            var windowStart = now.AddMinutes(-1);
            while (_requestTimestamps.TryPeek(out var oldest) && oldest < windowStart)
            {
                _requestTimestamps.TryDequeue(out _);
            }

            // リクエストを記録
            _requestTimestamps.Enqueue(now);

            // レート制限チェック
            var requestCount = _requestTimestamps.Count;
            if (requestCount > MaxRequestsPerMinute)
            {
                _blockedUntil = now.Add(BlockDuration);
                _logger.LogError(
                    "[Issue #299] KILL SWITCH ACTIVATED - Rate limit exceeded: {Count}/{Max} requests/min. " +
                    "All API requests blocked for {BlockDuration}s. Key={Key}",
                    requestCount,
                    MaxRequestsPerMinute,
                    BlockDuration.TotalSeconds,
                    key);
                return true;
            }

            // 警告閾値（80%）に達した場合は警告
            if (requestCount >= MaxRequestsPerMinute * 0.8)
            {
                _logger.LogWarning(
                    "[Issue #299] Rate limit warning: {Count}/{Max} requests/min (80% threshold)",
                    requestCount,
                    MaxRequestsPerMinute);
            }

            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cleanupLock.Dispose();
        _cache.Clear();
    }

    /// <summary>
    /// キャッシュエントリ
    /// </summary>
    private sealed record CacheEntry(Task<object?> Task, DateTime ExpiresAt);
}
