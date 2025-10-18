using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Capture;
// UltraThink Phase 77.4: OCR.TextDetection名前空間削除 - Capture.ITextRegionDetectorのみ使用
using Baketa.Core.Models.Capture;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Core.Exceptions.Capture;
using Baketa.Core.Abstractions.Factories;
using Baketa.Core.Abstractions.GPU;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events.Diagnostics;
using System.Drawing;
// 🔥 [PHASE_K-29-G] CaptureOptions統合: Baketa.Core.Abstractions.Servicesから取得
using CaptureOptions = Baketa.Core.Abstractions.Services.CaptureOptions;

namespace Baketa.Infrastructure.Platform.Windows.Capture.Strategies;

/// <summary>
/// 専用GPU向けROIベースキャプチャ戦略
/// </summary>
public class ROIBasedCaptureStrategy : ICaptureStrategy
{
    private readonly ILogger<ROIBasedCaptureStrategy> _logger;
    private readonly ITextRegionDetector _textDetector;
    private readonly NativeWindowsCaptureWrapper _nativeWrapper;
    private readonly Baketa.Core.Abstractions.Factories.IWindowsImageFactory _imageFactory;
    private readonly IEventAggregator _eventAggregator;

    public string StrategyName => "ROIBased";
    public int Priority => 25; // 🔧 Phase 0 WGC修復: WGC問題対応で低優先度（ネイティブラッパー依存）

    public ROIBasedCaptureStrategy(
        ILogger<ROIBasedCaptureStrategy> logger,
        ITextRegionDetector textDetector,
        NativeWindowsCaptureWrapper nativeWrapper,
        Baketa.Core.Abstractions.Factories.IWindowsImageFactory imageFactory,
        IEventAggregator eventAggregator)
    {
        // 🔥 [PHASE1-A] 確実なログ出力（ログレベル設定に依存しない）
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        Console.WriteLine($"🔥🔥🔥 [{timestamp}] [K-29-A_CTOR] ROIBasedCaptureStrategy コンストラクタ実行開始");

        try
        {
            var logPath = "E:\\dev\\Baketa\\Baketa.UI\\bin\\Debug\\net8.0-windows10.0.19041.0\\k29a_debug.log";
            System.IO.File.AppendAllText(logPath, $"[{timestamp}] CTOR開始\n");
        }
        catch { /* ファイル書き込み失敗は無視 */ }

        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _textDetector = textDetector ?? throw new ArgumentNullException(nameof(textDetector));
        _nativeWrapper = nativeWrapper ?? throw new ArgumentNullException(nameof(nativeWrapper));
        _imageFactory = imageFactory ?? throw new ArgumentNullException(nameof(imageFactory));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));

        // 🔥 [PHASE13.2.31K-29-A] ITextRegionDetectorの実装型をログ出力
        _logger.LogInformation("🔍 [K-29-A] ROIBasedCaptureStrategy初期化 - ITextRegionDetector実装型: {TextDetectorType}",
            _textDetector.GetType().FullName);

        // 🔥 [PHASE1-A] コンストラクタ完了ログ
        Console.WriteLine($"🔥🔥🔥 [{DateTime.Now:HH:mm:ss.fff}] [K-29-A_CTOR] ROIBasedCaptureStrategy コンストラクタ完了 - TextDetector: {_textDetector.GetType().Name}");

        try
        {
            var logPath = "E:\\dev\\Baketa\\Baketa.UI\\bin\\Debug\\net8.0-windows10.0.19041.0\\k29a_debug.log";
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss.fff}] CTOR完了 - TextDetector: {_textDetector.GetType().Name}\n");
        }
        catch { /* ファイル書き込み失敗は無視 */ }
    }

    public bool CanApply(GpuEnvironmentInfo environment, IntPtr hwnd)
    {
        try
        {
            // 専用GPUまたは大画面での制約回避が必要な場合
            var canApply = environment.IsDedicatedGpu || 
                          environment.MaximumTexture2DDimension < 8192;

            _logger.LogInformation("ROIBased戦略適用判定: {CanApply} (専用GPU: {IsDedicated}, MaxTexture: {MaxTexture})", 
                canApply, environment.IsDedicatedGpu, environment.MaximumTexture2DDimension);

            return canApply;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ROIBased戦略適用可能性チェック中にエラー");
            return false;
        }
    }

    public async Task<bool> ValidatePrerequisitesAsync(IntPtr hwnd)
    {
        try
        {
            if (hwnd == IntPtr.Zero)
                return false;

            // 非同期的にウィンドウ検証を実行
            return await Task.Run(() =>
            {
                var windowExists = IsWindow(hwnd);
                var isVisible = IsWindowVisible(hwnd);

                return windowExists && isVisible;
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ROIBased前提条件チェック中にエラー");
            return false;
        }
    }

    public async Task<CaptureStrategyResult> ExecuteCaptureAsync(IntPtr hwnd, CaptureOptions options)
    {
        // 🔥 [PHASE1-A] ExecuteCaptureAsync開始の確実なログ
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        Console.WriteLine($"🔥🔥🔥 [{timestamp}] [K-29-A_EXEC] ExecuteCaptureAsync開始 - HWND=0x{hwnd.ToInt64():X}");

        try
        {
            var logPath = "E:\\dev\\Baketa\\Baketa.UI\\bin\\Debug\\net8.0-windows10.0.19041.0\\k29a_debug.log";
            System.IO.File.AppendAllText(logPath, $"[{timestamp}] ExecuteCaptureAsync開始 - HWND=0x{hwnd.ToInt64():X}\n");
        }
        catch { /* ファイル書き込み失敗は無視 */ }

        var sessionId = Guid.NewGuid().ToString("N");
        var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = new CaptureStrategyResult
        {
            StrategyName = StrategyName,
            Metrics = new CaptureMetrics()
        };

        try
        {
            _logger.LogInformation("ROIBasedキャプチャ開始: ウィンドウ=0x{Hwnd:X}, セッション={SessionId}", hwnd.ToInt64(), sessionId);

            // Phase 1: 低解像度スキャン
            Console.WriteLine($"🔥🔥🔥 [{DateTime.Now:HH:mm:ss.fff}] [K-29-A_PHASE1] Phase 1開始: 低解像度スキャン");
            try { System.IO.File.AppendAllText("E:\\dev\\Baketa\\Baketa.UI\\bin\\Debug\\net8.0-windows10.0.19041.0\\k29a_debug.log", $"[{DateTime.Now:HH:mm:ss.fff}] Phase1開始\n"); } catch { }

            var phase1Stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var lowResImage = await CaptureLowResolutionAsync(hwnd, options.ROIScaleFactor).ConfigureAwait(false);
            phase1Stopwatch.Stop();

            // 🔥 [PHASE_K-29-E-1] Phase 1画像サイズログ追加 - 座標ズレ仮説検証
            var lowResImageSize = lowResImage != null ? $"{lowResImage.Width}x{lowResImage.Height}" : "null";
            Console.WriteLine($"🔥🔥🔥 [{DateTime.Now:HH:mm:ss.fff}] [K-29-A_PHASE1] Phase 1完了: {phase1Stopwatch.ElapsedMilliseconds}ms, 画像={lowResImage != null}, サイズ={lowResImageSize}");
            try { System.IO.File.AppendAllText("E:\\dev\\Baketa\\Baketa.UI\\bin\\Debug\\net8.0-windows10.0.19041.0\\k29a_debug.log", $"[{DateTime.Now:HH:mm:ss.fff}] Phase1完了: {phase1Stopwatch.ElapsedMilliseconds}ms, 画像={lowResImage != null}, サイズ={lowResImageSize}\n"); } catch { }

            await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
            {
                Stage = "ROI_LowResCapture",
                IsSuccess = lowResImage != null,
                ProcessingTimeMs = phase1Stopwatch.ElapsedMilliseconds,
                ErrorMessage = lowResImage == null ? "低解像度スキャンに失敗" : null,
                SessionId = sessionId,
                Severity = lowResImage == null ? DiagnosticSeverity.Error : DiagnosticSeverity.Information,
                Metrics = new Dictionary<string, object>
                {
                    { "ROIStage", "LowResCapture" },
                    { "ROIProcessingStrategy", StrategyName },
                    { "ScaleFactor", options.ROIScaleFactor },
                    { "InputImageSize", lowResImage != null ? $"{lowResImage.Width}x{lowResImage.Height}" : "N/A" }
                }
            }).ConfigureAwait(false);

            if (lowResImage == null)
            {
                result.Success = false;
                result.ErrorMessage = "低解像度スキャンに失敗";
                return result;
            }

            // Phase 2: テキスト領域検出
            // 🔥 [PHASE_K-29-E-1] Phase 2画像サイズログ追加 - 座標ズレ仮説検証
            Console.WriteLine($"🔥🔥🔥 [{DateTime.Now:HH:mm:ss.fff}] [K-29-A_PHASE2] Phase 2開始: テキスト領域検出 - Detector={_textDetector.GetType().Name}, 入力画像サイズ={lowResImageSize}");
            try { System.IO.File.AppendAllText("E:\\dev\\Baketa\\Baketa.UI\\bin\\Debug\\net8.0-windows10.0.19041.0\\k29a_debug.log", $"[{DateTime.Now:HH:mm:ss.fff}] Phase2開始 - Detector={_textDetector.GetType().Name}, 入力サイズ={lowResImageSize}\n"); } catch { }

            var phase2Stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // 🔥 [PHASE13.2.31K-29-A] Phase 2開始ログ - 3秒遅延の原因特定
            _logger.LogInformation("🔍 [K-29-A_PHASE2_START] テキスト領域検出開始 - 画像サイズ: {Width}x{Height}, Detector: {DetectorType}",
                lowResImage.Width, lowResImage.Height, _textDetector.GetType().Name);

            // 🔥 [PHASE_K-29-B-1] Phase 2に3秒タイムアウト設定
            // ユーザー要求: メモリ/GPU負荷軽減のため、ROIBasedで高速に完了できる場合のみ使用
            // 3秒以内に完了しない場合はPrintWindowFallbackに切り替え
            IList<System.Drawing.Rectangle> textRegions;
            using var phase2Cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                var detectionTask = _textDetector.DetectTextRegionsAsync(lowResImage);
                textRegions = await detectionTask.WaitAsync(phase2Cts.Token).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                phase2Stopwatch.Stop();
                _logger.LogWarning("⏱️ [K-29-B-1] Phase 2タイムアウト（3秒超過） - PrintWindowFallbackに切り替えます, 経過時間: {ElapsedMs}ms",
                    phase2Stopwatch.ElapsedMilliseconds);

                Console.WriteLine($"⏱️ [{DateTime.Now:HH:mm:ss.fff}] [K-29-B-1] Phase 2タイムアウト（3秒超過） - {phase2Stopwatch.ElapsedMilliseconds}ms");
                try { System.IO.File.AppendAllText("E:\\dev\\Baketa\\Baketa.UI\\bin\\Debug\\net8.0-windows10.0.19041.0\\k29a_debug.log", $"[{DateTime.Now:HH:mm:ss.fff}] Phase2タイムアウト（3秒超過） - {phase2Stopwatch.ElapsedMilliseconds}ms\n"); } catch { }

                result.Success = false;
                result.ErrorMessage = "Phase 2 (テキスト領域検出) がタイムアウトしました";
                return result;
            }
            catch (OperationCanceledException)
            {
                phase2Stopwatch.Stop();
                _logger.LogWarning("🚫 [K-29-B-1] Phase 2キャンセル - 経過時間: {ElapsedMs}ms", phase2Stopwatch.ElapsedMilliseconds);

                Console.WriteLine($"🚫 [{DateTime.Now:HH:mm:ss.fff}] [K-29-B-1] Phase 2キャンセル - {phase2Stopwatch.ElapsedMilliseconds}ms");
                try { System.IO.File.AppendAllText("E:\\dev\\Baketa\\Baketa.UI\\bin\\Debug\\net8.0-windows10.0.19041.0\\k29a_debug.log", $"[{DateTime.Now:HH:mm:ss.fff}] Phase2キャンセル - {phase2Stopwatch.ElapsedMilliseconds}ms\n"); } catch { }

                result.Success = false;
                result.ErrorMessage = "Phase 2 (テキスト領域検出) がキャンセルされました";
                return result;
            }

            phase2Stopwatch.Stop();

            Console.WriteLine($"🔥🔥🔥 [{DateTime.Now:HH:mm:ss.fff}] [K-29-A_PHASE2] Phase 2完了: {phase2Stopwatch.ElapsedMilliseconds}ms, 検出数={textRegions.Count}");
            try { System.IO.File.AppendAllText("E:\\dev\\Baketa\\Baketa.UI\\bin\\Debug\\net8.0-windows10.0.19041.0\\k29a_debug.log", $"[{DateTime.Now:HH:mm:ss.fff}] Phase2完了: {phase2Stopwatch.ElapsedMilliseconds}ms, 検出数={textRegions.Count}\n"); } catch { }

            // 🔥 [PHASE13.2.31K-29-A] Phase 2完了ログ
            _logger.LogInformation("✅ [K-29-A_PHASE2_END] テキスト領域検出完了 - 検出数: {RegionCount}, 処理時間: {ElapsedMs}ms",
                textRegions.Count, phase2Stopwatch.ElapsedMilliseconds);

            // テキスト領域検出の品質評価
            var regionSizes = textRegions.Select(r => r.Width * r.Height).ToList();
            var aspectRatios = textRegions.Select(r => (double)r.Width / r.Height).ToList();

            await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
            {
                Stage = "ROI_TextDetection",
                IsSuccess = textRegions.Count > 0,
                ProcessingTimeMs = phase2Stopwatch.ElapsedMilliseconds,
                ErrorMessage = textRegions.Count == 0 ? "テキスト領域が検出されませんでした" : null,
                SessionId = sessionId,
                Severity = textRegions.Count == 0 ? DiagnosticSeverity.Warning : DiagnosticSeverity.Information,
                Metrics = new Dictionary<string, object>
                {
                    { "ROIStage", "TextDetection" },
                    { "DetectedRegionCount", textRegions.Count },
                    { "AverageRegionSize", regionSizes.Count > 0 ? regionSizes.Average() : 0 },
                    { "MinRegionSize", regionSizes.Count > 0 ? regionSizes.Min() : 0 },
                    { "MaxRegionSize", regionSizes.Count > 0 ? regionSizes.Max() : 0 },
                    { "AverageAspectRatio", aspectRatios.Count > 0 ? aspectRatios.Average() : 0 },
                    { "TextDetectorType", _textDetector.GetType().Name },
                    { "DetectionConfig", _textDetector.GetCurrentConfig()?.ToString() ?? "N/A" }
                }
            }).ConfigureAwait(false);

            result.TextRegions = textRegions;

            // Phase 3: 高解像度部分キャプチャ
            var phase3Stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // 🔥 [PHASE13.2.31K-29-A] Phase 3開始ログ
            _logger.LogInformation("🔍 [K-29-A_PHASE3_START] 高解像度部分キャプチャ開始 - 対象領域数: {RegionCount}",
                textRegions.Count);

            // 🎯 [COORDINATE_FIX] ROIスケールファクターを渡して座標変換を実施
            var highResImages = await CaptureHighResRegionsAsync(hwnd, textRegions, options.ROIScaleFactor).ConfigureAwait(false);

            phase3Stopwatch.Stop();

            // 🔥 [PHASE13.2.31K-29-A] Phase 3完了ログ
            _logger.LogInformation("✅ [K-29-A_PHASE3_END] 高解像度部分キャプチャ完了 - 成功数: {SuccessCount}/{TotalCount}, 処理時間: {ElapsedMs}ms",
                highResImages.Count, textRegions.Count, phase3Stopwatch.ElapsedMilliseconds);

            // キャプチャ結果の品質評価
            var validRegionCount = highResImages.Count;
            var regionAccuracy = textRegions.Count > 0 ? (double)validRegionCount / textRegions.Count : 0;

            await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
            {
                Stage = "ROI_HighResCapture",
                IsSuccess = highResImages.Count > 0,
                ProcessingTimeMs = phase3Stopwatch.ElapsedMilliseconds,
                ErrorMessage = highResImages.Count == 0 ? "高解像度キャプチャに失敗" : null,
                SessionId = sessionId,
                Severity = highResImages.Count == 0 ? DiagnosticSeverity.Error : DiagnosticSeverity.Information,
                Metrics = new Dictionary<string, object>
                {
                    { "ROIStage", "HighResCapture" },
                    { "ValidRegionCount", validRegionCount },
                    { "RegionAccuracy", regionAccuracy },
                    { "SuccessfulCaptureCount", highResImages.Count },
                    { "FailedCaptureCount", textRegions.Count - highResImages.Count },
                    { "CapturedImageSizes", highResImages.Select(img => $"{img.Width}x{img.Height}").ToArray() }
                }
            }).ConfigureAwait(false);
            
            result.Success = highResImages.Count > 0;
            result.Images = highResImages;
            result.Metrics.ActualCaptureTime = totalStopwatch.Elapsed;
            result.Metrics.FrameCount = highResImages.Count;
            result.Metrics.PerformanceCategory = "Balanced";

            // 全体の処理結果サマリー
            totalStopwatch.Stop();
            await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
            {
                Stage = "ROI_Complete",
                IsSuccess = result.Success,
                ProcessingTimeMs = totalStopwatch.ElapsedMilliseconds,
                ErrorMessage = !result.Success ? "ROI処理全体が失敗" : null,
                SessionId = sessionId,
                Severity = !result.Success ? DiagnosticSeverity.Error : DiagnosticSeverity.Information,
                Metrics = new Dictionary<string, object>
                {
                    { "TotalRegionsDetected", textRegions.Count },
                    { "ValidRegionsExtracted", validRegionCount },
                    { "OverallAccuracy", regionAccuracy },
                    { "Phase1TimeMs", phase1Stopwatch.ElapsedMilliseconds },
                    { "Phase2TimeMs", phase2Stopwatch.ElapsedMilliseconds },
                    { "Phase3TimeMs", phase3Stopwatch.ElapsedMilliseconds },
                    { "TotalTimeMs", totalStopwatch.ElapsedMilliseconds }
                }
            }).ConfigureAwait(false);

            _logger.LogInformation("ROIBasedキャプチャ完了: {RegionCount}個の領域, 処理時間={ProcessingTime}ms, 精度={Accuracy:F2}", 
                textRegions.Count, totalStopwatch.ElapsedMilliseconds, regionAccuracy);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ROIBasedキャプチャ中にエラー");
            
            // エラー時の診断イベント
            await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
            {
                Stage = "ROI_Error",
                IsSuccess = false,
                ProcessingTimeMs = totalStopwatch.ElapsedMilliseconds,
                ErrorMessage = ex.Message,
                SessionId = sessionId,
                Severity = DiagnosticSeverity.Error,
                Metrics = new Dictionary<string, object>
                {
                    { "ExceptionType", ex.GetType().Name },
                    { "StackTrace", ex.StackTrace ?? "N/A" }
                }
            }).ConfigureAwait(false);
            
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }
        finally
        {
            result.CompletionTime = DateTime.Now;
            result.Metrics.TotalProcessingTime = totalStopwatch.Elapsed;
            totalStopwatch.Stop();
        }

        return result;
    }

    private async Task<IWindowsImage?> CaptureLowResolutionAsync(IntPtr hwnd, float scaleFactor)
    {
        try
        {
            _logger.LogInformation("低解像度スキャン実行: スケール={ScaleFactor}, 対象ウィンドウ=0x{Hwnd:X}", 
                scaleFactor, hwnd.ToInt64());

            // 🚨 CRITICAL FIX: ネイティブラッパーの初期化状態を確認・初期化
            if (!_nativeWrapper.IsInitialized)
            {
                _logger.LogInformation("ネイティブラッパー未初期化 - 初期化実行中");
                if (!_nativeWrapper.Initialize())
                {
                    _logger.LogError("ネイティブラッパー初期化失敗");
                    return null;
                }
                _logger.LogInformation("ネイティブラッパー初期化完了");
            }

            // ネイティブラッパーでキャプチャセッション作成
            if (!_nativeWrapper.CreateCaptureSession(hwnd))
            {
                _logger.LogError("ネイティブキャプチャセッション作成失敗 - リソースクリーンアップ実行");
                // ActiveInstancesリーク防止のため明示的にDispose実行
                try { _nativeWrapper.Dispose(); } catch { /* Dispose失敗は無視 */ }
                return null;
            }

            try
            {
                // フル解像度キャプチャ
                var fullImage = await _nativeWrapper.CaptureFrameAsync(5000).ConfigureAwait(false);
                if (fullImage == null)
                {
                    _logger.LogWarning("フル解像度キャプチャに失敗");
                    return null;
                }

                // スケールダウン（リサイズ）
                var targetWidth = Math.Max(1, (int)(fullImage.Width * scaleFactor));
                var targetHeight = Math.Max(1, (int)(fullImage.Height * scaleFactor));

                var lowResImage = _imageFactory.ResizeImage(fullImage, targetWidth, targetHeight);

                _logger.LogInformation("低解像度キャプチャ完了: {OriginalSize} → {ScaledSize} (スケール: {ScaleFactor})",
                    $"{fullImage.Width}x{fullImage.Height}", $"{targetWidth}x{targetHeight}", scaleFactor);

                // フル解像度画像はリソース解放
                fullImage.Dispose();

                return lowResImage;
            }
            finally
            {
                _nativeWrapper.StopCurrentSession();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "低解像度キャプチャ中にエラー");
            throw new CaptureStrategyException(StrategyName, "低解像度キャプチャに失敗", ex);
        }
    }

    /// <summary>
    /// 🎯 [COORDINATE_FIX] 高解像度部分キャプチャ実行（座標変換対応）
    /// </summary>
    /// <param name="hwnd">ターゲットウィンドウハンドル</param>
    /// <param name="textRegions">低解像度座標系でのテキスト領域</param>
    /// <param name="roiScaleFactor">ROIスケールファクター（通常0.5）</param>
    /// <returns>高解像度部分画像リスト</returns>
    private async Task<IList<IWindowsImage>> CaptureHighResRegionsAsync(IntPtr hwnd, IList<Rectangle> textRegions, float roiScaleFactor)
    {
        var results = new List<IWindowsImage>();

        try
        {
            _logger.LogInformation("高解像度部分キャプチャ実行: {RegionCount}個の領域, 対象ウィンドウ=0x{Hwnd:X}", 
                textRegions.Count, hwnd.ToInt64());

            if (textRegions.Count == 0)
            {
                _logger.LogDebug("テキスト領域が指定されていません");
                return results;
            }

            // 🚨 CRITICAL FIX: ネイティブラッパーの初期化状態を確認・初期化
            if (!_nativeWrapper.IsInitialized)
            {
                _logger.LogInformation("ネイティブラッパー未初期化 - 高解像度キャプチャ前に初期化実行");
                if (!_nativeWrapper.Initialize())
                {
                    _logger.LogError("ネイティブラッパー初期化失敗 - 高解像度キャプチャ中止");
                    return results;
                }
                _logger.LogInformation("ネイティブラッパー初期化完了 - 高解像度キャプチャ継続");
            }

            // ネイティブラッパーでキャプチャセッション作成
            if (!_nativeWrapper.CreateCaptureSession(hwnd))
            {
                _logger.LogError("高解像度キャプチャセッション作成失敗 - リソースクリーンアップ実行");
                // ActiveInstancesリーク防止のため明示的にDispose実行
                try { _nativeWrapper.Dispose(); } catch { /* Dispose失敗は無視 */ }
                return results;
            }

            try
            {
                // 高解像度全体キャプチャ
                var fullImage = await _nativeWrapper.CaptureFrameAsync(5000).ConfigureAwait(false);
                if (fullImage == null)
                {
                    _logger.LogWarning("高解像度フル画像キャプチャに失敗");
                    return results;
                }

                try
                {
                    // 並列処理でROI領域を切り出し（スレッドセーフなConcurrentBag使用）
                    var cropTasks = textRegions.Select(async region =>
                    {
                        try
                        {
                            // 🎯 [COORDINATE_TRANSFORM] 低解像度座標を高解像度座標に変換
                            var inverseScale = 1.0f / roiScaleFactor;
                            var highResRegion = new Rectangle(
                                (int)(region.X * inverseScale),
                                (int)(region.Y * inverseScale),
                                (int)(region.Width * inverseScale),
                                (int)(region.Height * inverseScale)
                            );
                            
                            _logger.LogDebug("🎯 [COORD_TRANSFORM] 座標変換: 低解像度{LowRes} → 高解像度{HighRes} (スケール:{Scale})",
                                region, highResRegion, inverseScale);
                            
                            // 境界チェック（高解像度座標でチェック）
                            if (highResRegion.X < 0 || highResRegion.Y < 0 ||
                                highResRegion.Right > fullImage.Width || highResRegion.Bottom > fullImage.Height ||
                                highResRegion.Width <= 0 || highResRegion.Height <= 0)
                            {
                                _logger.LogWarning("無効な領域をスキップ: 変換後{HighResRegion}, 画像サイズ: {ImageSize}",
                                    highResRegion, $"{fullImage.Width}x{fullImage.Height}");
                                return null;
                            }

                            // 並列実行での画像切り出し（CPU集約的処理）
                            return await Task.Run(() =>
                            {
                                // 🎯 [HIGH_RES_CROP] 高解像度座標でクロップ実行
                                var croppedImage = _imageFactory.CropImage(fullImage, highResRegion);
                                if (croppedImage != null)
                                {
                                    _logger.LogDebug("🎯 [CROP_SUCCESS] 領域キャプチャ完了: 低解像度{LowRes} → 高解像度{HighRes} → クロップ{Size}",
                                        region, highResRegion, $"{croppedImage.Width}x{croppedImage.Height}");
                                }
                                else
                                {
                                    _logger.LogWarning("🚫 [CROP_FAILED] クロップ失敗: 低解像度{LowRes} → 高解像度{HighRes}",
                                        region, highResRegion);
                                }
                                return croppedImage;
                            }).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "領域 {Region} のキャプチャに失敗", region);
                            return null;
                        }
                    });

                    // 全ての並列タスクを待機
                    var croppedImages = await Task.WhenAll(cropTasks).ConfigureAwait(false);
                    
                    // 成功した画像のみをresultsに追加
                    foreach (var image in croppedImages)
                    {
                        if (image != null)
                        {
                            results.Add(image);
                        }
                    }

                    _logger.LogInformation("高解像度部分キャプチャ完了: {SuccessCount}/{TotalCount}個の領域を並列処理",
                        results.Count, textRegions.Count);
                }
                finally
                {
                    fullImage.Dispose();
                }
            }
            finally
            {
                _nativeWrapper.StopCurrentSession();
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "高解像度部分キャプチャ中にエラー");
            
            // エラー時はリソースクリーンアップ
            foreach (var image in results)
            {
                try { image.Dispose(); } catch { /* ignore cleanup errors */ }
            }
            results.Clear();
            
            throw new CaptureStrategyException(StrategyName, "部分キャプチャに失敗", ex);
        }
    }

    // Windows API P/Invoke
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);
}