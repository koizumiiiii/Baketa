using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Baketa.Core.Abstractions.Configuration;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Processing;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.UI.Overlays; // 🔧 [OVERLAY_UNIFICATION]
using Baketa.Core.Abstractions.Platform.Windows; // 🔧 [Issue #275] IWindowsImage.OriginalWidth/Height
using Baketa.Infrastructure.Platform.Adapters; // 🔧 [Issue #275] WindowsImageAdapter.OriginalWidth/Height
// [Issue #230] テキストベース変化検知 - 画面点滅時の不要なOCR再実行を防止
using Baketa.Core.Events.Diagnostics;
using Baketa.Core.Events.EventTypes;
using Baketa.Core.Logging;
using Baketa.Core.Models.OCR;
using Baketa.Core.Performance;
using Baketa.Core.Settings;
using Baketa.Core.Translation.Abstractions; // [Issue #290] IFallbackOrchestrator, ImageTranslationRequest
using Baketa.Core.Translation.Models;
using Baketa.Core.Abstractions.License; // [Issue #290] ILicenseManager
using Language = Baketa.Core.Models.Translation.Language;
using Baketa.Core.License.Models; // [Issue #290] FeatureType
using Baketa.Core.Abstractions.Roi; // [Issue #293] IRoiManager
using Baketa.Core.Abstractions.Text; // [Issue #293] IGateStrategy
using Baketa.Core.Models.Roi; // [Issue #293] NormalizedRect
using Baketa.Core.Models.Text; // [Issue #293] TextChangeWithGateResult, GateRegionInfo
using IWindowManager = Baketa.Core.Abstractions.Platform.IWindowManager; // [Issue #293] ウィンドウ情報取得用
using Baketa.Core.Utilities;
// [Issue #392] Mechanism A/B削除: テキスト消失/変化検知はDetection段階のIsTextDisappearance()に移行
using System.Collections.Concurrent; // [Issue #397] PreviousOcrTextキャッシュ用
using System.Diagnostics; // [Issue #290] Fork-Join計測用
// NOTE: [PP-OCRv5削除] BatchProcessing参照削除
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Application.Services.Translation;

/// <summary>
/// 座標ベース翻訳表示サービス
/// バッチOCR処理と複数ウィンドウオーバーレイ表示を統合した座標ベース翻訳システム
/// </summary>
public sealed class CoordinateBasedTranslationService : IDisposable, IEventProcessor<Baketa.Core.Events.Translation.AggregatedChunksFailedEvent>
{
    private readonly ITranslationProcessingFacade _processingFacade;
    private readonly IConfigurationFacade _configurationFacade;
    // 🚀 [Phase 2.1] Service Locator Anti-pattern完全除去: _serviceProviderフィールド削除
    private readonly ILogger<CoordinateBasedTranslationService>? _logger;
    private readonly IEventAggregator? _eventAggregator;
    private readonly IStreamingTranslationService? _streamingTranslationService;
    private readonly ITextChunkAggregatorService _textChunkAggregatorService;
    private readonly ISmartProcessingPipelineService _pipelineService; // 🎯 [OPTION_A] 段階的フィルタリングパイプライン統合
    private readonly ITextChangeDetectionService? _textChangeDetectionService; // [Issue #230] テキストベース変化検知
    private readonly ITranslationModeService? _translationModeService; // 🔧 [SINGLESHOT_FIX] Singleshotモード判定用
    // [Issue #290] Fork-Join並列実行用の依存関係
    private readonly IFallbackOrchestrator? _fallbackOrchestrator;
    private readonly ILicenseManager? _licenseManager;
    private readonly ICloudTranslationAvailabilityService? _cloudTranslationAvailabilityService; // [Issue #290] Cloud翻訳可用性チェック
    private readonly IRoiManager? _roiManager; // [Issue #293] ROI学習マネージャー（ヒートマップ値取得用）
    private readonly IWindowManager? _windowManager; // [Issue #293] ウィンドウ情報取得用
    private readonly IOptionsMonitor<ImageChangeDetectionSettings>? _imageChangeSettings; // [Issue #401] 画面安定化設定
    private readonly ICloudTranslationCache? _cloudTranslationCache; // [Issue #415] Cloud翻訳キャッシュ
    private readonly IDetectionBoundsCache? _detectionBoundsCache; // [Issue #508] Detection-Onlyキャッシュからのフォールバックヒント
    private bool _disposed;

    // [Issue #401] ヒステリシス: ウィンドウごとの画面安定化スキップ状態
    private readonly ConcurrentDictionary<IntPtr, bool> _screenStabilizationActive = new();

    // 🔥 [PHASE13.1_P1] スレッドセーフなChunkID生成カウンター（衝突リスク完全排除）
    private static int _nextChunkId = 1000000;

    // [Issue #397] ウィンドウハンドルごとの前回OCRテキストキャッシュ（テキスト変化検知用）
    private readonly ConcurrentDictionary<IntPtr, string> _previousOcrTextCache = new();

    // [Issue #427] ウィンドウハンドルごとの翻訳履歴バッファ（文脈維持用、最大5件）
    private const int MaxTranslationHistoryCount = 5;
    private readonly ConcurrentDictionary<IntPtr, List<TranslationHistoryEntry>> _translationHistoryBuffer = new();

    // [Issue #429] 前回サイクルのOCRヒントキャッシュ（次サイクルのFork-Joinに注入）
    // ゲーム画面ではテキスト位置が連続フレームでほぼ同じため、前回結果が有効なヒントになる
    private readonly ConcurrentDictionary<IntPtr, OcrHints> _previousOcrHintsCache = new();

    // [Issue #508] Shot翻訳前Detection-Onlyの結果を直接受け渡すフィールド
    // DetectionBoundsCacheを経由しないことで、Detection-Onlyフィルタ（#500）の誤スキップを防止
    private volatile System.Drawing.Rectangle[]? _precomputedHintBounds;
    private volatile int _precomputedHintImageHeight; // Detection実行時の画像高さ（座標系補正用）


    // [Issue #381] Cloud AI翻訳用画像の最大長辺（ピクセル）
    // Gemini Vision APIの処理時間はピクセル数に比例するため、テキスト翻訳に十分な解像度に縮小
    private const int CloudImageMaxDimension = 960;

    /// <summary>
    /// [Issue #410] 翻訳開始時にキャッシュをリセット（Shot→Live遷移時の誤判定防止）
    /// </summary>
    public void ResetTranslationState()
    {
        _screenStabilizationActive.Clear();
        _previousOcrTextCache.Clear();
        _cloudTranslationCache?.ClearAll(); // [Issue #415] Cloud翻訳キャッシュもクリア
        _translationHistoryBuffer.Clear(); // [Issue #427] 翻訳履歴もクリア
        _previousOcrHintsCache.Clear(); // [Issue #429] OCRヒントキャッシュもクリア
        _logger?.LogDebug("[Issue #410] 翻訳状態リセット: 安定化フラグ・OCRテキストキャッシュ・翻訳履歴・OCRヒントをクリア");
    }

    private const int CloudJpegQuality = 85;
    private const string CloudImageMimeType = "image/jpeg";

    public CoordinateBasedTranslationService(
        ITranslationProcessingFacade processingFacade,
        IConfigurationFacade configurationFacade,
        IStreamingTranslationService? streamingTranslationService,
        ITextChunkAggregatorService textChunkAggregatorService,
        ISmartProcessingPipelineService pipelineService, // 🎯 [OPTION_A] 段階的フィルタリングパイプライン
        ITextChangeDetectionService? textChangeDetectionService = null, // [Issue #230] テキストベース変化検知
        ITranslationModeService? translationModeService = null, // 🔧 [SINGLESHOT_FIX] Singleshotモード判定用
        // [Issue #290] Fork-Join並列実行用の依存関係（オプショナル）
        IFallbackOrchestrator? fallbackOrchestrator = null,
        ILicenseManager? licenseManager = null,
        ICloudTranslationAvailabilityService? cloudTranslationAvailabilityService = null, // [Issue #290] Cloud翻訳可用性チェック
        IRoiManager? roiManager = null, // [Issue #293] ROI学習マネージャー（ヒートマップ値取得用）
        IWindowManager? windowManager = null, // [Issue #293] ウィンドウ情報取得用
        IOptionsMonitor<ImageChangeDetectionSettings>? imageChangeSettings = null, // [Issue #401] 画面安定化設定
        ICloudTranslationCache? cloudTranslationCache = null, // [Issue #415] Cloud翻訳キャッシュ
        IDetectionBoundsCache? detectionBoundsCache = null, // [Issue #508] Detection-Onlyキャッシュからのフォールバックヒント
        ILogger<CoordinateBasedTranslationService>? logger = null)
    {
        _processingFacade = processingFacade ?? throw new ArgumentNullException(nameof(processingFacade));
        _configurationFacade = configurationFacade ?? throw new ArgumentNullException(nameof(configurationFacade));
        _streamingTranslationService = streamingTranslationService;
        _textChunkAggregatorService = textChunkAggregatorService ?? throw new ArgumentNullException(nameof(textChunkAggregatorService));
        _pipelineService = pipelineService ?? throw new ArgumentNullException(nameof(pipelineService)); // 🎯 [OPTION_A] パイプラインサービス注入
        _textChangeDetectionService = textChangeDetectionService; // [Issue #230] オプショナル（nullでも機能する）
        _translationModeService = translationModeService; // 🔧 [SINGLESHOT_FIX] Singleshotモード判定用
        // [Issue #290] Fork-Join並列実行用の依存関係
        _fallbackOrchestrator = fallbackOrchestrator;
        _licenseManager = licenseManager;
        _cloudTranslationAvailabilityService = cloudTranslationAvailabilityService;
        _roiManager = roiManager; // [Issue #293] ROI学習マネージャー（ヒートマップ値取得用）
        _windowManager = windowManager; // [Issue #293] ウィンドウ情報取得用
        _imageChangeSettings = imageChangeSettings; // [Issue #401] 画面安定化設定
        _cloudTranslationCache = cloudTranslationCache; // [Issue #415] Cloud翻訳キャッシュ
        _detectionBoundsCache = detectionBoundsCache; // [Issue #508] Detection-Onlyキャッシュからのフォールバックヒント
        _logger = logger;

        // 🚀 [Phase 2.1] Service Locator Anti-pattern除去: ファサード経由でEventAggregatorを取得
        _eventAggregator = _configurationFacade.EventAggregator;

        if (_streamingTranslationService != null)
        {
            Console.WriteLine("🔥 [STREAMING] ストリーミング翻訳サービスが利用可能");
        }

        // 🎯 [TIMED_AGGREGATOR] TimedChunkAggregator統合完了
        Console.WriteLine("🎯 [TIMED_AGGREGATOR] TimedChunkAggregator統合完了 - 時間軸集約システム有効化");
        _logger?.LogInformation("🎯 TimedChunkAggregator統合完了 - 翻訳品質40-60%向上機能有効化");

        // 🔥 [FALLBACK] AggregatedChunksFailedEventハンドラー登録
        if (_eventAggregator != null)
        {
            _eventAggregator.Subscribe<Baketa.Core.Events.Translation.AggregatedChunksFailedEvent>(this);
            _logger?.LogInformation("✅ [FALLBACK] AggregatedChunksFailedEventハンドラー登録完了");
        }

        // 統一ログを使用（重複したConsole.WriteLineを統合）
        _configurationFacade.Logger?.LogDebug("CoordinateBasedTranslationService", "サービス初期化完了", new
        {
            EventAggregatorType = _configurationFacade.EventAggregator.GetType().Name,
            EventAggregatorHash = _configurationFacade.EventAggregator.GetHashCode(),
            EventAggregatorReference = _configurationFacade.EventAggregator.ToString()
        });

        // 統一設定サービス注入時の設定値確認
        try
        {
            var translationSettings = _configurationFacade.SettingsService.GetTranslationSettings();
            _configurationFacade.Logger?.LogInformation("CoordinateBasedTranslationService", "統一設定サービス注入完了", new
            {
                translationSettings.AutoDetectSourceLanguage,
                translationSettings.DefaultSourceLanguage,
                translationSettings.DefaultTargetLanguage
            });
        }
        catch (Exception ex)
        {
            _configurationFacade.Logger?.LogError("CoordinateBasedTranslationService", "設定値の取得に失敗", ex);
        }

        _logger?.LogInformation("🚀 CoordinateBasedTranslationService initialized - Hash: {Hash}", this.GetHashCode());
    }

    /// <summary>
    /// OCRテキストに基づく動的言語検出を含む言語ペア取得
    /// </summary>
    private (Language sourceLanguage, Language targetLanguage) GetLanguagesFromSettings(string? ocrText = null)
    {
        try
        {
            // 🚨 [SETTINGS_BASED_ONLY] 設定ファイルの値のみを使用（動的言語検出削除）
            var translationSettings = _configurationFacade.SettingsService.GetTranslationSettings();

            // 🚨 [SIMPLIFIED] AutoDetectSourceLanguage削除 - 常に設定ファイルの値を使用
            var sourceLanguageCode = translationSettings.DefaultSourceLanguage;
            var targetLanguageCode = translationSettings.DefaultTargetLanguage;

            Console.WriteLine($"🔍 [SETTINGS_BASED] 設定ファイルベースの言語ペア: {sourceLanguageCode} → {targetLanguageCode}");

            _logger?.LogDebug("🔍 [SETTINGS_BASED] 設定ファイルベースの言語ペア: {Source} → {Target}", sourceLanguageCode, targetLanguageCode);

            // Language enumに変換（統一ユーティリティ使用）
            var sourceLanguage = LanguageCodeConverter.ToLanguageEnum(sourceLanguageCode, Language.Japanese);
            var targetLanguage = LanguageCodeConverter.ToLanguageEnum(targetLanguageCode, Language.English);

            Console.WriteLine($"🌍 [COORDINATE_SETTINGS] 最終言語設定: {sourceLanguageCode} → {targetLanguageCode}");
            _logger?.LogDebug("🌍 [COORDINATE_SETTINGS] 最終言語設定: {Source} → {Target}", sourceLanguageCode, targetLanguageCode);

            return (sourceLanguage, targetLanguage);
        }
        catch (Exception ex)
        {
            _configurationFacade.Logger?.LogError("CoordinateBasedTranslationService", "設定取得エラー、デフォルト値を使用", ex);
            // エラー時はデフォルト値を使用
            return (Language.Japanese, Language.English);
        }
    }


    /// <summary>
    /// [Issue #508] Shot翻訳前のDetection-Only結果を直接設定する。
    /// DetectionBoundsCacheを経由しないことで、Detection-Onlyフィルタ（#500）の誤スキップを防止。
    /// 1回使用後に自動クリアされる。
    /// </summary>
    /// <param name="bounds">Detection-Only結果のバウンディングボックス配列</param>
    /// <param name="imageHeight">Detection実行時の画像高さ（座標系補正用）</param>
    public void SetPrecomputedHintBounds(System.Drawing.Rectangle[] bounds, int imageHeight)
    {
        _precomputedHintBounds = bounds;
        _precomputedHintImageHeight = imageHeight;
    }

    /// <summary>
    /// 座標ベース翻訳処理を実行
    /// バッチOCR処理 → 複数ウィンドウオーバーレイ表示の統合フロー
    /// </summary>
    /// <param name="options">パイプライン処理オプション（nullの場合はデフォルト設定を使用）</param>
    /// <param name="preExecutedOcrResult">🔥 [Issue #193/#194] キャプチャ時に実行済みのOCR結果（二重OCR防止）</param>
    public async Task ProcessWithCoordinateBasedTranslationAsync(
        IAdvancedImage image,
        IntPtr windowHandle,
        Baketa.Core.Models.Processing.ProcessingPipelineOptions? options = null,
        Baketa.Core.Abstractions.OCR.OcrResults? preExecutedOcrResult = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            _logger?.LogInformation("🎯 座標ベース翻訳処理開始 - 画像: {Width}x{Height}, ウィンドウ: 0x{Handle:X}",
                image.Width, image.Height, windowHandle.ToInt64());
            _logger?.LogDebug($"🎯 座標ベース翻訳処理開始 - 画像: {image.Width}x{image.Height}, ウィンドウ: 0x{windowHandle.ToInt64():X}");
            Console.WriteLine($"🎯 [DEBUG] ProcessWithCoordinateBasedTranslationAsync開始 - 画像: {image.Width}x{image.Height}");
            // 🔥 [FILE_CONFLICT_FIX_3] ファイルアクセス競合回避のためILogger使用
            _logger?.LogDebug("🎯 [DEBUG] ProcessWithCoordinateBasedTranslationAsync開始 - 画像: {Width}x{Height}", image.Width, image.Height);

            // 🔥🔥🔥 [ULTRA_DEBUG] ProcessWithCoordinateBasedTranslationAsync開始
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}→🔥🔥🔥 [COORD_TRANSLATE] ProcessWithCoordinateBasedTranslationAsync開始 - 画像: {image.Width}x{image.Height}{Environment.NewLine}");
            }
            catch { /* ログ失敗は無視 */ }

            // 🔍 [PHASE12.2_TRACE] トレースログ1: メソッド開始直後
            _logger?.LogDebug("🔍 [PHASE12.2_TRACE] TRACE-1: メソッド開始 - OCR処理前");
            _logger?.LogInformation("🔍 [PHASE12.2_TRACE] TRACE-1: メソッド開始 - OCR処理前");

            // バッチOCR処理でテキストチャンクを取得（詳細時間測定）
            var ocrMeasurement = new PerformanceMeasurement(
                MeasurementType.BatchOcrProcessing,
                $"バッチOCR処理 - 画像:{image.Width}x{image.Height}")
                .WithAdditionalInfo($"WindowHandle:0x{windowHandle.ToInt64():X}");

            // NOTE: [PP-OCRv5削除] BatchOcrProcessor参照削除
            // Surya OCRではgRPCベースのため、PaddleOCR失敗カウンターリセットは不要

            // ============================================================
            // [Issue #290] Fork-Join並列実行: OCRとCloud AI翻訳を同時に開始
            // ============================================================
            Task<FallbackTranslationResult?>? forkJoinCloudTask = null;
            // [Issue #397] Fork-Join用CTS: テキスト変化なし時にCloud翻訳をキャンセルしてトークン浪費を防止
            using var forkJoinCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            string? forkJoinImageBase64 = null;
            int forkJoinContextWidth = 0;
            int forkJoinContextHeight = 0;
            int forkJoinCloudImageWidth = 0;  // [Issue #381] 実際に送信する画像サイズ
            int forkJoinCloudImageHeight = 0;

            // Fork-Join用の画像データを事前に抽出
            try
            {
                // [Issue #381] Cloud AI用に解像度最適化 + JPEG変換
                var cloudData = await PrepareCloudImageDataAsync(image).ConfigureAwait(false);
                forkJoinImageBase64 = cloudData.Base64;
                forkJoinCloudImageWidth = cloudData.Width;
                forkJoinCloudImageHeight = cloudData.Height;

                // [Issue #275] OriginalWidth/OriginalHeightを使用（オーバーレイ座標計算用）
                (forkJoinContextWidth, forkJoinContextHeight) = image switch
                {
                    IWindowsImage windowsImage => (windowsImage.OriginalWidth, windowsImage.OriginalHeight),
                    WindowsImageAdapter adapter => (adapter.OriginalWidth, adapter.OriginalHeight),
                    _ => (image.Width, image.Height)
                };
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "[Issue #290] Fork-Join用画像データ抽出失敗");
            }

            // Fork-Join条件チェック＆Cloud AI翻訳タスク開始（OCRと並列実行）
            // [Issue #415] 画像ハッシュを上位スコープで保持（キャッシュチェック＋更新で再利用）
            long forkJoinImageHash = 0;
            FallbackTranslationResult? cachedCloudResult = null;

            // [Issue #431] 画面安定化中はFork-Joinを開始しない（Cloud APIトークン浪費防止）
            // 既存の#401安定化ガード（line 392+）はパイプライン実行後にチェックするため、
            // Fork-Joinが先に発火してCloud APIコールが送信済みになる問題を解決
            var isStabilizationActiveForForkJoin = _screenStabilizationActive.GetValueOrDefault(windowHandle, false);
            if (isStabilizationActiveForForkJoin)
            {
                _logger?.LogInformation(
                    "[Issue #431] 画面安定化中 - Fork-Join Cloud翻訳をスキップ（トークン浪費防止）");
            }

            if (!isStabilizationActiveForForkJoin &&
                ShouldUseForkJoinParallelExecution(forkJoinImageBase64, forkJoinContextWidth, forkJoinContextHeight))
            {
                // [Issue #415] 画像ハッシュによるCloud APIコール抑制
                if (_cloudTranslationCache != null)
                {
                    forkJoinImageHash = _cloudTranslationCache.ComputeImageHash(image.GetImageMemory());
                    if (_cloudTranslationCache.TryGetCachedResult(windowHandle, forkJoinImageHash, out cachedCloudResult))
                    {
                        _logger?.LogInformation(
                            "[Issue #415] 画像ハッシュ一致 - Cloud APIスキップ（キャッシュ結果を再利用）");
                    }
                }

                if (cachedCloudResult == null)
                {
                    // キャッシュミス → 通常のCloud APIコール
                    _logger?.LogInformation("🚀 [Issue #290] Fork-Join開始: OCR || Cloud AI を並列実行");

                    // [Issue #429] 前回サイクルのOCRヒントキャッシュを取得
                    // Fork-JoinではCloud AIとOCRが並列実行のため、現在のOCR結果は未取得。
                    // ゲーム画面ではテキスト位置が連続フレームでほぼ同じため、前回結果が有効なヒントになる。
                    _previousOcrHintsCache.TryGetValue(windowHandle, out var previousOcrHints);

                    // [Issue #508] フォールバック1: Shot翻訳前Detection-Onlyの直接結果
                    // DetectionBoundsCacheを経由しないことで、Detection-Onlyフィルタ（#500）の誤スキップを防止
                    if (previousOcrHints == null)
                    {
                        var precomputed = Interlocked.Exchange(ref _precomputedHintBounds, null);
                        if (precomputed is { Length: > 0 })
                        {
                            // Detection画像の高さを使用（boundsがDetection画像の座標系のため）
                            var hintHeight = _precomputedHintImageHeight > 0 ? _precomputedHintImageHeight : forkJoinContextHeight;
                            previousOcrHints = BuildOcrHintsFromBounds(precomputed, hintHeight);
                            _logger?.LogInformation(
                                "[Issue #508] Shot前Detection-Only結果からOCRヒントを構築: {Count}領域, Areas=[{Areas}], ImageHeight={Height}",
                                previousOcrHints.TextRegionCount, string.Join(", ", previousOcrHints.TextAreas), hintHeight);
                        }
                    }

                    // [Issue #508] フォールバック2: DetectionBoundsCacheから構築（Live翻訳用）
                    // Detection-Onlyフィルタが毎サイクルで書き込むバウンディングボックスを活用
                    if (previousOcrHints == null && _detectionBoundsCache != null)
                    {
                        try
                        {
                            var contextId = $"Window_{windowHandle.ToInt64()}";
                            var cachedEntry = _detectionBoundsCache.GetPreviousEntry(contextId);
                            if (cachedEntry?.Bounds is { Length: > 0 })
                            {
                                // キャッシュにはDetection画像の高さ情報がないため、bounds自体から推定
                                var estimatedHeight = EstimateImageHeightFromBounds(cachedEntry.Bounds);
                                previousOcrHints = BuildOcrHintsFromBounds(cachedEntry.Bounds, estimatedHeight);
                                _logger?.LogInformation(
                                    "[Issue #508] DetectionBoundsCacheからOCRヒントを構築: {Count}領域, Areas=[{Areas}], EstHeight={Height}",
                                    previousOcrHints.TextRegionCount, string.Join(", ", previousOcrHints.TextAreas), estimatedHeight);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogDebug(ex, "[Issue #508] DetectionBoundsCacheからのヒント構築に失敗（ヒントなしで続行）");
                        }
                    }

                    forkJoinCloudTask = ExecuteForkJoinCloudTranslationAsync(
                        forkJoinImageBase64!,
                        forkJoinContextWidth,
                        forkJoinContextHeight,
                        forkJoinCloudImageWidth,   // [Issue #381] 実際のCloud画像サイズ（ログ用）
                        forkJoinCloudImageHeight,  // [Issue #381]
                        forkJoinCts.Token,  // [Issue #397] Fork-Join専用CTS（テキスト未変化時キャンセル可能）
                        windowHandle,  // [Issue #427] 翻訳履歴用
                        previousOcrHints);  // [Issue #429] 前回サイクルのOCRヒント

                    _logger?.LogDebug("[Issue #290] Cloud AI翻訳タスク開始（OCRと並列実行中）");
                }
            }

            // 🎯 [OPTION_A] SmartProcessingPipelineServiceで段階的フィルタリング実行
            _logger?.LogDebug($"🎯 [OPTION_A] 段階的フィルタリングパイプライン開始 - ImageChangeDetection → OCR");
            _logger?.LogDebug("🎯 [OPTION_A] SmartProcessingPipelineService.ExecuteAsync実行開始");

            // ProcessingPipelineInput作成（ContextIdは計算プロパティのため省略）
            // 🔥 [PHASE2.5_ROI_COORD_FIX] image.CaptureRegionを保持し、ROI座標オフセットを適用可能にする
            // [Issue #397] 前回のOCRテキストをキャッシュから取得
            _previousOcrTextCache.TryGetValue(windowHandle, out var previousOcrText);

            var pipelineInput = new Baketa.Core.Models.Processing.ProcessingPipelineInput
            {
                CapturedImage = image,
                CaptureRegion = image.CaptureRegion ?? new System.Drawing.Rectangle(0, 0, image.Width, image.Height),
                SourceWindowHandle = windowHandle,
                Options = options ?? new Baketa.Core.Models.Processing.ProcessingPipelineOptions(),
                // 🚀 [Issue #193] GPU Shaderリサイズ後のOCR座標スケーリング用に元ウィンドウサイズを設定
                OriginalWindowSize = GetOriginalWindowSize(windowHandle),
                // 🔥 [Issue #193/#194] キャプチャ時に実行済みのOCR結果を伝達（二重OCR防止）
                PreExecutedOcrResult = preExecutedOcrResult,
                // [Issue #397] テキスト変化検知用の前回OCRテキスト
                PreviousOcrText = previousOcrText
            };

            // パイプライン実行（ImageChangeDetection → OcrExecution）
            var pipelineResult = await _pipelineService.ExecuteAsync(pipelineInput, cancellationToken)
                .ConfigureAwait(false);

            _logger?.LogDebug($"🎯 [OPTION_A] パイプライン完了 - ShouldContinue: {pipelineResult.ShouldContinue}, Success: {pipelineResult.Success}, LastCompletedStage: {pipelineResult.LastCompletedStage}");
            _logger?.LogDebug("🎯 [OPTION_A] パイプライン完了 - ShouldContinue: {ShouldContinue}, Success: {Success}, EarlyTerminated: {EarlyTerminated}",
                pipelineResult.ShouldContinue, pipelineResult.Success, pipelineResult.Metrics.EarlyTerminated);

            // 🎯 [OPTION_A] 早期リターンチェック - 画面変化なしで処理スキップ
            if (!pipelineResult.ShouldContinue || pipelineResult.Metrics.EarlyTerminated)
            {
                _logger?.LogDebug($"🎯 [OPTION_A] 画面変化なし検出 - 翻訳処理をスキップ (90%処理時間削減達成)");
                _logger?.LogInformation("🎯 [OPTION_A] 画面変化なし - 早期リターン (EarlyTerminated: {EarlyTerminated})",
                    pipelineResult.Metrics.EarlyTerminated);

                // [Issue #397] Fork-JoinのCloud翻訳をキャンセルしてトークン浪費を抑制
                if (forkJoinCloudTask != null)
                {
                    _logger?.LogDebug("[Issue #397] テキスト未変化 - Fork-Join Cloud翻訳をキャンセル");
                    await forkJoinCts.CancelAsync().ConfigureAwait(false);
                }

                ocrMeasurement.Complete();
                return; // 翻訳処理をスキップして即座にリターン
            }

            // [Issue #401] 画面安定化チェック（ヒステリシス付き）
            // 画面がまだ遷移中（シーン切替、テキスト送りの途中等）の可能性がある場合、
            // パイプライン全体をスキップして安定してからOCR + Cloud AIを実行する
            // [Issue #410] Singleshotモード時は安定化チェックをバイパス（次サイクルがないため）
            // [Issue #486] テキスト変化検知が成功した場合は安定化をバイパス（有効なテキストが検出済み）
            var isSingleshotForStabilization = _translationModeService?.CurrentMode == TranslationMode.Singleshot;
            var hasConfirmedTextChange = pipelineResult.TextChangeResult?.HasTextChanged == true;
            if (pipelineResult.ImageChangeResult != null && !isSingleshotForStabilization)
            {
                var settings = _imageChangeSettings?.CurrentValue;
                var stabilizationThreshold = settings?.ScreenStabilizationThreshold ?? 0.50f;
                var recoveryThreshold = settings?.ScreenStabilizationRecoveryThreshold ?? 0.35f;
                var changePercentage = pipelineResult.ImageChangeResult.ChangePercentage;
                var hasPreviousBaseline = _previousOcrTextCache.ContainsKey(windowHandle);
                var isStabilizationActive = _screenStabilizationActive.GetValueOrDefault(windowHandle, false);

                // ヒステリシス判定: スキップ中は低い閾値（recovery）、通常時は高い閾値で判定
                var shouldSkip = hasPreviousBaseline &&
                    (isStabilizationActive
                        ? changePercentage > recoveryThreshold   // スキップ中: recovery閾値を下回るまで継続
                        : changePercentage > stabilizationThreshold); // 通常: 高い閾値を超えたらスキップ開始

                // [Issue #486] パイプラインがTextChangeDetectionまで正常に完了し、
                // テキスト変化が確認された場合、安定化スキップをバイパスする。
                // 理由: OCRでテキストが検出・変化確認済みなら、画面は「不安定」ではなく
                // ダイアログ更新等の正常な遷移。安定化待ちすると次のテキストを見逃す。
                if (shouldSkip && hasConfirmedTextChange)
                {
                    _logger?.LogInformation(
                        "[Issue #486] テキスト変化確認済みのため安定化スキップをバイパス " +
                        "(ChangePercentage={Pct:F2}, TextChangeResult.HasTextChanged=True) - 翻訳を続行",
                        changePercentage);
                    shouldSkip = false;
                }

                if (shouldSkip)
                {
                    _screenStabilizationActive[windowHandle] = true;
                    _logger?.LogInformation(
                        "[Issue #401] 画面安定化待ち: パイプライン全体をスキップ " +
                        "(ChangePercentage={Pct:F2}, Threshold={Threshold:F2}, Recovery={Recovery:F2}, Active={Active}) - 次サイクルで再試行",
                        changePercentage, stabilizationThreshold, recoveryThreshold, isStabilizationActive);

                    // Fork-Join Cloud翻訳をキャンセル（トークン浪費防止）
                    if (forkJoinCloudTask != null)
                    {
                        await forkJoinCts.CancelAsync().ConfigureAwait(false);
                    }

                    // OCRテキストキャッシュは更新しない（次サイクルで再度変化を検知するため）
                    ocrMeasurement.Complete();
                    return;
                }

                // 安定化解除
                if (isStabilizationActive)
                {
                    _screenStabilizationActive[windowHandle] = false;
                    _logger?.LogInformation(
                        "[Issue #401] 画面安定化完了: 処理を再開 (ChangePercentage={Pct:F2})",
                        changePercentage);

                    // [Issue #431] 安定化解除フレームでFork-Join Cloud翻訳を遅延開始
                    // 安定化中にFork-Joinがスキップされていたため、ここで開始する
                    // （OCRは完了済みなので並列ではなく逐次実行だが、NLLBフォールバックを防止）
                    if (forkJoinCloudTask == null && cachedCloudResult == null &&
                        ShouldUseForkJoinParallelExecution(forkJoinImageBase64, forkJoinContextWidth, forkJoinContextHeight))
                    {
                        // キャッシュチェック
                        if (_cloudTranslationCache != null && forkJoinImageBase64 != null)
                        {
                            forkJoinImageHash = _cloudTranslationCache.ComputeImageHash(image.GetImageMemory());
                            if (_cloudTranslationCache.TryGetCachedResult(windowHandle, forkJoinImageHash, out cachedCloudResult))
                            {
                                _logger?.LogInformation(
                                    "[Issue #431] 安定化解除後 - キャッシュヒット（Cloud APIスキップ）");
                            }
                        }

                        if (cachedCloudResult == null)
                        {
                            _logger?.LogInformation(
                                "🚀 [Issue #431] 安定化解除 - Fork-Join Cloud翻訳を遅延開始");
                            // [Issue #429] 前回サイクルのOCRヒントキャッシュを取得
                            _previousOcrHintsCache.TryGetValue(windowHandle, out var recoveryOcrHints);

                            forkJoinCloudTask = ExecuteForkJoinCloudTranslationAsync(
                                forkJoinImageBase64!,
                                forkJoinContextWidth,
                                forkJoinContextHeight,
                                forkJoinCloudImageWidth,
                                forkJoinCloudImageHeight,
                                forkJoinCts.Token,
                                windowHandle,
                                recoveryOcrHints);  // [Issue #429] 前回サイクルのOCRヒント
                        }
                    }
                }
            }

            // [Issue #397] 安定化チェック通過後にOCRテキストキャッシュを更新（次サイクルのテキスト変化検知用）
            // ※安定化スキップ時はここに到達しないため、キャッシュは更新されない
            if (!string.IsNullOrEmpty(pipelineResult.OcrResultText))
            {
                _previousOcrTextCache[windowHandle] = pipelineResult.OcrResultText;
            }

            // ✅ [DEBUG_FIX] 画面変化が検出されたことを明示的にログ出力
            _logger?.LogDebug("✅ [OPTION_A] 画面変化を検出 - OCR処理を続行します");

            // 🔥 [PHASE13.1_FIX] OCR結果からテキストチャンクを取得（OcrTextRegion → TextChunk変換）
            var textChunks = new List<Baketa.Core.Abstractions.Translation.TextChunk>();
            if (pipelineResult.OcrResult?.TextChunks != null)
            {
                foreach (var chunk in pipelineResult.OcrResult.TextChunks)
                {
                    if (chunk is Baketa.Core.Abstractions.Translation.TextChunk textChunk)
                    {
                        // 🔥 [FIX5_CACHE_COORD_NORMALIZE] 座標の二重変換バグを修正。
                        // キャッシュから取得したTextChunkは既に絶対座標を持っているため、
                        // 再度CaptureRegionオフセットを加算しないように修正。
                        // チャンクをそのままリストに追加します。
                        textChunks.Add(textChunk);
                    }
                    else if (chunk is Baketa.Core.Abstractions.OCR.OcrTextRegion ocrRegion)
                    {
                        // 🔥 [PHASE2.5_ROI_COORD_FIX] 座標変換はPaddleOcrResultConverterに集約。
                        // このサービスでは変換済みの座標をそのまま使用する。
                        var boundingBox = ocrRegion.Bounds;

                        // 🔥 [PHASE13.1_P1] OcrTextRegion → TextChunk変換（P1改善: ChunkId衝突防止）
                        var positionedResult = new Baketa.Core.Abstractions.OCR.Results.PositionedTextResult
                        {
                            Text = ocrRegion.Text,
                            BoundingBox = boundingBox,  // 🔥 [ROI_COORD_FIX] 調整済み画像絶対座標を使用
                            Confidence = (float)ocrRegion.Confidence,
                            // 🔥 [P1_FIX_1] スレッドセーフなアトミックカウンター使用（Random.Shared衝突リスク完全排除）
                            ChunkId = Interlocked.Increment(ref _nextChunkId),
                            // ProcessingTimeとDetectedLanguageはOcrTextRegionに存在しないため、親のOcrResultsから取得が必要
                            // ここでは現在の実装を維持（将来的な改善: OcrExecutionResultからメタデータを渡す設計）
                            ProcessingTime = TimeSpan.Zero,
                            DetectedLanguage = "jpn"
                        };

                        var convertedChunk = new Baketa.Core.Abstractions.Translation.TextChunk
                        {
                            ChunkId = positionedResult.ChunkId,
                            TextResults = new[] { positionedResult },
                            CombinedBounds = positionedResult.BoundingBox,
                            CombinedText = positionedResult.Text,
                            SourceWindowHandle = windowHandle,
                            DetectedLanguage = positionedResult.DetectedLanguage,
                            CaptureRegion = pipelineInput.CaptureRegion
                        };
                        textChunks.Add(convertedChunk);
                    }
                }
            }

            _logger?.LogDebug($"🎯 [OPTION_A] OCR結果取得 - ChunkCount: {textChunks.Count}");
            _logger?.LogDebug("🎯 [OPTION_A] OCR結果取得 - ChunkCount: {ChunkCount}, CancellationToken.IsCancellationRequested: {IsCancellationRequested}",
                textChunks.Count, cancellationToken.IsCancellationRequested);

            // [Issue #429] OCRヒントキャッシュを更新（次サイクルのFork-Joinに注入するため）
            if (textChunks.Count > 0 && forkJoinContextHeight > 0)
            {
                _previousOcrHintsCache[windowHandle] = BuildOcrHints(textChunks, forkJoinContextHeight);
            }

            // [Issue #397] Gate B: OCR結果が空の場合、Cloud AI結果を破棄
            if (textChunks.Count == 0 && forkJoinCloudTask != null)
            {
                _logger?.LogInformation(
                    "[Issue #397] Gate B: OCRチャンク0件 - Cloud AI結果を破棄してトークン浪費防止");
                await forkJoinCts.CancelAsync().ConfigureAwait(false);
                forkJoinCloudTask = null;
            }

            // 🚀 [FIX] OCR完了後はキャンセル無視でバッチ翻訳を実行（並列チャンク処理実現のため）
            if (textChunks.Count > 0 && cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine("🚀 [PARALLEL_CHUNKS_FIX] OCR完了後のキャンセル要求を無視してバッチ翻訳を実行");
                // 🔥 [FILE_CONFLICT_FIX_6] ファイルアクセス競合回避のためILogger使用
                _logger?.LogDebug("🚀 [PARALLEL_CHUNKS_FIX] OCR完了後のキャンセル要求を無視してバッチ翻訳を実行");
            }

            var ocrResult = ocrMeasurement.Complete();
            var ocrProcessingTime = ocrResult.Duration;

            _logger?.LogInformation("✅ バッチOCR完了 - チャンク数: {ChunkCount}, 処理時間: {ProcessingTime}ms",
                textChunks.Count, ocrProcessingTime.TotalMilliseconds);

            // ============================================================
            // 🎯 [Issue #230] テキストベース変化検知
            // 画面点滅等の非テキスト変化でOCRが実行されても、
            // テキストが前回と同じなら翻訳・オーバーレイ更新をスキップ
            // ============================================================
            if (_textChangeDetectionService != null && textChunks.Count > 0)
            {
                // コンテキストIDとしてウィンドウハンドルを使用
                var contextId = $"window_{windowHandle.ToInt64():X}";

                // 全TextChunksのテキストを結合（順序を統一するためY座標→X座標でソート）
                var currentCombinedText = string.Join(" ", textChunks
                    .OrderBy(c => c.CombinedBounds.Y)
                    .ThenBy(c => c.CombinedBounds.X)
                    .Select(c => c.CombinedText));

                // 前回のテキストを取得
                var previousText = _textChangeDetectionService.GetPreviousText(contextId);

                if (previousText != null)
                {
                    // 🔧 [SINGLESHOT_FIX] Singleshotモードの場合はテキスト変化検出をバイパス
                    var isSingleshotMode = _translationModeService?.CurrentMode == TranslationMode.Singleshot;

                    if (isSingleshotMode)
                    {
                        _logger?.LogInformation("🎯 [SINGLESHOT_FIX] Singleshotモード - テキスト変化検出をバイパス");
                        Console.WriteLine("🎯 [SINGLESHOT_FIX] Singleshotモード - テキスト変化検出をバイパスして翻訳続行");
                    }
                    else
                    {
                        // テキスト変化を検知（Liveモードのみ）
                        var changeResult = await _textChangeDetectionService.DetectTextChangeAsync(
                            previousText, currentCombinedText, contextId).ConfigureAwait(false);

                        // [Issue #410] Service層のHasChangedは独自の高い閾値（例: 19%）を使用しており、
                        // ゲームダイアログの変化が永久にブロックされるケースがある。
                        // Pipeline Strategy層と同じ10%閾値で独立判定する。
                        const float textChangeThreshold = 0.10f;
                        if (changeResult.ChangePercentage < textChangeThreshold)
                        {
                            // テキスト変化なし → 翻訳・オーバーレイ更新をスキップ
                            _logger?.LogInformation("🎯 [Issue #230] テキスト変化なし - 翻訳をスキップ (変化率: {ChangePercentage:P1}, 閾値: {Threshold:P1})",
                                changeResult.ChangePercentage, textChangeThreshold);
                            return; // 早期リターン
                        }

                        // [Issue #392] テキスト変化時のオーバーレイクリアはDetection段階のIsTextDisappearance()に移行
                        _logger?.LogDebug("🎯 [Issue #230] テキスト変化検知 - 翻訳を続行 (変化率: {ChangePercentage:P1})",
                            changeResult.ChangePercentage);
                    }
                }
                else
                {
                    _logger?.LogDebug("🎯 [Issue #230] 初回実行 - テキストをキャッシュ");
                }

                // 現在のテキストをキャッシュに保存（次回比較用）
                _textChangeDetectionService.SetPreviousText(contextId, currentCombinedText);
            }

            // [Issue #78 Phase 4] Cloud AI翻訳用の画像コンテキストを設定
            // [Issue #275] 元サイズ(OriginalWidth/Height)を使用してGemini座標変換を正しく行う
            // リサイズ後サイズ(Width/Height)を使うとCloud AI座標がローカルOCR座標とずれる
            try
            {
                // [Issue #381] Fork-Joinで生成済みのCloud画像データを再利用（ダウンスケール処理の最適化）
                string imageBase64;
                int cloudW, cloudH;

                if (!string.IsNullOrEmpty(forkJoinImageBase64) && forkJoinCloudImageWidth > 0)
                {
                    imageBase64 = forkJoinImageBase64;
                    cloudW = forkJoinCloudImageWidth;
                    cloudH = forkJoinCloudImageHeight;
                    _logger?.LogDebug("[Issue #381] Fork-JoinのCloud画像データを再利用: {W}x{H}", cloudW, cloudH);
                }
                else
                {
                    // Fork-Join未使用時のみ新規準備
                    var cloudData = await PrepareCloudImageDataAsync(image).ConfigureAwait(false);
                    imageBase64 = cloudData.Base64;
                    cloudW = cloudData.Width;
                    cloudH = cloudData.Height;
                }

                // 🔥 [Issue #275] OriginalWidth/OriginalHeightを使用（オーバーレイ座標計算用）
                // ローカルOCR座標は元サイズにスケールバック済み(Issue #193)なので、
                // Cloud AI座標も元サイズ基準で計算する必要がある
                var (contextWidth, contextHeight) = image switch
                {
                    IWindowsImage windowsImage => (windowsImage.OriginalWidth, windowsImage.OriginalHeight),
                    WindowsImageAdapter adapter => (adapter.OriginalWidth, adapter.OriginalHeight),
                    _ => (image.Width, image.Height)
                };
                // [Issue #381] 実際のCloud画像サイズもセット（ログ・トークン推定用）
                _textChunkAggregatorService.SetImageContext(imageBase64, contextWidth, contextHeight, cloudW, cloudH);

                // [Issue #379] Singleshotモード時にGateフィルタリングをバイパスするためモードを伝播
                var translationMode = options?.ForceCompleteExecution == true
                    ? Baketa.Core.Abstractions.Services.TranslationMode.Singleshot
                    : Baketa.Core.Abstractions.Services.TranslationMode.Live;
                _textChunkAggregatorService.SetTranslationMode(translationMode);

                _logger?.LogDebug("[Issue #78] 画像コンテキスト設定: {Width}x{Height} (元サイズ), Cloud={CloudW}x{CloudH}, Base64Length={Length}, Mode={Mode}",
                    contextWidth, contextHeight, cloudW, cloudH, imageBase64.Length, translationMode);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[Issue #78] 画像コンテキスト設定失敗 - Cloud AI翻訳は利用不可");
            }

            // ============================================================
            // [Issue #290] Fork-Join完了: Cloud AI翻訳結果を待機してセット
            // [Issue #415] キャッシュヒット時はAPIコール不要 → 即座にセット
            // ============================================================
            if (cachedCloudResult != null)
            {
                // [Issue #415] キャッシュヒット → 前回のCloud結果を再利用
                _textChunkAggregatorService.SetPreComputedCloudResult(cachedCloudResult);
                _logger?.LogInformation(
                    "✅ [Issue #415] キャッシュヒット: Cloud AI翻訳結果をセット (Success={Success}, Engine={Engine})",
                    cachedCloudResult.IsSuccess, cachedCloudResult.UsedEngine);
            }
            else if (forkJoinCloudTask != null)
            {
                try
                {
                    var forkJoinStopwatch = Stopwatch.StartNew();
                    _logger?.LogDebug("[Issue #290] Fork-Join: Cloud AI翻訳結果を待機中...");

                    var cloudResult = await forkJoinCloudTask.ConfigureAwait(false);
                    forkJoinStopwatch.Stop();

                    if (cloudResult != null)
                    {
                        _textChunkAggregatorService.SetPreComputedCloudResult(cloudResult);

                        // [Issue #415] 成功した結果をキャッシュに保存
                        if (cloudResult.IsSuccess && _cloudTranslationCache != null && forkJoinImageHash != 0)
                        {
                            _cloudTranslationCache.CacheResult(windowHandle, forkJoinImageHash, cloudResult);
                        }

                        _logger?.LogInformation(
                            "✅ [Issue #290] Fork-Join完了: Cloud AI翻訳結果をセット (Success={Success}, Engine={Engine}, WaitTime={WaitTime}ms)",
                            cloudResult.IsSuccess, cloudResult.UsedEngine, forkJoinStopwatch.ElapsedMilliseconds);
                    }
                    else
                    {
                        _logger?.LogDebug("[Issue #290] Fork-Join: Cloud AI翻訳結果がnull（キャンセルまたはエラー）");
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "[Issue #290] Fork-Join: Cloud AI翻訳結果の待機中にエラー");
                }
            }

            // [Issue #227] TimedChunkAggregatorにバッチ追加
            try
            {
                var addedCount = await _textChunkAggregatorService.TryAddTextChunksBatchAsync(
                    textChunks, cancellationToken).ConfigureAwait(false);

                _logger?.LogDebug("TimedChunkAggregator: {AddedCount}/{TotalCount}個のチャンクを追加",
                    addedCount, textChunks.Count);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "TimedChunkAggregator処理でエラー");
            }

            // TimedChunkAggregatorが集約完了時にAggregatedChunksReadyEventを発行
            // AggregatedChunksReadyEventHandlerで翻訳・オーバーレイ表示を実行

            // [Issue #293] ROI学習: テキスト検出位置をヒートマップに記録
            // 🔥 [Issue #293 FIX] CombinedBoundsは元ウィンドウサイズ基準（OcrExecutionStageStrategyでスケーリング済み）
            // そのため、正規化にはOriginalWidth/OriginalHeight（元ウィンドウサイズ）を使用する必要がある
            // image.Width/Heightはキャプチャ画像サイズ（例: 1280x720）であり、座標系が異なる
            var (normalizeWidth, normalizeHeight) = image switch
            {
                IWindowsImage windowsImage => (windowsImage.OriginalWidth, windowsImage.OriginalHeight),
                WindowsImageAdapter adapter => (adapter.OriginalWidth, adapter.OriginalHeight),
                _ => (image.Width, image.Height) // フォールバック: リサイズなしの場合
            };

            // [Issue #354] 変化領域をNormalizedRectに変換（ROI学習フィルタ用）
            IReadOnlyList<NormalizedRect>? normalizedChangedRegions = null;
            if (pipelineResult.ImageChangeResult?.ChangedRegions is { Length: > 0 } changedRects)
            {
                // 変化検知結果はキャプチャ画像サイズ基準なので、その座標系で正規化
                var captureWidth = (float)image.Width;
                var captureHeight = (float)image.Height;
                normalizedChangedRegions = changedRects
                    .Select(r => new NormalizedRect
                    {
                        X = r.X / captureWidth,
                        Y = r.Y / captureHeight,
                        Width = r.Width / captureWidth,
                        Height = r.Height / captureHeight
                    })
                    .ToList();

                _logger?.LogDebug(
                    "[Issue #354] 変化領域を正規化: {Count}個の領域 (キャプチャサイズ={Width}x{Height})",
                    normalizedChangedRegions.Count, captureWidth, captureHeight);
            }

            // [Gemini Feedback] ゼロ除算防止のガード
            if (_roiManager != null && textChunks.Count > 0 && normalizeWidth > 0 && normalizeHeight > 0)
            {
                _logger?.LogInformation(
                    "[Issue #293] ROI学習チェック: RoiManager.IsEnabled={IsEnabled}, ChunkCount={ChunkCount}, NormalizeSize={Width}x{Height} (CaptureSize={CaptureWidth}x{CaptureHeight})",
                    _roiManager.IsEnabled, textChunks.Count, normalizeWidth, normalizeHeight, image.Width, image.Height);

                if (_roiManager.IsEnabled)
                {
                    try
                    {
                        var detections = textChunks
                            .Where(chunk => !string.IsNullOrWhiteSpace(chunk.CombinedText))
                            .Select(chunk => (
                                bounds: new NormalizedRect
                                {
                                    // 🔥 [Issue #293 FIX] 元ウィンドウサイズで正規化（CombinedBoundsと同じ座標系）
                                    X = (float)chunk.CombinedBounds.X / normalizeWidth,
                                    Y = (float)chunk.CombinedBounds.Y / normalizeHeight,
                                    Width = (float)chunk.CombinedBounds.Width / normalizeWidth,
                                    Height = (float)chunk.CombinedBounds.Height / normalizeHeight
                                },
                                confidence: chunk.TextResults.FirstOrDefault()?.Confidence ?? 0.8f
                            ))
                            .ToList();

                        if (detections.Count > 0)
                        {
                            // [Issue #293 FIX] 正規化座標の検証ログ
                            var firstDetection = detections[0];
                            _logger?.LogInformation(
                                "[Issue #293 FIX] 正規化座標確認: First region at ({X:F3}, {Y:F3}), 範囲内={InRange}",
                                firstDetection.bounds.X, firstDetection.bounds.Y,
                                firstDetection.bounds.X >= 0 && firstDetection.bounds.X <= 1 &&
                                firstDetection.bounds.Y >= 0 && firstDetection.bounds.Y <= 1);

                            // [Issue #293] ウィンドウ情報を取得してプロファイルに紐づけ
                            var windowTitle = _windowManager?.GetWindowTitle(windowHandle) ?? string.Empty;
                            var executablePath = GetExecutablePathFromWindow(windowHandle);

                            _logger?.LogDebug(
                                "[Issue #293] ROI学習ウィンドウ情報: Handle=0x{Handle:X}, Title='{Title}', ExePath='{ExePath}'",
                                windowHandle.ToInt64(), windowTitle, executablePath);

                            // 非同期でROI学習を実行（fire-and-forget、エラーは内部でログ）
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    // [Issue #354] 変化領域フィルタを適用
                                    await _roiManager.ReportTextDetectionsAsync(
                                        detections,
                                        windowHandle,
                                        windowTitle,
                                        executablePath,
                                        normalizedChangedRegions,
                                        cancellationToken).ConfigureAwait(false);
                                }
                                catch (Exception ex)
                                {
                                    _logger?.LogWarning(ex, "[Issue #293] ROI学習非同期処理でエラー");
                                }
                            });

                            _logger?.LogInformation(
                                "[Issue #293] ROI学習: {Count}個のテキスト領域を記録開始 (Window='{Title}')",
                                detections.Count, windowTitle);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "[Issue #293] ROI学習記録でエラーが発生（処理は継続）");
                    }
                }
            }
            else if (_roiManager == null)
            {
                _logger?.LogDebug("[Issue #293] IRoiManager is null - ROI learning skipped");
            }

            // Phase 12.2完全移行完了: AggregatedChunksReadyEventHandler経由で翻訳 + オーバーレイ表示
            // [Issue #386] Phase 12.2デッドコード削除完了
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // [Issue #402] Stop操作によるキャンセル → DEBUGレベルでログ出力し、rethrowで呼び出し元に伝搬
            _logger?.LogDebug("座標ベース翻訳処理がキャンセルされました（Stop操作）");
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ 座標ベース翻訳処理でエラーが発生しました");
            throw;
        }
    }

    /// <summary>
    /// バッチ翻訳を実行（TransformersOpusMtEngineバッチ処理による最適化）
    /// </summary>
    private async Task<List<string>> TranslateBatchAsync(
        List<string> texts,
        Language sourceLanguage,
        Language targetLanguage,
        CancellationToken cancellationToken)
    {
        var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
        // 🚨 [CRITICAL_DEBUG] メソッド開始の即座ログ出力
        Console.WriteLine($"🚨 [BATCH_CRITICAL] TranslateBatchAsync開始 - テキスト数: {texts?.Count ?? 0}");
        Console.WriteLine($"🔍 [BATCH_LANGUAGE] 受信した言語設定: Source={sourceLanguage?.Code}({sourceLanguage?.DisplayName}) → Target={targetLanguage?.Code}({targetLanguage?.DisplayName})");

        _logger?.LogInformation("🔍 [BATCH_DEBUG] TranslateBatchAsync呼び出し開始 - テキスト数: {Count}", texts.Count);
        _logger?.LogInformation("[TIMING] CoordinateBasedTranslationService.TranslateBatchAsync開始 - テキスト数: {Count}", texts.Count);
        Console.WriteLine($"🚀 [FACADE_DEBUG] TranslationService via Facade: {_processingFacade.TranslationService?.GetType().Name}");
        // 🔥 [FILE_CONFLICT_FIX_18] ファイルアクセス競合回避のためILogger使用
        _logger?.LogDebug("🚀 [FACADE_DEBUG] TranslationService via Facade: {ServiceType}",
            _processingFacade.TranslationService?.GetType().Name);

        // 🔍 [VERIFICATION] バッチ翻訳の実際の動作を検証
        // 🚀 汎用的なITranslationServiceベースのアプローチに変更
        var translationService = _processingFacade.TranslationService;
        if (translationService != null)
        {
            Console.WriteLine($"🚀 [VERIFICATION] 翻訳サービス取得成功 - バッチ翻訳検証開始: {translationService.GetType().Name}");
            _logger?.LogDebug("🚀 [VERIFICATION] 翻訳サービス取得成功 - バッチ翻訳検証開始: {ServiceType}", translationService.GetType().Name);

            // 汎用的なバッチ翻訳処理（ITranslationServiceの標準的なアプローチ）
            Console.WriteLine($"📏 [VERIFICATION] バッチ翻訳開始 - テキスト数: {texts.Count}");
            _logger?.LogDebug("📏 [VERIFICATION] バッチ翻訳開始 - テキスト数: {Count}", texts.Count);

            // ITranslationServiceのTranslateBatchAsyncメソッドを使用
            try
            {
                Console.WriteLine($"🎯 [VERIFICATION] ITranslationService.TranslateBatchAsync実行開始");
                _logger?.LogDebug("🎯 [VERIFICATION] ITranslationService.TranslateBatchAsync実行開始");

                var timeoutSetupStopwatch = System.Diagnostics.Stopwatch.StartNew();
                // 🔧 [EMERGENCY_FIX] 60秒タイムアウトを設定（Python翻訳サーバー重要処理対応）
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
                timeoutSetupStopwatch.Stop();
                _logger?.LogInformation("[TIMING] タイムアウト設定: {ElapsedMs}ms", timeoutSetupStopwatch.ElapsedMilliseconds);

                var startTime = DateTime.Now;
                var batchCallStopwatch = System.Diagnostics.Stopwatch.StartNew();

                // 翻訳品質診断: セッションID生成
                var translationId = Guid.NewGuid().ToString("N")[..8];
                var totalTextLength = texts.Sum(t => t?.Length ?? 0);

                // 翻訳品質診断: 言語検出イベント
                await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
                {
                    Stage = "LanguageDetection",
                    IsSuccess = true,
                    ProcessingTimeMs = 0,
                    SessionId = translationId,
                    Severity = DiagnosticSeverity.Information,
                    Message = $"フォールバック経路言語検出完了: {sourceLanguage.Code} → {targetLanguage.Code}",
                    Metrics = new Dictionary<string, object>
                    {
                        { "SourceLanguage", sourceLanguage.Code },
                        { "TargetLanguage", targetLanguage.Code },
                        { "TextCount", texts.Count },
                        { "TotalTextLength", totalTextLength },
                        { "TranslationPath", "FallbackBatch" }
                    }
                }).ConfigureAwait(false);

                // 翻訳品質診断: 翻訳エンジン選択イベント
                var engineName = translationService.GetType().Name;
                await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
                {
                    Stage = "TranslationEngineSelection",
                    IsSuccess = true,
                    ProcessingTimeMs = 0,
                    SessionId = translationId,
                    Severity = DiagnosticSeverity.Information,
                    Message = $"フォールバック翻訳エンジン選択: {engineName}",
                    Metrics = new Dictionary<string, object>
                    {
                        { "SelectedEngine", engineName },
                        { "TranslationPath", "FallbackBatch" },
                        { "TextCount", texts.Count }
                    }
                }).ConfigureAwait(false);

                // ITranslationServiceのTranslateBatchAsyncメソッドを使用（文字列リスト）
                var batchResults = await translationService.TranslateBatchAsync(
                    texts,
                    sourceLanguage,
                    targetLanguage,
                    null,
                    combinedCts.Token).ConfigureAwait(false);

                batchCallStopwatch.Stop();
                var endTime = DateTime.Now;
                var duration = endTime - startTime;

                // 翻訳品質診断: 翻訳実行結果イベント
                var isTranslationSuccess = batchResults != null && batchResults.Any(r => r.IsSuccess);
                await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
                {
                    Stage = "TranslationExecution",
                    IsSuccess = isTranslationSuccess,
                    ProcessingTimeMs = (long)duration.TotalMilliseconds,
                    SessionId = translationId,
                    Severity = isTranslationSuccess ? DiagnosticSeverity.Information : DiagnosticSeverity.Warning,
                    Message = isTranslationSuccess
                        ? $"フォールバック翻訳実行成功: {batchResults?.Count(r => r.IsSuccess) ?? 0}/{batchResults?.Count ?? 0}件"
                        : "フォールバック翻訳実行失敗",
                    Metrics = new Dictionary<string, object>
                    {
                        { "ExecutionTimeMs", duration.TotalMilliseconds },
                        { "SuccessCount", batchResults?.Count(r => r.IsSuccess) ?? 0 },
                        { "TotalCount", batchResults?.Count ?? 0 },
                        { "TranslationPath", "FallbackBatch" },
                        { "UsedEngine", engineName }
                    }
                }).ConfigureAwait(false);

                Console.WriteLine($"✅ [VERIFICATION] バッチ翻訳完了 - 実行時間: {duration.TotalMilliseconds:F0}ms");
                _logger?.LogDebug("✅ [VERIFICATION] バッチ翻訳完了 - 実行時間: {Duration:F0}ms", duration.TotalMilliseconds);
                _logger?.LogInformation("[TIMING] ITranslationService.TranslateBatchAsync実行: {ElapsedMs}ms", batchCallStopwatch.ElapsedMilliseconds);

                // 結果を詳細分析
                if (batchResults != null && batchResults.Count > 0)
                {
                    var successCount = batchResults.Count(r => r.IsSuccess);
                    var translations = batchResults.Select(r => r.TranslatedText ?? "").ToList();

                    Console.WriteLine($"🔍 [VERIFICATION] 結果分析: SuccessCount={successCount}/{batchResults.Count}, Translations={translations.Count}");
                    _logger?.LogDebug("🔍 [VERIFICATION] 結果分析: SuccessCount={SuccessCount}/{TotalCount}, Translations={TranslationCount}",
                        successCount, batchResults.Count, translations.Count);

                    if (successCount == batchResults.Count)
                    {
                        // 🔍 翻訳品質診断: 高精度言語比較による翻訳失敗検出（フォールバックルート）
                        var sameLanguageCount = 0;
                        var sameLanguageFailures = new List<string>();
                        for (int i = 0; i < Math.Min(texts.Count, translations.Count); i++)
                        {
                            if (!string.IsNullOrEmpty(texts[i]) && !string.IsNullOrEmpty(translations[i]))
                            {
                                try
                                {
                                    // 改良された翻訳失敗検出ロジック（フォールバックバッチ処理）
                                    // TODO: 将来的に言語検出APIが統合された場合に高精度検出を実装予定
                                    var isSameText = string.Equals(texts[i].Trim(), translations[i].Trim(), StringComparison.OrdinalIgnoreCase);

                                    if (isSameText)
                                    {
                                        sameLanguageCount++;
                                        sameLanguageFailures.Add($"{texts[i]} -> {translations[i]} (fallback text comparison)");
                                        Console.WriteLine($"🚨 [FALLBACK_ENHANCED_DIAGNOSTIC] 翻訳失敗検出（文字列一致）: '{texts[i]}' -> '{translations[i]}'");
                                    }
                                }
                                catch (Exception detectionEx)
                                {
                                    // 検出処理でエラーが発生した場合のフォールバック
                                    if (string.Equals(texts[i].Trim(), translations[i].Trim(), StringComparison.OrdinalIgnoreCase))
                                    {
                                        sameLanguageCount++;
                                        sameLanguageFailures.Add($"{texts[i]} -> {translations[i]} (error fallback)");
                                        Console.WriteLine($"🚨 [ERROR_FALLBACK] 検出エラー時の文字列比較: '{texts[i]}' (エラー: {detectionEx.Message})");
                                    }
                                }
                            }
                        }

                        var qualityIsGood = sameLanguageCount == 0;
                        await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
                        {
                            Stage = "TranslationQualityCheck",
                            IsSuccess = qualityIsGood,
                            ProcessingTimeMs = 0,
                            SessionId = translationId,
                            Severity = qualityIsGood ? DiagnosticSeverity.Information : DiagnosticSeverity.Warning,
                            Message = qualityIsGood
                                ? $"フォールバック翻訳品質良好: 全{translations.Count}件成功（改良された診断検証済み）"
                                : $"フォールバック翻訳品質問題検出: {sameLanguageCount}件翻訳失敗（改良された診断使用）",
                            Metrics = new Dictionary<string, object>
                            {
                                { "SameLanguageCount", sameLanguageCount },
                                { "TotalTranslations", translations.Count },
                                { "QualityScore", qualityIsGood ? 1.0 : (double)(translations.Count - sameLanguageCount) / translations.Count },
                                { "TranslationPath", "FallbackBatch" },
                                { "SourceLanguage", sourceLanguage.Code },
                                { "TargetLanguage", targetLanguage.Code },
                                { "DetectionMethod", "EnhancedTextComparison" },
                                { "FailureDetails", sameLanguageFailures.Count > 0 ? sameLanguageFailures.Take(3) : new List<string>() },
                                { "IsTextComparisonBased", true }
                            }
                        }).ConfigureAwait(false);

                        Console.WriteLine($"🎉 [VERIFICATION] バッチ翻訳成功！フォールバックせずに結果を返します");
                        _logger?.LogDebug("🎉 [VERIFICATION] バッチ翻訳成功！フォールバックせずに結果を返します");
                        totalStopwatch.Stop();
                        _logger?.LogInformation("[TIMING] CoordinateBasedTranslationService.TranslateBatchAsync完了（成功）: {ElapsedMs}ms", totalStopwatch.ElapsedMilliseconds);
                        return translations;
                    }
                    else
                    {
                        // 翻訳品質診断: 部分失敗の診断
                        await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
                        {
                            Stage = "TranslationQualityCheck",
                            IsSuccess = false,
                            ProcessingTimeMs = 0,
                            SessionId = translationId,
                            Severity = DiagnosticSeverity.Warning,
                            Message = $"フォールバック翻訳部分失敗: {successCount}/{batchResults.Count}件成功",
                            Metrics = new Dictionary<string, object>
                            {
                                { "SuccessCount", successCount },
                                { "TotalCount", batchResults.Count },
                                { "FailureCount", batchResults.Count - successCount },
                                { "TranslationPath", "FallbackBatch" },
                                { "FailureReason", "PartialBatchFailure" }
                            }
                        }).ConfigureAwait(false);

                        Console.WriteLine($"❌ [VERIFICATION] バッチ翻訳の一部が失敗 - 個別翻訳にフォールバック");
                        _logger?.LogDebug("❌ [VERIFICATION] バッチ翻訳の一部が失敗 - 個別翻訳にフォールバック");
                    }
                }
                else
                {
                    // 翻訳品質診断: 空結果の診断
                    await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
                    {
                        Stage = "TranslationQualityCheck",
                        IsSuccess = false,
                        ProcessingTimeMs = 0,
                        SessionId = translationId,
                        Severity = DiagnosticSeverity.Error,
                        Message = "フォールバック翻訳結果が空 - 翻訳エンジン応答なし",
                        Metrics = new Dictionary<string, object>
                        {
                            { "ResultCount", batchResults?.Count ?? 0 },
                            { "TranslationPath", "FallbackBatch" },
                            { "FailureReason", "EmptyResults" }
                        }
                    }).ConfigureAwait(false);

                    Console.WriteLine($"❌ [VERIFICATION] バッチ翻訳結果が空 - 個別翻訳にフォールバック");
                    _logger?.LogDebug("❌ [VERIFICATION] バッチ翻訳結果が空 - 個別翻訳にフォールバック");
                }
            }
            catch (OperationCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
            {
                // 翻訳品質診断: タイムアウト診断イベント
                var translationId = Guid.NewGuid().ToString("N")[..8]; // タイムアウト時は新しいIDを生成
                await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
                {
                    Stage = "TranslationQualityCheck",
                    IsSuccess = false,
                    ProcessingTimeMs = 60000, // 60秒タイムアウト
                    SessionId = translationId,
                    Severity = DiagnosticSeverity.Error,
                    Message = "フォールバック翻訳タイムアウト - 60秒制限超過",
                    Metrics = new Dictionary<string, object>
                    {
                        { "TimeoutMs", 60000 },
                        { "TranslationPath", "FallbackBatch" },
                        { "FailureReason", "Timeout" },
                        { "TextCount", texts?.Count ?? 0 }
                    }
                }).ConfigureAwait(false); // タイムアウト時はCancellationTokenを使用しない

                Console.WriteLine($"⏰ [VERIFICATION] バッチ翻訳が60秒でタイムアウト - Python翻訳サーバー処理時間が60秒を超過");
                // 🔥 [FILE_CONFLICT_FIX_28] ファイルアクセス競合回避のためILogger使用
                _logger?.LogWarning("⏰ [VERIFICATION] バッチ翻訳が60秒でタイムアウト - Python翻訳サーバー処理時間が60秒を超過");
            }
            catch (Exception ex)
            {
                // 翻訳品質診断: 例外診断イベント
                var translationId = Guid.NewGuid().ToString("N")[..8]; // 例外時は新しいIDを生成
                await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
                {
                    Stage = "TranslationQualityCheck",
                    IsSuccess = false,
                    ProcessingTimeMs = 0,
                    SessionId = translationId,
                    Severity = DiagnosticSeverity.Error,
                    Message = $"フォールバック翻訳例外: {ex.GetType().Name}: {ex.Message}",
                    Metrics = new Dictionary<string, object>
                    {
                        { "ExceptionType", ex.GetType().Name },
                        { "ExceptionMessage", ex.Message },
                        { "TranslationPath", "FallbackBatch" },
                        { "FailureReason", "Exception" },
                        { "TextCount", texts?.Count ?? 0 }
                    }
                }).ConfigureAwait(false); // 例外時はCancellationTokenを使用しない

                Console.WriteLine($"💥 [VERIFICATION] バッチ翻訳で例外発生: {ex.GetType().Name}: {ex.Message}");
                // 🔥 [FILE_CONFLICT_FIX_29] ファイルアクセス競合回避のためILogger使用
                _logger?.LogError(ex, "💫 [VERIFICATION] バッチ翻訳で例外発生: {ExceptionType}", ex.GetType().Name);
            }
        }

        // 個別翻訳にフォールバック
        Console.WriteLine($"🌟 [BATCH_DEBUG] バッチ翻訳が利用できないため個別翻訳にフォールバック");
        // 🔥 [FILE_CONFLICT_FIX_30] ファイルアクセス競合回避のためILogger使用
        _logger?.LogDebug("🌟 [BATCH_DEBUG] バッチ翻訳が利用できないため個別翻訳にフォールバック");


        // 🔧 一時的に並列処理を無効化（TransformersOpusMtEngineのIOException問題調査のため）
        var results = new List<string>();

        _logger?.LogInformation("🔄 順次翻訳開始 - チャンク数: {Count}", texts.Count);

        foreach (var text in texts)
        {
            try
            {
                Console.WriteLine($"🌍 [FACADE_DEBUG] Individual translate call for: '{text[..Math.Min(20, text.Length)]}...'");
                // 🔥 [FILE_CONFLICT_FIX_31] ファイルアクセス競合回避のためILogger使用
                _logger?.LogDebug("🌍 [FACADE_DEBUG] Individual translate call for: '{TextPreview}...'",
                    text[..Math.Min(20, text.Length)]);

                var result = await _processingFacade.TranslationService.TranslateAsync(
                    text, sourceLanguage, targetLanguage, null, cancellationToken)
                    .ConfigureAwait(false);

                Console.WriteLine($"🔍 [FACADE_DEBUG] Translation result: IsSuccess={result?.IsSuccess}, Text='{result?.TranslatedText?[..Math.Min(20, result?.TranslatedText?.Length ?? 0)] ?? "null"}...'");
                // 🔥 [FILE_CONFLICT_FIX_32] ファイルアクセス競合回避のためILogger使用
                _logger?.LogDebug("🔍 [FACADE_DEBUG] Translation result: IsSuccess={IsSuccess}, Text='{TextPreview}...'",
                    result?.IsSuccess, result?.TranslatedText?[..Math.Min(20, result?.TranslatedText?.Length ?? 0)] ?? "null");
                results.Add(result.TranslatedText ?? "[Translation Failed]");

                _logger?.LogDebug("✅ 順次翻訳完了: {Text} → {Result}",
                    text.Length > 20 ? string.Concat(text.AsSpan(0, 20), "...") : text,
                    (result.TranslatedText ?? "[Translation Failed]").Length > 20 ?
                        string.Concat(result.TranslatedText.AsSpan(0, 20), "...") : result.TranslatedText ?? "[Translation Failed]");
            }
            catch (TaskCanceledException)
            {
                results.Add("[Translation Timeout]");
                _logger?.LogWarning("⚠️ 翻訳タイムアウト: {Text}", text.Length > 20 ? string.Concat(text.AsSpan(0, 20), "...") : text);
            }
            catch (Exception ex)
            {
                results.Add("[Translation Failed]");
                _logger?.LogError(ex, "❌ 翻訳エラー: {Text}", text.Length > 20 ? string.Concat(text.AsSpan(0, 20), "...") : text);
            }
        }

        _logger?.LogInformation("🏁 順次翻訳完了 - 成功: {Success}/{Total}",
            results.Count(r => !r.StartsWith('[')), results.Count);

        return results;
    }

    // OPUS-MT削除済み: TransformersOpusMtEngine関連機能はNLLB-200統一により不要


    /// <summary>
    /// インプレース翻訳オーバーレイ表示
    /// </summary>
    private async Task DisplayInPlaceTranslationOverlay(
        IReadOnlyList<TextChunk> textChunks,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger?.LogDebug("🖼️ インプレース翻訳オーバーレイ表示開始");
            _logger?.LogDebug("🖼️ インプレース翻訳オーバーレイ表示開始");

            _logger?.LogDebug($"🔥🔥🔥 インプレース翻訳オーバーレイ表示直前 - overlayManager null?: {_processingFacade.OverlayManager == null}");
            if (_processingFacade.OverlayManager != null)
            {
                // 各TextChunkを個別にインプレース表示
                foreach (var textChunk in textChunks)
                {
                    // 🚫 [TRANSLATION_ONLY] 失敗・エラー結果の表示を包括的に防止
                    var hasValidTranslation = TranslationValidator.IsValid(textChunk.TranslatedText, textChunk.CombinedText);

                    if (hasValidTranslation)
                    {
                        // 🚫 Phase 11.2: 重複表示修正 - DisplayInPlaceTranslationOverlay内も無効化
                        // TranslationWithBoundsCompletedEvent → OverlayUpdateEvent 経由で既に表示されている
                        Console.WriteLine($"🚫 [PHASE11.2] DisplayInPlaceTranslationOverlay直接表示スキップ - チャンク {textChunk.ChunkId}");
                        // await _processingFacade.OverlayManager.ShowInPlaceOverlayAsync(textChunk, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        _logger?.LogDebug($"🚫 [TRANSLATION_ONLY] オーバーレイ表示スキップ - ChunkId: {textChunk.ChunkId}, 原文: '{textChunk.CombinedText}'");
                    }
                }
            }
            _logger?.LogDebug("🔥🔥🔥 インプレース翻訳オーバーレイ表示完了");
        }
        catch (TaskCanceledException)
        {
            _logger?.LogDebug("インプレース翻訳オーバーレイ表示がキャンセルされました");
            return;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ インプレース翻訳オーバーレイ表示でエラーが発生");
            _logger?.LogDebug($"❌❌❌ インプレース翻訳オーバーレイエラー: {ex.GetType().Name} - {ex.Message}");
            _logger?.LogDebug($"❌❌❌ スタックトレース: {ex.StackTrace}");
            throw;
        }
    }

    /// <summary>
    /// OCR完了イベントを発行する
    /// </summary>
    /// <param name="image">OCR処理元画像</param>
    /// <param name="textChunks">OCR結果のテキストチャンク</param>
    /// <param name="processingTime">OCR処理時間</param>
    private async Task PublishOcrCompletedEventAsync(IAdvancedImage image, IReadOnlyList<TextChunk> textChunks, TimeSpan processingTime)
    {
        Console.WriteLine($"🔥 [DEBUG] PublishOcrCompletedEventAsync呼び出し開始: チャンク数={textChunks.Count}");
        // 🔥 [FILE_CONFLICT_FIX_33] ファイルアクセス競合回避のためILogger使用
        _logger?.LogDebug("🔥 [DEBUG] PublishOcrCompletedEventAsync呼び出し開始: チャンク数={ChunkCount}", textChunks.Count);

        try
        {
            Console.WriteLine($"🔥 [DEBUG] SelectMany実行開始 - textChunks.Count={textChunks.Count}");
            var positionedResults = textChunks.SelectMany(chunk => chunk.TextResults).ToList();
            Console.WriteLine($"🔥 [DEBUG] SelectMany実行完了 - positionedResults作成成功");
            Console.WriteLine($"🔥 [DEBUG] TextResults検証: チャンク数={textChunks.Count}, positionedResults数={positionedResults.Count}");
            // 🔥 [FILE_CONFLICT_FIX_34] ファイルアクセス競合回避のためILogger使用
            _logger?.LogDebug("🔥 [DEBUG] TextResults検証: チャンク数={ChunkCount}, positionedResults数={ResultsCount}",
                textChunks.Count, positionedResults.Count);

            Console.WriteLine($"🔥 [DEBUG] 条件判定: positionedResults.Count={positionedResults.Count}, 条件結果={positionedResults.Count > 0}");
            if (positionedResults.Count > 0)
            {
                Console.WriteLine($"🔥 [DEBUG] OcrResult作成開始 - positionedResults数: {positionedResults.Count}");

                // 🔥 [PHASE2.5_ROI_COORD_FIX] ROI画像の場合、OCR相対座標を絶対座標に変換
                System.Drawing.Rectangle? captureRegion = null;
                if (image is IAdvancedImage advancedImage)
                {
                    captureRegion = advancedImage.CaptureRegion;
                    if (captureRegion.HasValue)
                    {
                        _logger?.LogDebug("🔥 [ROI_COORD_TRANSFORM] CaptureRegion検出: ({X}, {Y}) - ROI相対座標を絶対座標に変換します",
                            captureRegion.Value.X, captureRegion.Value.Y);
                    }
                }

                var ocrResults = positionedResults.Select(posResult =>
                {
                    var bounds = posResult.BoundingBox;

                    // ROI画像の場合: 相対座標を絶対座標に変換
                    if (captureRegion.HasValue)
                    {
                        var absoluteBounds = new System.Drawing.Rectangle(
                            bounds.X + captureRegion.Value.X,
                            bounds.Y + captureRegion.Value.Y,
                            bounds.Width,
                            bounds.Height);

                        _logger?.LogDebug("🔥 [ROI_COORD_TRANSFORM] 座標変換: 相対({RelX}, {RelY}) → 絶対({AbsX}, {AbsY})",
                            bounds.X, bounds.Y, absoluteBounds.X, absoluteBounds.Y);

                        return new OcrResult(
                            text: posResult.Text,
                            bounds: absoluteBounds,
                            confidence: posResult.Confidence);
                    }
                    else
                    {
                        // 通常画像の場合: OCR座標をそのまま使用
                        return new OcrResult(
                            text: posResult.Text,
                            bounds: bounds,
                            confidence: posResult.Confidence);
                    }
                }).ToList();

                Console.WriteLine($"🔥 [DEBUG] OcrResult作成完了 - ocrResults数: {ocrResults.Count}");

                var ocrCompletedEvent = new OcrCompletedEvent(
                    sourceImage: image,
                    results: ocrResults,
                    processingTime: processingTime);

                Console.WriteLine($"🔥 [DEBUG] OcrCompletedEvent作成完了 - ID: {ocrCompletedEvent.Id}");

                _logger?.LogDebug("🔥 OCR完了イベント発行開始 - Results: {ResultCount}", ocrResults.Count);
                Console.WriteLine($"🔥 [DEBUG] OCR完了イベント発行開始 - Results: {ocrResults.Count}");
                // 🔥 [FILE_CONFLICT_FIX_35] ファイルアクセス競合回避のためILogger使用
                _logger?.LogDebug("🔥 [DEBUG] OCR完了イベント発行開始 - Results: {ResultCount}", ocrResults.Count);

                try
                {
                    Console.WriteLine($"🔥 [DEBUG] EventAggregator.PublishAsync呼び出し直前");
                    Console.WriteLine($"🔥 [DEBUG] EventAggregator型: {_configurationFacade.EventAggregator.GetType().FullName}");
                    Console.WriteLine($"🔥 [DEBUG] EventAggregatorハッシュ: {_configurationFacade.EventAggregator.GetHashCode()}");
                    // 🔥 [FILE_CONFLICT_FIX_36] ファイルアクセス競合回避のためILogger使用
                    _logger?.LogDebug("🔥 [DEBUG] PublishAsync直前 - EventAggregator型: {EventAggregatorType}, ハッシュ: {HashCode}",
                        _configurationFacade.EventAggregator.GetType().FullName, _configurationFacade.EventAggregator.GetHashCode());
                    await _configurationFacade.EventAggregator.PublishAsync(ocrCompletedEvent).ConfigureAwait(false);
                    Console.WriteLine($"🔥 [DEBUG] EventAggregator.PublishAsync呼び出し完了");
                }
                catch (Exception publishEx)
                {
                    Console.WriteLine($"🔥 [ERROR] EventAggregator.PublishAsync例外: {publishEx.GetType().Name} - {publishEx.Message}");
                    // 🔥 [FILE_CONFLICT_FIX_37] ファイルアクセス競合回避のためILogger使用
                    _logger?.LogError(publishEx, "🔥 [ERROR] EventAggregator.PublishAsync例外: {ExceptionType}", publishEx.GetType().Name);
                    throw;
                }

                _logger?.LogDebug("🔥 OCR完了イベント発行完了 - Results: {ResultCount}", ocrResults.Count);
                Console.WriteLine($"🔥 [DEBUG] OCR完了イベント発行完了 - Results: {ocrResults.Count}");
                // 🔥 [FILE_CONFLICT_FIX_38] ファイルアクセス競合回避のためILogger使用
                _logger?.LogDebug("🔥 [DEBUG] OCR完了イベント発行完了 - Results: {ResultCount}", ocrResults.Count);
            }
            else
            {
                _logger?.LogInformation("📝 OCR結果が0件のため、OCR完了イベントの発行をスキップ");
                Console.WriteLine($"🔥 [DEBUG] OCR結果が0件のため、OCR完了イベントの発行をスキップ");
                // 🔥 [FILE_CONFLICT_FIX_39] ファイルアクセス競合回避のためILogger使用
                _logger?.LogDebug("🔥 [DEBUG] OCR結果が0件のため、OCR完了イベントの発行をスキップ");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "OCR完了イベントの発行に失敗しました");
            Console.WriteLine($"🔥 [ERROR] PublishOcrCompletedEventAsync例外: {ex.GetType().Name} - {ex.Message}");
            // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化.Name} - {ex.Message}{Environment.NewLine}");
        }
    }

    /// <summary>
    /// 座標ベース翻訳システムが利用可能かどうかを確認
    /// </summary>
    public bool IsCoordinateBasedTranslationAvailable()
    {
        ThrowIfDisposed();

        try
        {
            var batchOcrAvailable = _processingFacade.OcrProcessor != null;
            var overlayAvailable = _processingFacade.OverlayManager != null;
            var available = batchOcrAvailable && overlayAvailable;

            _logger?.LogDebug($"🔍 [CoordinateBasedTranslationService] 座標ベース翻訳システム可用性チェック:");
            _logger?.LogDebug($"   📦 BatchOcrProcessor: {batchOcrAvailable}");
            _logger?.LogDebug($"   🖼️ OverlayManager: {overlayAvailable}");
            _logger?.LogDebug($"   ✅ 総合判定: {available}");

            _logger?.LogDebug("🔍 座標ベース翻訳システム可用性チェック: {Available}", available);
            return available;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "⚠️ 座標ベース翻訳システム可用性チェックでエラー");
            return false;
        }
    }

    /// <summary>
    /// IEventProcessorインターフェース実装: イベント処理優先度
    /// </summary>
    public int Priority => 100;

    /// <summary>
    /// IEventProcessorインターフェース実装: 同期実行フラグ
    /// </summary>
    public bool SynchronousExecution => false;

    /// <summary>
    /// 🔥 [FALLBACK] 個別翻訳失敗時のフォールバックハンドラー
    /// AggregatedChunksFailedEventを受信し、全画面一括翻訳を実行
    /// </summary>
    public async Task HandleAsync(Baketa.Core.Events.Translation.AggregatedChunksFailedEvent eventData, CancellationToken cancellationToken = default)
    {
        _logger?.LogWarning("🔄 [FALLBACK] 個別翻訳失敗 - 全画面一括翻訳にフォールバック - SessionId: {SessionId}, エラー: {Error}",
            eventData.SessionId, eventData.ErrorMessage);

        try
        {
            if (_streamingTranslationService == null)
            {
                _logger?.LogError("❌ [FALLBACK] StreamingTranslationServiceが利用不可 - フォールバック翻訳を実行できません");
                return;
            }

            // 失敗したチャンクを全て結合
            var combinedText = string.Join(" ", eventData.FailedChunks.Select(c => c.CombinedText));

            _logger?.LogInformation("🔄 [FALLBACK] 全画面一括翻訳実行 - テキスト長: {Length}, チャンク数: {Count}",
                combinedText.Length, eventData.FailedChunks.Count);

            // 全画面一括翻訳実行
            var translationResult = await _streamingTranslationService.TranslateBatchWithStreamingAsync(
                [combinedText],
                Language.FromCode(eventData.SourceLanguage),
                Language.FromCode(eventData.TargetLanguage),
                null!,
                CancellationToken.None).ConfigureAwait(false);

            if (translationResult != null && translationResult.Count > 0)
            {
                var translatedText = translationResult[0];

                // 全画面翻訳結果の座標を計算（全チャンクを包含する矩形）
                var bounds = CalculateCombinedBounds(eventData.FailedChunks);

                _logger?.LogInformation("✅ [FALLBACK] 全画面一括翻訳成功 - Text: '{Text}', Bounds: {Bounds}",
                    translatedText.Substring(0, Math.Min(50, translatedText.Length)), bounds);

                // TranslationWithBoundsCompletedEventを発行（IsFallbackTranslation = true）
                if (_eventAggregator != null)
                {
                    var translationEvent = new TranslationWithBoundsCompletedEvent(
                        sourceText: combinedText,
                        translatedText: translatedText,
                        sourceLanguage: eventData.SourceLanguage,
                        targetLanguage: eventData.TargetLanguage,
                        bounds: bounds,
                        confidence: 1.0f,
                        engineName: "Fallback",
                        isFallbackTranslation: true); // 🔥 [FALLBACK] フォールバックフラグを設定

                    await _eventAggregator.PublishAsync(translationEvent).ConfigureAwait(false);
                    _logger?.LogInformation("✅ [FALLBACK] TranslationWithBoundsCompletedEvent発行完了（IsFallbackTranslation=true）");
                }
            }
            else
            {
                _logger?.LogWarning("⚠️ [FALLBACK] 全画面一括翻訳結果が空 - フォールバック失敗");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ [FALLBACK] 全画面一括翻訳失敗 - 翻訳を表示できません - SessionId: {SessionId}",
                eventData.SessionId);
        }
    }

    /// <summary>
    /// 複数チャンクを包含する矩形を計算
    /// </summary>
    private System.Drawing.Rectangle CalculateCombinedBounds(System.Collections.Generic.List<Baketa.Core.Abstractions.Translation.TextChunk> chunks)
    {
        if (chunks.Count == 0)
            return System.Drawing.Rectangle.Empty;

        var minX = chunks.Min(c => c.CombinedBounds.X);
        var minY = chunks.Min(c => c.CombinedBounds.Y);
        var maxX = chunks.Max(c => c.CombinedBounds.Right);
        var maxY = chunks.Max(c => c.CombinedBounds.Bottom);

        return new System.Drawing.Rectangle(minX, minY, maxX - minX, maxY - minY);
    }

    #region [Issue #290] Fork-Join並列実行

    /// <summary>
    /// [Issue #381] Cloud AI翻訳用に画像を準備（ダウンスケール + JPEG変換）
    /// </summary>
    /// <remarks>
    /// 1. 長辺がCloudImageMaxDimensionを超える場合、アスペクト比を維持して縮小
    /// 2. PNG → JPEG変換でファイルサイズを60-70%削減
    /// BoundingBoxは0-1000正規化スケールのため、解像度変更による座標補正は不要。
    /// </remarks>
    private async Task<(string Base64, int Width, int Height)> PrepareCloudImageDataAsync(IImage image)
    {
        // 1. ダウンスケール
        var maxDim = Math.Max(image.Width, image.Height);
        IImage? resizedImage = null;
        var cloudImage = image;

        if (maxDim > CloudImageMaxDimension)
        {
            var scale = (double)CloudImageMaxDimension / maxDim;
            var newWidth = (int)(image.Width * scale);
            var newHeight = (int)(image.Height * scale);

            resizedImage = await image.ResizeAsync(newWidth, newHeight).ConfigureAwait(false);
            cloudImage = resizedImage;

            _logger?.LogDebug(
                "[Issue #381] Cloud AI用画像ダウンスケール: {OrigW}x{OrigH} → {NewW}x{NewH} (scale={Scale:F2})",
                image.Width, image.Height, newWidth, newHeight, scale);
        }

        try
        {
            var width = cloudImage.Width;
            var height = cloudImage.Height;

            // 2. PNG → JPEG変換（サイズ削減）
            var pngData = cloudImage.GetImageMemory();
            var jpegData = ConvertToJpeg(pngData, CloudJpegQuality);
            var base64 = Convert.ToBase64String(jpegData);

            _logger?.LogDebug(
                "[Issue #381] JPEG変換: PNG={PngKB}KB → JPEG={JpegKB}KB (quality={Quality}, 削減={Reduction:P0})",
                pngData.Length / 1024, jpegData.Length / 1024, CloudJpegQuality,
                1.0 - (double)jpegData.Length / pngData.Length);

            return (base64, width, height);
        }
        finally
        {
            resizedImage?.Dispose();
        }
    }

    /// <summary>
    /// [Issue #381] PNG画像データをJPEGに変換
    /// </summary>
    private static byte[] ConvertToJpeg(ReadOnlyMemory<byte> pngImageData, int quality)
    {
        using var inputStream = new MemoryStream(pngImageData.ToArray());
        using var bitmap = new System.Drawing.Bitmap(inputStream);
        using var outputStream = new MemoryStream();
        using var encoderParams = new System.Drawing.Imaging.EncoderParameters(1);

        encoderParams.Param[0] = new System.Drawing.Imaging.EncoderParameter(
            System.Drawing.Imaging.Encoder.Quality, (long)quality);

        var jpegCodec = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders()
            .FirstOrDefault(c => c.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);

        if (jpegCodec != null)
            bitmap.Save(outputStream, jpegCodec, encoderParams);
        else
            bitmap.Save(outputStream, System.Drawing.Imaging.ImageFormat.Jpeg);

        return outputStream.ToArray();
    }

    /// <summary>
    /// [Issue #290] Fork-Join並列実行（OCR || Cloud AI）が利用可能かチェック
    /// </summary>
    /// <param name="imageBase64">画像データ（Base64エンコード）</param>
    /// <param name="contextWidth">画像幅</param>
    /// <param name="contextHeight">画像高さ</param>
    /// <returns>Fork-Joinが利用可能な場合true</returns>
    private bool ShouldUseForkJoinParallelExecution(string? imageBase64, int contextWidth, int contextHeight)
    {
        // フォールバックオーケストレーターが必要
        if (_fallbackOrchestrator == null)
        {
            _logger?.LogDebug("[Issue #290] Fork-Joinスキップ: FallbackOrchestrator未登録");
            return false;
        }

        // ライセンスマネージャーが必要
        if (_licenseManager == null)
        {
            _logger?.LogDebug("[Issue #290] Fork-Joinスキップ: LicenseManager未登録");
            return false;
        }

        // Cloud翻訳可用性サービスでの判定（優先）
        if (_cloudTranslationAvailabilityService != null)
        {
            if (!_cloudTranslationAvailabilityService.IsEffectivelyEnabled)
            {
                _logger?.LogDebug(
                    "[Issue #290] Fork-Joinスキップ: Cloud翻訳無効 (Entitled={Entitled}, Preferred={Preferred})",
                    _cloudTranslationAvailabilityService.IsEntitled,
                    _cloudTranslationAvailabilityService.IsPreferred);
                return false;
            }
        }
        else
        {
            // フォールバック: 旧ロジック（ICloudTranslationAvailabilityService未登録時）
            if (!_licenseManager.IsFeatureAvailable(FeatureType.CloudAiTranslation))
            {
                _logger?.LogDebug("[Issue #290] Fork-Joinスキップ: CloudAiTranslation機能が無効");
                return false;
            }

            // ユーザー設定でCloud AI翻訳が有効か確認
            var translationSettings = _configurationFacade.SettingsService.GetTranslationSettings();
            if (translationSettings.UseLocalEngine)
            {
                _logger?.LogDebug("[Issue #290] Fork-Joinスキップ: UseLocalEngine=true");
                return false;
            }
        }

        // 画像データが必要
        if (string.IsNullOrEmpty(imageBase64) || contextWidth <= 0 || contextHeight <= 0)
        {
            _logger?.LogDebug("[Issue #290] Fork-Joinスキップ: 画像データなし");
            return false;
        }

        // セッショントークンが必要
        var sessionId = _licenseManager.CurrentState.SessionId;
        if (string.IsNullOrEmpty(sessionId))
        {
            _logger?.LogDebug("[Issue #290] Fork-Joinスキップ: セッショントークンなし");
            return false;
        }

        _logger?.LogInformation("✅ [Issue #290] Fork-Join並列実行: 全条件クリア");
        return true;
    }

    /// <summary>
    /// [Issue #290] Cloud AI翻訳を非同期実行（Fork-Join用）
    /// </summary>
    /// <param name="imageBase64">画像データ（Base64エンコード）</param>
    /// <param name="contextWidth">画像幅（座標マッピング用、元サイズ）</param>
    /// <param name="contextHeight">画像高さ（座標マッピング用、元サイズ）</param>
    /// <param name="cloudImageWidth">[Issue #381] 実際に送信するCloud画像幅（ログ・トークン推定用）</param>
    /// <param name="cloudImageHeight">[Issue #381] 実際に送信するCloud画像高さ（ログ・トークン推定用）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <param name="windowHandle">[Issue #427] ウィンドウハンドル（翻訳履歴取得用）</param>
    /// <param name="cachedOcrHints">[Issue #429] 前回サイクルのOCRヒントキャッシュ</param>
    /// <returns>フォールバック翻訳結果</returns>
    private async Task<FallbackTranslationResult?> ExecuteForkJoinCloudTranslationAsync(
        string imageBase64,
        int contextWidth,
        int contextHeight,
        int cloudImageWidth,
        int cloudImageHeight,
        CancellationToken cancellationToken,
        IntPtr windowHandle = default,
        OcrHints? cachedOcrHints = null)
    {
        if (_fallbackOrchestrator == null || _licenseManager == null)
            return null;

        try
        {
            var stopwatch = Stopwatch.StartNew();
            _logger?.LogInformation("🚀 [Issue #290] Fork-Join Cloud AI翻訳開始");

            // 言語ペアを取得
            var translationSettings = _configurationFacade.SettingsService.GetTranslationSettings();
            var targetLanguage = translationSettings.DefaultTargetLanguage ?? "ja";
            _logger?.LogInformation("[Issue #517] Cloud AI翻訳リクエスト - TargetLanguage='{TargetLanguage}' (from settings: '{SettingsValue}')",
                targetLanguage, translationSettings.DefaultTargetLanguage);

            // セッショントークンを取得
            var sessionToken = _licenseManager.CurrentState.SessionId ?? string.Empty;

            // [Issue #427] 翻訳履歴を取得
            IReadOnlyList<TranslationHistoryEntry>? history = null;
            if (windowHandle != IntPtr.Zero &&
                _translationHistoryBuffer.TryGetValue(windowHandle, out var historyList) &&
                historyList.Count > 0)
            {
                lock (historyList)
                {
                    history = historyList.ToList().AsReadOnly();
                }
                _logger?.LogDebug("[Issue #427] 翻訳履歴を注入: {Count}件", history.Count);
            }

            // [Issue #429] OCR配置ヒントを注入（前回サイクルのキャッシュから取得）
            OcrHints? ocrHints = cachedOcrHints;
            if (ocrHints != null)
            {
                _logger?.LogDebug("[Issue #429] OCR配置ヒント注入: {Count}領域, Areas=[{Areas}]",
                    ocrHints.TextRegionCount, string.Join(", ", ocrHints.TextAreas));
            }

            // リクエストを作成
            // [Issue #381] Width/Heightは実際に送信するCloud画像サイズ（ログ・トークン推定用）
            var request = new ImageTranslationRequest
            {
                ImageBase64 = imageBase64,
                Width = cloudImageWidth > 0 ? cloudImageWidth : contextWidth,
                Height = cloudImageHeight > 0 ? cloudImageHeight : contextHeight,
                TargetLanguage = targetLanguage,
                SessionToken = sessionToken,
                MimeType = CloudImageMimeType,
                TranslationHistory = history,
                OcrHints = ocrHints
            };

            // Cloud AI翻訳を実行（フォールバック付き）
            var result = await _fallbackOrchestrator.TranslateWithFallbackAsync(request, cancellationToken)
                .ConfigureAwait(false);

            stopwatch.Stop();
            _logger?.LogInformation(
                "✅ [Issue #290] Fork-Join Cloud AI翻訳完了: Success={Success}, Engine={Engine}, Duration={Duration}ms",
                result.IsSuccess, result.UsedEngine, stopwatch.ElapsedMilliseconds);

            // [Issue #427] 翻訳成功時に履歴バッファに蓄積
            if (result.IsSuccess && windowHandle != IntPtr.Zero &&
                result.Response?.Texts is { Count: > 0 } texts)
            {
                AppendTranslationHistory(windowHandle, texts);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger?.LogDebug("[Issue #290] Fork-Join Cloud AI翻訳がキャンセルされました");
            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[Issue #290] Fork-Join Cloud AI翻訳でエラー発生");
            return null;
        }
    }

    /// <summary>
    /// [Issue #427] 翻訳履歴バッファに蓄積（最大MaxTranslationHistoryCount件）
    /// </summary>
    private void AppendTranslationHistory(IntPtr windowHandle, IReadOnlyList<TranslatedTextItem> texts)
    {
        var historyList = _translationHistoryBuffer.GetOrAdd(windowHandle, _ => new List<TranslationHistoryEntry>());
        lock (historyList)
        {
            // 翻訳結果から代表的なテキスト（最長のもの）を1件履歴に追加
            var representative = texts
                .Where(t => !string.IsNullOrWhiteSpace(t.Original) && !string.IsNullOrWhiteSpace(t.Translation))
                .OrderByDescending(t => t.Original.Length)
                .FirstOrDefault();

            if (representative != null)
            {
                historyList.Add(new TranslationHistoryEntry
                {
                    Original = representative.Original,
                    Translation = representative.Translation
                });

                // 上限超過時は古いものを削除
                while (historyList.Count > MaxTranslationHistoryCount)
                {
                    historyList.RemoveAt(0);
                }
            }
        }
    }

    /// <summary>
    /// [Issue #508] Detection-Onlyのバウンディングボックスから軽量配置ヒントを生成
    /// </summary>
    /// <summary>
    /// [Issue #508] DetectionBoundsCacheのboundsから画像高さを推定する。
    /// キャッシュにはDetection画像の高さ情報がないため、boundsの最下端座標から推定。
    /// </summary>
    private static int EstimateImageHeightFromBounds(System.Drawing.Rectangle[] bounds)
    {
        var maxBottom = 0;
        foreach (var rect in bounds)
        {
            var bottom = rect.Y + rect.Height;
            if (bottom > maxBottom) maxBottom = bottom;
        }
        // 最下端の矩形が画面下部にあると仮定し、余裕を持たせて高さを推定
        // 一般的なキャプチャ解像度: 720, 1080, 1440, 2160
        return maxBottom switch
        {
            <= 800 => 720,
            <= 1200 => 1080,
            <= 1600 => 1440,
            _ => 2160
        };
    }

    internal static OcrHints BuildOcrHintsFromBounds(System.Drawing.Rectangle[] bounds, int contextHeight)
    {
        var areas = new List<string>();
        var topThird = contextHeight / 3;
        var bottomThird = contextHeight * 2 / 3;

        foreach (var rect in bounds)
        {
            var centerY = rect.Y + rect.Height / 2;
            var area = centerY < topThird ? "Top"
                     : centerY < bottomThird ? "Center"
                     : "Bottom";
            if (!areas.Contains(area))
                areas.Add(area);
        }

        return new OcrHints
        {
            TextRegionCount = bounds.Length,
            TextAreas = areas.AsReadOnly()
        };
    }

    /// <summary>
    /// [Issue #429] OCR検出結果から軽量配置ヒントを生成
    /// </summary>
    private static OcrHints BuildOcrHints(
        IReadOnlyList<Baketa.Core.Abstractions.Translation.TextChunk> textChunks,
        int contextHeight)
    {
        var areas = new List<string>();
        var topThird = contextHeight / 3;
        var bottomThird = contextHeight * 2 / 3;

        foreach (var chunk in textChunks)
        {
            var centerY = chunk.CombinedBounds.Y + chunk.CombinedBounds.Height / 2;
            var area = centerY < topThird ? "Top"
                     : centerY < bottomThird ? "Center"
                     : "Bottom";
            if (!areas.Contains(area))
                areas.Add(area);
        }

        return new OcrHints
        {
            TextRegionCount = textChunks.Count,
            TextAreas = areas.AsReadOnly()
        };
    }

    #endregion

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>
    /// [Issue #293] ウィンドウハンドルから実行ファイルパスを取得
    /// </summary>
    /// <param name="windowHandle">ウィンドウハンドル</param>
    /// <returns>実行ファイルパス、取得失敗時は空文字列</returns>
    private string GetExecutablePathFromWindow(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return string.Empty;
        }

        try
        {
            // Win32 API: GetWindowThreadProcessId でプロセスIDを取得
            _ = GetWindowThreadProcessId(windowHandle, out uint processId);
            if (processId == 0)
            {
                _logger?.LogDebug("[Issue #293] GetWindowThreadProcessId failed for handle 0x{Handle:X}", windowHandle.ToInt64());
                return string.Empty;
            }

            // プロセスIDからプロセス情報を取得
            using var process = System.Diagnostics.Process.GetProcessById((int)processId);
            var exePath = process.MainModule?.FileName ?? string.Empty;

            _logger?.LogDebug("[Issue #293] GetExecutablePathFromWindow: PID={ProcessId}, ExePath='{ExePath}'", processId, exePath);
            return exePath;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // アクセス権限不足など
            _logger?.LogDebug(ex, "[Issue #293] GetExecutablePathFromWindow: Win32 error for handle 0x{Handle:X}", windowHandle.ToInt64());
            return string.Empty;
        }
        catch (InvalidOperationException ex)
        {
            // プロセスが既に終了している
            _logger?.LogDebug(ex, "[Issue #293] GetExecutablePathFromWindow: Process already exited for handle 0x{Handle:X}", windowHandle.ToInt64());
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[Issue #293] GetExecutablePathFromWindow: Unexpected error for handle 0x{Handle:X}", windowHandle.ToInt64());
            return string.Empty;
        }
    }

    // [Issue #293] Win32 API declaration for GetWindowThreadProcessId
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    public void Dispose()
    {
        if (_disposed) return;

        try
        {
            // 🔥 [GEMINI_FIX] メモリリーク防止のためイベントの購読を解除
            if (_eventAggregator != null)
            {
                _eventAggregator.Unsubscribe<Baketa.Core.Events.Translation.AggregatedChunksFailedEvent>(this);
                _logger?.LogDebug("✅ [DISPOSE] AggregatedChunksFailedEventハンドラー登録解除完了");
            }

            // MultiWindowOverlayManagerのクリーンアップ
            if (_processingFacade.OverlayManager is IDisposable disposableOverlayManager)
            {
                disposableOverlayManager.Dispose();
            }

            // BatchOcrProcessorのクリーンアップ
            if (_processingFacade.OcrProcessor is IDisposable disposableBatchProcessor)
            {
                disposableBatchProcessor.Dispose();
            }

            // [Issue #397] OCRテキストキャッシュのクリア（メモリリーク防止）
            _previousOcrTextCache.Clear();
            _previousOcrHintsCache.Clear();

            _disposed = true;
            _logger?.LogInformation("🧹 CoordinateBasedTranslationService disposed - Hash: {Hash}", this.GetHashCode());
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ CoordinateBasedTranslationService dispose error");
        }
    }

    #region 🚀 [Issue #193] Win32 API for coordinate scaling

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    /// <summary>
    /// 🚀 [Issue #193] ウィンドウハンドルから元のウィンドウサイズを取得
    /// GPU Shaderリサイズ後のOCR座標をスケーリングするために使用
    /// </summary>
    private static Size GetOriginalWindowSize(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return Size.Empty;

        if (GetWindowRect(hwnd, out RECT rect))
        {
            return new Size(rect.Right - rect.Left, rect.Bottom - rect.Top);
        }

        return Size.Empty;
    }

    #endregion
}
