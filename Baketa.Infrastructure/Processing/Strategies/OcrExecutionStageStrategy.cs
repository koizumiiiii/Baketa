using System.Buffers; // 🔥 [PHASE5.2C] ArrayPool<byte>用
using System.Diagnostics;
using System.Drawing; // 🎯 UltraThink Phase 77.6: Bitmap用 + ROI_IMAGE_SAVE Graphics, Pen, Color等用
using System.Drawing.Imaging; // 🎯 [ROI_IMAGE_SAVE] ImageFormat用
using System.IO; // 🎯 [ROI_IMAGE_SAVE] Directory, Path用
using System.Linq;
using Baketa.Core.Abstractions.Factories; // 🎯 UltraThink Phase 76: IImageFactory for SafeImage→IImage変換
using Baketa.Core.Abstractions.Imaging; // 🔧 [PHASE3.2_FIX] IImage用
using Baketa.Core.Abstractions.Memory; // 🎯 UltraThink Phase 75: SafeImage統合
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.OCR.Results; // 🔧 [TRANSLATION_FIX] PositionedTextResult用
using Baketa.Core.Abstractions.Platform.Windows; // 🎯 UltraThink: IWindowsImage用
using Baketa.Core.Abstractions.Processing;
using Baketa.Core.Abstractions.Roi; // [Issue #293 Phase 7] IRoiManager用
using Baketa.Core.Abstractions.Services; // 🔥 [COORDINATE_FIX] ICoordinateTransformationService用
using Baketa.Core.Models.ImageProcessing; // [Issue #500] HashAlgorithmType用
using Baketa.Core.Abstractions.Translation; // 🔧 [TRANSLATION_FIX] ITextChunkAggregatorService, TextChunk用
using Baketa.Core.Models.Roi; // [Issue #293 Phase 7] RoiRegion, NormalizedRect用
using Baketa.Core.Extensions; // 🔥 [PHASE5.2C] ToPooledByteArrayWithLengthAsync拡張メソッド用
using Baketa.Core.Models.OCR;
using Baketa.Core.Models.Processing;
using Baketa.Core.Settings; // [Issue #293] RoiManagerSettings用
using Baketa.Core.Utilities; // 🎯 [OCR_DEBUG_LOG] DebugLogUtility用
using Baketa.Infrastructure.Roi.Services; // [Issue #293] RoiRegionMerger用
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options; // [Issue #293] IOptions<T>用
using IImageFactoryInterface = Baketa.Core.Abstractions.Factories.IImageFactory; // 🔧 [PHASE3.2_FIX] 名前空間競合回避
using Rectangle = System.Drawing.Rectangle; // 🎯 UltraThink Phase 75: 名前空間競合回避

namespace Baketa.Infrastructure.Processing.Strategies;

/// <summary>
/// OCR実行段階の処理戦略
/// 既存のOCR処理システムとの統合
/// 🎯 UltraThink Phase 50: ROI検出統合による翻訳表示復旧
/// </summary>
public class OcrExecutionStageStrategy : IProcessingStageStrategy, IDisposable
{
    // [Issue #380] バッチOCR重複除去のIoU閾値
    // 隣接ブロック境界の拡張による同一テキストの重複検出を除去するために使用
    private const float DeduplicationIoUThreshold = 0.3f;

    private readonly ILogger<OcrExecutionStageStrategy> _logger;
    private readonly Baketa.Core.Abstractions.OCR.IOcrEngine _ocrEngine;
    private readonly IImageLifecycleManager _imageLifecycleManager; // 🎯 UltraThink Phase 75: 安全な画像管理
    private readonly IImageFactoryInterface _imageFactory; // 🎯 UltraThink Phase 76: SafeImage→IImage変換用
    private readonly ITextChunkAggregatorService? _textChunkAggregator; // 🔧 [TRANSLATION_FIX] 翻訳パイプライン統合
    private readonly ICoordinateTransformationService _coordinateTransformationService; // 🔥 [COORDINATE_FIX] ROI→スクリーン座標変換
    private readonly IRoiRegionMerger? _regionMerger; // [Issue #293] 隣接領域結合サービス
    private readonly IRoiManager? _roiManager; // [Issue #293 Phase 7] 学習済みROI管理
    private readonly IDetectionBoundsCache? _detectionBoundsCache; // [Issue #500] Detection-Onlyフィルタ用キャッシュ
    private readonly ImageChangeDetectionSettings? _changeDetectionSettings; // [Issue #500] Detection-Onlyフィルタ設定
    private readonly IPerceptualHashService? _perceptualHashService; // [Issue #500] pHash比較用

    private readonly int _nextChunkId = 1; // 🔧 [TRANSLATION_FIX] チャンクID生成用

    // 🔥 [PHASE2.1] ボーダーレス/フルスクリーン検出結果のMetadataキー
    private const string METADATA_KEY_BORDERLESS = "IsBorderlessOrFullscreen";

    // [Issue #293] 部分OCRの設定（設定ファイルから取得）
    private readonly bool _enablePartialOcr;
    private readonly int _minPartialOcrWidth;
    private readonly int _minPartialOcrHeight;
    private readonly float _maxPartialOcrCoverageRatio;
    private readonly int _maxMergedRegions;

    // [Issue #293 Phase 7] 学習済みROI設定
    private const int RoiPaddingPixels = 5; // ROI領域のパディング（5px）

    // [Issue #293 Phase 7.2] テキスト欠落防止: 水平方向拡張（Gemini推奨）
    // 理由: テキストは通常水平方向に伸びるため、ROIの左右を相対的に拡張
    // 例: 元の幅640pxに対して左右各15% → 640 + 96×2 = 832px
    private const float RoiHorizontalExpansionRatio = 0.15f; // 学習済みROI用: 水平方向15%拡張

    // [Issue #404] 変化検出ベースROI専用: テキストが変化領域境界を跨ぐケースに対応
    // 学習済みROIは精度が高いため控えめ(15%)、変化領域は境界にテキストが跨ぐリスクが高いため大きめ(25%)
    private const float ChangeDetectionRoiHorizontalExpansionRatio = 0.25f;

    // [Issue #293 Phase 7.1] 探索モード時のテキスト欠落防止
    // ヒートマップ値がこの閾値以上のブロックもOCR対象に追加（MinConfidenceForRegion=0.3より低く設定）
    private const float HeatmapTextLikelihoodThreshold = 0.05f;

    // [Issue #397] テキスト隣接ブロック拡張範囲
    private const int AdjacentHorizontalRange = 2; // 水平方向 ±2ブロック
    private const int AdjacentVerticalRange = 1;   // 垂直方向 ±1ブロック

    // [Issue #397] グリッドサイズ（変化検知と同じ16x9）
    private readonly int _gridColumns;
    private readonly int _gridRows;

    // [Issue #397] 前サイクルでテキストが検出されたグリッドブロックの記録
    // テキスト隣接ブロック自動包含のために使用
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, HashSet<(int Row, int Col)>> _previousTextBlocksPerContext = new();

    // [Issue #397] P0-3: 部分OCRサイクルカウンター（初回数サイクルは拡張適用）
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _partialOcrCycleCount = new();
    private const int InitialExpansionCycleLimit = 3;

    public ProcessingStageType StageType => ProcessingStageType.OcrExecution;
    public TimeSpan EstimatedProcessingTime => TimeSpan.FromMilliseconds(80);

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <remarks>
    /// [コードレビュー対応] IOptions&lt;RoiManagerSettings&gt;を追加して部分OCR設定を注入可能に
    /// [Issue #293 Phase 7] IRoiManagerを追加して学習済みROI優先OCRを実現
    /// </remarks>
    public OcrExecutionStageStrategy(
        ILogger<OcrExecutionStageStrategy> logger,
        Baketa.Core.Abstractions.OCR.IOcrEngine ocrEngine,
        IImageLifecycleManager imageLifecycleManager, // 🎯 UltraThink Phase 75: 必須依存関係として追加
        IImageFactoryInterface imageFactory, // 🎯 UltraThink Phase 76: SafeImage→IImage変換用
        ICoordinateTransformationService coordinateTransformationService, // 🔥 [COORDINATE_FIX] ROI→スクリーン座標変換サービス注入
        IOptions<RoiManagerSettings>? roiSettings = null, // [Issue #293] 部分OCR設定（オプショナル）
        IRoiRegionMerger? regionMerger = null, // [Issue #293] 隣接領域結合（オプショナル）
        IRoiManager? roiManager = null, // [Issue #293 Phase 7] 学習済みROI管理（オプショナル）
        ITextChunkAggregatorService? textChunkAggregator = null, // 🔧 [TRANSLATION_FIX] 翻訳パイプライン統合
        IDetectionBoundsCache? detectionBoundsCache = null, // [Issue #500] Detection-Onlyフィルタ用キャッシュ
        IOptions<ImageChangeDetectionSettings>? changeDetectionSettings = null, // [Issue #500] Detection-Onlyフィルタ設定
        IPerceptualHashService? perceptualHashService = null) // [Issue #500] pHash比較用
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _ocrEngine = ocrEngine ?? throw new ArgumentNullException(nameof(ocrEngine));
        _imageLifecycleManager = imageLifecycleManager ?? throw new ArgumentNullException(nameof(imageLifecycleManager));
        _imageFactory = imageFactory ?? throw new ArgumentNullException(nameof(imageFactory));
        _coordinateTransformationService = coordinateTransformationService ?? throw new ArgumentNullException(nameof(coordinateTransformationService)); // 🔥 [COORDINATE_FIX]
        _regionMerger = regionMerger; // null許容（後方互換性）
        _roiManager = roiManager; // [Issue #293 Phase 7] null許容（後方互換性）
        _textChunkAggregator = textChunkAggregator; // null許容（翻訳無効時対応）
        _detectionBoundsCache = detectionBoundsCache; // [Issue #500] null許容（後方互換性）
        _changeDetectionSettings = changeDetectionSettings?.Value; // [Issue #500] null許容
        _perceptualHashService = perceptualHashService; // [Issue #500] null許容（後方互換性）

        // [Issue #397] グリッドサイズ設定（変化検知と同期）
        var changeDetectionDefaults = new ImageChangeDetectionSettings();
        _gridColumns = changeDetectionDefaults.GridColumns; // 16
        _gridRows = changeDetectionDefaults.GridRows; // 9

        // [Issue #293] 部分OCR設定の読み込み（設定がない場合はデフォルト値）
        var settings = roiSettings?.Value ?? new RoiManagerSettings();
        _enablePartialOcr = settings.EnablePartialOcr;
        _minPartialOcrWidth = settings.MinPartialOcrWidth;
        _minPartialOcrHeight = settings.MinPartialOcrHeight;
        _maxPartialOcrCoverageRatio = settings.MaxPartialOcrCoverageRatio;
        _maxMergedRegions = settings.MaxMergedRegions;
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

        // 🔥 [PHASE5] ROI診断ログ削除 - ROI廃止により不要

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

            // ✅ [PHASE5_COMPLETE] ROI検出と2回目OCRループを完全削除 - シンプルな1回OCR実行のみ

            // [Issue #293 Phase 7.3] 「記憶優先 + 変化領域併用」判定
            // 学習済みROIを優先しつつ、ROI外で大きな変化がある領域もOCR対象に追加
            // これにより、新出現テキスト（2行目に折り返したテキストなど）を検出できる
            var changeResult = context.GetStageResult<ImageChangeDetectionResult>(ProcessingStageType.ImageChangeDetection);

            // [Issue #397] 前サイクルのテキスト隣接ブロックを取得
            var contextId = context.Input.ContextId ?? "default";

            // [Issue #500] Detection-Only中間フィルタ
            var detectionSkipResult = await TrySkipWithDetectionOnlyAsync(ocrImage, contextId, cancellationToken)
                .ConfigureAwait(false);
            if (detectionSkipResult != null)
            {
                stopwatch.Stop();
                _logger.LogInformation(
                    "[Issue #500] Detection-Onlyフィルタでスキップ - ContextId: {ContextId}, 処理時間: {ElapsedMs:F1}ms",
                    contextId, stopwatch.Elapsed.TotalMilliseconds);
                return new ProcessingStageResult
                {
                    StageType = StageType,
                    Success = true,
                    ProcessingTime = stopwatch.Elapsed,
                    Data = detectionSkipResult
                };
            }

            var textAdjacentBlocks = GetTextAdjacentBlocks(ocrImage.Width, ocrImage.Height, contextId);

            // [Issue #397] P0-3: サイクルカウンターベースの拡張適用判定
            var partialOcrCount = _partialOcrCycleCount.GetValueOrDefault(contextId, 0);
            var shouldApplyInitialExpansion = partialOcrCount < InitialExpansionCycleLimit;

            if (TryGetLearnedRoiRegions(ocrImage.Width, ocrImage.Height, out var learnedRegions))
            {
                // [Issue #293 Phase 7.3] 学習済みROI + 変化領域を併用
                var combinedRegions = CombineLearnedRoiWithChangedRegions(
                    learnedRegions,
                    changeResult,
                    textAdjacentBlocks);

                // [Issue #293 Phase 8] テキスト欠落防止: 学習済みROIにも垂直・水平拡張を適用
                combinedRegions = ExpandRegionsHorizontally(combinedRegions, ocrImage.Width, ocrImage.Height);

                // [Issue #397] P0-3: 初回数サイクルはPhase 2相当の追加拡張を適用（水平 + 下方）
                if (shouldApplyInitialExpansion)
                {
                    combinedRegions = ApplyInitialDetectionExpansion(combinedRegions, ocrImage.Width, ocrImage.Height);
                    _logger.LogDebug(
                        "[Issue #397] 初回拡張適用 (サイクル {Cycle}/{Limit}): 水平±{HRange}、下方+{VRange}グリッドセル",
                        partialOcrCount + 1, InitialExpansionCycleLimit, AdjacentHorizontalRange, AdjacentVerticalRange);
                }

                // [Issue #448] クライアント領域制約の適用（タイトルバー除外）
                if (context.Input.ClientAreaBounds is { } clientArea1)
                {
                    combinedRegions = ApplyClientAreaConstraint(combinedRegions, ocrImage.Width, ocrImage.Height, clientArea1);
                    if (combinedRegions.Count == 0)
                    {
                        _logger.LogDebug("[Issue #448] クライアント領域制約により全領域が除外されました（学習済みROIパス）");
                        return ProcessingStageResult.CreateSkipped(StageType, "All regions outside client area");
                    }
                }

                _logger.LogInformation("🎯 [Issue #293 Phase 7.3] 学習済みROI + 変化領域併用OCR: 学習済み{LearnedCount}領域 + 変化{ChangedCount}領域 = 合計{TotalCount}領域",
                    learnedRegions.Count, combinedRegions.Count - learnedRegions.Count, combinedRegions.Count);

                return await ExecutePartialOcrAsync(context, combinedRegions, ocrImage, stopwatch, cancellationToken)
                    .ConfigureAwait(false);
            }

            // [Issue #293] 部分OCR実行の判定（結合済み領域を取得）- 学習済みROIがない場合のフォールバック
            if (TryGetPartialOcrRegions(changeResult, ocrImage.Width, ocrImage.Height, out var mergedRegions, textAdjacentBlocks))
            {
                // [Issue #397] P0-3: 初回数サイクルはPhase 2相当の追加拡張を適用（水平 + 下方）
                if (shouldApplyInitialExpansion)
                {
                    mergedRegions = ApplyInitialDetectionExpansion(mergedRegions, ocrImage.Width, ocrImage.Height);
                    _logger.LogDebug(
                        "[Issue #397] 初回拡張適用 (サイクル {Cycle}/{Limit}, 探索モード): 水平±{HRange}、下方+{VRange}グリッドセル",
                        partialOcrCount + 1, InitialExpansionCycleLimit, AdjacentHorizontalRange, AdjacentVerticalRange);
                }

                // [Issue #448] クライアント領域制約の適用（タイトルバー除外）
                if (context.Input.ClientAreaBounds is { } clientArea2)
                {
                    mergedRegions = ApplyClientAreaConstraint(mergedRegions, ocrImage.Width, ocrImage.Height, clientArea2);
                    if (mergedRegions.Count == 0)
                    {
                        _logger.LogDebug("[Issue #448] クライアント領域制約により全領域が除外されました（変化領域パス）");
                        return ProcessingStageResult.CreateSkipped(StageType, "All regions outside client area");
                    }
                }

                _logger.LogInformation("🎯 [Issue #293] 変化領域ベース部分OCR実行: {RegionCount}結合領域を処理（探索モード）", mergedRegions.Count);
                return await ExecutePartialOcrAsync(context, mergedRegions, ocrImage, stopwatch, cancellationToken)
                    .ConfigureAwait(false);
            }

            // [Issue #448] 全画面OCRパス: クライアント領域制約がある場合は部分OCRにリダイレクト
            if (context.Input.ClientAreaBounds is { } clientArea3)
            {
                var clientRect = new Rectangle(
                    (int)(clientArea3.X * ocrImage.Width),
                    (int)(clientArea3.Y * ocrImage.Height),
                    (int)(clientArea3.Width * ocrImage.Width),
                    (int)(clientArea3.Height * ocrImage.Height));

                _logger.LogInformation("[Issue #448] タイトルバー除外: 全画面OCR→クライアント領域部分OCR ({X},{Y},{W}x{H})",
                    clientRect.X, clientRect.Y, clientRect.Width, clientRect.Height);

                return await ExecutePartialOcrAsync(context, [clientRect], ocrImage, stopwatch, cancellationToken)
                    .ConfigureAwait(false);
            }

            // 実際のOCRサービス統合（全画面OCR）
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

                    // 🔥 [PHASE5] ROI/全画面条件分岐削除 - FullScreenOcr統一で常に全画面最小サイズ要件
                    // FullScreenOcr: 50x50ピクセル（Detection + Recognition の安全マージン）
                    const int minimumOcrImageSize = 50;
                    if (testWidth < minimumOcrImageSize || testHeight < minimumOcrImageSize)
                    {
                        var error = $"🎯 OCRに適さない極小画像サイズ: {testWidth}x{testHeight} (最小要件: {minimumOcrImageSize}x{minimumOcrImageSize})";
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

                // ✅ [PHASE5_COMPLETE] シンプルな全画面OCR実行のみ
                _logger.LogInformation("🎯 [PHASE5_COMPLETE] 全画面OCR実行開始 - サイズ: {Width}x{Height}",
                    ocrImage.Width, ocrImage.Height);

                // 🎯 [OPTION_B_PHASE2] OcrContext使用（CaptureRegion=null）
                var ocrContext = new OcrContext(
                    ocrImage,
                    context.Input.SourceWindowHandle,
                    null, // 全体画像処理
                    cancellationToken);

                ocrResults = await _ocrEngine.RecognizeAsync(ocrContext).ConfigureAwait(false);

                // OCR結果から文字列を取得
                detectedText = string.Join(" ", ocrResults.TextRegions.Select(r => r.Text));

                // 🚀 [Issue #193 FIX] GPU Shaderリサイズ後の座標スケーリング
                // OCRは1280x720等にリサイズされた画像で実行されるため、
                // 元のウィンドウサイズに座標を戻す必要がある
                //
                // 🔥 [CRITICAL FIX] OcrTextRegion.Boundsは読み取り専用のため、
                // 新しいOcrTextRegionインスタンスを作成してスケーリング済み座標を設定
                var originalSize = context.Input.OriginalWindowSize;
                var capturedSize = new Size(ocrImage.Width, ocrImage.Height);

                // 🔍 [Issue #193 DEBUG] スケーリング条件の診断ログ
                _logger.LogInformation("🔍 [Issue #193 DEBUG] OriginalWindowSize: {OriginalWidth}x{OriginalHeight}, CapturedSize: {CapturedWidth}x{CapturedHeight}, SourceWindowHandle: {Handle}",
                    originalSize.Width, originalSize.Height, capturedSize.Width, capturedSize.Height, context.Input.SourceWindowHandle.ToInt64());

                if (originalSize != Size.Empty &&
                    capturedSize.Width > 0 && capturedSize.Height > 0 &&
                    (originalSize.Width != capturedSize.Width || originalSize.Height != capturedSize.Height))
                {
                    double scaleX = (double)originalSize.Width / capturedSize.Width;
                    double scaleY = (double)originalSize.Height / capturedSize.Height;

                    _logger.LogInformation("🚀 [Issue #193] 座標スケーリング適用 - 元サイズ: {OriginalWidth}x{OriginalHeight}, キャプチャサイズ: {CapturedWidth}x{CapturedHeight}, スケール: ({ScaleX:F3}, {ScaleY:F3})",
                        originalSize.Width, originalSize.Height, capturedSize.Width, capturedSize.Height, scaleX, scaleY);

                    // スケーリング済みの新しいOcrTextRegionリストを作成
                    var scaledRegions = ocrResults.TextRegions.Select(r =>
                    {
                        var scaledBounds = new Rectangle(
                            (int)(r.Bounds.X * scaleX),
                            (int)(r.Bounds.Y * scaleY),
                            (int)(r.Bounds.Width * scaleX),
                            (int)(r.Bounds.Height * scaleY));

                        // 🔥 [Issue #193 FIX] 新しいOcrTextRegionインスタンスを作成（Boundsは読み取り専用）
                        return new Baketa.Core.Abstractions.OCR.OcrTextRegion(
                            text: r.Text,
                            bounds: scaledBounds,
                            confidence: r.Confidence,
                            contour: r.Contour?.Select(p => new Point(
                                (int)(p.X * scaleX),
                                (int)(p.Y * scaleY))).ToArray(),
                            direction: r.Direction);
                    }).ToList();

                    textChunks = [.. scaledRegions.Cast<object>()];

                    // [Issue #193] スケーリング結果確認
                    if (scaledRegions.Count > 0)
                    {
                        var first = scaledRegions[0];
                        _logger.LogDebug("[Issue #193] スケーリング完了: 最初の領域 ({X},{Y},{Width}x{Height})",
                            first.Bounds.X, first.Bounds.Y, first.Bounds.Width, first.Bounds.Height);
                    }
                }
                else
                {
                    // スケーリング不要の場合はそのまま使用
                    textChunks = [.. ocrResults.TextRegions.Cast<object>()];
                }

                // [Issue #397] テキスト位置をグリッドブロックに記録（次サイクルの隣接ブロック拡張に使用）
                // 全画面OCRではOCR画像サイズ基準の座標で記録（変化検知と同じ座標系）
                var fullOcrContextId = context.Input.ContextId ?? "default";
                RecordTextBlockPositions(ocrResults.TextRegions, ocrImage.Width, ocrImage.Height, fullOcrContextId);

                _logger.LogInformation("✅ [PHASE5_COMPLETE] 全画面OCR完了 - テキスト長: {TextLength}文字, 領域数: {RegionCount}個",
                    detectedText.Length, textChunks.Count);
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

            _logger.LogInformation("[PHASE3.2_FIX] OCR実行段階完了 - 処理時間: {ElapsedMs}ms, 検出テキスト長: {TextLength}",
                stopwatch.ElapsedMilliseconds, detectedText.Length);

            // 🎯 [OCR_DEBUG_LOG] OCR認識結果をデバッグログファイルに出力
            try
            {
                _logger?.LogDebug($"📝 [OCR_RESULT] 認識完了 - 処理時間: {stopwatch.ElapsedMilliseconds}ms");
                _logger?.LogDebug($"📝 [OCR_RESULT] 検出テキスト: '{detectedText}'");
                _logger?.LogDebug($"📝 [OCR_RESULT] テキスト長: {detectedText.Length}文字");

                if (textChunks.Count > 0)
                {
                    _logger?.LogDebug($"📝 [OCR_RESULT] 検出チャンク数: {textChunks.Count}");

                    // テキストチャンクごとの詳細情報を出力
                    for (int i = 0; i < Math.Min(textChunks.Count, 10); i++) // 最大10個まで
                    {
                        var chunk = textChunks[i];
                        if (chunk is Baketa.Core.Abstractions.OCR.TextDetection.TextRegion textRegion)
                        {
                            // [ROI_DELETION] TextRegionにTextプロパティは存在しない（位置情報のみ）
                            _logger?.LogDebug($"📝 [OCR_RESULT] チャンク{i + 1}: " +
                                $"座標=({textRegion.Bounds.X},{textRegion.Bounds.Y}), " +
                                $"サイズ=({textRegion.Bounds.Width}x{textRegion.Bounds.Height}), " +
                                $"信頼度={textRegion.Confidence:F3}");
                        }
                        else
                        {
                            _logger?.LogDebug($"📝 [OCR_RESULT] チャンク{i + 1}: {chunk}");
                        }
                    }

                    if (textChunks.Count > 10)
                    {
                        _logger?.LogDebug($"📝 [OCR_RESULT] ... (残り{textChunks.Count - 10}個のチャンクは省略)");
                    }
                }
                else
                {
                    _logger?.LogDebug("📝 [OCR_RESULT] テキストチャンクなし");
                }

                _logger?.LogDebug("📝 [OCR_RESULT] ==========================================");
            }
            catch (Exception logEx)
            {
                _logger.LogWarning(logEx, "🎯 [OCR_DEBUG_LOG] デバッグログ出力でエラー");
            }

            // 🔥 [PERFORMANCE_FIX] ROI画像保存をデバッグビルド専用に制限
            //
            // **修正理由:**
            // ROI画像保存処理が3840x2160ピクセルのPNG保存に1.1-1.3秒かかり、
            // OCR実行の度に2回実行されるため、合計2.4秒（処理時間の30%）を消費していた。
            //
            // **期待効果:**
            // - 開発ビルド: ROI画像保存を維持（デバッグ用途）
            // - 本番ビルド: ROI画像保存を無効化 → 2.4秒削減（30%改善）
            //
#if DEBUG
            // 🎯 [ROI_IMAGE_SAVE] ROI実行時にテキスト検出領域枠をつけた画像を保存
            try
            {
                // 🔍 [ULTRATHINK_PHASE20] ocrImage状態確認
                _logger?.LogWarning("🔍 [ULTRATHINK_PHASE20] SaveRoiImageWithTextBounds呼び出し前 - ocrImage型: {ImageType}, Size: {Width}x{Height}",
                    ocrImage.GetType().Name, ocrImage.Width, ocrImage.Height);

                // context.Input.CapturedImageとの比較
                if (ocrImage == context.Input.CapturedImage)
                {
                    _logger?.LogWarning("🔍 [ULTRATHINK_PHASE20] ocrImage == context.Input.CapturedImage (同一インスタンス)");
                }
                else
                {
                    _logger?.LogWarning("🔍 [ULTRATHINK_PHASE20] ocrImage != context.Input.CapturedImage (異なるインスタンス)");
                    _logger?.LogWarning("🔍 [ULTRATHINK_PHASE20] context.Input.CapturedImage - 型: {ImageType}, Size: {Width}x{Height}",
                        context.Input.CapturedImage?.GetType().Name ?? "NULL",
                        context.Input.CapturedImage?.Width ?? 0,
                        context.Input.CapturedImage?.Height ?? 0);
                }

                // IImage.ToByteArrayAsync()を使用して画像変換による保存機能を実行
                await SaveRoiImageWithTextBounds(ocrImage, textChunks, context.Input.ContextId, stopwatch.Elapsed);
            }
            catch (Exception imageEx)
            {
                _logger.LogWarning(imageEx, "🎯 [ROI_IMAGE_SAVE] ROI画像保存でエラー");
            }
#else
            // 本番ビルド: ROI画像保存をスキップ（パフォーマンス優先）
            _logger?.LogDebug("🔥 [PERFORMANCE_FIX] 本番ビルド - ROI画像保存をスキップ（デバッグ専用機能）");
#endif

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
            var error = $"[PHASE3.2_FIX] OCR実行段階で重大エラー: {ex.GetType().Name} - {ex.Message}";
            _logger.LogError(ex, error);
            return ProcessingStageResult.CreateError(StageType, error, stopwatch.Elapsed);
        }
    }

    public bool ShouldExecute(ProcessingContext context)
    {
        _logger.LogDebug("🎯 [OCR_SKIP_DEBUG] ShouldExecute呼び出し - PreviousStageResult: {HasPrevious}, Success: {Success}",
            context.PreviousStageResult != null, context.PreviousStageResult?.Success);

        // 🔧 [Issue #193] キャプチャ段階でOCRが実行済みの場合はスキップ（二重OCR防止）
        if (context.Input?.PreExecutedOcrResult != null)
        {
            _logger.LogInformation("🎯 [OCR_SKIP] キャプチャ段階でOCR実行済み ({RegionCount} regions) - 二重OCR防止のためスキップ",
                context.Input.PreExecutedOcrResult.TextRegions.Count);
            return false;
        }

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

    #region [Issue #293 Phase 7] 学習済みROI優先OCR

    /// <summary>
    /// [Issue #293 Phase 7] 学習済みROIに基づいて部分OCR領域を取得
    /// </summary>
    /// <param name="imageWidth">画像幅（ピクセル）</param>
    /// <param name="imageHeight">画像高さ（ピクセル）</param>
    /// <param name="learnedRegions">学習済みROI領域（ピクセル座標、パディング適用済み）</param>
    /// <returns>学習済みROIが有効な場合true</returns>
    /// <remarks>
    /// 「記憶優先」アルゴリズム:
    /// - 画像変化に関わらず、学習済みのテキスト出現位置を優先
    /// - シーンチェンジ時でも8.3秒の全画面OCRを回避し、約900msを維持
    /// - 正規化座標（0.0-1.0）からピクセル座標への変換 + 5pxパディング
    /// </remarks>
    private bool TryGetLearnedRoiRegions(
        int imageWidth,
        int imageHeight,
        out List<Rectangle> learnedRegions)
    {
        learnedRegions = [];

        // IRoiManagerが未注入の場合はスキップ
        if (_roiManager == null)
        {
            _logger.LogDebug("[Issue #293 Phase 7] 学習済みROIスキップ: IRoiManager未注入");
            return false;
        }

        // ROI管理が無効の場合はスキップ
        if (!_roiManager.IsEnabled)
        {
            _logger.LogDebug("[Issue #293 Phase 7] 学習済みROIスキップ: ROI管理無効");
            return false;
        }

        // 学習済みROI領域を取得
        var roiRegions = _roiManager.GetAllRegions();
        if (roiRegions.Count == 0)
        {
            _logger.LogDebug("[Issue #293 Phase 7] 学習済みROIスキップ: 学習済み領域なし（探索モードへ）");
            return false;
        }

        // 正規化座標 → ピクセル座標へ変換（5pxパディング付き）
        foreach (var region in roiRegions)
        {
            // 除外領域はスキップ
            if (region.RegionType == RoiRegionType.Exclusion)
            {
                continue;
            }

            // 信頼度が低すぎる領域はスキップ
            if (region.ConfidenceLevel == RoiConfidenceLevel.Low && region.DetectionCount < 3)
            {
                _logger.LogDebug("[Issue #293 Phase 7] 低信頼度領域スキップ: {Id}, DetectionCount={Count}",
                    region.Id, region.DetectionCount);
                continue;
            }

            var pixelRect = ConvertRoiToPixelRect(region.NormalizedBounds, imageWidth, imageHeight);

            _logger.LogDebug("[Issue #293 Phase 7] ROI変換: Id={Id}, Norm=({NX:F3},{NY:F3},{NW:F3},{NH:F3}), Image=({IW}x{IH}) → Pixel={Rect}",
                region.Id,
                region.NormalizedBounds.X, region.NormalizedBounds.Y,
                region.NormalizedBounds.Width, region.NormalizedBounds.Height,
                imageWidth, imageHeight, pixelRect);

            // 最小サイズチェック
            if (pixelRect.Width < _minPartialOcrWidth || pixelRect.Height < _minPartialOcrHeight)
            {
                _logger.LogDebug("[Issue #293 Phase 7] 小さすぎる領域スキップ: {Size}", pixelRect);
                continue;
            }

            learnedRegions.Add(pixelRect);
        }

        if (learnedRegions.Count == 0)
        {
            _logger.LogDebug("[Issue #293 Phase 7] 学習済みROIスキップ: 有効な領域なし");
            return false;
        }

        // [Issue #293 Phase 7.4] Y座標範囲が重なるROIを統合（学習による分割を防止）
        // ヒートマップ学習で同一テキスト領域が複数ROIに分割されることがあるため、
        // Y範囲が重複する領域は1つのバウンディングボックスにマージする
        learnedRegions = MergeVerticallyOverlappingRegions(learnedRegions, imageHeight);

        // 領域数が多すぎる場合は統合
        if (learnedRegions.Count > _maxMergedRegions && _regionMerger != null)
        {
            learnedRegions = _regionMerger.MergeAdjacentRegions([.. learnedRegions]);
            _logger.LogDebug("[Issue #293 Phase 7] 領域統合: {OriginalCount}→{MergedCount}",
                roiRegions.Count, learnedRegions.Count);
        }

        // 統合後も多すぎる場合は全画面OCRにフォールバック
        if (learnedRegions.Count > _maxMergedRegions)
        {
            _logger.LogDebug("[Issue #293 Phase 7] 学習済みROIスキップ: 統合後も領域数が多すぎる ({Count} > {Max})",
                learnedRegions.Count, _maxMergedRegions);
            learnedRegions = [];
            return false;
        }

        // カバー率チェック
        var totalImageArea = imageWidth * imageHeight;
        var totalRoiArea = learnedRegions.Sum(r => r.Width * r.Height);
        var coverageRatio = (float)totalRoiArea / totalImageArea;

        if (coverageRatio > _maxPartialOcrCoverageRatio)
        {
            _logger.LogDebug("[Issue #293 Phase 7] 学習済みROIスキップ: カバー率が高すぎる ({Ratio:P1} > {Max:P1})",
                coverageRatio, _maxPartialOcrCoverageRatio);
            learnedRegions = [];
            return false;
        }

        _logger.LogInformation("[Issue #293 Phase 7] 学習済みROI判定: 有効 - {Count}領域, カバー率{Ratio:P1}",
            learnedRegions.Count, coverageRatio);

        return true;
    }

    /// <summary>
    /// [Issue #293 Phase 7] 正規化座標をピクセル座標に変換（パディング + 水平拡張付き）
    /// </summary>
    /// <param name="normalizedBounds">正規化座標（0.0-1.0）</param>
    /// <param name="imageWidth">画像幅</param>
    /// <param name="imageHeight">画像高さ</param>
    /// <returns>ピクセル座標の矩形（5pxパディング + 水平15%拡張適用済み、境界クランプ済み）</returns>
    /// <remarks>
    /// [Issue #293 Phase 7.2] Gemini推奨: テキスト欠落防止のため水平方向に15%拡張
    /// - 垂直方向: 従来通り5pxパディング
    /// - 水平方向: 元の幅の15%を左右に追加（テキストは水平方向に伸びやすいため）
    /// </remarks>
    private static Rectangle ConvertRoiToPixelRect(NormalizedRect normalizedBounds, int imageWidth, int imageHeight)
    {
        // 正規化座標 → ピクセル座標
        var x = (int)(normalizedBounds.X * imageWidth);
        var y = (int)(normalizedBounds.Y * imageHeight);
        var width = (int)(normalizedBounds.Width * imageWidth);
        var height = (int)(normalizedBounds.Height * imageHeight);

        // [Issue #293 Phase 7.2] 水平方向15%拡張（テキスト欠落防止）
        var horizontalExpansion = (int)(width * RoiHorizontalExpansionRatio);

        // パディング適用（垂直: 5px、水平: 15%拡張 + 5px）
        x = Math.Max(0, x - RoiPaddingPixels - horizontalExpansion);
        y = Math.Max(0, y - RoiPaddingPixels);
        width = Math.Min(imageWidth - x, width + (RoiPaddingPixels + horizontalExpansion) * 2);
        height = Math.Min(imageHeight - y, height + RoiPaddingPixels * 2);

        return new Rectangle(x, y, width, height);
    }

    /// <summary>
    /// [Issue #293 Phase 7.4] Y座標範囲が重なり、かつ水平方向で隣接するROI領域を統合
    /// </summary>
    /// <remarks>
    /// ヒートマップ学習により同一テキスト領域が複数ROIに分割されることがある。
    /// Y範囲（縦方向の位置）が大きく重複し、かつ水平方向で隣接/重複している領域は、
    /// 同じテキストエリアに属すると判断し、1つのバウンディングボックスに統合する。
    ///
    /// 水平方向の隣接チェックにより、離れたUI要素（例: 左右に分かれたメニューボタン）が
    /// 誤って統合されることを防止。
    /// </remarks>
    private List<Rectangle> MergeVerticallyOverlappingRegions(List<Rectangle> regions, int imageHeight)
    {
        if (regions.Count <= 1)
        {
            return regions;
        }

        // Y範囲の重複閾値: 高さの50%以上が重複していれば同一領域とみなす
        const float VerticalOverlapThreshold = 0.5f;
        // 水平方向の隣接判定: この距離以内なら隣接とみなす（重複またはギャップ50px以内）
        const int HorizontalAdjacencyMargin = 50;

        var merged = new List<Rectangle>();
        var used = new bool[regions.Count];

        for (int i = 0; i < regions.Count; i++)
        {
            if (used[i]) continue;

            var current = regions[i];
            var mergeGroup = new List<Rectangle> { current };
            used[i] = true;

            // 他の領域とのY重複 + 水平隣接をチェック
            for (int j = i + 1; j < regions.Count; j++)
            {
                if (used[j]) continue;

                var other = regions[j];

                // Y範囲の重複を計算
                var overlapTop = Math.Max(current.Top, other.Top);
                var overlapBottom = Math.Min(current.Bottom, other.Bottom);
                var overlapHeight = Math.Max(0, overlapBottom - overlapTop);

                // 小さい方の高さに対する重複率
                var minHeight = Math.Min(current.Height, other.Height);
                var verticalOverlapRatio = minHeight > 0 ? (float)overlapHeight / minHeight : 0;

                // 水平方向の隣接チェック: 重複しているか、ギャップがマージン以内か
                var horizontalGap = Math.Max(0, Math.Max(current.Left, other.Left) - Math.Min(current.Right, other.Right));
                var isHorizontallyAdjacent = current.IntersectsWith(other) || horizontalGap <= HorizontalAdjacencyMargin;

                // Y重複かつ水平隣接の場合のみ統合
                if (verticalOverlapRatio >= VerticalOverlapThreshold && isHorizontallyAdjacent)
                {
                    mergeGroup.Add(other);
                    used[j] = true;

                    // currentを更新して次の比較に使用
                    current = CalculateBoundingBox(mergeGroup);
                }
            }

            // グループのバウンディングボックスを追加
            merged.Add(CalculateBoundingBox(mergeGroup));
        }

        if (merged.Count < regions.Count)
        {
            _logger.LogDebug("[Issue #293 Phase 7.4] Y重複+水平隣接ROI統合: {Original}領域 → {Merged}領域",
                regions.Count, merged.Count);
        }

        return merged;
    }

    /// <summary>
    /// 複数のRectangleを包含するバウンディングボックスを計算
    /// </summary>
    private static Rectangle CalculateBoundingBox(List<Rectangle> rectangles)
    {
        if (rectangles.Count == 0) return Rectangle.Empty;
        if (rectangles.Count == 1) return rectangles[0];

        var minX = rectangles.Min(r => r.X);
        var minY = rectangles.Min(r => r.Y);
        var maxX = rectangles.Max(r => r.Right);
        var maxY = rectangles.Max(r => r.Bottom);

        return new Rectangle(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>
    /// [Issue #293 Phase 7.3] 学習済みROI領域と変化領域を併用
    /// </summary>
    /// <remarks>
    /// 学習済みROIだけでは検出できない新出現テキスト（2行目に折り返したテキストなど）に対応。
    /// 変化領域のうち、学習済みROIとオーバーラップしない領域を追加でOCR対象にする。
    /// [コードレビュー対応] 未使用パラメータ削除、オーバーラップ判定をRectangle.Inflateでシンプル化
    /// </remarks>
    private List<Rectangle> CombineLearnedRoiWithChangedRegions(
        List<Rectangle> learnedRegions,
        ImageChangeDetectionResult? changeResult,
        List<Rectangle>? textAdjacentBlocks = null)
    {
        // 学習済みROI領域を基本として使用
        var combinedRegions = new List<Rectangle>(learnedRegions);

        // [Issue #526] テキスト隣接ブロックを全て追加（変化検知に依存しない探索領域）
        // ROIとの重複チェック（IntersectsWith）を削除: 部分的に重なるブロックを除外すると
        // crop領域が不足し、テキスト全体を包含できない問題が発生していた
        // 重複追加しても後段のMergeAdjacentRegionsで統合されるため実害なし
        // [Issue #526] テキスト隣接ブロックを全てcrop領域に追加
        // グリッドブロック（16x9=80x80px）は最小サイズ閾値（100x50）より小さいため
        // 最小サイズチェックを適用しない（後段のMergeAdjacentRegionsで統合される）
        if (textAdjacentBlocks is { Count: > 0 })
        {
            if (_regionMerger != null)
            {
                var mergedAdjacent = _regionMerger.MergeAdjacentRegions([.. textAdjacentBlocks]);
                combinedRegions.AddRange(mergedAdjacent);
                _logger.LogInformation("[Issue #526] テキスト隣接ブロック追加: {Input}ブロック → {Count}領域（マージ後）",
                    textAdjacentBlocks.Count, mergedAdjacent.Count);
            }
            else
            {
                combinedRegions.AddRange(textAdjacentBlocks);
                _logger.LogInformation("[Issue #526] テキスト隣接ブロック追加: {Count}領域", textAdjacentBlocks.Count);
            }
        }

        // 変化領域がない場合はここで返す
        if (changeResult?.ChangedRegions == null || changeResult.ChangedRegions.Length == 0)
        {
            _logger.LogDebug("[Issue #293 Phase 7.3] 変化領域なし - 学習済みROI{AdjacentInfo}使用",
                textAdjacentBlocks is { Count: > 0 } ? "+テキスト隣接ブロック" : "のみ");
            return combinedRegions;
        }

        // 変化領域のうち、学習済みROI（パディング含む）とオーバーラップしない領域を追加
        var additionalRegions = new List<Rectangle>();
        foreach (var changedRegion in changeResult.ChangedRegions)
        {
            // 最小サイズチェック
            if (changedRegion.Width < _minPartialOcrWidth || changedRegion.Height < _minPartialOcrHeight)
            {
                continue;
            }

            // [コードレビュー対応] 学習済みROIの影響範囲（パディング付き）とのオーバーラップをチェック
            var isWithinLearnedArea = learnedRegions.Any(learned =>
            {
                var expandedLearned = Rectangle.Inflate(learned, RoiPaddingPixels, RoiPaddingPixels);
                return expandedLearned.IntersectsWith(changedRegion);
            });

            if (!isWithinLearnedArea)
            {
                additionalRegions.Add(changedRegion);
                _logger.LogDebug("[Issue #293 Phase 7.3] 変化領域追加（ROI外）: {Region}", changedRegion);
            }
        }

        var additionalCount = additionalRegions.Count;
        if (additionalCount > 0)
        {
            // 追加領域を統合して追加
            if (_regionMerger != null)
            {
                var mergedAdditional = _regionMerger.MergeAdjacentRegions([.. additionalRegions]);
                combinedRegions.AddRange(mergedAdditional);
                _logger.LogInformation("[Issue #293 Phase 7.3] ROI外変化領域を追加: {Count}領域（マージ後）", mergedAdditional.Count);
            }
            else
            {
                combinedRegions.AddRange(additionalRegions);
                _logger.LogInformation("[Issue #293 Phase 7.3] ROI外変化領域を追加: {Count}領域", additionalCount);
            }
        }

        // 領域数チェック（多すぎる場合は統合）
        if (combinedRegions.Count > _maxMergedRegions && _regionMerger != null)
        {
            combinedRegions = _regionMerger.MergeAdjacentRegions([.. combinedRegions]);
        }

        return combinedRegions;
    }

    #endregion

    #region [Issue #293] 部分OCR実行

    /// <summary>
    /// [Issue #293] 部分OCRを使用すべきかどうかを判定し、使用する場合は結合済み領域を返す
    /// </summary>
    /// <param name="changeResult">変化検知結果</param>
    /// <param name="imageWidth">画像幅</param>
    /// <param name="imageHeight">画像高さ</param>
    /// <param name="mergedRegions">結合済み領域（部分OCR使用時のみ有効）</param>
    /// <returns>部分OCRを使用すべき場合true</returns>
    /// <remarks>
    /// [コードレビュー対応] MergeAdjacentRegionsの重複呼び出しを解消
    /// 判定と領域取得を1回の呼び出しで行い、結果をoutパラメータで返す
    /// </remarks>
    private bool TryGetPartialOcrRegions(
        ImageChangeDetectionResult? changeResult,
        int imageWidth,
        int imageHeight,
        out List<Rectangle> mergedRegions,
        List<Rectangle>? textAdjacentBlocks = null)
    {
        mergedRegions = [];

        // 部分OCR機能が無効の場合は全画面OCR
        if (!_enablePartialOcr)
        {
            _logger.LogDebug("[Issue #293] 部分OCRスキップ: 機能無効（設定: EnablePartialOcr=false）");
            return false;
        }

        // RoiRegionMergerが利用できない場合は全画面OCR
        if (_regionMerger == null)
        {
            _logger.LogDebug("[Issue #293] 部分OCRスキップ: RoiRegionMerger未注入");
            return false;
        }

        // [Issue #293 Phase 7.1] 変化領域 + ヒートマップ高値ブロック + テキスト隣接ブロックを収集
        var allRegions = new List<Rectangle>();

        // 1. 変化領域を追加（従来ロジック）
        if (changeResult?.ChangedRegions != null && changeResult.ChangedRegions.Length > 0)
        {
            allRegions.AddRange(changeResult.ChangedRegions);
            _logger.LogDebug("[Issue #293 Phase 7.1] 変化領域追加: {Count}ブロック", changeResult.ChangedRegions.Length);
        }

        // 2. ヒートマップで「テキストがありそう」なブロックを追加（テキスト欠落防止）
        var heatmapBlocks = GetHeatmapHighValueBlocks(imageWidth, imageHeight);
        if (heatmapBlocks.Count > 0)
        {
            allRegions.AddRange(heatmapBlocks);
            _logger.LogDebug("[Issue #293 Phase 7.1] ヒートマップ高値ブロック追加: {Count}ブロック（閾値>={Threshold:F2}）",
                heatmapBlocks.Count, HeatmapTextLikelihoodThreshold);
        }

        // 3. [Issue #397] テキスト隣接ブロックを追加（前サイクルのOCR結果に基づく探索拡張）
        if (textAdjacentBlocks is { Count: > 0 })
        {
            allRegions.AddRange(textAdjacentBlocks);
            _logger.LogDebug("[Issue #397] テキスト隣接ブロック追加（探索モード）: {Count}ブロック", textAdjacentBlocks.Count);
        }

        // 変化領域もヒートマップ高値ブロックもテキスト隣接ブロックもない場合は全画面OCR
        if (allRegions.Count == 0)
        {
            _logger.LogDebug("[Issue #293] 部分OCRスキップ: 変化領域・ヒートマップ高値ブロック・テキスト隣接ブロックなし");
            return false;
        }

        // 隣接領域を結合（変化領域 + ヒートマップブロックをマージ）
        mergedRegions = _regionMerger.MergeAdjacentRegions([.. allRegions]);
        _logger.LogDebug("[Issue #293 Phase 7.1] 領域結合: {InputCount}→{OutputCount}",
            allRegions.Count, mergedRegions.Count);

        // [Issue #293 Phase 8] テキスト欠落防止: 変化領域にも水平拡張を適用
        // 学習済みROIと同様に、テキストが領域境界で切れないように水平方向15%拡張
        mergedRegions = ExpandRegionsHorizontally(mergedRegions, imageWidth, imageHeight);
        _logger.LogDebug("[Issue #293 Phase 8] 水平拡張適用: 15%拡張 + 境界クランプ");

        // 結合後の領域数が多すぎる場合は全画面OCR
        if (mergedRegions.Count > _maxMergedRegions)
        {
            _logger.LogDebug("[Issue #293] 部分OCRスキップ: 結合後領域数が多すぎる ({Count} > {Max})",
                mergedRegions.Count, _maxMergedRegions);
            mergedRegions = [];
            return false;
        }

        // 変化領域の総面積を計算
        var totalImageArea = imageWidth * imageHeight;
        var totalChangedArea = mergedRegions.Sum(r => r.Width * r.Height);
        var coverageRatio = (float)totalChangedArea / totalImageArea;

        // 変化領域が画面の大部分を占める場合は全画面OCR
        if (coverageRatio > _maxPartialOcrCoverageRatio)
        {
            _logger.LogDebug("[Issue #293] 部分OCRスキップ: 変化領域が広すぎる ({Ratio:P1} > {Max:P1})",
                coverageRatio, _maxPartialOcrCoverageRatio);
            mergedRegions = [];
            return false;
        }

        // 各領域が最小サイズを満たすかチェック（フィルタリングして上書き）
        mergedRegions = mergedRegions.Where(r =>
            r.Width >= _minPartialOcrWidth && r.Height >= _minPartialOcrHeight).ToList();

        if (mergedRegions.Count == 0)
        {
            _logger.LogDebug("[Issue #293] 部分OCRスキップ: 有効な領域なし（最小サイズ未満）");
            return false;
        }

        _logger.LogInformation("[Issue #293] 部分OCR判定: 有効 - {ValidCount}領域, カバー率{Ratio:P1}",
            mergedRegions.Count, coverageRatio);

        return true;
    }

    // [Issue #293 Phase 8] テキスト欠落防止: 垂直方向拡張率
    // 複数行テキスト（3行以上）が切れないように垂直方向にも拡張
    private const float RoiVerticalExpansionRatio = 0.30f; // 垂直方向30%拡張（上下各15%）

    /// <summary>
    /// [Issue #293 Phase 8] 領域を水平・垂直両方向に拡張（テキスト欠落防止）
    /// </summary>
    /// <param name="regions">結合済み領域リスト</param>
    /// <param name="imageWidth">画像幅</param>
    /// <param name="imageHeight">画像高さ</param>
    /// <returns>拡張適用済みの領域リスト</returns>
    /// <remarks>
    /// 変化領域ベースの部分OCRでテキストが境界で切れる問題を解決。
    /// - 水平方向: 25%拡張（[Issue #404] テキストが変化領域境界を跨ぐケースに対応）
    /// - 垂直方向: 30%拡張（複数行テキスト対応、上下に行が追加される場合）
    /// </remarks>
    private static List<Rectangle> ExpandRegionsHorizontally(
        List<Rectangle> regions,
        int imageWidth,
        int imageHeight)
    {
        if (regions.Count == 0)
        {
            return regions;
        }

        var expandedRegions = new List<Rectangle>(regions.Count);

        foreach (var region in regions)
        {
            // [Issue #404] 水平方向25%拡張（変化検出ROI専用 - 境界テキスト対応）
            var horizontalExpansion = (int)(region.Width * ChangeDetectionRoiHorizontalExpansionRatio);

            // 垂直方向30%拡張（複数行テキスト対応）
            var verticalExpansion = (int)(region.Height * RoiVerticalExpansionRatio);

            // パディング適用（水平: 15%拡張 + 5px、垂直: 30%拡張 + 5px）
            var x = Math.Max(0, region.X - horizontalExpansion - RoiPaddingPixels);
            var y = Math.Max(0, region.Y - verticalExpansion - RoiPaddingPixels);
            var width = Math.Min(imageWidth - x, region.Width + (horizontalExpansion + RoiPaddingPixels) * 2);
            var height = Math.Min(imageHeight - y, region.Height + (verticalExpansion + RoiPaddingPixels) * 2);

            expandedRegions.Add(new Rectangle(x, y, width, height));
        }

        return expandedRegions;
    }

    /// <summary>
    /// [Issue #448] クライアント領域制約の適用（タイトルバー除外）
    /// 各OCR対象領域をクライアント領域と交差させ、タイトルバー・ウィンドウ枠内の領域を除外する。
    /// 交差結果が小さすぎる（10x10未満）領域もフィルタする。
    /// </summary>
    internal static List<Rectangle> ApplyClientAreaConstraint(
        List<Rectangle> regions, int imageWidth, int imageHeight, NormalizedRect clientArea)
    {
        var clientRect = new Rectangle(
            (int)(clientArea.X * imageWidth),
            (int)(clientArea.Y * imageHeight),
            (int)(clientArea.Width * imageWidth),
            (int)(clientArea.Height * imageHeight));

        return regions
            .Select(r => Rectangle.Intersect(r, clientRect))
            .Where(r => !r.IsEmpty && r.Width > 10 && r.Height > 10)
            .ToList();
    }

    /// <summary>
    /// [Issue #397] 初回検出時の追加拡張（水平 + 下方）
    /// テキスト隣接ブロック履歴がない場合、Phase 2と同等の水平拡張に加え、
    /// 下方にも1グリッドセル分の拡張を適用する。
    /// これにより、初回cropでテキストが途切れる問題を軽減する。
    /// </summary>
    private List<Rectangle> ApplyInitialDetectionExpansion(List<Rectangle> regions, int imageWidth, int imageHeight)
    {
        if (regions.Count == 0 || imageWidth <= 0 || imageHeight <= 0) return regions;

        // Phase 2の水平拡張範囲（±AdjacentHorizontalRange グリッドセル）と同等の絶対ピクセル値
        var cellWidth = imageWidth / _gridColumns;
        var cellHeight = imageHeight / _gridRows;
        var horizontalExpansion = cellWidth * AdjacentHorizontalRange;
        // 下方拡張: 1グリッドセル分（テキストが下にはみ出すケースに対応）
        var verticalExpansion = cellHeight * AdjacentVerticalRange;

        var expanded = new List<Rectangle>(regions.Count);
        foreach (var r in regions)
        {
            var x = Math.Max(0, r.X - horizontalExpansion);
            var right = Math.Min(imageWidth, r.Right + horizontalExpansion);
            var bottom = Math.Min(imageHeight, r.Bottom + verticalExpansion);
            expanded.Add(new Rectangle(x, r.Y, right - x, bottom - r.Y));
        }

        return expanded;
    }

    /// <summary>
    /// [Issue #293 Phase 7.1] ヒートマップで「テキストがありそう」なブロックを取得
    /// </summary>
    /// <remarks>
    /// [Issue #397] 16x9グリッドの各セルのヒートマップ値をチェックし、閾値以上のブロックを返す。
    /// これにより、変化検知が見逃したがテキストが存在する可能性のある領域もOCR対象に含める。
    /// [コードレビュー対応] 整数除算による端の領域漏れを修正、小さい画像のエッジケース処理を追加
    /// </remarks>
    private List<Rectangle> GetHeatmapHighValueBlocks(int imageWidth, int imageHeight)
    {
        // [コードレビュー対応] 容量を事前に指定
        var blocks = new List<Rectangle>(_gridRows * _gridColumns);

        // IRoiManagerが未注入または無効の場合はスキップ
        if (_roiManager == null || !_roiManager.IsEnabled)
        {
            return blocks;
        }

        // [コードレビュー対応] 非常に小さい画像のエッジケース処理
        if (imageWidth < _gridColumns || imageHeight < _gridRows)
        {
            // 画像が小さすぎる場合は全体を1つのブロックとして判定
            if (_roiManager.GetHeatmapValueAt(0.5f, 0.5f) >= HeatmapTextLikelihoodThreshold)
            {
                blocks.Add(new Rectangle(0, 0, imageWidth, imageHeight));
                _logger.LogDebug("[Issue #293 Phase 7.1] 小さい画像: 全体をOCR対象に追加 ({Width}x{Height})",
                    imageWidth, imageHeight);
            }
            return blocks;
        }

        // [Issue #397] 16x9グリッドの各セルをチェック
        for (int row = 0; row < _gridRows; row++)
        {
            for (int col = 0; col < _gridColumns; col++)
            {
                // [コードレビュー対応] 整数除算による端の領域漏れを修正
                var x = imageWidth * col / _gridColumns;
                var y = imageHeight * row / _gridRows;
                var nextX = imageWidth * (col + 1) / _gridColumns;
                var nextY = imageHeight * (row + 1) / _gridRows;

                // セルの中心座標を正規化（0.0-1.0）
                var normalizedX = (col + 0.5f) / _gridColumns;
                var normalizedY = (row + 0.5f) / _gridRows;

                // ヒートマップ値を取得
                var heatmapValue = _roiManager.GetHeatmapValueAt(normalizedX, normalizedY);

                // 閾値以上ならOCR対象に追加
                if (heatmapValue >= HeatmapTextLikelihoodThreshold)
                {
                    blocks.Add(new Rectangle(x, y, nextX - x, nextY - y));

                    _logger.LogDebug("[Issue #293 Phase 7.1] ヒートマップブロック追加: ({Row},{Col}) 値={Value:F3}",
                        row, col, heatmapValue);
                }
            }
        }

        return blocks;
    }

    /// <summary>
    /// [Issue #397] 前サイクルでテキストが検出されたブロックの隣接ブロックをRectangleリストとして返す
    /// </summary>
    /// <remarks>
    /// テキスト行は水平方向に長いため、水平方向に2ブロック・垂直方向に1ブロック拡張する。
    /// これにより、変化検知で拾えなかったダイアログの端のテキストも次サイクルでOCR対象に含まれる。
    /// </remarks>
    private List<Rectangle> GetTextAdjacentBlocks(int imageWidth, int imageHeight, string contextId)
    {
        var blocks = new List<Rectangle>();

        if (!_previousTextBlocksPerContext.TryGetValue(contextId, out var previousTextBlocks) || previousTextBlocks.Count == 0)
        {
            return blocks;
        }

        // 隣接ブロック収集（定数で範囲制御）
        var adjacentSet = new HashSet<(int Row, int Col)>();
        foreach (var (row, col) in previousTextBlocks)
        {
            // 水平方向: ±AdjacentHorizontalRange ブロック
            for (int dc = -AdjacentHorizontalRange; dc <= AdjacentHorizontalRange; dc++)
            {
                var nc = col + dc;
                if (nc >= 0 && nc < _gridColumns)
                {
                    adjacentSet.Add((row, nc));
                }
            }
            // 垂直方向: ±AdjacentVerticalRange ブロック
            for (int dr = -AdjacentVerticalRange; dr <= AdjacentVerticalRange; dr++)
            {
                if (dr == 0) continue; // 中心行は水平ループで追加済み
                var nr = row + dr;
                if (nr >= 0 && nr < _gridRows)
                {
                    adjacentSet.Add((nr, col));
                }
            }
        }

        // [Issue #397] テキストブロック自体も含める（除外しない）
        // 変化検知や学習ROIでカバーされない場合にテキスト領域が欠落する問題を防止
        // マージ処理で重複は統合されるため、含めても実害なし

        // Rectangleに変換
        foreach (var (row, col) in adjacentSet)
        {
            var x = imageWidth * col / _gridColumns;
            var y = imageHeight * row / _gridRows;
            var nextX = imageWidth * (col + 1) / _gridColumns;
            var nextY = imageHeight * (row + 1) / _gridRows;
            blocks.Add(new Rectangle(x, y, nextX - x, nextY - y));
        }

        if (blocks.Count > 0)
        {
            _logger.LogDebug("[Issue #397] テキスト隣接ブロック追加: {Count}ブロック（前サイクルテキスト{TextCount}ブロックから拡張）",
                blocks.Count, previousTextBlocks.Count);
        }

        return blocks;
    }

    /// <summary>
    /// [Issue #397] OCR結果のテキスト位置をグリッドブロックに逆マッピングして記録
    /// </summary>
    /// <remarks>
    /// imageWidth/imageHeightはtextRegionsの座標系と一致させる必要がある。
    /// グリッド位置(Row, Col)は解像度非依存のため、全画面OCR(1280x720)と
    /// 部分OCR(3840x2160)が交互に呼ばれても同じグリッドセルにマッピングされる。
    /// </remarks>
    private void RecordTextBlockPositions(
        IReadOnlyList<Baketa.Core.Abstractions.OCR.OcrTextRegion> textRegions,
        int imageWidth, int imageHeight,
        string contextId)
    {
        // ゼロ除算ガード
        if (imageWidth <= 0 || imageHeight <= 0) return;

        var textBlocks = new HashSet<(int Row, int Col)>();

        foreach (var region in textRegions)
        {
            if (region.Bounds.Width <= 0 || region.Bounds.Height <= 0) continue;

            // テキスト中心座標からグリッドブロックを特定
            var centerX = region.Bounds.X + region.Bounds.Width / 2;
            var centerY = region.Bounds.Y + region.Bounds.Height / 2;

            var col = Math.Clamp(centerX * _gridColumns / imageWidth, 0, _gridColumns - 1);
            var row = Math.Clamp(centerY * _gridRows / imageHeight, 0, _gridRows - 1);
            textBlocks.Add((row, col));

            // テキストの左端・右端もカバー
            var leftCol = Math.Clamp(region.Bounds.X * _gridColumns / imageWidth, 0, _gridColumns - 1);
            var rightCol = Math.Clamp(region.Bounds.Right * _gridColumns / imageWidth, 0, _gridColumns - 1);
            for (int c = leftCol; c <= rightCol; c++)
            {
                textBlocks.Add((row, c));
            }
        }

        _previousTextBlocksPerContext[contextId] = textBlocks;

        // [Issue #397] P0-3: 部分OCRサイクルカウンターをインクリメント
        _partialOcrCycleCount.AddOrUpdate(contextId, 1, (_, count) => count + 1);

        if (textBlocks.Count > 0)
        {
            _logger.LogDebug("[Issue #397] テキストブロック位置記録: {Count}ブロック, ContextId={ContextId}",
                textBlocks.Count, contextId);
        }
    }

    /// <summary>
    /// [Issue #293] 部分OCRを実行
    /// 変化領域のみを切り出してOCRを実行し、座標を元画像の絶対座標に変換
    /// </summary>
    /// <param name="context">処理コンテキスト</param>
    /// <param name="validRegions">結合・フィルタリング済みの有効領域リスト（TryGetPartialOcrRegionsから取得）</param>
    /// <param name="fullImage">全画面画像</param>
    /// <param name="stopwatch">処理時間計測用</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <remarks>
    /// [コードレビュー対応] TryGetPartialOcrRegionsで既に結合・フィルタリング済みの領域を受け取る
    /// MergeAdjacentRegionsの重複呼び出しを解消
    /// </remarks>
    private async Task<ProcessingStageResult> ExecutePartialOcrAsync(
        ProcessingContext context,
        List<Rectangle> validRegions,
        IImage fullImage,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        try
        {
            // validRegionsは既にTryGetPartialOcrRegionsで結合・フィルタリング済み
            if (validRegions.Count == 0)
            {
                _logger.LogWarning("[Issue #293] 部分OCR: 有効な領域なし、全画面OCRにフォールバック");
                return ProcessingStageResult.CreateError(StageType, "No valid regions for partial OCR", stopwatch.Elapsed);
            }

            _logger.LogInformation("[Issue #330] 部分OCR開始（バッチモード）: {Count}領域を処理", validRegions.Count);

            // Phase 1: 全領域を先に切り出し
            var croppedImages = new List<IImage>();
            var regionMapping = new List<Rectangle>(); // croppedImagesとvalidRegionsの対応

            foreach (var region in validRegions)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var croppedImage = await CropImageAsync(fullImage, region, cancellationToken).ConfigureAwait(false);

                    if (croppedImage != null)
                    {
                        croppedImages.Add(croppedImage);
                        regionMapping.Add(region);
                    }
                    else
                    {
                        _logger.LogWarning("[Issue #330] 部分OCR: 領域切り出し失敗 {Region}", region);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[Issue #330] 部分OCR: 領域切り出しエラー {Region}", region);
                }
            }

            if (croppedImages.Count == 0)
            {
                _logger.LogWarning("[Issue #330] 部分OCR: 切り出し成功領域なし");
                return ProcessingStageResult.CreateError(StageType, "No cropped images for partial OCR", stopwatch.Elapsed);
            }

            _logger.LogInformation("[Issue #330] バッチOCR開始: {Count}画像", croppedImages.Count);

            // Phase 2: バッチOCR実行（gRPC呼び出し1回）
            IReadOnlyList<OcrResults> batchResults;
            try
            {
                batchResults = await _ocrEngine.RecognizeBatchAsync(croppedImages, null, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                // 切り出した画像をDispose
                foreach (var img in croppedImages)
                {
                    (img as IDisposable)?.Dispose();
                }
            }

            // Phase 3: 結果を座標変換して集約
            var allTransformedRegions = new List<Baketa.Core.Abstractions.OCR.OcrTextRegion>();
            var allDetectedText = new System.Text.StringBuilder();

            for (var i = 0; i < batchResults.Count && i < regionMapping.Count; i++)
            {
                var ocrResults = batchResults[i];
                var region = regionMapping[i];

                // 座標変換: ROI相対座標 → 元画像絶対座標
                var transformedRegions = TransformOcrResultsToAbsoluteCoordinates(ocrResults, region, context.Input);

                allTransformedRegions.AddRange(transformedRegions);
                allDetectedText.Append(string.Join(" ", ocrResults.TextRegions.Select(r => r.Text)));
                allDetectedText.Append(' ');

                _logger.LogDebug("[Issue #330] バッチOCR結果: 領域{Region}, 検出テキスト{Count}個",
                    region, ocrResults.TextRegions.Count);
            }

            // [Issue #380] バッチOCR結果のデデュプリケーション
            // 隣接ブロック境界の重複により、同じテキストが複数回検出される問題を解決
            var originalCount = allTransformedRegions.Count;
            var deduplicatedRegions = DeduplicateBatchOcrResults(allTransformedRegions);
            var removedCount = originalCount - deduplicatedRegions.Count;

            if (removedCount > 0)
            {
                _logger.LogInformation(
                    "[Issue #380] バッチOCRデデュプリケーション完了: {OriginalCount}個 → {DeduplicatedCount}個 (削除: {RemovedCount}個)",
                    originalCount, deduplicatedRegions.Count, removedCount);
            }

            // [Issue #397] テキスト位置をグリッドブロックに記録（次サイクルの隣接ブロック拡張に使用）
            // deduplicatedRegions はTransformOcrResultsToAbsoluteCoordinatesでOriginalWindowSize空間に
            // スケーリング済みのため、グリッドマッピングにも同じ座標系の寸法を使用する
            var partialContextId = context.Input.ContextId ?? "default";
            var originalSize = context.Input.OriginalWindowSize;
            var capturedSize = new Size(fullImage.Width, fullImage.Height);
            var hasScaling = originalSize != Size.Empty &&
                capturedSize.Width > 0 && capturedSize.Height > 0 &&
                (originalSize.Width != capturedSize.Width || originalSize.Height != capturedSize.Height);
            var recordWidth = hasScaling ? originalSize.Width : fullImage.Width;
            var recordHeight = hasScaling ? originalSize.Height : fullImage.Height;
            RecordTextBlockPositions(deduplicatedRegions, recordWidth, recordHeight, partialContextId);

            var allTextChunks = deduplicatedRegions.Cast<object>().ToList();

            stopwatch.Stop();

            var detectedText = allDetectedText.ToString().Trim();

            _logger.LogInformation("✅ [Issue #330] バッチ部分OCR完了 - 処理時間: {ElapsedMs}ms, テキスト長: {TextLength}文字, 領域数: {RegionCount}",
                stopwatch.ElapsedMilliseconds, detectedText.Length, allTextChunks.Count);

            // 成功結果を作成
            var ocrResult = new OcrExecutionResult
            {
                Success = true,
                DetectedText = detectedText,
                TextChunks = allTextChunks,
                ProcessingTime = stopwatch.Elapsed
            };

            return ProcessingStageResult.CreateSuccess(StageType, ocrResult, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Issue #330] バッチ部分OCRエラー");
            return ProcessingStageResult.CreateError(StageType, $"Batch partial OCR error: {ex.Message}", stopwatch.Elapsed);
        }
    }

    /// <summary>
    /// [Issue #293] 画像から指定領域を切り出す
    /// </summary>
    /// <remarks>
    /// [Geminiレビュー対応] コメント修正: 「ゼロコピー」→「直接ピクセルアクセス」
    /// LockPixelData()を使用して、ToByteArrayAsync()のPNG変換を回避し、
    /// より直接的なピクセルデータアクセスで最適化。
    /// 従来: ToByteArrayAsync() → Bitmap → Crop → PNG → CreateFromBytesAsync()
    /// 最適化: LockPixelData() → 直接Bitmap作成 → Crop → PNG → CreateFromBytesAsync()
    /// ※ Marshal.CopyとToArray()によるコピーは発生する
    /// </remarks>
    private async Task<IImage?> CropImageAsync(IImage sourceImage, Rectangle region, CancellationToken cancellationToken)
    {
        try
        {
            // 境界チェック
            var clampedRegion = ClampRegionToBounds(region, sourceImage.Width, sourceImage.Height);

            if (clampedRegion.Width < _minPartialOcrWidth || clampedRegion.Height < _minPartialOcrHeight)
            {
                _logger.LogDebug("[Issue #293] CropImage: クランプ後のサイズが小さすぎる {Region}", clampedRegion);
                return null;
            }

            Bitmap? sourceBitmap = null;
            try
            {
                // [コードレビュー対応] LockPixelData()を使用した最適化を試行
                var (success, bitmap) = TryCreateBitmapFromPixelData(sourceImage);
                if (success && bitmap != null)
                {
                    sourceBitmap = bitmap;
                    _logger.LogDebug("[Issue #293] CropImage: LockPixelData()による最適化成功");
                }
                else
                {
                    // LockPixelData()が失敗した場合は従来方式にフォールバック
                    _logger.LogDebug("[Issue #293] CropImage: 従来方式にフォールバック");
                    var imageBytes = await sourceImage.ToByteArrayAsync().ConfigureAwait(false);
                    using var memoryStream = new MemoryStream(imageBytes);
                    sourceBitmap = new Bitmap(memoryStream);
                }

                // 指定領域をCrop
                using var croppedBitmap = new Bitmap(clampedRegion.Width, clampedRegion.Height);
                using (var graphics = Graphics.FromImage(croppedBitmap))
                {
                    graphics.DrawImage(sourceBitmap,
                        new Rectangle(0, 0, clampedRegion.Width, clampedRegion.Height),
                        clampedRegion.X, clampedRegion.Y, clampedRegion.Width, clampedRegion.Height,
                        GraphicsUnit.Pixel);
                }

                // Crop画像 → byte[] → IImage
                using var outputStream = new MemoryStream();
                croppedBitmap.Save(outputStream, System.Drawing.Imaging.ImageFormat.Png);
                var croppedBytes = outputStream.ToArray();

                var croppedImage = await _imageFactory.CreateFromBytesAsync(croppedBytes).ConfigureAwait(false);

                _logger.LogDebug("[Issue #293] CropImage成功: 元サイズ={SourceWidth}x{SourceHeight}, 切り出し領域={Region}, 出力サイズ={Width}x{Height}",
                    sourceImage.Width, sourceImage.Height, clampedRegion, croppedImage.Width, croppedImage.Height);

                return croppedImage;
            }
            finally
            {
                sourceBitmap?.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Issue #293] CropImage: エラー {Region}", region);
            return null;
        }
    }

    /// <summary>
    /// [Issue #293] 領域を画像境界内にクランプ
    /// </summary>
    private static Rectangle ClampRegionToBounds(Rectangle region, int imageWidth, int imageHeight)
    {
        var x = Math.Max(0, region.X);
        var y = Math.Max(0, region.Y);
        var right = Math.Min(imageWidth, region.Right);
        var bottom = Math.Min(imageHeight, region.Bottom);

        return new Rectangle(x, y, right - x, bottom - y);
    }

    /// <summary>
    /// [Issue #293] LockPixelData()を使用してIImageからBitmapを作成（non-async）
    /// </summary>
    /// <remarks>
    /// unsafe コードは async メソッド内で直接使用できないため、別メソッドとして分離。
    /// Marshal.Copy を使用してマネージドコードでピクセルデータをコピーします。
    /// </remarks>
    private static (bool Success, Bitmap? Bitmap) TryCreateBitmapFromPixelData(IImage sourceImage)
    {
        try
        {
            using var pixelLock = sourceImage.LockPixelData();
            var bitmap = new Bitmap(sourceImage.Width, sourceImage.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            var bitmapData = bitmap.LockBits(
                new Rectangle(0, 0, sourceImage.Width, sourceImage.Height),
                System.Drawing.Imaging.ImageLockMode.WriteOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            try
            {
                // マネージドコードでピクセルデータをコピー（unsafeを回避）
                var srcData = pixelLock.Data.ToArray();
                var srcStride = pixelLock.Stride;
                var dstStride = bitmapData.Stride;

                for (int y = 0; y < sourceImage.Height; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(
                        srcData,
                        y * srcStride,
                        bitmapData.Scan0 + y * dstStride,
                        sourceImage.Width * 4);
                }
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }

            return (true, bitmap);
        }
        catch
        {
            // LockPixelData()がサポートされていない場合など
            return (false, null);
        }
    }

    /// <summary>
    /// [Issue #293] OCR結果の座標をROI相対座標から元画像絶対座標に変換
    /// </summary>
    /// <remarks>
    /// [Geminiレビュー対応] 可読性向上: Contour変換を2ステップに分離
    /// Step 1: ROI相対座標 → 元画像絶対座標
    /// Step 2: スケーリング適用（GPU Shaderリサイズ対応）
    /// </remarks>
    private List<Baketa.Core.Abstractions.OCR.OcrTextRegion> TransformOcrResultsToAbsoluteCoordinates(
        OcrResults ocrResults,
        Rectangle roiRegion,
        ProcessingPipelineInput input)
    {
        var transformedRegions = new List<Baketa.Core.Abstractions.OCR.OcrTextRegion>();

        // スケーリング係数を事前計算
        var originalSize = input.OriginalWindowSize;
        var capturedSize = new Size(input.CapturedImage.Width, input.CapturedImage.Height);

        var needsScaling = originalSize != Size.Empty &&
            capturedSize.Width > 0 && capturedSize.Height > 0 &&
            (originalSize.Width != capturedSize.Width || originalSize.Height != capturedSize.Height);

        double scaleX = needsScaling ? (double)originalSize.Width / capturedSize.Width : 1.0;
        double scaleY = needsScaling ? (double)originalSize.Height / capturedSize.Height : 1.0;

        foreach (var textRegion in ocrResults.TextRegions)
        {
            // Step 1: ROI相対座標 → 元画像絶対座標
            var absoluteBounds = new Rectangle(
                roiRegion.X + textRegion.Bounds.X,
                roiRegion.Y + textRegion.Bounds.Y,
                textRegion.Bounds.Width,
                textRegion.Bounds.Height);

            // Step 2: スケーリング適用（Bounds）
            if (needsScaling)
            {
                absoluteBounds = new Rectangle(
                    (int)(absoluteBounds.X * scaleX),
                    (int)(absoluteBounds.Y * scaleY),
                    (int)(absoluteBounds.Width * scaleX),
                    (int)(absoluteBounds.Height * scaleY));
            }

            // Contour変換（2ステップ分離で可読性向上）
            Point[]? transformedContour = null;
            if (textRegion.Contour != null)
            {
                transformedContour = textRegion.Contour.Select(p =>
                {
                    // Step 1: ROI相対座標 → 元画像絶対座標
                    var absoluteX = roiRegion.X + p.X;
                    var absoluteY = roiRegion.Y + p.Y;

                    // Step 2: スケーリング適用
                    return new Point(
                        (int)(absoluteX * scaleX),
                        (int)(absoluteY * scaleY));
                }).ToArray();
            }

            // 変換後のOcrTextRegionを作成
            var transformedRegion = new Baketa.Core.Abstractions.OCR.OcrTextRegion(
                text: textRegion.Text,
                bounds: absoluteBounds,
                confidence: textRegion.Confidence,
                contour: transformedContour,
                direction: textRegion.Direction);

            transformedRegions.Add(transformedRegion);
        }

        _logger.LogDebug("[Issue #293] 座標変換完了: {Count}テキスト領域, ROI={RoiRegion}",
            transformedRegions.Count, roiRegion);

        return transformedRegions;
    }

    /// <summary>
    /// [Issue #380] バッチOCR結果のデデュプリケーション
    /// </summary>
    /// <remarks>
    /// バッチOCR実行時、隣接ブロック境界の拡張（15%+30%）により
    /// 同じテキストが複数の重複領域から検出される問題を解決します。
    ///
    /// アルゴリズム:
    /// 1. 全ペア(i, j)のBBox IoUを計算（O(n²)、通常n&lt;30で問題なし）
    /// 2. IoU &gt;= DeduplicationIoUThreshold の場合:
    ///    a. テキストA.Contains(B) or B.Contains(A) → 長い方を残す
    ///    b. テキスト同一 → Confidenceが高い方を残す
    ///    c. テキストが異なる（IoUが高くても別テキスト）→ 両方残す
    /// 3. 重複フラグが立ったものを除外して返す
    ///
    /// 設計判断: IoUのみでの重複除去（テキスト類似度を無視）は行わない。
    /// 理由: 画面上の同一位置に異なるテキスト（例: ボタンラベルとツールチップ）が
    /// 重なるケースがあり、テキスト内容を考慮しないと意図しない削除が発生するため。
    /// </remarks>
    private List<Baketa.Core.Abstractions.OCR.OcrTextRegion> DeduplicateBatchOcrResults(
        List<Baketa.Core.Abstractions.OCR.OcrTextRegion> regions)
    {
        if (regions.Count <= 1)
            return regions;

        var removed = new HashSet<int>();

        for (int i = 0; i < regions.Count; i++)
        {
            if (removed.Contains(i))
                continue;

            for (int j = i + 1; j < regions.Count; j++)
            {
                if (removed.Contains(j))
                    continue;

                var iou = CalculateRectangleIoU(regions[i].Bounds, regions[j].Bounds);
                if (iou < DeduplicationIoUThreshold)
                    continue;

                var textA = regions[i].Text?.Trim() ?? string.Empty;
                var textB = regions[j].Text?.Trim() ?? string.Empty;

                // テキスト類似度チェック: 同一または包含関係
                if (textA == textB || textA.Contains(textB) || textB.Contains(textA))
                {
                    // 長いテキスト優先、同長ならConfidence優先
                    var keepI = textA.Length > textB.Length ||
                               (textA.Length == textB.Length && regions[i].Confidence >= regions[j].Confidence);
                    removed.Add(keepI ? j : i);

                    _logger.LogDebug(
                        "[Issue #380] 重複OCR結果を除去: IoU={IoU:F2}, Keep='{KeepText}', Remove='{RemoveText}'",
                        iou,
                        keepI ? (textA.Length > 30 ? textA[..30] + "..." : textA) : (textB.Length > 30 ? textB[..30] + "..." : textB),
                        keepI ? (textB.Length > 30 ? textB[..30] + "..." : textB) : (textA.Length > 30 ? textA[..30] + "..." : textA));
                }
                // テキストが異なる場合（IoUが高くても別のテキスト）は両方残す
            }
        }

        return regions.Where((_, idx) => !removed.Contains(idx)).ToList();
    }

    /// <summary>
    /// [Issue #380] 2つのRectangleのIoU（Intersection over Union）を計算
    /// </summary>
    private static float CalculateRectangleIoU(Rectangle a, Rectangle b)
    {
        var intersectX = Math.Max(a.X, b.X);
        var intersectY = Math.Max(a.Y, b.Y);
        var intersectRight = Math.Min(a.Right, b.Right);
        var intersectBottom = Math.Min(a.Bottom, b.Bottom);

        if (intersectRight <= intersectX || intersectBottom <= intersectY)
            return 0f;

        var intersectionArea = (float)(intersectRight - intersectX) * (intersectBottom - intersectY);
        var unionArea = (float)a.Width * a.Height + (float)b.Width * b.Height - intersectionArea;

        return unionArea > 0 ? intersectionArea / unionArea : 0f;
    }

    #endregion

    #region [Issue #500] Detection-Only中間フィルタ

    /// <summary>
    /// [Issue #500] Detection-Only + pHashでフルOCRをスキップ可能か判定
    /// </summary>
    /// <returns>スキップ可能な場合はOcrExecutionResult、フルOCR続行の場合はnull</returns>
    private async Task<OcrExecutionResult?> TrySkipWithDetectionOnlyAsync(
        IImage ocrImage,
        string contextId,
        CancellationToken cancellationToken)
    {
        try
        {
            // 設定無効 or キャッシュ未登録 → フルOCR続行
            if (_detectionBoundsCache == null ||
                _changeDetectionSettings is not { EnableDetectionOnlyFilter: true })
            {
                return null;
            }

            // Detection-Only実行（~200ms）— 初回でもキャッシュ初期化のために実行
            var previousEntry = _detectionBoundsCache.GetPreviousEntry(contextId);

            var detectionResults = await _ocrEngine.DetectTextRegionsAsync(ocrImage, cancellationToken)
                .ConfigureAwait(false);
            var currentBounds = detectionResults.TextRegions
                .Select(r => r.Bounds)
                .Where(b => b.Width > 0 && b.Height > 0)
                .ToArray();

            // [Issue #500] 各矩形のpHash計算（~1ms total）
            var currentHashes = ComputeRegionHashes(ocrImage, currentBounds);

            // キャッシュ更新（Detection成功時は常に）
            _detectionBoundsCache.UpdateEntry(contextId, new DetectionCacheEntry(currentBounds, currentHashes));

            // 前回エントリなし（初回） → フルOCR続行（キャッシュは初期化済み）
            if (previousEntry == null)
            {
                _logger.LogDebug("[Issue #500] 初回Detection-Only実行 - キャッシュ初期化: {Count}個の矩形", currentBounds.Length);
                return null;
            }

            var previousBounds = previousEntry.Bounds;

            // ボックス数差がtolerance超過 → フルOCR続行
            var countDiff = Math.Abs(currentBounds.Length - previousBounds.Length);
            if (countDiff > _changeDetectionSettings.DetectionBoxCountTolerance)
            {
                _logger.LogDebug(
                    "[Issue #500] Detection矩形数差がtolerance超過: current={Current}, previous={Previous}, tolerance={Tolerance}",
                    currentBounds.Length, previousBounds.Length, _changeDetectionSettings.DetectionBoxCountTolerance);
                return null;
            }

            // IoUベース双方向マッチング
            if (!AreDetectionBoundsMatching(currentBounds, previousBounds, _changeDetectionSettings.DetectionIoUThreshold))
            {
                _logger.LogDebug(
                    "[Issue #500] Detection矩形IoUマッチング失敗 - フルOCR続行: current={Current}個, previous={Previous}個",
                    currentBounds.Length, previousBounds.Length);
                return null;
            }

            // [Issue #500] IoUマッチした矩形ペアのpHash比較（コンテンツ変化検出）
            if (!AreRegionHashesMatching(
                    currentBounds, currentHashes,
                    previousBounds, previousEntry.RegionHashes,
                    _changeDetectionSettings.DetectionIoUThreshold,
                    _changeDetectionSettings.DetectionRegionHashThreshold))
            {
                _logger.LogInformation(
                    "[Issue #500] pHash不一致 - 位置同一だがコンテンツ変化あり: ContextId={ContextId}",
                    contextId);
                return null;
            }

            // 全マッチ → スキップ
            return new OcrExecutionResult
            {
                DetectedText = string.Empty,
                DetectionOnlySkipped = true,
                ProcessingTime = TimeSpan.Zero,
                Success = true
            };
        }
        catch (Exception ex)
        {
            // 例外発生 → フルOCR続行（安全側に倒す）
            _logger.LogWarning(ex, "[Issue #500] Detection-Onlyフィルタで例外発生 - フルOCR続行");
            return null;
        }
    }

    /// <summary>
    /// [Issue #500] 各矩形領域のpHashを計算
    /// </summary>
    private string[] ComputeRegionHashes(IImage image, Rectangle[] bounds)
    {
        if (_perceptualHashService == null || bounds.Length == 0)
            return [];

        var hashes = new string[bounds.Length];
        for (int i = 0; i < bounds.Length; i++)
        {
            try
            {
                hashes[i] = _perceptualHashService.ComputeHashForRegion(
                    image, bounds[i], HashAlgorithmType.DifferenceHash);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[Issue #500] pHash計算失敗 - 矩形{Index}: {Bounds}", i, bounds[i]);
                hashes[i] = string.Empty;
            }
        }
        return hashes;
    }

    /// <summary>
    /// [Issue #500] IoUマッチした矩形ペアのpHash類似度を検証
    /// </summary>
    internal bool AreRegionHashesMatching(
        Rectangle[] currentBounds, string[] currentHashes,
        Rectangle[] previousBounds, string[] previousHashes,
        float iouThreshold, float hashThreshold)
    {
        // pHashサービス未登録 → pHash検証スキップ（IoUのみで判定）
        if (_perceptualHashService == null)
            return true;

        // ハッシュ配列が空 → pHash検証スキップ
        if (currentHashes.Length == 0 || previousHashes.Length == 0)
            return true;

        for (int i = 0; i < currentBounds.Length; i++)
        {
            if (i >= currentHashes.Length || string.IsNullOrEmpty(currentHashes[i]))
                continue;

            int bestMatchIdx = -1;
            float bestIoU = 0f;
            for (int j = 0; j < previousBounds.Length; j++)
            {
                var iou = CalculateRectangleIoU(currentBounds[i], previousBounds[j]);
                if (iou > bestIoU) { bestIoU = iou; bestMatchIdx = j; }
            }

            if (bestMatchIdx < 0 || bestIoU < iouThreshold)
                return false;

            if (bestMatchIdx >= previousHashes.Length || string.IsNullOrEmpty(previousHashes[bestMatchIdx]))
                continue;

            var similarity = _perceptualHashService.CompareHashes(
                currentHashes[i], previousHashes[bestMatchIdx],
                HashAlgorithmType.DifferenceHash);

            if (similarity < hashThreshold)
                return false;
        }
        return true;
    }

    /// <summary>
    /// [Issue #500] 2つのDetection矩形配列が双方向でIoUマッチングするか判定
    /// </summary>
    internal static bool AreDetectionBoundsMatching(
        Rectangle[] current,
        Rectangle[] previous,
        float iouThreshold)
    {
        // 両方空 → マッチ
        if (current.Length == 0 && previous.Length == 0)
            return true;

        // 片方空、他方非空 → アンマッチ
        if (current.Length == 0 || previous.Length == 0)
            return false;

        // current→previous: 全currentに対してpreviousのいずれかとIoU≧閾値
        foreach (var c in current)
        {
            var bestIoU = previous.Max(p => CalculateRectangleIoU(c, p));
            if (bestIoU < iouThreshold)
                return false;
        }

        // previous→current: 全previousに対してcurrentのいずれかとIoU≧閾値
        foreach (var p in previous)
        {
            var bestIoU = current.Max(c => CalculateRectangleIoU(p, c));
            if (bestIoU < iouThreshold)
                return false;
        }

        return true;
    }

    #endregion

#if DEBUG
    /// <summary>
    /// 🎯 [ROI_IMAGE_SAVE] ROI実行時にテキスト検出領域枠をつけた画像を保存
    /// ⚠️ デバッグビルド専用機能 - 本番ビルドでは無効化
    /// </summary>
    /// <param name="ocrImage">OCR処理に使用された画像</param>
    /// <param name="textChunks">検出されたテキストチャンク</param>
    /// <param name="contextId">処理コンテキストID</param>
    /// <param name="processingTime">処理時間</param>
    private async Task SaveRoiImageWithTextBounds(IImage ocrImage, List<object> textChunks, string contextId, TimeSpan processingTime)
    {
        try
        {
            // 🔍 [ULTRATHINK_PHASE20] 詳細ログ追加 - AppData ROI画像破損調査
            _logger?.LogWarning("🔍 [ULTRATHINK_PHASE20] SaveRoiImageWithTextBounds開始 - ocrImage型: {ImageType}, Size: {Width}x{Height}, テキスト領域数: {ChunkCount}",
                ocrImage.GetType().Name, ocrImage.Width, ocrImage.Height, textChunks.Count);

            var imageBytes = await ocrImage.ToByteArrayAsync().ConfigureAwait(false);
            _logger?.LogWarning("🔍 [ULTRATHINK_PHASE20] ToByteArrayAsync完了 - バイト数: {ByteCount}", imageBytes.Length);

            using var memoryStream = new MemoryStream(imageBytes);
            using var sourceBitmap = new Bitmap(memoryStream);
            _logger?.LogWarning("🔍 [ULTRATHINK_PHASE20] sourceBitmap作成完了 - Size: {Width}x{Height}, PixelFormat: {Format}",
                sourceBitmap.Width, sourceBitmap.Height, sourceBitmap.PixelFormat);

            // 🔥 [ARRAYPOOL_FIX] SafeImage ArrayPool破損回避 - 防御的Bitmapクローン作成
            // 問題: ReferencedSafeImage.ToByteArrayAsync()がArrayPoolメモリから読み取り
            //       SafeImage.Dispose()後にArrayPool.Return()されたメモリを参照する可能性
            // 解決策: 即座にBitmapをクローンし、ArrayPoolから完全に独立したコピーを作成
            using var bitmap = new Bitmap(sourceBitmap.Width, sourceBitmap.Height, sourceBitmap.PixelFormat);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.DrawImage(sourceBitmap, 0, 0);
                _logger?.LogWarning("🔍 [ULTRATHINK_PHASE20] Bitmapクローン作成完了 - DrawImage(sourceBitmap, 0, 0) 実行");
            }

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
                if (chunk is Baketa.Core.Abstractions.OCR.TextDetection.TextRegion textRegion)
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
            _logger?.LogWarning("🔍 [ULTRATHINK_PHASE20] PNG保存直前 - bitmap: {Width}x{Height}, ファイル: {FilePath}",
                bitmap.Width, bitmap.Height, filePath);

            bitmap.Save(filePath, System.Drawing.Imaging.ImageFormat.Png);

            _logger?.LogWarning("🔍 [ULTRATHINK_PHASE20] PNG保存完了 - ファイル: {FileName}, テキスト領域数: {RegionCount}",
                fileName, regionCount);

            // ファイル情報確認
            var fileInfo = new System.IO.FileInfo(filePath);
            _logger?.LogWarning("🔍 [ULTRATHINK_PHASE20] 保存ファイル情報 - サイズ: {FileSize} bytes, 存在: {Exists}",
                fileInfo.Length, fileInfo.Exists);

            _logger.LogInformation("🎯 [ROI_IMAGE_SAVE] ROI画像保存完了 - ファイル: {FileName}, 領域数: {RegionCount}",
                fileName, regionCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🎯 [ROI_IMAGE_SAVE] ROI画像保存でエラー");
            _logger?.LogDebug($"❌ [ROI_IMAGE_SAVE] ROI画像保存エラー: {ex.Message}");
        }
    }
#endif

    /// <summary>
    /// [Issue #397] リソース解放: テキストブロックキャッシュのクリア
    /// </summary>
    public void Dispose()
    {
        _previousTextBlocksPerContext.Clear();
        _partialOcrCycleCount.Clear(); // [Issue #397] P0-3
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

    // 🚀 [Issue #193] InlineAdapterはリサイズを行わないため、常にWidth/Heightと同じ
    public int OriginalWidth => Width;
    public int OriginalHeight => Height;

    public InlineImageToWindowsImageAdapter(IImage underlyingImage, ILogger logger)
    {
        _underlyingImage = underlyingImage ?? throw new ArgumentNullException(nameof(underlyingImage));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogDebug("🔄 [PHASE77.6] InlineImageToWindowsImageAdapter 作成 - Size: {Width}x{Height}", Width, Height);
    }

    // 🔥 [PHASE5.2C] async化 + ArrayPool対応によりスレッド爆発とメモリリークを防止
    public async Task<Bitmap> GetBitmapAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_cachedBitmap != null)
        {
            return _cachedBitmap;
        }

        byte[]? pooledArray = null;
        try
        {
            _logger.LogDebug("🔄 [PHASE5.2C] IImage → Bitmap async変換開始（ArrayPool使用）");

            // 🔥 [PHASE5.2C] ArrayPool<byte>使用でメモリリーク防止
            int actualLength;
            (pooledArray, actualLength) = await _underlyingImage.ToPooledByteArrayWithLengthAsync(cancellationToken).ConfigureAwait(false);

            // 🔥 [PHASE5.2C_FIX] actualLengthで正確なサイズのMemoryStreamを作成
            // 重要: MemoryStream/ArrayPoolへの依存を切断するため、Bitmapクローンを作成
            using var memoryStream = new MemoryStream(pooledArray, 0, actualLength, writable: false);
            using var tempBitmap = new Bitmap(memoryStream);

            // 🔥 [PHASE5.2C_FIX] Bitmapクローン作成でMemoryStream依存を切断
            // 理由: MemoryStream Dispose後もBitmapが有効であることを保証
            _cachedBitmap = new Bitmap(tempBitmap);

            _logger.LogDebug("✅ [PHASE5.2C] Bitmap async変換成功 - Size: {Width}x{Height}",
                _cachedBitmap.Width, _cachedBitmap.Height);

            return _cachedBitmap;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE5.2C] IImage → Bitmap async変換失敗: {ErrorMessage}", ex.Message);
            throw new InvalidOperationException($"Failed to convert IImage to Bitmap: {ex.Message}", ex);
        }
        finally
        {
            // 🔥 [PHASE5.2C] ArrayPool<byte>から借りた配列を必ず返却（メモリリーク防止）
            if (pooledArray != null)
            {
                ArrayPool<byte>.Shared.Return(pooledArray);
            }
        }
    }

    // 🔥 [PHASE5.2] 同期版GetBitmap()は後方互換性のために残すが、内部でGetBitmapAsync()を呼び出す
    // TODO: Phase 5.2C-Step4で全呼び出し側をasync化した後、この同期版を削除する
    [Obsolete("Use GetBitmapAsync instead. This synchronous method will be removed in Phase 5.2C-Step4.")]
    public Bitmap GetBitmap()
    {
        return GetBitmapAsync().GetAwaiter().GetResult();
    }

    // 🔥 [PHASE5.2] async化対応
    public async Task<Image> GetNativeImageAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return await GetBitmapAsync(cancellationToken).ConfigureAwait(false);
    }

    // 後方互換性のために同期版を残す（Obsolete）
    [Obsolete("Use GetNativeImageAsync instead.")]
    public Image GetNativeImage()
    {
        return GetBitmap();
    }

    // 🔥 [PHASE5.2] async化完全対応（既存asyncメソッドを修正）
    public async Task SaveAsync(string path, System.Drawing.Imaging.ImageFormat? format = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var bitmap = await GetBitmapAsync(cancellationToken).ConfigureAwait(false);
        bitmap.Save(path, format ?? System.Drawing.Imaging.ImageFormat.Png);
    }

    public async Task<IWindowsImage> ResizeAsync(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var resizedImage = await _underlyingImage.ResizeAsync(width, height);
        return new InlineImageToWindowsImageAdapter(resizedImage, _logger);
    }

    // 🔥 [PHASE5.2] async化完全対応
    public async Task<IWindowsImage> CropAsync(Rectangle rectangle, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var bitmap = await GetBitmapAsync(cancellationToken).ConfigureAwait(false);
        // 🔧 [MEMORY_LEAK_FIX] using文でBitmapを確実に破棄（2回目のOCR実行時のメモリ不足エラー対策）
        using var croppedBitmap = new Bitmap(rectangle.Width, rectangle.Height);

        using (var graphics = Graphics.FromImage(croppedBitmap))
        {
            // 🔧 [CRITICAL_FIX] Graphics.DrawImage引数修正 - Segmentation Fault原因 (Line 601)
            // 正しいシグネチャ: DrawImage(Image, Rectangle destRect, int srcX, srcY, srcWidth, srcHeight, GraphicsUnit)
            graphics.DrawImage(bitmap,
                new System.Drawing.Rectangle(0, 0, rectangle.Width, rectangle.Height),  // 描画先の矩形
                rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height,            // ソース領域
                GraphicsUnit.Pixel);
        }

        using var memoryStream = new MemoryStream();
        croppedBitmap.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
        var croppedBytes = memoryStream.ToArray();

        // IImageFactoryを介してIImageを作成する必要があるが、循環参照回避のため簡易実装
        throw new NotImplementedException("CropAsync requires IImageFactory which would create circular reference");
    }

    // 🔥 [PHASE5.2] async化完全対応（既存asyncメソッドを修正）
    public async Task<byte[]> ToByteArrayAsync(System.Drawing.Imaging.ImageFormat? format = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var bitmap = await GetBitmapAsync(cancellationToken).ConfigureAwait(false);
        using var memoryStream = new MemoryStream();
        bitmap.Save(memoryStream, format ?? System.Drawing.Imaging.ImageFormat.Png);
        return memoryStream.ToArray();
    }

    /// <summary>
    /// 🔥 [PHASE7.2] LockPixelData実装 - IWindowsImageインターフェース完全対応
    /// Bitmap.LockBits()を使用してゼロコピーピクセルアクセスを提供
    ///
    /// 実装詳細:
    /// - GetBitmap()で_cachedBitmapを取得（既にキャッシュ済みの場合は再利用）
    /// - Bitmap.LockBits()でBGRA32形式のピクセルデータをロック
    /// - PixelDataLockを返してusingパターンで自動UnlockBits()実行
    ///
    /// Phase 3実装保留を解消: OCRパイプラインでの使用が可能に
    /// WindowsImage.LockPixelData()と同じ実装パターンを採用
    /// </summary>
    public PixelDataLock LockPixelData()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // 🔥 同期版GetBitmap()を使用（LockPixelDataは同期メソッドのため）
        // _cachedBitmapが既にある場合は再利用、ない場合はGetBitmapAsync()を同期実行
#pragma warning disable CS0618 // 型またはメンバーが旧型式です
        var bitmap = GetBitmap();
#pragma warning restore CS0618

        // Bitmap.LockBits()でBGRA32形式のピクセルデータをロック（WindowsImageと同じFormat32bppArgb）
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var bitmapData = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        try
        {
            // ピクセルデータへの直接ポインタ取得（WindowsImageと同じパターン）
            unsafe
            {
                var ptr = (byte*)bitmapData.Scan0.ToPointer();
                var length = Math.Abs(bitmapData.Stride) * bitmapData.Height;
                var span = new ReadOnlySpan<byte>(ptr, length);

                _logger.LogDebug("🔥 [PHASE7.2] PixelDataLock作成成功 - Size: {Width}x{Height}, Stride: {Stride}",
                    bitmap.Width, bitmap.Height, bitmapData.Stride);

                // PixelDataLockを作成（Dispose時にUnlockBitsが自動実行される）
                return new PixelDataLock(
                    span,                                   // data: ReadOnlySpan<byte>
                    bitmapData.Stride,                      // stride: int
                    () => bitmap.UnlockBits(bitmapData)     // unlockAction: Action
                );
            }
        }
        catch
        {
            // エラー時は即座にUnlockBits実行
            bitmap.UnlockBits(bitmapData);
            throw;
        }
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
