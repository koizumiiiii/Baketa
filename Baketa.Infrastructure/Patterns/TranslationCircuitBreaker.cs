using System;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Patterns;
using Baketa.Core.Translation.Models;
using Baketa.Core.Translation.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.Patterns;

/// <summary>
/// 翻訳専用サーキットブレーカー実装
/// Phase2: C#側サーキットブレーカー実装
/// </summary>
public class TranslationCircuitBreaker : ICircuitBreaker<TranslationResponse>
{
    private readonly ILogger<TranslationCircuitBreaker> _logger;
    private readonly CircuitBreakerSettings _settings;
    private readonly object _stateLock = new();
    
    // 状態管理
    private CircuitBreakerState _state = CircuitBreakerState.Closed;
    private int _failureCount = 0;
    private DateTime? _lastFailureTime;
    private DateTime? _lastSuccessTime;
    private DateTime? _circuitOpenTime;
    
    // 統計情報
    private long _totalExecutions = 0;
    private long _totalFailures = 0;
    private long _circuitOpenCount = 0;
    
    public CircuitBreakerState State
    {
        get
        {
            lock (_stateLock)
            {
                return _state;
            }
        }
    }
    
    public bool IsCircuitOpen => State == CircuitBreakerState.Open;
    
    public int FailureCount
    {
        get
        {
            lock (_stateLock)
            {
                return _failureCount;
            }
        }
    }
    
    public DateTime? LastFailureTime
    {
        get
        {
            lock (_stateLock)
            {
                return _lastFailureTime;
            }
        }
    }

    public TranslationCircuitBreaker(
        ILogger<TranslationCircuitBreaker> logger,
        IOptions<CircuitBreakerSettings> settings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        
        _logger.LogInformation("TranslationCircuitBreaker初期化完了 - FailureThreshold: {Threshold}, Timeout: {Timeout}ms", 
            _settings.FailureThreshold, _settings.TimeoutMs);
    }

    public async Task<TranslationResponse> ExecuteAsync(
        Func<CancellationToken, Task<TranslationResponse>> operation, 
        CancellationToken cancellationToken = default)
    {
        // 前処理: サーキット状態チェック
        CheckCircuitState();
        
        if (IsCircuitOpen)
        {
            _logger.LogWarning("サーキットブレーカーが開いています - 実行をブロック");
            throw new CircuitBreakerOpenException("サーキットブレーカーが開いているため翻訳を実行できません");
        }
        
        var executionStopwatch = System.Diagnostics.Stopwatch.StartNew();
        Interlocked.Increment(ref _totalExecutions);
        
        using var timeoutCts = new CancellationTokenSource(_settings.TimeoutMs);
        try
        {
            // タイムアウト付きで操作実行
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            
            var result = await operation(combinedCts.Token).ConfigureAwait(false);
            
            executionStopwatch.Stop();
            
            // 成功時の処理
            OnSuccess(executionStopwatch.ElapsedMilliseconds);
            
            return result;
        }
        catch (OperationCanceledException ex) when (timeoutCts.Token.IsCancellationRequested)
        {
            executionStopwatch.Stop();
            _logger.LogWarning("翻訳タイムアウト - 制限時間: {TimeoutMs}ms", _settings.TimeoutMs);
            
            OnFailure(new TimeoutException($"翻訳がタイムアウトしました ({_settings.TimeoutMs}ms)", ex));
            throw new TranslationTimeoutException($"翻訳がタイムアウトしました ({_settings.TimeoutMs}ms)", ex);
        }
        catch (Exception ex)
        {
            executionStopwatch.Stop();
            _logger.LogError(ex, "翻訳実行エラー - 処理時間: {ElapsedMs}ms", executionStopwatch.ElapsedMilliseconds);
            
            OnFailure(ex);
            throw;
        }
    }
    
    private void CheckCircuitState()
    {
        lock (_stateLock)
        {
            if (_state == CircuitBreakerState.Open)
            {
                // タイムアウト後に半開きに移行
                var timeSinceOpen = DateTime.UtcNow - _circuitOpenTime;
                if (timeSinceOpen >= TimeSpan.FromMilliseconds(_settings.RecoveryTimeoutMs))
                {
                    _state = CircuitBreakerState.HalfOpen;
                    _logger.LogInformation("サーキットブレーカーを半開きに移行 - 復旧テスト開始");
                }
            }
        }
    }
    
    private void OnSuccess(long elapsedMs)
    {
        lock (_stateLock)
        {
            var previousState = _state;
            
            // 成功時はサーキットをクローズし、失敗カウンターをリセット
            _failureCount = 0;
            _lastSuccessTime = DateTime.UtcNow;
            _state = CircuitBreakerState.Closed;
            
            if (previousState != CircuitBreakerState.Closed)
            {
                _logger.LogInformation("サーキットブレーカー復旧完了 - 処理時間: {ElapsedMs}ms", elapsedMs);
            }
            else
            {
                _logger.LogDebug("翻訳成功 - 処理時間: {ElapsedMs}ms", elapsedMs);
            }
        }
    }
    
    private void OnFailure(Exception exception)
    {
        lock (_stateLock)
        {
            _failureCount++;
            _lastFailureTime = DateTime.UtcNow;
            Interlocked.Increment(ref _totalFailures);
            
            _logger.LogWarning("翻訳失敗 - 連続失敗回数: {FailureCount}/{Threshold}", 
                _failureCount, _settings.FailureThreshold);
            
            // 失敗閾値に達したらサーキットを開く
            if (_failureCount >= _settings.FailureThreshold && _state != CircuitBreakerState.Open)
            {
                _state = CircuitBreakerState.Open;
                _circuitOpenTime = DateTime.UtcNow;
                Interlocked.Increment(ref _circuitOpenCount);
                
                _logger.LogError("サーキットブレーカーを開きました - 失敗回数: {FailureCount}, 復旧まで: {RecoveryTimeoutMs}ms", 
                    _failureCount, _settings.RecoveryTimeoutMs);
            }
        }
    }
    
    public void Reset()
    {
        lock (_stateLock)
        {
            var previousState = _state;
            
            _state = CircuitBreakerState.Closed;
            _failureCount = 0;
            _circuitOpenTime = null;
            
            _logger.LogInformation("サーキットブレーカーを手動リセット - 前の状態: {PreviousState}", previousState);
        }
    }
    
    public CircuitBreakerStats GetStats()
    {
        lock (_stateLock)
        {
            var circuitOpenDuration = _state == CircuitBreakerState.Open && _circuitOpenTime.HasValue
                ? DateTime.UtcNow - _circuitOpenTime.Value
                : (TimeSpan?)null;
                
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
}

/// <summary>
/// サーキットブレーカー設定
/// </summary>
public class CircuitBreakerSettings
{
    /// <summary>
    /// サーキットを開くまでの失敗回数閾値
    /// </summary>
    public int FailureThreshold { get; set; } = 5;
    
    /// <summary>
    /// 個別操作のタイムアウト時間（ミリ秒）
    /// </summary>
    public int TimeoutMs { get; set; } = 30000; // 30秒
    
    /// <summary>
    /// サーキットが開いてから復旧テストまでの時間（ミリ秒）
    /// </summary>
    public int RecoveryTimeoutMs { get; set; } = 60000; // 60秒

    /// <summary>
    /// 🆕 Gemini推奨: 接続プール使用可否フラグ
    /// true: 接続プール使用（推奨）、false: 毎回新規接続
    /// </summary>
    public bool EnableConnectionPool { get; set; } = true; // デフォルトは有効
}

/// <summary>
/// フォールバック機能付きサーキットブレーカー - Gemini推奨強化版
/// Phase2: プライマリ（Python）→フォールバック（Gemini）自動切替え
/// </summary>
public class EnhancedTranslationCircuitBreaker : ICircuitBreaker<TranslationResponse>
{
    private readonly ILogger<EnhancedTranslationCircuitBreaker> _logger;
    private readonly CircuitBreakerSettings _settings;
    private readonly object _stateLock = new();

    // 状態管理
    private CircuitBreakerState _state = CircuitBreakerState.Closed;
    private int _failureCount = 0;
    private DateTime? _lastFailureTime;
    private DateTime? _lastSuccessTime;
    private DateTime? _circuitOpenTime;

    // 統計情報
    private long _totalExecutions = 0;
    private long _totalFailures = 0;
    private long _circuitOpenCount = 0;
    private long _fallbackExecutions = 0;

    public CircuitBreakerState State
    {
        get
        {
            lock (_stateLock)
            {
                return _state;
            }
        }
    }

    public bool IsCircuitOpen => State == CircuitBreakerState.Open;

    public int FailureCount
    {
        get
        {
            lock (_stateLock)
            {
                return _failureCount;
            }
        }
    }

    public DateTime? LastFailureTime
    {
        get
        {
            lock (_stateLock)
            {
                return _lastFailureTime;
            }
        }
    }

    public EnhancedTranslationCircuitBreaker(
        ILogger<EnhancedTranslationCircuitBreaker> logger,
        IOptions<CircuitBreakerSettings> settings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));

        _logger.LogInformation("🚀 EnhancedTranslationCircuitBreaker初期化完了 - FailureThreshold: {Threshold}, Timeout: {Timeout}ms",
            _settings.FailureThreshold, _settings.TimeoutMs);
    }

    /// <summary>
    /// 🆕 Gemini推奨: プライマリ操作とフォールバック操作を受け取るExecuteAsync
    /// プライマリ失敗時にフォールバックを自動実行
    /// </summary>
    public async Task<TranslationResponse> ExecuteWithFallbackAsync(
        Func<CancellationToken, Task<TranslationResponse>> primaryOperation,
        Func<CancellationToken, Task<TranslationResponse>> fallbackOperation,
        CancellationToken cancellationToken = default)
    {
        // 前処理: サーキット状態チェック
        CheckCircuitState();

        var executionStopwatch = System.Diagnostics.Stopwatch.StartNew();
        Interlocked.Increment(ref _totalExecutions);

        switch (_state)
        {
            case CircuitBreakerState.Open:
                // サーキットが開いている場合は即座にフォールバックに切り替え
                _logger.LogInformation("⚡ サーキットブレーカー Open - Gemini翻訳に即座フォールバック");
                Interlocked.Increment(ref _fallbackExecutions);
                return await ExecuteFallbackOperationAsync(fallbackOperation, cancellationToken).ConfigureAwait(false);

            case CircuitBreakerState.HalfOpen:
                // 半開き状態では復旧テストを実行
                try
                {
                    var result = await ExecutePrimaryOperationAsync(primaryOperation, cancellationToken).ConfigureAwait(false);
                    if (result.IsSuccess)
                    {
                        OnSuccess(executionStopwatch.ElapsedMilliseconds);
                        _logger.LogInformation("✅ サーキットブレーカー Closed - Python復旧完了");
                    }
                    return result;
                }
                catch (Exception ex)
                {
                    OnFailure(ex);
                    _logger.LogWarning("🔄 復旧テスト失敗 - Geminiフォールバック実行");
                    Interlocked.Increment(ref _fallbackExecutions);
                    return await ExecuteFallbackOperationAsync(fallbackOperation, cancellationToken).ConfigureAwait(false);
                }

            case CircuitBreakerState.Closed:
            default:
                // 通常状態ではプライマリ操作を実行、失敗時にフォールバック
                try
                {
                    var result = await ExecutePrimaryOperationAsync(primaryOperation, cancellationToken).ConfigureAwait(false);
                    OnSuccess(executionStopwatch.ElapsedMilliseconds);
                    return result;
                }
                catch (Exception ex)
                {
                    OnFailure(ex);

                    // 閾値到達でサーキットが開いた場合はフォールバック実行
                    if (_state == CircuitBreakerState.Open)
                    {
                        _logger.LogWarning("🔄 プライマリ失敗、サーキットOpen - Geminiフォールバック実行");
                        Interlocked.Increment(ref _fallbackExecutions);
                        return await ExecuteFallbackOperationAsync(fallbackOperation, cancellationToken).ConfigureAwait(false);
                    }

                    throw; // 閾値未到達の場合は例外を再スロー
                }
        }
    }

    /// <summary>
    /// レガシー互換性のためのExecuteAsync - 既存コードとの互換性維持
    /// </summary>
    public async Task<TranslationResponse> ExecuteAsync(
        Func<CancellationToken, Task<TranslationResponse>> operation,
        CancellationToken cancellationToken = default)
    {
        // 従来のサーキットブレーカー機能のみ
        CheckCircuitState();

        if (IsCircuitOpen)
        {
            _logger.LogWarning("サーキットブレーカーが開いています - 実行をブロック");
            throw new CircuitBreakerOpenException("サーキットブレーカーが開いているため翻訳を実行できません");
        }

        return await ExecutePrimaryOperationAsync(operation, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TranslationResponse> ExecutePrimaryOperationAsync(
        Func<CancellationToken, Task<TranslationResponse>> operation,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(_settings.TimeoutMs);
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            return await operation(combinedCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (timeoutCts.Token.IsCancellationRequested)
        {
            _logger.LogWarning("翻訳タイムアウト - 制限時間: {TimeoutMs}ms", _settings.TimeoutMs);
            throw new TranslationTimeoutException($"翻訳がタイムアウトしました ({_settings.TimeoutMs}ms)", ex);
        }
    }

    private async Task<TranslationResponse> ExecuteFallbackOperationAsync(
        Func<CancellationToken, Task<TranslationResponse>> fallbackOperation,
        CancellationToken cancellationToken)
    {
        try
        {
            var fallbackResult = await fallbackOperation(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("🎯 Geminiフォールバック翻訳成功 - 処理継続");
            return fallbackResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🚨 Geminiフォールバックも失敗 - 翻訳不可能");
            throw;
        }
    }

    private void CheckCircuitState()
    {
        lock (_stateLock)
        {
            if (_state == CircuitBreakerState.Open)
            {
                // タイムアウト後に半開きに移行
                var timeSinceOpen = DateTime.UtcNow - _circuitOpenTime;
                if (timeSinceOpen >= TimeSpan.FromMilliseconds(_settings.RecoveryTimeoutMs))
                {
                    _state = CircuitBreakerState.HalfOpen;
                    _logger.LogInformation("🔄 サーキットブレーカーを半開きに移行 - 復旧テスト開始");
                }
            }
        }
    }

    private void OnSuccess(long elapsedMs)
    {
        lock (_stateLock)
        {
            var previousState = _state;

            // 成功時はサーキットをクローズし、失敗カウンターをリセット
            _failureCount = 0;
            _lastSuccessTime = DateTime.UtcNow;
            _state = CircuitBreakerState.Closed;

            if (previousState != CircuitBreakerState.Closed)
            {
                _logger.LogInformation("✅ サーキットブレーカー復旧完了 - 処理時間: {ElapsedMs}ms", elapsedMs);
            }
            else
            {
                _logger.LogDebug("翻訳成功 - 処理時間: {ElapsedMs}ms", elapsedMs);
            }
        }
    }

    private void OnFailure(Exception exception)
    {
        lock (_stateLock)
        {
            _failureCount++;
            _lastFailureTime = DateTime.UtcNow;
            Interlocked.Increment(ref _totalFailures);

            _logger.LogWarning("翻訳失敗 - 連続失敗回数: {FailureCount}/{Threshold}",
                _failureCount, _settings.FailureThreshold);

            // 失敗閾値に達したらサーキットを開く
            if (_failureCount >= _settings.FailureThreshold && _state != CircuitBreakerState.Open)
            {
                _state = CircuitBreakerState.Open;
                _circuitOpenTime = DateTime.UtcNow;
                Interlocked.Increment(ref _circuitOpenCount);

                _logger.LogError("🚨 サーキットブレーカーを開きました - 失敗回数: {FailureCount}, 復旧まで: {RecoveryTimeoutMs}ms",
                    _failureCount, _settings.RecoveryTimeoutMs);
            }
        }
    }

    public void Reset()
    {
        lock (_stateLock)
        {
            var previousState = _state;

            _state = CircuitBreakerState.Closed;
            _failureCount = 0;
            _circuitOpenTime = null;

            _logger.LogInformation("サーキットブレーカーを手動リセット - 前の状態: {PreviousState}", previousState);
        }
    }

    public CircuitBreakerStats GetStats()
    {
        lock (_stateLock)
        {
            var circuitOpenDuration = _state == CircuitBreakerState.Open && _circuitOpenTime.HasValue
                ? DateTime.UtcNow - _circuitOpenTime.Value
                : (TimeSpan?)null;

            return new CircuitBreakerStats
            {
                TotalExecutions = _totalExecutions,
                TotalFailures = _totalFailures,
                ConsecutiveFailures = _failureCount,
                LastSuccessTime = _lastSuccessTime,
                LastFailureTime = _lastFailureTime,
                CircuitOpenDuration = circuitOpenDuration,
                CircuitOpenCount = _circuitOpenCount,
                // 🆕 フォールバック統計を追加
                FallbackExecutions = _fallbackExecutions
            };
        }
    }
}

/// <summary>
/// サーキットブレーカーが開いている時の例外
/// </summary>
public class CircuitBreakerOpenException : TranslationException
{
    public CircuitBreakerOpenException(string message) : base(message) { }
    public CircuitBreakerOpenException(string message, Exception innerException) : base(message, innerException) { }
}