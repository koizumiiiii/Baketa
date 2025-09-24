using Baketa.Core.Abstractions.Processing;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Capture; // 🎯 UltraThink: ITextRegionDetector用
using Baketa.Core.Abstractions.Platform.Windows; // 🎯 UltraThink: IWindowsImage用
using Baketa.Core.Abstractions.Memory; // 🎯 UltraThink Phase 75: SafeImage統合
using Baketa.Core.Abstractions.Factories; // 🎯 UltraThink Phase 76: IImageFactory for SafeImage→IImage変換
using Baketa.Core.Abstractions.Imaging; // 🔧 [PHASE3.2_FIX] IImage用
using Baketa.Core.Models.Processing;
using Baketa.Core.Models.OCR;
using Baketa.Core.Utilities; // 🎯 [OCR_DEBUG_LOG] DebugLogUtility用
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Drawing; // 🎯 UltraThink Phase 77.6: Bitmap用 + ROI_IMAGE_SAVE Graphics, Pen, Color等用
using System.Drawing.Imaging; // 🎯 [ROI_IMAGE_SAVE] ImageFormat用
using System.IO; // 🎯 [ROI_IMAGE_SAVE] Directory, Path用
using System.Linq;
using Rectangle = System.Drawing.Rectangle; // 🎯 UltraThink Phase 75: 名前空間競合回避
using IImageFactoryInterface = Baketa.Core.Abstractions.Factories.IImageFactory; // 🔧 [PHASE3.2_FIX] 名前空間競合回避

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
    private readonly IImageFactoryInterface _imageFactory; // 🎯 UltraThink Phase 76: SafeImage→IImage変換用
    
    public ProcessingStageType StageType => ProcessingStageType.OcrExecution;
    public TimeSpan EstimatedProcessingTime => TimeSpan.FromMilliseconds(80);

    public OcrExecutionStageStrategy(
        ILogger<OcrExecutionStageStrategy> logger,
        Baketa.Core.Abstractions.OCR.IOcrEngine ocrEngine,
        IImageLifecycleManager imageLifecycleManager, // 🎯 UltraThink Phase 75: 必須依存関係として追加
        IImageFactoryInterface imageFactory, // 🎯 UltraThink Phase 76: SafeImage→IImage変換用
        ITextRegionDetector? textRegionDetector = null) // 🎯 UltraThink: ROI検出器をオプション依存で追加
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _ocrEngine = ocrEngine ?? throw new ArgumentNullException(nameof(ocrEngine));
        _imageLifecycleManager = imageLifecycleManager ?? throw new ArgumentNullException(nameof(imageLifecycleManager));
        _imageFactory = imageFactory ?? throw new ArgumentNullException(nameof(imageFactory));
        _textRegionDetector = textRegionDetector; // null許容（フォールバック対応）
    }

    public async Task<ProcessingStageResult> ExecuteAsync(ProcessingContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Input);
        ArgumentNullException.ThrowIfNull(context.Input.CapturedImage);

        var stopwatch = Stopwatch.StartNew();
        const string OriginalRequestId = "OCR処理";

        _logger.LogInformation("🔍 OCR実行段階開始 - 画像サイズ: {Width}x{Height}", 
            context.Input.CapturedImage.Width, context.Input.CapturedImage.Height);

        try
        {
            // 🔧 [PHASE3.3_FIX] 防御的画像検証強化でObjectDisposedException完全回避
            IImage ocrImage;

            // 🎯 入力画像の事前検証
            try
            {
                var inputImage = context.Input.CapturedImage;
                if (inputImage == null)
                {
                    var error = "🔧 [PHASE3.3_FIX] 入力画像がnullです";
                    _logger.LogError(error);
                    return ProcessingStageResult.CreateError(StageType, error, stopwatch.Elapsed);
                }

                // 🔧 [PHASE3.4_FIX] 防御的画像情報アクセス - レースコンディション解決
                int testWidth = 0, testHeight = 0;
                try
                {
                    // 防御的プロパティアクセス - ObjectDisposedException発生時即座にエラー処理
                    testWidth = inputImage.Width;
                    testHeight = inputImage.Height;
                    _logger.LogDebug("🔧 [PHASE3.4_FIX] 画像基本情報確認成功 - サイズ: {Width}x{Height}", testWidth, testHeight);
                }
                catch (ObjectDisposedException ex)
                {
                    var error = $"🔧 [PHASE3.4_FIX] 画像プロパティアクセス中ObjectDisposedException - 画像が破棄済み: {ex.Message}";
                    _logger.LogError(ex, error);
                    return ProcessingStageResult.CreateError(StageType, error, stopwatch.Elapsed);
                }

                if (inputImage is ReferencedSafeImage referencedSafeImage)
                {
                    // 🔧 [PHASE3.4_FIX] ReferencedSafeImage参照カウント防御的検証
                    int refCount = 0;
                    try
                    {
                        refCount = referencedSafeImage.ReferenceCount;
                        if (refCount <= 0)
                        {
                            var error = $"🔧 [PHASE3.4_FIX] ReferencedSafeImage参照カウントが無効: {refCount}";
                            _logger.LogError(error);
                            return ProcessingStageResult.CreateError(StageType, error, stopwatch.Elapsed);
                        }
                    }
                    catch (ObjectDisposedException ex)
                    {
                        var error = $"🔧 [PHASE3.4_FIX] 参照カウントアクセス中ObjectDisposedException: {ex.Message}";
                        _logger.LogError(ex, error);
                        return ProcessingStageResult.CreateError(StageType, error, stopwatch.Elapsed);
                    }

                    // 🎯 SafeImage本体の有効性確認
                    try
                    {
                        var safeImage = referencedSafeImage.GetUnderlyingSafeImage();
                        if (safeImage == null || safeImage.IsDisposed)
                        {
                            var error = "🔧 [PHASE3.3_FIX] SafeImage本体が破棄済みまたはnull";
                            _logger.LogError(error);
                            return ProcessingStageResult.CreateError(StageType, error, stopwatch.Elapsed);
                        }
                    }
                    catch (ObjectDisposedException ex)
                    {
                        var error = $"🔧 [PHASE3.3_FIX] SafeImage本体アクセス時ObjectDisposedException: {ex.Message}";
                        _logger.LogError(ex, error);
                        return ProcessingStageResult.CreateError(StageType, error, stopwatch.Elapsed);
                    }

                    // ✅ ReferencedSafeImageを直接使用
                    ocrImage = inputImage;
                    _logger.LogInformation("🔧 [PHASE3.4_FIX] ReferencedSafeImage検証済み使用 - サイズ: {Width}x{Height}, 参照カウント: {RefCount}",
                        testWidth, testHeight, refCount);
                    _logger.LogDebug("🔧 [PHASE3.4_FIX] ReferencedSafeImage検証済み - サイズ: {Width}x{Height}, RefCount: {RefCount}",
                        testWidth, testHeight, refCount);
                }
                else
                {
                    // ✅ 従来のIImage処理
                    ocrImage = inputImage;
                    _logger.LogInformation("🔧 [PHASE3.4_FIX] 従来IImage検証済み使用 - サイズ: {Width}x{Height}",
                        testWidth, testHeight);
                    _logger.LogDebug("🔧 [PHASE3.4_FIX] 従来IImage検証済み - サイズ: {Width}x{Height}",
                        testWidth, testHeight);
                }
            }
            catch (ObjectDisposedException ex)
            {
                var error = $"🔧 [PHASE3.4_FIX] 画像事前検証でObjectDisposedException: {ex.Message}";
                _logger.LogError(ex, error);
                return ProcessingStageResult.CreateError(StageType, error, stopwatch.Elapsed);
            }
            catch (Exception ex)
            {
                var error = $"🔧 [PHASE3.4_FIX] 画像事前検証で予期しないエラー: {ex.Message}";
                _logger.LogError(ex, error);
                return ProcessingStageResult.CreateError(StageType, error, stopwatch.Elapsed);
            }

            // 🎯 UltraThink Phase 50.1: ROI検出統合による テキスト領域特定処理
            // Note: ここではocrImageを使用してROI検出を実行
            IList<Rectangle>? detectedRegions = null;
            if (_textRegionDetector != null)
            {
                try
                {
                    _logger.LogDebug("🎯 UltraThink: ROI検出開始 - テキスト領域を事前検出");

                    // 🎯 UltraThink Phase 77.6: IImage → IWindowsImage アダプター変換でROI検出器動作
                    IWindowsImage windowsImage;
                    bool needsDisposal = false;

                    if (ocrImage is IWindowsImage directWindowsImage)
                    {
                        // 既に IWindowsImage の場合は直接使用
                        windowsImage = directWindowsImage;
                        _logger.LogDebug("🎯 [PHASE77.6] 既存 IWindowsImage を直接使用");
                    }
                    else
                    {
                        // IImage → IWindowsImage インライン アダプター変換
                        _logger.LogDebug("🎯 [PHASE77.6] IImage → IWindowsImage インラインアダプター変換開始 - Type: {ImageType}", ocrImage.GetType().Name);

                        windowsImage = new InlineImageToWindowsImageAdapter(ocrImage, _logger);
                        needsDisposal = true; // アダプターは後でDispose必要

                        _logger.LogInformation("✅ [PHASE77.6] IWindowsImageインラインアダプター作成完了 - Size: {Width}x{Height}", windowsImage.Width, windowsImage.Height);
                    }

                    try
                    {
                        // TextRegionDetectorAdapter による高精度 ROI 検出実行
                        detectedRegions = await _textRegionDetector.DetectTextRegionsAsync(windowsImage).ConfigureAwait(false);
                        _logger.LogInformation("🎯 UltraThink: ROI検出完了 - 検出領域数: {RegionCount}", detectedRegions.Count);
                    }
                    finally
                    {
                        // アダプターが作成された場合のリソース解放
                        if (needsDisposal && windowsImage is IDisposable disposableAdapter)
                        {
                            disposableAdapter.Dispose();
                            _logger.LogDebug("🎯 [PHASE77.6] InlineImageToWindowsImageAdapter リソース解放完了");
                        }
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
            
            // 🔧 [PHASE3.2_FIX] OCRエンジン内部での非同期画像アクセス時のObjectDisposedException対応
            OcrResults ocrResults;
            try
            {
                // 🔧 [PHASE3.2_FIX] 画像状態検証の簡素化（ObjectDisposedException回避）
                try
                {
                    // 最低限の画像状態確認のみ実行
                    var testWidth = ocrImage.Width;
                    var testHeight = ocrImage.Height;
                    
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

                    _logger.LogDebug("🔧 [PHASE3.2_FIX] 画像状態確認OK - サイズ: {Width}x{Height}", testWidth, testHeight);
                }
                catch (ObjectDisposedException ex)
                {
                    var error = "🔧 [PHASE3.2_FIX] 画像アクセス時ObjectDisposedException - 画像が既に破棄済み";
                    _logger.LogError(ex, error);
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

                            // 🎯 [OCR_DEBUG_LOG] ROI領域情報をデバッグログに出力
                            DebugLogUtility.WriteLog($"🔍 [ROI_OCR] 領域OCR開始 - 座標=({region.X},{region.Y}), サイズ=({region.Width}x{region.Height})");

                            var regionOcrResults = await _ocrEngine.RecognizeAsync(
                                ocrImage, // 🔧 [PHASE3.2_FIX] 直接画像使用でObjectDisposedException回避
                                region,
                                cancellationToken: cancellationToken).ConfigureAwait(false);

                            if (regionOcrResults?.TextRegions?.Count > 0)
                            {
                                var regionText = string.Join(" ", regionOcrResults.TextRegions.Select(r => r.Text));
                                if (!string.IsNullOrWhiteSpace(regionText))
                                {
                                    allTextResults.Add(regionText);
                                    allTextChunks.AddRange(regionOcrResults.TextRegions.Cast<object>());

                                    // 🎯 [OCR_DEBUG_LOG] 領域OCR結果をデバッグログに出力
                                    DebugLogUtility.WriteLog($"🔍 [ROI_OCR] 領域OCR成功 - テキスト='{regionText}', チャンク数={regionOcrResults.TextRegions.Count}");
                                }
                                else
                                {
                                    DebugLogUtility.WriteLog($"🔍 [ROI_OCR] 領域OCR結果 - 空文字列");
                                }
                            }
                            else
                            {
                                DebugLogUtility.WriteLog($"🔍 [ROI_OCR] 領域OCR結果 - テキスト領域なし");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "🎯 UltraThink: 領域({X},{Y},{Width},{Height})のOCR処理でエラー - スキップ",
                                region.X, region.Y, region.Width, region.Height);
                            DebugLogUtility.WriteLog($"🔍 [ROI_OCR] 領域OCRエラー - 座標=({region.X},{region.Y}), エラー={ex.Message}");
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
                    
                    if (context.Input.CaptureRegion != Rectangle.Empty)
                    {
                        // 特定領域でのOCR処理
                        _logger.LogDebug("🔧 [PHASE3.2_FIX] 領域指定OCR実行 - ({X},{Y},{Width},{Height})",
                            context.Input.CaptureRegion.X, context.Input.CaptureRegion.Y,
                            context.Input.CaptureRegion.Width, context.Input.CaptureRegion.Height);

                        // 🎯 [OCR_DEBUG_LOG] 領域指定OCR実行をデバッグログに出力
                        DebugLogUtility.WriteLog($"🔍 [REGION_OCR] 領域指定OCR開始 - 座標=({context.Input.CaptureRegion.X},{context.Input.CaptureRegion.Y}), サイズ=({context.Input.CaptureRegion.Width}x{context.Input.CaptureRegion.Height})");

                        ocrResults = await _ocrEngine.RecognizeAsync(
                            ocrImage, // 🔧 [PHASE3.2_FIX] 直接画像使用
                            context.Input.CaptureRegion,
                            cancellationToken: cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        // 全体画像でのOCR処理
                        _logger.LogDebug("🔧 [PHASE3.2_FIX] 全体画像OCR実行 - {Width}x{Height}",
                            ocrImage.Width, ocrImage.Height);

                        // 🎯 [OCR_DEBUG_LOG] 全体画像OCR実行をデバッグログに出力
                        DebugLogUtility.WriteLog($"🔍 [FULL_OCR] 全体画像OCR開始 - サイズ=({ocrImage.Width}x{ocrImage.Height})");

                        ocrResults = await _ocrEngine.RecognizeAsync(
                            ocrImage, // 🔧 [PHASE3.2_FIX] 直接画像使用
                            cancellationToken: cancellationToken).ConfigureAwait(false);
                    }
                    
                    // OCR結果から文字列とチャンクを取得
                    detectedText = string.Join(" ", ocrResults.TextRegions.Select(r => r.Text));
                    textChunks = ocrResults.TextRegions.Cast<object>().ToList();
                }
            }
            catch (ObjectDisposedException ex)
            {
                var error = $"🔧 [PHASE3.2_FIX] OCR処理中に画像が破棄されました: {ex.Message}";
                _logger.LogError(ex, error);
                return ProcessingStageResult.CreateError(StageType, error, stopwatch.Elapsed);
            }
            catch (Exception ex)
            {
                var error = $"🔧 [PHASE3.2_FIX] OCR処理で予期しないエラー: {ex.Message}";
                _logger.LogError(ex, error);
                return ProcessingStageResult.CreateError(StageType, error, stopwatch.Elapsed);
            }

            stopwatch.Stop();

            _logger.LogInformation("🔧 [PHASE3.2_FIX] OCR実行段階完了 - 処理時間: {ElapsedMs}ms, 検出テキスト長: {TextLength}",
                stopwatch.ElapsedMilliseconds, detectedText.Length);
            Console.WriteLine($"🔧 [PHASE3.2_FIX] OCR完了 - 処理時間: {stopwatch.ElapsedMilliseconds}ms, テキスト: '{detectedText.Substring(0, Math.Min(50, detectedText.Length))}...'");

            // 🎯 [OCR_DEBUG_LOG] OCR認識結果をデバッグログファイルに出力
            try
            {
                DebugLogUtility.WriteLog($"📝 [OCR_RESULT] 認識完了 - 処理時間: {stopwatch.ElapsedMilliseconds}ms");
                DebugLogUtility.WriteLog($"📝 [OCR_RESULT] 検出テキスト: '{detectedText}'");
                DebugLogUtility.WriteLog($"📝 [OCR_RESULT] テキスト長: {detectedText.Length}文字");

                if (textChunks.Count > 0)
                {
                    DebugLogUtility.WriteLog($"📝 [OCR_RESULT] 検出チャンク数: {textChunks.Count}");

                    // テキストチャンクごとの詳細情報を出力
                    for (int i = 0; i < Math.Min(textChunks.Count, 10); i++) // 最大10個まで
                    {
                        var chunk = textChunks[i];
                        if (chunk is Baketa.Core.Abstractions.OCR.TextRegion textRegion)
                        {
                            DebugLogUtility.WriteLog($"📝 [OCR_RESULT] チャンク{i + 1}: テキスト='{textRegion.Text}', " +
                                $"座標=({textRegion.Bounds.X},{textRegion.Bounds.Y}), " +
                                $"サイズ=({textRegion.Bounds.Width}x{textRegion.Bounds.Height}), " +
                                $"信頼度={textRegion.Confidence:F3}");
                        }
                        else
                        {
                            DebugLogUtility.WriteLog($"📝 [OCR_RESULT] チャンク{i + 1}: {chunk}");
                        }
                    }

                    if (textChunks.Count > 10)
                    {
                        DebugLogUtility.WriteLog($"📝 [OCR_RESULT] ... (残り{textChunks.Count - 10}個のチャンクは省略)");
                    }
                }
                else
                {
                    DebugLogUtility.WriteLog("📝 [OCR_RESULT] テキストチャンクなし");
                }

                DebugLogUtility.WriteLog("📝 [OCR_RESULT] ==========================================");
            }
            catch (Exception logEx)
            {
                _logger.LogWarning(logEx, "🎯 [OCR_DEBUG_LOG] デバッグログ出力でエラー");
            }

            // 🎯 [ROI_IMAGE_SAVE] ROI実行時にテキスト検出領域枠をつけた画像を保存
            try
            {
                // IImage.ToByteArrayAsync()を使用して画像変換による保存機能を実行
                await SaveRoiImageWithTextBounds(ocrImage, textChunks, context.Input.ContextId, stopwatch.Elapsed);
            }
            catch (Exception imageEx)
            {
                _logger.LogWarning(imageEx, "🎯 [ROI_IMAGE_SAVE] ROI画像保存でエラー");
            }

            // ProcessingStageResult作成
            var ocrResult = new OcrExecutionResult
            {
                Success = true,
                DetectedText = detectedText,
                TextChunks = textChunks,
                ProcessingTime = stopwatch.Elapsed
            };

            var result = new ProcessingStageResult
            {
                StageType = StageType,
                Success = true,
                ProcessingTime = stopwatch.Elapsed,
                Data = ocrResult
            };

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var error = $"🔧 [PHASE3.2_FIX] OCR実行段階で重大エラー: {ex.GetType().Name} - {ex.Message}";
            _logger.LogError(ex, error);
            Console.WriteLine($"🔧 [PHASE3.2_FIX] OCRエラー: {error}");
            return ProcessingStageResult.CreateError(StageType, error, stopwatch.Elapsed);
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

    /// <summary>
    /// 🎯 [ROI_IMAGE_SAVE] ROI実行時にテキスト検出領域枠をつけた画像を保存
    /// </summary>
    /// <param name="ocrImage">OCR処理に使用された画像</param>
    /// <param name="textChunks">検出されたテキストチャンク</param>
    /// <param name="contextId">処理コンテキストID</param>
    /// <param name="processingTime">処理時間</param>
    private async Task SaveRoiImageWithTextBounds(IImage ocrImage, List<object> textChunks, string contextId, TimeSpan processingTime)
    {
        try
        {
            // 🎯 [ROI_IMAGE_SAVE] IImage.ToByteArrayAsync()を使用してBitmapに変換
            DebugLogUtility.WriteLog($"🖼️ [ROI_IMAGE_SAVE] 画像変換開始 - テキスト領域数: {textChunks.Count}");

            var imageBytes = await ocrImage.ToByteArrayAsync().ConfigureAwait(false);

            using var memoryStream = new MemoryStream(imageBytes);
            using var bitmap = new Bitmap(memoryStream);

            // 保存ディレクトリの準備
            var roiImagesPath = @"C:\Users\suke0\AppData\Roaming\Baketa\ROI\Images";
            Directory.CreateDirectory(roiImagesPath);

            // ファイル名生成（タイムスタンプとコンテキストID）
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            var fileName = $"roi_ocr_{timestamp}_{contextId[..8]}.png";
            var filePath = Path.Combine(roiImagesPath, fileName);

            using var graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // テキスト領域に枠を描画
            using var pen = new Pen(Color.Red, 2);
            using var font = new Font("Arial", 10);
            using var brush = new SolidBrush(Color.FromArgb(128, Color.Yellow)); // 半透明黄色

            int regionCount = 0;
            foreach (var chunk in textChunks)
            {
                if (chunk is Baketa.Core.Abstractions.OCR.TextRegion textRegion)
                {
                    // テキスト領域に赤い枠を描画
                    var rect = new System.Drawing.Rectangle(
                        textRegion.Bounds.X, textRegion.Bounds.Y,
                        textRegion.Bounds.Width, textRegion.Bounds.Height);
                    graphics.DrawRectangle(pen, rect);

                    // 信頼度スコアをテキストで表示
                    var confidenceText = $"{textRegion.Confidence:F2}";
                    var textRect = new System.Drawing.Rectangle(textRegion.Bounds.X, textRegion.Bounds.Y - 20, 60, 18);
                    graphics.FillRectangle(brush, textRect);
                    graphics.DrawString(confidenceText, font, Brushes.Black, textRect.Location);

                    regionCount++;
                }
            }

            // 画像として保存
            bitmap.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);

            // デバッグログ出力
            DebugLogUtility.WriteLog($"🖼️ [ROI_IMAGE_SAVE] ROI画像保存成功 - ファイル: {fileName}");
            DebugLogUtility.WriteLog($"🖼️ [ROI_IMAGE_SAVE] テキスト領域数: {regionCount}, 画像サイズ: {bitmap.Width}x{bitmap.Height}");
            DebugLogUtility.WriteLog($"🖼️ [ROI_IMAGE_SAVE] 保存先: {filePath}");

            _logger.LogInformation("🎯 [ROI_IMAGE_SAVE] ROI画像保存完了 - ファイル: {FileName}, 領域数: {RegionCount}",
                fileName, regionCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🎯 [ROI_IMAGE_SAVE] ROI画像保存でエラー");
            DebugLogUtility.WriteLog($"❌ [ROI_IMAGE_SAVE] ROI画像保存エラー: {ex.Message}");
        }
    }
}

/// <summary>
/// 🎯 UltraThink Phase 77.6: 循環参照回避インライン実装
/// IImage → IWindowsImage アダプター (最小限実装)
/// </summary>
internal sealed class InlineImageToWindowsImageAdapter : IWindowsImage, IDisposable
{
    private readonly IImage _underlyingImage;
    private readonly ILogger _logger;
    private Bitmap? _cachedBitmap;
    private bool _disposed;

    public int Width => _underlyingImage.Width;
    public int Height => _underlyingImage.Height;

    public InlineImageToWindowsImageAdapter(IImage underlyingImage, ILogger logger)
    {
        _underlyingImage = underlyingImage ?? throw new ArgumentNullException(nameof(underlyingImage));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogDebug("🔄 [PHASE77.6] InlineImageToWindowsImageAdapter 作成 - Size: {Width}x{Height}", Width, Height);
    }

    public Bitmap GetBitmap()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_cachedBitmap != null)
        {
            return _cachedBitmap;
        }

        try
        {
            _logger.LogDebug("🔄 [PHASE77.6] IImage → Bitmap 変換開始");

            var imageBytes = _underlyingImage.ToByteArrayAsync().Result;
            using var memoryStream = new MemoryStream(imageBytes);
            _cachedBitmap = new Bitmap(memoryStream);

            _logger.LogDebug("✅ [PHASE77.6] Bitmap 変換成功 - Size: {Width}x{Height}",
                _cachedBitmap.Width, _cachedBitmap.Height);

            return _cachedBitmap;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE77.6] IImage → Bitmap 変換失敗: {ErrorMessage}", ex.Message);
            throw new InvalidOperationException($"Failed to convert IImage to Bitmap: {ex.Message}", ex);
        }
    }

    public Image GetNativeImage()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return GetBitmap();
    }

    public async Task SaveAsync(string path, System.Drawing.Imaging.ImageFormat? format = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var bitmap = GetBitmap();
        bitmap.Save(path, format ?? System.Drawing.Imaging.ImageFormat.Png);
        await Task.CompletedTask;
    }

    public async Task<IWindowsImage> ResizeAsync(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var resizedImage = await _underlyingImage.ResizeAsync(width, height);
        return new InlineImageToWindowsImageAdapter(resizedImage, _logger);
    }

    public async Task<IWindowsImage> CropAsync(Rectangle rectangle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var bitmap = GetBitmap();
        var croppedBitmap = new Bitmap(rectangle.Width, rectangle.Height);

        using (var graphics = Graphics.FromImage(croppedBitmap))
        {
            graphics.DrawImage(bitmap, 0, 0, rectangle, GraphicsUnit.Pixel);
        }

        using var memoryStream = new MemoryStream();
        croppedBitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
        var croppedBytes = memoryStream.ToArray();

        // IImageFactoryを介してIImageを作成する必要があるが、循環参照回避のため簡易実装
        throw new NotImplementedException("CropAsync requires IImageFactory which would create circular reference");
    }

    public async Task<byte[]> ToByteArrayAsync(System.Drawing.Imaging.ImageFormat? format = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var bitmap = GetBitmap();
        using var memoryStream = new MemoryStream();
        bitmap.Save(memoryStream, format ?? System.Drawing.Imaging.ImageFormat.Png);
        return memoryStream.ToArray();
    }

    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            _cachedBitmap?.Dispose();
            _cachedBitmap = null;
            _logger.LogDebug("🔄 [PHASE77.6] InlineImageToWindowsImageAdapter リソース解放完了");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [PHASE77.6] InlineImageToWindowsImageAdapter リソース解放で警告: {ErrorMessage}", ex.Message);
        }
        finally
        {
            _disposed = true;
        }
    }

    public override string ToString()
    {
        return $"InlineImageToWindowsImageAdapter[{Width}x{Height}, Type: {_underlyingImage.GetType().Name}, Disposed: {_disposed}]";
    }
}