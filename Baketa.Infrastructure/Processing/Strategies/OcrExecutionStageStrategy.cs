using Baketa.Core.Abstractions.Processing;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Capture; // 🎯 UltraThink: ITextRegionDetector用
using Baketa.Core.Abstractions.Platform.Windows; // 🎯 UltraThink: IWindowsImage用
using Baketa.Core.Abstractions.Memory; // 🎯 UltraThink Phase 75: SafeImage統合
using Baketa.Core.Abstractions.Factories; // 🎯 UltraThink Phase 76: IImageFactory for SafeImage→IImage変換
using Baketa.Core.Models.Processing;
using Baketa.Core.Models.OCR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Linq;
using Rectangle = System.Drawing.Rectangle; // 🎯 UltraThink Phase 75: 名前空間競合回避

namespace Baketa.Infrastructure.Processing.Strategies;

/// <summary>
/// OCR実行段階の処理戦略
/// 既存のOCR処理システムとの統合
/// 🎯 UltraThink Phase 50: ROI検出統合による翻訳表示復旧
/// </summary>
public class OcrExecutionStageStrategy : IProcessingStageStrategy
{
    private readonly ILogger<OcrExecutionStageStrategy> _logger;
    private readonly Baketa.Core.Abstractions.OCR.IOcrEngine _ocrEngine;
    private readonly ITextRegionDetector? _textRegionDetector; // 🎯 UltraThink: ROI検出器統合
    private readonly IImageLifecycleManager _imageLifecycleManager; // 🎯 UltraThink Phase 75: 安全な画像管理
    private readonly IImageFactory _imageFactory; // 🎯 UltraThink Phase 76: SafeImage→IImage変換用
    
    public ProcessingStageType StageType => ProcessingStageType.OcrExecution;
    public TimeSpan EstimatedProcessingTime => TimeSpan.FromMilliseconds(80);

    public OcrExecutionStageStrategy(
        ILogger<OcrExecutionStageStrategy> logger,
        Baketa.Core.Abstractions.OCR.IOcrEngine ocrEngine,
        IImageLifecycleManager imageLifecycleManager, // 🎯 UltraThink Phase 75: 必須依存関係として追加
        IImageFactory imageFactory, // 🎯 UltraThink Phase 76: SafeImage→IImage変換用
        ITextRegionDetector? textRegionDetector = null) // 🎯 UltraThink: ROI検出器をオプション依存で追加
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _ocrEngine = ocrEngine ?? throw new ArgumentNullException(nameof(ocrEngine));
        _imageLifecycleManager = imageLifecycleManager ?? throw new ArgumentNullException(nameof(imageLifecycleManager));
        _imageFactory = imageFactory ?? throw new ArgumentNullException(nameof(imageFactory));
        _textRegionDetector = textRegionDetector; // null許容（フォールバック対応）
    }

    public async Task<ProcessingStageResult> ExecuteAsync(ProcessingContext context, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // 🎯 UltraThink Phase 61.26: ExecuteAsyncメソッド到達確認ログ
            _logger.LogDebug("🎯 [OCR_EXECUTION_DEBUG] ExecuteAsyncメソッド開始 - ContextId: {ContextId}", context.Input.ContextId);

            _logger.LogDebug("OCR実行段階開始 - ContextId: {ContextId}", context.Input.ContextId);

            // 🎯 UltraThink Phase 75: SafeImage パターンによる安全な画像処理
            _logger.LogDebug("🎯 [OCR_EXECUTION_DEBUG] SafeImage変換開始");

            // 🔧 UltraThink Phase 75: IImage → SafeImage 変換でObjectDisposedException根絶
            if (context.Input.CapturedImage == null)
            {
                var error = "キャプチャ画像がnullです";
                _logger.LogError(error);
                return ProcessingStageResult.CreateError(StageType, error, stopwatch.Elapsed);
            }

            SafeImage safeImage;
            try
            {
                // 🎯 UltraThink Phase 76: IImageからSafeImageへの完全な安全変換実装
                _logger.LogDebug("🎯 [OCR_EXECUTION_DEBUG] IImage→SafeImage変換実行");

                // 🔧 Phase 76: IImageから安全にデータ取得
                int width, height;
                byte[] imageData;

                try
                {
                    // IImageプロパティとデータの安全な取得（ObjectDisposedException対策）
                    _logger.LogDebug("🎯 [OCR_EXECUTION_DEBUG] 画像状態確認開始");

                    width = context.Input.CapturedImage.Width;
                    height = context.Input.CapturedImage.Height;

                    _logger.LogDebug("🎯 [OCR_EXECUTION_DEBUG] 画像Width/Heightアクセス完了");
                }
                catch (ObjectDisposedException ex)
                {
                    var error = "🚨 [OCR_EXECUTION_DEBUG] ObjectDisposedException発生: キャプチャ画像が既に破棄されています";
                    _logger.LogError(ex, error);
                    Console.WriteLine($"🚨 [OCR_EXECUTION_DEBUG] ObjectDisposedException: {error} - {ex.Message}");
                    return ProcessingStageResult.CreateError(StageType, error, stopwatch.Elapsed);
                }

                // 🔧 画像データの安全な取得
                try
                {
                    imageData = await context.Input.CapturedImage.ToByteArrayAsync().ConfigureAwait(false);
                    _logger.LogDebug("🎯 [SAFE_IMAGE] 画像データ取得完了 - サイズ: {Width}x{Height}, データサイズ: {DataSize}bytes", width, height, imageData.Length);
                }
                catch (ObjectDisposedException ex)
                {
                    var error = "画像データ取得中にObjectDisposedExceptionが発生しました";
                    _logger.LogError(ex, error);
                    return ProcessingStageResult.CreateError(StageType, error, stopwatch.Elapsed);
                }

                // 🎯 SafeImage作成（IImageLifecycleManager使用）
                safeImage = await _imageLifecycleManager.CreateSafeImageAsync(
                    imageData.AsMemory(),
                    width,
                    height,
                    ImagePixelFormat.Bgra32, // Windows標準フォーマット
                    cancellationToken).ConfigureAwait(false);

                _logger.LogInformation("🎯 [SAFE_IMAGE] SafeImage作成完了 - サイズ: {Width}x{Height}", width, height);
                Console.WriteLine($"🎯 [SAFE_IMAGE] SafeImage作成完了 - サイズ: {width}x{height}");

                // 🔧 Phase 76: SafeImageからOCR用IImageへの安全な変換
                _logger.LogInformation("🎯 [SAFE_IMAGE] OCR用IImage変換開始");
                Console.WriteLine("🎯 [SAFE_IMAGE] OCR用IImage変換開始");
            }
            catch (ObjectDisposedException ex)
            {
                var error = "🚨 [SAFE_IMAGE] IImage変換中にObjectDisposedException発生 - 画像が破棄済み";
                _logger.LogError(ex, error);
                Console.WriteLine($"🚨 [SAFE_IMAGE] ObjectDisposedException: {error}");
                return ProcessingStageResult.CreateError(StageType, error, stopwatch.Elapsed);
            }
            catch (Exception ex)
            {
                var error = "SafeImage変換中に予期しないエラーが発生しました";
                _logger.LogError(ex, error);
                return ProcessingStageResult.CreateError(StageType, $"{error}: {ex.Message}", stopwatch.Elapsed);
            }
            
            // 🔧 Phase 76: SafeImageからIImageへの変換実行（OCRエンジン用）
            Baketa.Core.Abstractions.Imaging.IImage ocrImage;
            try
            {
                var safeImageData = safeImage.GetImageData().ToArray();
                ocrImage = await _imageFactory.CreateFromBytesAsync(safeImageData).ConfigureAwait(false);
                _logger.LogInformation("🎯 [SAFE_IMAGE] OCR用IImage変換完了 - サイズ: {Width}x{Height}", ocrImage.Width, ocrImage.Height);
                Console.WriteLine($"🎯 [SAFE_IMAGE] OCR用IImage変換完了 - サイズ: {ocrImage.Width}x{ocrImage.Height}");
            }
            catch (Exception ex)
            {
                var error = "SafeImageからOCR用IImageへの変換に失敗しました";
                _logger.LogError(ex, error);
                return ProcessingStageResult.CreateError(StageType, $"{error}: {ex.Message}", stopwatch.Elapsed);
            }

            // 🎯 UltraThink Phase 50.1: ROI検出統合によるテキスト領域特定処理
            // Note: ここではocrImageを使用してROI検出を実行
            IList<Rectangle>? detectedRegions = null;
            if (_textRegionDetector != null)
            {
                try
                {
                    _logger.LogDebug("🎯 UltraThink: ROI検出開始 - テキスト領域を事前検出");

                    // OCR用IImageをIWindowsImageへ変換が必要な場合の処理
                    if (ocrImage is IWindowsImage windowsImage)
                    {
                        detectedRegions = await _textRegionDetector.DetectTextRegionsAsync(windowsImage).ConfigureAwait(false);
                        _logger.LogInformation("🎯 UltraThink: ROI検出完了 - 検出領域数: {RegionCount}", detectedRegions.Count);
                    }
                    else
                    {
                        _logger.LogWarning("🎯 UltraThink: IImage→IWindowsImage変換が必要 - ROI検出をスキップ");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "🎯 UltraThink: ROI検出でエラー - 全画面OCRにフォールバック");
                    detectedRegions = null; // フォールバック処理へ
                }
            }
            else
            {
                _logger.LogDebug("🎯 UltraThink: ITextRegionDetectorが未注入 - 全画面OCR実行");
            }
            
            // 実際のOCRサービス統合
            string detectedText;
            List<object> textChunks = [];
            
            // 🔧 UltraThink Phase 28: OCRエンジン内部での非同期画像アクセス時のObjectDisposedException対応
            OcrResults ocrResults;
            try
            {
                // 🔧 Phase 76: SafeImageとCaptureRegionの包括的検証（ObjectDisposedException安全）
                try
                {
                    // SafeImage/ocrImageのサイズを使用（破棄済みの元画像にアクセスしない）
                    var testWidth = ocrImage.Width; // SafeImage経由で安全
                    var testHeight = ocrImage.Height; // SafeImage経由で安全
                    var testRegion = context.Input.CaptureRegion;
                    
                    // 🎯 UltraThink Phase 35: Empty span防止のため画像サイズ検証
                    if (testWidth <= 0 || testHeight <= 0)
                    {
                        var error = $"無効な画像サイズ検出: {testWidth}x{testHeight}";
                        _logger.LogError(error);
                        return ProcessingStageResult.CreateError(StageType, error, stopwatch.Elapsed);
                    }
                    
                    // 🎯 UltraThink Phase 36: OCRに適さない極小画像を除外
                    const int MinimumOcrImageSize = 50; // 50x50ピクセル未満はOCR不適
                    if (testWidth < MinimumOcrImageSize || testHeight < MinimumOcrImageSize)
                    {
                        var error = $"🎯 UltraThink Phase 36: OCRに適さない極小画像サイズ: {testWidth}x{testHeight} (最小要件: {MinimumOcrImageSize}x{MinimumOcrImageSize})";
                        _logger.LogWarning(error);
                        return ProcessingStageResult.CreateError(StageType, error, stopwatch.Elapsed);
                    }

                    // 🎯 UltraThink Phase 35: CaptureRegionの妥当性検証
                    if (testRegion != Rectangle.Empty)
                    {
                        if (testRegion.Width <= 0 || testRegion.Height <= 0 || 
                            testRegion.X < 0 || testRegion.Y < 0 ||
                            testRegion.X + testRegion.Width > testWidth ||
                            testRegion.Y + testRegion.Height > testHeight)
                        {
                            var error = $"無効なキャプチャ領域: ({testRegion.X},{testRegion.Y},{testRegion.Width},{testRegion.Height}) vs 画像: {testWidth}x{testHeight}";
                            _logger.LogError(error);
                            return ProcessingStageResult.CreateError(StageType, error, stopwatch.Elapsed);
                        }
                    }
                    
                    _logger.LogDebug("OCR前画像状態確認OK - サイズ: {Width}x{Height}, 領域: ({X},{Y},{Width},{Height})", 
                        testWidth, testHeight, testRegion.X, testRegion.Y, testRegion.Width, testRegion.Height);
                }
                catch (ObjectDisposedException ex)
                {
                    var error = "OCR処理前の画像状態確認で予期しないObjectDisposedExceptionが発生";
                    _logger.LogError(ex, error);
                    // Note: この時点でSafeImage経由のアクセスのため、本来は発生しないはず
                    return ProcessingStageResult.CreateError(StageType, $"{error}: {ex.Message}", stopwatch.Elapsed);
                }
                
                // 🎯 UltraThink Phase 50.2: ROI検出結果に基づくOCR実行戦略
                if (detectedRegions?.Count > 0)
                {
                    _logger.LogInformation("🎯 UltraThink: {RegionCount}個の検出領域でROI指定OCR実行", detectedRegions.Count);
                    
                    var allTextResults = new List<string>();
                    var allTextChunks = new List<object>();
                    
                    // 各検出領域に対してOCR実行
                    foreach (var region in detectedRegions)
                    {
                        try
                        {
                            _logger.LogDebug("🎯 UltraThink: 領域指定OCR実行 - ({X},{Y},{Width},{Height})", 
                                region.X, region.Y, region.Width, region.Height);
                            
                            var regionOcrResults = await _ocrEngine.RecognizeAsync(
                                ocrImage, // 🔧 Phase 76: SafeImage経由の安全な画像使用
                                region,
                                cancellationToken: cancellationToken).ConfigureAwait(false);
                            
                            if (regionOcrResults?.TextRegions?.Count > 0)
                            {
                                var regionText = string.Join(" ", regionOcrResults.TextRegions.Select(r => r.Text));
                                if (!string.IsNullOrWhiteSpace(regionText))
                                {
                                    allTextResults.Add(regionText);
                                    allTextChunks.AddRange(regionOcrResults.TextRegions.Cast<object>());
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "🎯 UltraThink: 領域({X},{Y},{Width},{Height})のOCR処理でエラー - スキップ", 
                                region.X, region.Y, region.Width, region.Height);
                        }
                    }
                    
                    // 結果統合
                    detectedText = string.Join(" ", allTextResults);
                    textChunks = allTextChunks;
                    
                    _logger.LogInformation("🎯 UltraThink: ROI指定OCR完了 - 総テキスト長: {TextLength}", detectedText.Length);
                }
                else
                {
                    // 🎯 UltraThink Phase 50.3: フォールバック - 従来の全画面OCR実行
                    _logger.LogDebug("🎯 UltraThink: ROI検出結果なし - 全画面OCR実行");
                    
                    // 🎯 UltraThink Phase 35: OCR呼び出し前の最終検証
                    // 🔧 Phase 76: SafeImage経由の安全な画像状態確認（ObjectDisposedException回避）
                    try
                    {
                        // ocrImage（SafeImage経由）の有効性確認
                        _ = ocrImage.Width; // SafeImage経由で安全
                        _ = ocrImage.Height; // SafeImage経由で安全
                        _logger.LogDebug("🎯 [SAFE_IMAGE] OCR実行直前の画像状態確認成功 - サイズ: {Width}x{Height}", ocrImage.Width, ocrImage.Height);
                    }
                    catch (ObjectDisposedException ex)
                    {
                        var error = "OCR実行直前に予期しない画像破棄を検出（SafeImage経由であるため異常）";
                        _logger.LogError(ex, error);
                        return ProcessingStageResult.CreateError(StageType, $"{error}: {ex.Message}", stopwatch.Elapsed);
                    }

                    if (context.Input.CaptureRegion != Rectangle.Empty)
                    {
                        // 特定領域でのOCR処理（SafeImage経由で安全）
                        _logger.LogDebug("🔧 Phase 76: 領域指定OCR実行 - ({X},{Y},{Width},{Height})",
                            context.Input.CaptureRegion.X, context.Input.CaptureRegion.Y,
                            context.Input.CaptureRegion.Width, context.Input.CaptureRegion.Height);
                        ocrResults = await _ocrEngine.RecognizeAsync(
                            ocrImage, // 🔧 Phase 76: SafeImage経由の安全な画像
                            context.Input.CaptureRegion,
                            cancellationToken: cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        // 全体画像でのOCR処理（SafeImage経由で安全）
                        _logger.LogDebug("🔧 Phase 76: 全体画像OCR実行 - {Width}x{Height}",
                            ocrImage.Width, ocrImage.Height); // SafeImage経由で安全
                        ocrResults = await _ocrEngine.RecognizeAsync(
                            ocrImage, // 🔧 Phase 76: SafeImage経由の安全な画像
                            cancellationToken: cancellationToken).ConfigureAwait(false);
                    }
                    
                    // OCR結果から文字列とチャンクを取得
                    detectedText = string.Join(" ", ocrResults.TextRegions.Select(r => r.Text));
                    textChunks = ocrResults.TextRegions.Cast<object>().ToList();
                }
            }
            catch (ObjectDisposedException ex)
            {
                var error = $"OCR処理中に画像が破棄されました: {ex.Message}";
                _logger.LogError(ex, error);
                return ProcessingStageResult.CreateError(StageType, error, stopwatch.Elapsed);
            }
            catch (ArgumentException ex) when (ex.Message.Contains("Empty span"))
            {
                var error = $"🎯 UltraThink Phase 35: OCR処理でEmpty span例外: {ex.Message}";
                _logger.LogError(ex, error);
                return ProcessingStageResult.CreateError(StageType, error, stopwatch.Elapsed);
            }
            catch (Exception ex) when (ex.Message.Contains("Empty span") || ex.Message.Contains("span"))
            {
                var error = $"🎯 UltraThink Phase 35: OCR処理でspan関連例外: {ex.Message}";
                _logger.LogError(ex, error);
                return ProcessingStageResult.CreateError(StageType, error, stopwatch.Elapsed);
            }
            
            // 🎯 UltraThink: 結果統合は上記のROI処理またはフォールバック処理内で完了済み
            
            var result = new OcrExecutionResult
            {
                DetectedText = detectedText ?? "",
                TextChunks = textChunks,
                ProcessingTime = stopwatch.Elapsed,
                Success = !string.IsNullOrEmpty(detectedText),
                ErrorMessage = string.IsNullOrEmpty(detectedText) ? "OCRでテキストが検出されませんでした" : null
            };
            
            _logger.LogDebug("OCR実行段階完了 - テキスト長: {TextLength}, 処理時間: {ProcessingTime}ms",
                result.DetectedText.Length, stopwatch.Elapsed.TotalMilliseconds);
            
            return ProcessingStageResult.CreateSuccess(StageType, result);
        }
        catch (Exception ex)
        {
            // 🎯 UltraThink Phase 61.24: 詳細なエラー情報をログ出力
            _logger.LogError(ex, "OCR実行段階でエラーが発生 - 例外種別: {ExceptionType}, メッセージ: {Message}, スタックトレース: {StackTrace}",
                ex.GetType().Name, ex.Message, ex.StackTrace);
            return ProcessingStageResult.CreateError(StageType, ex.Message, stopwatch.Elapsed);
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    public bool ShouldExecute(ProcessingContext context)
    {
        // 🎯 UltraThink Phase 61.25: OCR段階スキップ原因調査のためのデバッグログ追加
        _logger.LogDebug("🎯 [OCR_SKIP_DEBUG] ShouldExecute呼び出し - PreviousStageResult: {HasPrevious}, Success: {Success}",
            context.PreviousStageResult != null, context.PreviousStageResult?.Success);

        // Stage 1で画像変化が検知された場合のみ実行
        if (context.PreviousStageResult?.Success == true &&
            context.PreviousStageResult.Data is ImageChangeDetectionResult imageChange)
        {
            _logger.LogDebug("🎯 [OCR_SKIP_DEBUG] ImageChangeDetection結果: HasChanged={HasChanged}, ChangePercentage={ChangePercentage}",
                imageChange.HasChanged, imageChange.ChangePercentage);
            return imageChange.HasChanged;
        }

        // Stage 1が実行されていない場合は実行する
        var hasImageChangeResult = context.HasStageResult(ProcessingStageType.ImageChangeDetection);
        _logger.LogDebug("🎯 [OCR_SKIP_DEBUG] ImageChangeDetectionStage存在: {HasResult}, 実行判定: {WillExecute}",
            hasImageChangeResult, !hasImageChangeResult);
        return !hasImageChangeResult;
    }

}