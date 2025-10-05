using System;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Infrastructure.OCR.PaddleOCR.Abstractions;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Sdcb.PaddleOCR;

namespace Baketa.Infrastructure.OCR.PaddleOCR.Services;

/// <summary>
/// PaddleOCR実行、タイムアウト管理、リトライ処理を担当するサービス
/// Phase 2.7: PaddleOcrEngineから抽出されたOCR実行実装
///
/// 🔧 [SKELETON_IMPL] 現在はスケルトン実装
/// 将来の完全実装時に追加予定:
/// - _errorHandler統合（try-catchブロック内でエラー処理委譲）
/// - _performanceTracker統合（OCR実行時間計測）
/// - メモリ分離戦略（byte[]抽出によるスレッドセーフティ向上）
/// - 適応的タイムアウト計算（画像サイズに基づく動的タイムアウト）
/// - リトライロジック実装
/// </summary>
public sealed class PaddleOcrExecutor : IPaddleOcrExecutor
{
    private readonly IPaddleOcrEngineInitializer _engineInitializer;
    private readonly IPaddleOcrErrorHandler _errorHandler; // 🔧 [TODO_FUTURE] エラー処理統合予定
    private readonly IPaddleOcrPerformanceTracker _performanceTracker; // 🔧 [TODO_FUTURE] パフォーマンス計測統合予定
    private readonly ILogger<PaddleOcrExecutor>? _logger;

    private CancellationTokenSource? _currentOcrCancellation;
    private readonly object _lockObject = new();

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
        _logger?.LogInformation("🚀 PaddleOcrExecutor初期化完了");
    }

    /// <summary>
    /// OCR実行（認識付き）
    /// </summary>
    public async Task<PaddleOcrResult> ExecuteOcrAsync(
        Mat processedMat,
        IProgress<OcrProgress>? progress,
        CancellationToken cancellationToken)
    {
        _logger?.LogDebug("⚙️ ExecuteOcrAsync開始: {Width}x{Height}", processedMat.Width, processedMat.Height);
        progress?.Report(new OcrProgress(0, 100, "OCR実行開始"));

        var engine = _engineInitializer.GetOcrEngine();
        if (engine == null)
        {
            _logger?.LogError("OCRエンジンが初期化されていません");
            throw new InvalidOperationException("OCRエンジンが初期化されていません");
        }

        try
        {
            // 🎯 [PHASE2.7] OCR実行（簡略版）
            // 実際のPaddleOcrEngine.csから主要ロジックを移行予定
            var result = await ExecuteOcrInSeparateTaskAsync(processedMat, cancellationToken).ConfigureAwait(false);

            progress?.Report(new OcrProgress(100, 100, "OCR完了"));
            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "OCR実行エラー");
            throw;
        }
    }

    /// <summary>
    /// 検出専用OCR実行
    /// </summary>
    public async Task<PaddleOcrResult> ExecuteDetectionOnlyAsync(
        Mat processedMat,
        CancellationToken cancellationToken)
    {
        _logger?.LogDebug("⚡ ExecuteDetectionOnlyAsync開始 - 高速検出モード");

        var engine = _engineInitializer.GetOcrEngine();
        if (engine == null)
        {
            _logger?.LogError("OCRエンジンが初期化されていません");
            throw new InvalidOperationException("OCRエンジンが初期化されていません");
        }

        try
        {
            // 🎯 [PHASE2.7] 検出専用実行（簡略版）
            var result = await ExecuteDetectionOnlyInternalAsync(processedMat, cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "検出専用OCR実行エラー");
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
    /// OCR実行（非同期タスク）- 簡略版
    /// </summary>
    private async Task<PaddleOcrResult> ExecuteOcrInSeparateTaskAsync(
        Mat processedMat,
        CancellationToken cancellationToken)
    {
        _logger?.LogDebug("🚀 ExecuteOcrInSeparateTask開始");

        var engine = _engineInitializer.GetOcrEngine();
        if (engine == null)
        {
            throw new InvalidOperationException("OCRエンジンが初期化されていません");
        }

        // タイムアウト設定（30秒デフォルト）
        // 🔧 [TODO_FUTURE] タイムアウト値をappsettings.jsonに外部化し、IOptions<OcrSettings>で注入する
        var timeoutSeconds = 30;

        lock (_lockObject)
        {
            _currentOcrCancellation?.Dispose();
            _currentOcrCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        }

        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _currentOcrCancellation.Token);

        try
        {
            var ocrTask = Task.Run(() =>
            {
                _logger?.LogDebug("🚀 Task.Run開始 - OCR処理実行");

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
    /// 検出専用OCR実行（内部実装）- 簡略版
    /// </summary>
    private async Task<PaddleOcrResult> ExecuteDetectionOnlyInternalAsync(
        Mat mat,
        CancellationToken cancellationToken)
    {
        _logger?.LogDebug("🎯 ExecuteDetectionOnlyInternal開始");

        var engine = _engineInitializer.GetOcrEngine();
        if (engine == null)
        {
            throw new InvalidOperationException("OCRエンジンが初期化されていません");
        }

        // 🔍 [GEMINI_REVIEW] 検出専用もタイムアウト機構を追加（検出処理は高速だが、ハング対策として）
        var timeoutSeconds = 15; // 検出専用は15秒（通常の半分）

        lock (_lockObject)
        {
            _currentOcrCancellation?.Dispose();
            _currentOcrCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        }

        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _currentOcrCancellation.Token);

        try
        {
            var ocrTask = Task.Run(() =>
            {
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

    #endregion
}
