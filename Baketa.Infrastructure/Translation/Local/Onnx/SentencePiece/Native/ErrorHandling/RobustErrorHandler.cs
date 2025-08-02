using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece.Native.ErrorHandling;

/// <summary>
/// OPUS-MT Native Tokenizer用の堅牢なエラーハンドリングシステム
/// リトライ、回復、フォールバック機能を提供
/// </summary>
public sealed class RobustErrorHandler : IDisposable
{
    private readonly ILogger<RobustErrorHandler> _logger;
    private readonly ErrorHandlingPolicy _policy;
    private readonly Dictionary<Type, int> _errorCounts = [];
    private readonly System.Threading.Timer _errorCountResetTimer;
    private bool _disposed;

    /// <summary>
    /// エラー統計情報
    /// </summary>
    public ErrorStatistics Statistics { get; private set; } = new();

    public RobustErrorHandler(ILogger<RobustErrorHandler> logger, ErrorHandlingPolicy? policy = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _policy = policy ?? ErrorHandlingPolicy.CreateDefault();
        
        // エラーカウントを定期的にリセット
        _errorCountResetTimer = new System.Threading.Timer(ResetErrorCounts, null, 
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    /// <summary>
    /// リトライ機能付きでアクションを実行
    /// </summary>
    public async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> action,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentException.ThrowIfNullOrEmpty(operationName);

        var attempt = 0;
        Exception? lastException = null;

        while (attempt < _policy.MaxRetryAttempts)
        {
            try
            {
                attempt++;
                _logger.LogDebug("操作実行開始: {OperationName} (試行 {Attempt}/{MaxAttempts})", 
                    operationName, attempt, _policy.MaxRetryAttempts);

                var result = await action().ConfigureAwait(false);
                
                if (attempt > 1)
                {
                    _logger.LogInformation("操作が{Attempt}回目の試行で成功: {OperationName}", 
                        attempt, operationName);
                    Statistics.IncrementRecovery();
                }

                return result;
            }
            catch (Exception ex) when (ShouldRetry(ex, attempt))
            {
                lastException = ex;
                RecordError(ex);
                
                _logger.LogWarning(ex, "操作失敗（試行 {Attempt}/{MaxAttempts}）: {OperationName}。リトライします...", 
                    attempt, _policy.MaxRetryAttempts, operationName);

                if (attempt < _policy.MaxRetryAttempts)
                {
                    var delay = CalculateRetryDelay(attempt);
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                // リトライしない例外
                RecordError(ex);
                _logger.LogError(ex, "リトライ不可能なエラー: {OperationName}", operationName);
                throw new TokenizerOperationException(operationName, ex);
            }
        }

        // すべてのリトライが失敗
        RecordError(lastException!);
        _logger.LogError(lastException, "最大リトライ回数({MaxAttempts})を超過: {OperationName}", 
            _policy.MaxRetryAttempts, operationName);
        
        throw new TokenizerOperationException(operationName, lastException!);
    }

    /// <summary>
    /// 同期版のリトライ実行
    /// </summary>
    public T ExecuteWithRetry<T>(Func<T> action, string operationName)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentException.ThrowIfNullOrEmpty(operationName);

        return ExecuteWithRetryAsync(() => Task.FromResult(action()), operationName)
            .GetAwaiter().GetResult();
    }

    /// <summary>
    /// フォールバック付きでアクションを実行
    /// </summary>
    public async Task<T> ExecuteWithFallbackAsync<T>(
        Func<Task<T>> primaryAction,
        Func<Task<T>> fallbackAction,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(primaryAction);
        ArgumentNullException.ThrowIfNull(fallbackAction);
        ArgumentException.ThrowIfNullOrEmpty(operationName);

        try
        {
            _logger.LogDebug("プライマリ操作実行: {OperationName}", operationName);
            return await ExecuteWithRetryAsync(primaryAction, $"{operationName}(Primary)", cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception primaryEx)
        {
            _logger.LogWarning(primaryEx, "プライマリ操作失敗、フォールバックに切り替え: {OperationName}", operationName);
            Statistics.IncrementFallback();

            try
            {
                var result = await ExecuteWithRetryAsync(fallbackAction, $"{operationName}(Fallback)", cancellationToken)
                    .ConfigureAwait(false);
                
                _logger.LogInformation("フォールバック操作が成功: {OperationName}", operationName);
                return result;
            }
            catch (Exception fallbackEx)
            {
                _logger.LogError("プライマリとフォールバック両方が失敗: {OperationName}", operationName);
                _logger.LogError(primaryEx, "プライマリエラー");
                _logger.LogError(fallbackEx, "フォールバックエラー");
                
                throw new AggregateException(
                    $"Both primary and fallback operations failed for {operationName}",
                    primaryEx, fallbackEx);
            }
        }
    }

    /// <summary>
    /// エラーの記録と分析
    /// </summary>
    private void RecordError(Exception exception)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var exceptionType = exception.GetType();
        
        lock (_errorCounts)
        {
            _errorCounts.TryGetValue(exceptionType, out var count);
            _errorCounts[exceptionType] = count + 1;
        }

        Statistics.IncrementError(exceptionType);

        // 高頻度エラーの警告
        if (_errorCounts[exceptionType] > _policy.HighFrequencyErrorThreshold)
        {
            _logger.LogWarning("高頻度エラーを検出: {ExceptionType} (発生回数: {Count})", 
                exceptionType.Name, _errorCounts[exceptionType]);
        }
    }

    /// <summary>
    /// リトライ可否の判定
    /// </summary>
    private bool ShouldRetry(Exception exception, int attempt)
    {
        if (attempt >= _policy.MaxRetryAttempts)
            return false;

        // リトライ可能な例外タイプの確認
        var exceptionType = exception.GetType();
        
        // 明示的に除外される例外
        if (_policy.NonRetryableExceptions.Contains(exceptionType))
            return false;

        // ArgumentException系は基本的にリトライしない
        if (exception is ArgumentException or ArgumentNullException or ArgumentOutOfRangeException)
            return false;

        // ObjectDisposedException もリトライしない
        if (exception is ObjectDisposedException)
            return false;

        // その他の例外は基本的にリトライ対象
        return true;
    }

    /// <summary>
    /// リトライ遅延時間の計算（指数バックオフ）
    /// </summary>
    private TimeSpan CalculateRetryDelay(int attempt)
    {
        var baseDelay = _policy.BaseRetryDelay.TotalMilliseconds;
        var exponentialDelay = baseDelay * Math.Pow(2, attempt - 1);
        var jitteredDelay = exponentialDelay * (0.8 + Random.Shared.NextDouble() * 0.4); // ±20% ジッター
        
        var maxDelay = _policy.MaxRetryDelay.TotalMilliseconds;
        var finalDelay = Math.Min(jitteredDelay, maxDelay);
        
        return TimeSpan.FromMilliseconds(finalDelay);
    }

    /// <summary>
    /// エラーカウントの定期リセット
    /// </summary>
    private void ResetErrorCounts(object? state)
    {
        if (_disposed) return;

        lock (_errorCounts)
        {
            _errorCounts.Clear();
        }

        _logger.LogDebug("エラーカウントをリセットしました");
    }

    /// <summary>
    /// 現在のエラー統計の取得
    /// </summary>
    public ErrorStatistics GetCurrentStatistics()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Statistics;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _errorCountResetTimer?.Dispose();
        _disposed = true;
        
        _logger.LogDebug("RobustErrorHandler disposed");
    }
}

/// <summary>
/// エラーハンドリングポリシー設定
/// </summary>
public sealed class ErrorHandlingPolicy
{
    /// <summary>
    /// 最大リトライ回数
    /// </summary>
    public int MaxRetryAttempts { get; init; } = 3;

    /// <summary>
    /// 基本リトライ遅延時間
    /// </summary>
    public TimeSpan BaseRetryDelay { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// 最大リトライ遅延時間
    /// </summary>
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 高頻度エラーの閾値
    /// </summary>
    public int HighFrequencyErrorThreshold { get; init; } = 10;

    /// <summary>
    /// リトライしない例外タイプ
    /// </summary>
    public HashSet<Type> NonRetryableExceptions { get; init; } = [];

    /// <summary>
    /// デフォルトポリシーの作成
    /// </summary>
    public static ErrorHandlingPolicy CreateDefault()
    {
        return new ErrorHandlingPolicy
        {
            MaxRetryAttempts = 3,
            BaseRetryDelay = TimeSpan.FromMilliseconds(100),
            MaxRetryDelay = TimeSpan.FromSeconds(5),
            HighFrequencyErrorThreshold = 10,
            NonRetryableExceptions = 
            [
                typeof(ArgumentException),
                typeof(ArgumentNullException),
                typeof(ArgumentOutOfRangeException),
                typeof(ObjectDisposedException),
                typeof(NotSupportedException)
            ]
        };
    }

    /// <summary>
    /// 保守的なポリシーの作成（より多くのリトライ）
    /// </summary>
    public static ErrorHandlingPolicy CreateConservative()
    {
        return new ErrorHandlingPolicy
        {
            MaxRetryAttempts = 5,
            BaseRetryDelay = TimeSpan.FromMilliseconds(200),
            MaxRetryDelay = TimeSpan.FromSeconds(10),
            HighFrequencyErrorThreshold = 20,
            NonRetryableExceptions = 
            [
                typeof(ArgumentException),
                typeof(ArgumentNullException),
                typeof(ObjectDisposedException)
            ]
        };
    }
}

/// <summary>
/// エラー統計情報
/// </summary>
public sealed class ErrorStatistics
{
    private readonly Dictionary<Type, int> _errorCounts = [];
    private int _totalErrors;
    private int _recoveries;
    private int _fallbacks;

    /// <summary>
    /// 総エラー数
    /// </summary>
    public int TotalErrors => _totalErrors;

    /// <summary>
    /// 回復成功数
    /// </summary>
    public int Recoveries => _recoveries;

    /// <summary>
    /// フォールバック実行数
    /// </summary>
    public int Fallbacks => _fallbacks;

    /// <summary>
    /// エラータイプ別カウント
    /// </summary>
    public IReadOnlyDictionary<Type, int> ErrorCountsByType => _errorCounts;

    /// <summary>
    /// 信頼性スコア（0-1）
    /// </summary>
    public double ReliabilityScore
    {
        get
        {
            if (_totalErrors == 0) return 1.0;
            return Math.Max(0.0, 1.0 - (double)_totalErrors / (_totalErrors + _recoveries + 100));
        }
    }

    internal void IncrementError(Type exceptionType)
    {
        lock (_errorCounts)
        {
            _errorCounts.TryGetValue(exceptionType, out var count);
            _errorCounts[exceptionType] = count + 1;
            _totalErrors++;
        }
    }

    internal void IncrementRecovery()
    {
        Interlocked.Increment(ref _recoveries);
    }

    internal void IncrementFallback()
    {
        Interlocked.Increment(ref _fallbacks);
    }

    public override string ToString()
    {
        return $"Errors: {TotalErrors}, Recoveries: {Recoveries}, Fallbacks: {Fallbacks}, " +
               $"Reliability: {ReliabilityScore:P1}";
    }
}

/// <summary>
/// トークナイザー操作専用例外
/// </summary>
public sealed class TokenizerOperationException : Exception
{
    public string OperationName { get; }

    public TokenizerOperationException(string operationName, Exception innerException)
        : base($"Tokenizer operation '{operationName}' failed after retries", innerException)
    {
        OperationName = operationName;
    }

    public TokenizerOperationException(string operationName, string message)
        : base($"Tokenizer operation '{operationName}' failed: {message}")
    {
        OperationName = operationName;
    }
}