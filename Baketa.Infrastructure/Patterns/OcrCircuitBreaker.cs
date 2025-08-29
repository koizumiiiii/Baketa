using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using Baketa.Core.Abstractions.Patterns;
using Baketa.Core.Abstractions.OCR;

namespace Baketa.Infrastructure.Patterns;

/// <summary>
/// OCR専用Circuit Breaker実装
/// Sprint 2: IntelligentOcrEngine統合対応
/// GPU→CPU自動フォールバック制御
/// </summary>
public sealed class OcrCircuitBreaker : ICircuitBreaker<OcrResults>, IDisposable
{
    private readonly ILogger<OcrCircuitBreaker> _logger;
    private readonly OcrCircuitBreakerOptions _options;
    private readonly object _lock = new();
    private readonly System.Threading.Timer _resetTimer;
    
    private CircuitBreakerState _state = CircuitBreakerState.Closed;
    private int _failureCount = 0;
    private DateTime? _lastFailureTime;
    private DateTime? _lastSuccessTime;
    private long _totalExecutions = 0;
    private long _totalFailures = 0;
    private long _circuitOpenCount = 0;
    private DateTime _lastOpenTime = DateTime.MinValue;
    private bool _disposed;

    public OcrCircuitBreaker(IOptions<OcrCircuitBreakerOptions> options, ILogger<OcrCircuitBreaker> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // 定期的なリセット処理タイマー
        _resetTimer = new System.Threading.Timer(CheckForReset, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        
        _logger.LogInformation("🔧 OcrCircuitBreaker初期化完了 - 失敗閾値: {Threshold}, オープン時間: {OpenTime}, " +
            "半開き復帰間隔: {HalfOpenInterval}, 自動フォールバック: {AutoFallback}",
            _options.FailureThreshold, _options.OpenTimeout, _options.HalfOpenRetryInterval, _options.AutoFallbackEnabled);
    }

    public CircuitBreakerState State 
    { 
        get 
        { 
            lock (_lock) 
            { 
                return _state; 
            } 
        } 
    }
    
    public bool IsCircuitOpen 
    { 
        get 
        { 
            lock (_lock) 
            { 
                return _state == CircuitBreakerState.Open; 
            } 
        } 
    }
    
    public int FailureCount 
    { 
        get 
        { 
            lock (_lock) 
            { 
                return _failureCount; 
            } 
        } 
    }
    
    public DateTime? LastFailureTime 
    { 
        get 
        { 
            lock (_lock) 
            { 
                return _lastFailureTime; 
            } 
        } 
    }

    public async Task<OcrResults> ExecuteAsync(Func<CancellationToken, Task<OcrResults>> operation, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _totalExecutions);
        
        lock (_lock)
        {
            // Circuit Breaker状態チェック
            if (_state == CircuitBreakerState.Open)
            {
                var timeSinceOpen = DateTime.UtcNow - _lastOpenTime;
                if (timeSinceOpen < _options.OpenTimeout)
                {
                    _logger.LogDebug("⚠️ Circuit Breaker開放中 - 残り時間: {Remaining}s", 
                        (_options.OpenTimeout - timeSinceOpen).TotalSeconds);
                    throw new CircuitBreakerOpenException($"Circuit breaker is open. Time remaining: {(_options.OpenTimeout - timeSinceOpen).TotalSeconds:F1}s");
                }
                else
                {
                    // 半開き状態に移行
                    _state = CircuitBreakerState.HalfOpen;
                    _logger.LogInformation("🔄 Circuit Breaker半開き状態に移行 - 復旧テスト開始");
                }
            }
        }

        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogDebug("🔧 OCR Circuit Breaker実行 - 状態: {State}, 失敗数: {Failures}", _state, _failureCount);
            
            var result = await operation(cancellationToken);
            stopwatch.Stop();
            
            // 成功時の処理
            OnSuccess(stopwatch.Elapsed);
            
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            // 失敗時の処理
            OnFailure(ex, stopwatch.Elapsed);
            throw;
        }
    }

    private void OnSuccess(TimeSpan executionTime)
    {
        lock (_lock)
        {
            _failureCount = 0;
            _lastSuccessTime = DateTime.UtcNow;
            
            if (_state == CircuitBreakerState.HalfOpen)
            {
                _state = CircuitBreakerState.Closed;
                _logger.LogInformation("✅ Circuit Breaker正常状態に復旧 - 実行時間: {Time}ms", executionTime.TotalMilliseconds);
            }
            else
            {
                _logger.LogDebug("✅ OCR実行成功 - 実行時間: {Time}ms", executionTime.TotalMilliseconds);
            }
        }
    }

    private void OnFailure(Exception ex, TimeSpan executionTime)
    {
        Interlocked.Increment(ref _totalFailures);
        
        lock (_lock)
        {
            _failureCount++;
            _lastFailureTime = DateTime.UtcNow;
            
            _logger.LogWarning(ex, "❌ OCR実行失敗 - 失敗数: {Failures}/{Threshold}, 実行時間: {Time}ms", 
                _failureCount, _options.FailureThreshold, executionTime.TotalMilliseconds);
            
            if (_failureCount >= _options.FailureThreshold && _state != CircuitBreakerState.Open)
            {
                _state = CircuitBreakerState.Open;
                _lastOpenTime = DateTime.UtcNow;
                Interlocked.Increment(ref _circuitOpenCount);
                
                _logger.LogError("🚨 Circuit Breaker開放 - 失敗閾値到達: {Failures}, 開放時間: {OpenTime}, " +
                    "自動フォールバック: {AutoFallback}", 
                    _failureCount, _options.OpenTimeout, _options.AutoFallbackEnabled);
            }
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            var previousState = _state;
            _failureCount = 0;
            _state = CircuitBreakerState.Closed;
            _lastFailureTime = null;
            
            _logger.LogInformation("🔄 Circuit Breaker手動リセット - 前状態: {PreviousState} → 正常状態", previousState);
        }
    }

    public CircuitBreakerStats GetStats()
    {
        lock (_lock)
        {
            var circuitOpenDuration = _state == CircuitBreakerState.Open ? 
                DateTime.UtcNow - _lastOpenTime : 
                TimeSpan.Zero;

            return new CircuitBreakerStats
            {
                TotalExecutions = _totalExecutions,
                TotalFailures = _totalFailures,
                ConsecutiveFailures = _failureCount,
                LastSuccessTime = _lastSuccessTime,
                LastFailureTime = _lastFailureTime,
                CircuitOpenDuration = circuitOpenDuration,
                CircuitOpenCount = _circuitOpenCount
            };
        }
    }

    private void CheckForReset(object? state)
    {
        if (_disposed) return;
        
        try
        {
            lock (_lock)
            {
                if (_state == CircuitBreakerState.Open)
                {
                    var timeSinceOpen = DateTime.UtcNow - _lastOpenTime;
                    if (timeSinceOpen >= _options.OpenTimeout)
                    {
                        _state = CircuitBreakerState.HalfOpen;
                        _logger.LogInformation("⏰ Circuit Breaker自動半開き移行 - 開放時間満了: {Duration}", timeSinceOpen);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Circuit Breaker定期チェックエラー");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        try
        {
            _resetTimer?.Dispose();
            
            // 最終統計
            var stats = GetStats();
            _logger.LogInformation("📊 OcrCircuitBreaker統計 - " +
                "総実行: {Total}, 総失敗: {Failures}, 失敗率: {Rate:P2}, 開放回数: {Opens}",
                stats.TotalExecutions, stats.TotalFailures, stats.FailureRate, stats.CircuitOpenCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ OcrCircuitBreaker解放エラー");
        }
        
        _disposed = true;
        _logger.LogInformation("✅ OcrCircuitBreaker解放完了");
    }
}

/// <summary>
/// OCR Circuit Breaker設定
/// </summary>
public class OcrCircuitBreakerOptions
{
    /// <summary>
    /// 失敗閾値（この回数失敗するとサーキットオープン）
    /// </summary>
    public int FailureThreshold { get; set; } = 5;
    
    /// <summary>
    /// サーキットオープン時間
    /// </summary>
    public TimeSpan OpenTimeout { get; set; } = TimeSpan.FromMinutes(1);
    
    /// <summary>
    /// 半開き復帰テスト間隔
    /// </summary>
    public TimeSpan HalfOpenRetryInterval { get; set; } = TimeSpan.FromSeconds(30);
    
    /// <summary>
    /// 自動フォールバックが有効かどうか
    /// </summary>
    public bool AutoFallbackEnabled { get; set; } = true;
    
    /// <summary>
    /// GPU失敗時の即座フォールバック有効
    /// </summary>
    public bool ImmediateFallbackOnGpuError { get; set; } = true;
    
    /// <summary>
    /// 詳細ログ出力
    /// </summary>
    public bool EnableVerboseLogging { get; set; } = false;
}