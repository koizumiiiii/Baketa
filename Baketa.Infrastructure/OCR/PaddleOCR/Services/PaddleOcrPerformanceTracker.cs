using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using Baketa.Core.Abstractions.OCR;
using Baketa.Infrastructure.OCR.PaddleOCR.Abstractions;
using Microsoft.Extensions.Logging;
using OpenCvSharp;

namespace Baketa.Infrastructure.OCR.PaddleOCR.Services;

/// <summary>
/// パフォーマンス統計、タイムアウト管理、エラー追跡を担当するサービス
/// Phase 2.2: PaddleOcrEngineから抽出された175行のパフォーマンス追跡実装
/// スレッドセーフ対応：Interlocked操作とConcurrentQueueを使用
/// </summary>
public sealed class PaddleOcrPerformanceTracker : IPaddleOcrPerformanceTracker
{
    private readonly ILogger<PaddleOcrPerformanceTracker>? _logger;

    // パフォーマンス統計フィールド（スレッドセーフ）
    private readonly ConcurrentQueue<double> _processingTimes = new();
    private int _totalProcessedImages;
    private int _errorCount;
    private readonly DateTime _startTime = DateTime.UtcNow;

    // 適応的タイムアウト用の統計
    private DateTime _lastOcrTime = DateTime.MinValue;
    private int _consecutiveTimeouts;

    // PaddlePredictor失敗統計
    private int _consecutivePaddleFailures;

    public PaddleOcrPerformanceTracker(ILogger<PaddleOcrPerformanceTracker>? logger = null)
    {
        _logger = logger;
        _logger?.LogInformation("🚀 PaddleOcrPerformanceTracker初期化完了");
    }

    /// <summary>
    /// パフォーマンス統計更新
    /// </summary>
    public void UpdatePerformanceStats(double processingTimeMs, bool success)
    {
        Interlocked.Increment(ref _totalProcessedImages);

        if (!success)
        {
            Interlocked.Increment(ref _errorCount);
        }

        _processingTimes.Enqueue(processingTimeMs);

        // キューサイズを制限（最新1000件のみ保持）
        while (_processingTimes.Count > 1000)
        {
            _processingTimes.TryDequeue(out _);
        }
    }

    /// <summary>
    /// パフォーマンス統計取得
    /// </summary>
    public OcrPerformanceStats GetPerformanceStats()
    {
        var times = _processingTimes.ToArray();
        var avgTime = times.Length > 0 ? times.Average() : 0.0;
        var minTime = times.Length > 0 ? times.Min() : 0.0;
        var maxTime = times.Length > 0 ? times.Max() : 0.0;
        var successRate = _totalProcessedImages > 0
            ? (double)(_totalProcessedImages - _errorCount) / _totalProcessedImages
            : 0.0;

        return new OcrPerformanceStats
        {
            TotalProcessedImages = _totalProcessedImages,
            AverageProcessingTimeMs = avgTime,
            MinProcessingTimeMs = minTime,
            MaxProcessingTimeMs = maxTime,
            ErrorCount = _errorCount,
            SuccessRate = successRate,
            StartTime = _startTime,
            LastUpdateTime = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 解像度とモデルに応じた基本タイムアウトを計算
    /// </summary>
    /// <param name="mat">処理対象の画像Mat</param>
    /// <returns>基本タイムアウト（秒）</returns>
    public int CalculateTimeout(Mat mat)
    {
        // 🛡️ [MEMORY_PROTECTION] Mat状態の安全性チェック
        try
        {
            // Mat.Empty()チェックが最も安全（内部でColsやRowsチェックも行う）
            if (mat == null || mat.Empty())
            {
                _logger?.LogWarning("⚠️ Mat is null or empty in CalculateTimeout - using default timeout");
                return 30; // V5統一タイムアウト
            }

            // Mat基本プロパティの安全な取得（AccessViolationException & ObjectDisposedException回避）
            int width, height;
            try
            {
                // 🛡️ [LIFECYCLE_PROTECTION] Mat処分状態チェック
                if (mat.IsDisposed)
                {
                    _logger?.LogWarning("⚠️ Mat is disposed in CalculateTimeout - using default timeout");
                    return 30; // V5統一タイムアウト
                }

                width = mat.Width;   // 内部でmat.get_Cols()を呼び出し
                height = mat.Height; // 内部でmat.get_Rows()を呼び出し
            }
            catch (ObjectDisposedException ex)
            {
                _logger?.LogError(ex, "🚨 [MAT_DISPOSED] ObjectDisposedException in Mat.Width/Height access");
                return 30; // V5統一タイムアウト
            }
            catch (AccessViolationException ex)
            {
                _logger?.LogError(ex, "🚨 AccessViolationException in Mat.Width/Height access - Mat may be corrupted or disposed");
                return 30; // V5統一タイムアウト
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "⚠️ Unexpected exception in Mat property access: {ExceptionType}", ex.GetType().Name);
                return 30; // V5統一タイムアウト
            }

            // 有効なサイズかチェック
            if (width <= 0 || height <= 0)
            {
                _logger?.LogWarning("⚠️ Invalid Mat dimensions: {Width}x{Height} - using default timeout", width, height);
                return 30; // V5統一タイムアウト
            }

            var pixelCount = (long)width * height; // オーバーフロー防止のためlong使用
            var isV4Model = false; // V5統一により常にfalse

            // 解像度ベースのタイムアウト計算
            int baseTimeout = isV4Model ? 25 : 30; // V4=25秒, V5=30秒（初期値を延長）

            // ピクセル数に応じたタイムアウト調整
            if (pixelCount > 2500000) // 2.5M pixel超 (2560x1080相当以上)
            {
                baseTimeout = isV4Model ? 45 : 50; // 大画面対応（V5を延長）
            }
            else if (pixelCount > 2000000) // 2M pixel超 (1920x1080相当以上)
            {
                baseTimeout = isV4Model ? 35 : 40; // V5を延長
            }
            else if (pixelCount > 1000000) // 1M pixel超 (1280x720相当以上)
            {
                baseTimeout = isV4Model ? 30 : 35; // V5を延長
            }

            _logger?.LogDebug("🖼️ 解像度ベースタイムアウト: {Width}x{Height}({PixelCount:N0}px) → {BaseTimeout}秒 (V4={IsV4Model})",
                width, height, pixelCount, baseTimeout, isV4Model);

            return baseTimeout;
        }
        catch (ObjectDisposedException ex)
        {
            _logger?.LogError(ex, "🚨 [MAT_LIFECYCLE] Mat disposed during CalculateTimeout - using default timeout");
            return 30; // フォールバック
        }
        catch (AccessViolationException ex)
        {
            _logger?.LogError(ex, "🚨 AccessViolationException in CalculateTimeout - using default timeout");
            return 30; // フォールバック
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "🚨 Unexpected error in CalculateTimeout - using default timeout");
            return 30; // フォールバック
        }
    }

    /// <summary>
    /// 適応的タイムアウト取得
    /// </summary>
    public int GetAdaptiveTimeout(int baseTimeout)
    {
        var timeSinceLastOcr = DateTime.UtcNow - _lastOcrTime;

        // 連続処理による性能劣化を考慮
        var adaptiveTimeout = baseTimeout;

        // 短時間での連続処理の場合、タイムアウトを延長
        if (timeSinceLastOcr.TotalSeconds < 10)
        {
            adaptiveTimeout = (int)(baseTimeout * 1.5);
            _logger?.LogDebug("🔄 連続処理検出: 前回から{TimeSinceLastOcr:F1}秒, タイムアウト延長", timeSinceLastOcr.TotalSeconds);
        }

        // 連続タイムアウトの場合、さらに延長
        if (_consecutiveTimeouts > 0)
        {
            adaptiveTimeout = (int)(adaptiveTimeout * (1 + 0.3 * _consecutiveTimeouts));
            _logger?.LogDebug("⚠️ 連続タイムアウト={ConsecutiveTimeouts}回, タイムアウト追加延長", _consecutiveTimeouts);
        }

        // 🎯 [LEVEL1_FIX] 大画面対応スケーリング処理を考慮したタイムアウト延長
        // Level 1実装により、Mat再構築やスケーリング処理で追加時間が必要
        adaptiveTimeout = (int)(adaptiveTimeout * 1.8); // 80%延長
        _logger?.LogDebug("🎯 [LEVEL1_TIMEOUT] 大画面対応タイムアウト延長: {BaseTimeout}秒 → {AdaptiveTimeout}秒 (80%延長)",
            baseTimeout, adaptiveTimeout);

        // 最大値制限を緩和 (3倍 → 4倍)
        var maxTimeout = Math.Min(adaptiveTimeout, baseTimeout * 4);

        // 🔍 [ULTRATHINK_FIX] タイムアウト設定の詳細ログ
        _logger?.LogWarning("⏱️ [TIMEOUT_CONFIG] 最終タイムアウト設定: {FinalTimeout}秒 (ベース: {Base}秒, 適応: {Adaptive}秒, 連続失敗: {Failures}回)",
            maxTimeout, baseTimeout, adaptiveTimeout, _consecutiveTimeouts);

        // 最後のOCR時刻を更新
        _lastOcrTime = DateTime.UtcNow;

        return maxTimeout;
    }

    /// <summary>
    /// 失敗カウンタリセット
    /// </summary>
    public void ResetFailureCounter()
    {
        var previousCount = _consecutivePaddleFailures;
        _consecutivePaddleFailures = 0;
        _logger?.LogWarning("🔄 [MANUAL_RESET] PaddleOCR失敗カウンターを手動リセット: {PreviousCount} → 0", previousCount);
    }

    /// <summary>
    /// 連続失敗数取得
    /// </summary>
    public int GetConsecutiveFailureCount()
    {
        return _consecutivePaddleFailures;
    }
}
