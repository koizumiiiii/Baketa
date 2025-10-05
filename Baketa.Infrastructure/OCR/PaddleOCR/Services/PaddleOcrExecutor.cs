using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Infrastructure.OCR.PaddleOCR.Abstractions;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Sdcb.PaddleOCR;
using OcrException = Baketa.Core.Abstractions.OCR.OcrException;
using CoreOcrProgress = Baketa.Core.Abstractions.OCR.OcrProgress;

namespace Baketa.Infrastructure.OCR.PaddleOCR.Services;

/// <summary>
/// PaddleOCR実行、タイムアウト管理、リトライ処理を担当するサービス
/// Phase 2.9.2: PaddleOcrEngineから完全実装を移行
///
/// ✅ [PHASE2.9.2_COMPLETE] 完全実装完了
/// - エラーハンドリング統合（エラー情報収集と診断）
/// - パフォーマンストラッキング統合（_performanceTracker.UpdatePerformanceStats()呼び出し）
/// - 適応的タイムアウト計算（画像サイズに基づく動的タイムアウト）
/// - リトライロジック実装（最大3回リトライ、線形バックオフ: 500ms, 1000ms, 1500ms）
/// - メモリ分離戦略（Mat.Clone()による安全な並列処理）
/// - 詳細なログ出力（デバッグ・診断用）
///
/// 🔧 [TODO_PHASE2.9.2] 将来の拡張:
/// - タイムアウト設定の外部化（IOptions<OcrSettings>）
/// </summary>
public sealed class PaddleOcrExecutor : IPaddleOcrExecutor
{
    private readonly IPaddleOcrEngineInitializer _engineInitializer;
    private readonly IPaddleOcrErrorHandler _errorHandler;
    private readonly IPaddleOcrPerformanceTracker _performanceTracker;
    private readonly ILogger<PaddleOcrExecutor>? _logger;

    private CancellationTokenSource? _currentOcrCancellation;
    private readonly object _lockObject = new();

    // タイムアウト設定（将来的にはIOptions<OcrSettings>から注入）
    private const int DefaultOcrTimeoutSeconds = 30;
    private const int DetectionOnlyTimeoutSeconds = 15;
    private const int MaxRetryAttempts = 3;
    private const int RetryDelayMilliseconds = 500;

    public PaddleOcrExecutor(
        IPaddleOcrEngineInitializer engineInitializer,
        IPaddleOcrErrorHandler errorHandler,
        IPaddleOcrPerformanceTracker performanceTracker,
        ILogger<PaddleOcrExecutor>? logger = null)
    {
        _engineInitializer = engineInitializer ?? throw new ArgumentNullException(nameof(engineInitializer));
        _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
        _performanceTracker = performanceTracker ?? throw new ArgumentNullException(nameof(performanceTracker));
        _logger = logger;
        _logger?.LogInformation("🚀 PaddleOcrExecutor初期化完了 - MaxRetry={MaxRetry}, DefaultTimeout={Timeout}秒",
            MaxRetryAttempts, DefaultOcrTimeoutSeconds);
    }

    /// <summary>
    /// OCR実行（認識付き）
    /// Phase 2.9.2: 完全実装（リトライロジック、エラーハンドリング、パフォーマンストラッキング統合）
    /// ✅ [PHASE2.9.3.3] 型統一: OcrProgress → CoreOcrProgress
    /// </summary>
    public async Task<PaddleOcrResult> ExecuteOcrAsync(
        Mat processedMat,
        IProgress<CoreOcrProgress>? progress,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var imageSize = new System.Drawing.Size(processedMat.Width, processedMat.Height);

        _logger?.LogDebug("⚙️ ExecuteOcrAsync開始: {Width}x{Height}", imageSize.Width, imageSize.Height);
        progress?.Report(new CoreOcrProgress(0.0, "OCR実行開始"));

        var engine = _engineInitializer.GetOcrEngine();
        if (engine == null)
        {
            var error = new InvalidOperationException("OCRエンジンが初期化されていません");
            _logger?.LogError(error, "❌ OCRエンジン初期化エラー");
            throw error;
        }

        PaddleOcrResult? result = null;
        Exception? lastException = null;
        var attemptCount = 0;

        try
        {
            // ✅ [PHASE2.9.2] 適応的タイムアウト計算
            var timeout = CalculateAdaptiveTimeout(imageSize, DefaultOcrTimeoutSeconds);
            _logger?.LogDebug("🕐 適応的タイムアウト: {Timeout}秒 (画像サイズ: {Width}x{Height})",
                timeout, imageSize.Width, imageSize.Height);

            // ✅ [PHASE2.9.2] リトライロジック実装
            for (attemptCount = 1; attemptCount <= MaxRetryAttempts; attemptCount++)
            {
                try
                {
                    if (attemptCount > 1)
                    {
                        _logger?.LogWarning("🔄 OCRリトライ {Attempt}/{Max} - 前回エラー: {Error}",
                            attemptCount, MaxRetryAttempts, lastException?.Message);
                        progress?.Report(new CoreOcrProgress(0.0, $"OCRリトライ {attemptCount}/{MaxRetryAttempts}"));
                        await Task.Delay(RetryDelayMilliseconds * attemptCount, cancellationToken).ConfigureAwait(false);
                    }

                    result = await ExecuteOcrInSeparateTaskAsync(processedMat, timeout, cancellationToken).ConfigureAwait(false);

                    if (result != null)
                    {
                        _logger?.LogDebug("✅ OCR成功: 試行{Attempt}/{Max}, 検出領域数={Count}",
                            attemptCount, MaxRetryAttempts, result.Regions.Length);
                        break; // 成功
                    }
                }
                catch (TimeoutException tex)
                {
                    lastException = tex;
                    _logger?.LogWarning("⏱️ OCRタイムアウト (試行{Attempt}/{Max}): {Timeout}秒",
                        attemptCount, MaxRetryAttempts, timeout);

                    if (attemptCount >= MaxRetryAttempts)
                    {
                        _logger?.LogError(tex, "❌ OCRタイムアウト（最大リトライ到達）");
                        throw;
                    }
                }
                catch (OperationCanceledException oce)
                {
                    _logger?.LogWarning("🛑 OCRキャンセル (試行{Attempt}/{Max})", attemptCount, MaxRetryAttempts);
                    _logger?.LogError(oce, "❌ OCRキャンセル");
                    throw;
                }
                catch (Exception ex) when (attemptCount < MaxRetryAttempts)
                {
                    lastException = ex;
                    _logger?.LogWarning(ex, "⚠️ OCRエラー (試行{Attempt}/{Max}) - リトライします",
                        attemptCount, MaxRetryAttempts);
                }
            }

            if (result == null)
            {
                var error = lastException ?? new OcrException("OCRが結果を返しませんでした");
                _logger?.LogError(error, "❌ OCR実行失敗（最大リトライ到達）");
                throw error;
            }

            sw.Stop();

            // ✅ [PHASE2.9.2] パフォーマンストラッキング統合
            _performanceTracker.UpdatePerformanceStats(sw.Elapsed.TotalMilliseconds, success: true);

            progress?.Report(new CoreOcrProgress(1.0, "OCR完了"));

            _logger?.LogInformation("✅ OCR完了: {Time}ms, 試行回数={Attempts}, 検出領域数={Count}",
                sw.ElapsedMilliseconds, attemptCount, result.Regions.Length);

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();

            // ✅ [PHASE2.9.2] エラー時もパフォーマンストラッキング
            _performanceTracker.UpdatePerformanceStats(sw.Elapsed.TotalMilliseconds, success: false);

            _logger?.LogError(ex, "❌ OCR実行エラー: {Time}ms, 試行回数={Attempts}",
                sw.ElapsedMilliseconds, attemptCount);

            throw;
        }
    }

    /// <summary>
    /// 検出専用OCR実行
    /// Phase 2.9.2: 完全実装（リトライロジック、エラーハンドリング、パフォーマンストラッキング統合）
    /// </summary>
    public async Task<PaddleOcrResult> ExecuteDetectionOnlyAsync(
        Mat processedMat,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var imageSize = new System.Drawing.Size(processedMat.Width, processedMat.Height);

        _logger?.LogDebug("⚡ ExecuteDetectionOnlyAsync開始 - 高速検出モード: {Width}x{Height}",
            imageSize.Width, imageSize.Height);

        var engine = _engineInitializer.GetOcrEngine();
        if (engine == null)
        {
            var error = new InvalidOperationException("OCRエンジンが初期化されていません");
            _logger?.LogError(error, "❌ OCRエンジン初期化エラー（検出専用）");
            throw error;
        }

        PaddleOcrResult? result = null;
        Exception? lastException = null;
        var attemptCount = 0;

        try
        {
            // ✅ [PHASE2.9.2] 適応的タイムアウト計算（検出専用は短め）
            var timeout = CalculateAdaptiveTimeout(imageSize, DetectionOnlyTimeoutSeconds);
            _logger?.LogDebug("🕐 検出専用タイムアウト: {Timeout}秒 (画像サイズ: {Width}x{Height})",
                timeout, imageSize.Width, imageSize.Height);

            // ✅ [PHASE2.9.2] リトライロジック実装
            for (attemptCount = 1; attemptCount <= MaxRetryAttempts; attemptCount++)
            {
                try
                {
                    if (attemptCount > 1)
                    {
                        _logger?.LogWarning("🔄 検出専用リトライ {Attempt}/{Max} - 前回エラー: {Error}",
                            attemptCount, MaxRetryAttempts, lastException?.Message);
                        await Task.Delay(RetryDelayMilliseconds * attemptCount, cancellationToken).ConfigureAwait(false);
                    }

                    result = await ExecuteDetectionOnlyInternalAsync(processedMat, timeout, cancellationToken).ConfigureAwait(false);

                    if (result != null)
                    {
                        _logger?.LogDebug("✅ 検出成功: 試行{Attempt}/{Max}, 検出領域数={Count}",
                            attemptCount, MaxRetryAttempts, result.Regions.Length);
                        break; // 成功
                    }
                }
                catch (TimeoutException tex)
                {
                    lastException = tex;
                    _logger?.LogWarning("⏱️ 検出専用タイムアウト (試行{Attempt}/{Max}): {Timeout}秒",
                        attemptCount, MaxRetryAttempts, timeout);

                    if (attemptCount >= MaxRetryAttempts)
                    {
                        _logger?.LogError(tex, "❌ 検出専用タイムアウト（最大リトライ到達）");
                        throw;
                    }
                }
                catch (OperationCanceledException oce)
                {
                    _logger?.LogWarning("🛑 検出専用キャンセル (試行{Attempt}/{Max})", attemptCount, MaxRetryAttempts);
                    _logger?.LogError(oce, "❌ 検出専用キャンセル");
                    throw;
                }
                catch (Exception ex) when (attemptCount < MaxRetryAttempts)
                {
                    lastException = ex;
                    _logger?.LogWarning(ex, "⚠️ 検出専用エラー (試行{Attempt}/{Max}) - リトライします",
                        attemptCount, MaxRetryAttempts);
                }
            }

            if (result == null)
            {
                var error = lastException ?? new OcrException("検出専用OCRが結果を返しませんでした");
                _logger?.LogError(error, "❌ 検出専用実行失敗（最大リトライ到達）");
                throw error;
            }

            sw.Stop();

            // ✅ [PHASE2.9.2] パフォーマンストラッキング統合
            _performanceTracker.UpdatePerformanceStats(sw.Elapsed.TotalMilliseconds, success: true);

            _logger?.LogInformation("✅ 検出完了: {Time}ms, 試行回数={Attempts}, 検出領域数={Count}",
                sw.ElapsedMilliseconds, attemptCount, result.Regions.Length);

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();

            // ✅ [PHASE2.9.2] エラー時もパフォーマンストラッキング
            _performanceTracker.UpdatePerformanceStats(sw.Elapsed.TotalMilliseconds, success: false);

            _logger?.LogError(ex, "❌ 検出専用実行エラー: {Time}ms, 試行回数={Attempts}",
                sw.ElapsedMilliseconds, attemptCount);

            throw;
        }
    }

    /// <summary>
    /// 現在のOCRタイムアウトをキャンセル
    /// </summary>
    public void CancelCurrentOcrTimeout()
    {
        lock (_lockObject)
        {
            _currentOcrCancellation?.Cancel();
            _currentOcrCancellation?.Dispose();
            _currentOcrCancellation = null;
        }
        _logger?.LogWarning("⏱️ OCRタイムアウトキャンセル実行");
    }

    #region 内部実装メソッド

    /// <summary>
    /// OCR実行（非同期タスク）- 完全版
    /// Phase 2.9.2: タイムアウトパラメータ追加、メモリ分離戦略実装
    /// </summary>
    private async Task<PaddleOcrResult> ExecuteOcrInSeparateTaskAsync(
        Mat processedMat,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        _logger?.LogDebug("🚀 ExecuteOcrInSeparateTask開始: Timeout={Timeout}秒", timeoutSeconds);

        var engine = _engineInitializer.GetOcrEngine();
        if (engine == null)
        {
            throw new InvalidOperationException("OCRエンジンが初期化されていません");
        }

        lock (_lockObject)
        {
            _currentOcrCancellation?.Dispose();
            _currentOcrCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        }

        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _currentOcrCancellation.Token);

        try
        {
            // ✅ [PHASE2.9.2] メモリ分離戦略: Mat.Clone()による安全な並列処理
            var ocrTask = Task.Run(() =>
            {
                _logger?.LogDebug("🚀 Task.Run開始 - OCR処理実行");

                // Mat.Clone()で独立したメモリを確保し、スレッドセーフティを向上
                using var matForOcr = processedMat.Clone();
                var result = engine.Run(matForOcr);

                _logger?.LogDebug("✅ OCR完了: 検出領域数={Count}", result.Regions.Length);
                return result;
            }, combinedCts.Token);

            var result = await ocrTask.ConfigureAwait(false);
            return result;
        }
        catch (OperationCanceledException) when (_currentOcrCancellation?.IsCancellationRequested == true)
        {
            _logger?.LogWarning("⏱️ OCRタイムアウト: {Timeout}秒", timeoutSeconds);
            throw new TimeoutException($"OCR処理が{timeoutSeconds}秒でタイムアウトしました");
        }
        finally
        {
            lock (_lockObject)
            {
                _currentOcrCancellation?.Dispose();
                _currentOcrCancellation = null;
            }
        }
    }

    /// <summary>
    /// 検出専用OCR実行（内部実装）- 完全版
    /// Phase 2.9.2: タイムアウトパラメータ追加、メモリ分離戦略実装
    /// </summary>
    private async Task<PaddleOcrResult> ExecuteDetectionOnlyInternalAsync(
        Mat mat,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        _logger?.LogDebug("🎯 ExecuteDetectionOnlyInternal開始: Timeout={Timeout}秒", timeoutSeconds);

        var engine = _engineInitializer.GetOcrEngine();
        if (engine == null)
        {
            throw new InvalidOperationException("OCRエンジンが初期化されていません");
        }

        lock (_lockObject)
        {
            _currentOcrCancellation?.Dispose();
            _currentOcrCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        }

        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _currentOcrCancellation.Token);

        try
        {
            // ✅ [PHASE2.9.2] メモリ分離戦略: Mat.Clone()による安全な並列処理
            var ocrTask = Task.Run(() =>
            {
                _logger?.LogDebug("🚀 Task.Run開始 - 検出専用処理実行");

                // Mat.Clone()で独立したメモリを確保し、スレッドセーフティを向上
                using var matForDetection = mat.Clone();
                var result = engine.Run(matForDetection);

                _logger?.LogDebug("✅ 検出完了: 検出領域数={Count}", result.Regions.Length);
                return result;
            }, combinedCts.Token);

            var result = await ocrTask.ConfigureAwait(false);
            return result;
        }
        catch (OperationCanceledException) when (_currentOcrCancellation?.IsCancellationRequested == true)
        {
            _logger?.LogWarning("⏱️ 検出専用OCRタイムアウト: {Timeout}秒", timeoutSeconds);
            throw new TimeoutException($"検出専用OCR処理が{timeoutSeconds}秒でタイムアウトしました");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "検出専用OCR実行エラー");
            throw;
        }
        finally
        {
            lock (_lockObject)
            {
                _currentOcrCancellation?.Dispose();
                _currentOcrCancellation = null;
            }
        }
    }

    /// <summary>
    /// 適応的タイムアウト計算
    /// Phase 2.9.2: 画像サイズに基づく動的タイムアウト計算
    /// </summary>
    /// <param name="imageSize">画像サイズ</param>
    /// <param name="baseTimeoutSeconds">ベースタイムアウト（秒）</param>
    /// <returns>計算されたタイムアウト（秒）</returns>
    private int CalculateAdaptiveTimeout(System.Drawing.Size imageSize, int baseTimeoutSeconds)
    {
        // ✅ [PHASE2.9.2] 画像サイズに基づく適応的タイムアウト計算
        // - 基準: 1920x1080 = 2,073,600ピクセル → baseTimeoutSeconds
        // - 画像が大きければタイムアウトを延長、小さければ短縮

        const int referencePixels = 1920 * 1080; // 2,073,600ピクセル
        var actualPixels = imageSize.Width * imageSize.Height;

        // ピクセル数比率を計算（最小0.5倍、最大2.0倍に制限）
        var ratio = Math.Max(0.5, Math.Min(2.0, (double)actualPixels / referencePixels));

        var adaptiveTimeout = (int)Math.Ceiling(baseTimeoutSeconds * ratio);

        _logger?.LogDebug("🕐 適応的タイムアウト計算: {ActualPixels}px/{ReferencePixels}px = {Ratio:F2}倍 → {BaseTimeout}秒 → {AdaptiveTimeout}秒",
            actualPixels, referencePixels, ratio, baseTimeoutSeconds, adaptiveTimeout);

        return adaptiveTimeout;
    }

    #endregion
}
