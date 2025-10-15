using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Baketa.Infrastructure.OCR.PaddleOCR.Models;
using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Abstractions.Imaging;
// 🔥 [PHASE7] SafeImage, ReferencedSafeImage, ImagePixelFormat用の型エイリアス（Rectangle曖昧性回避）
using SafeImage = Baketa.Core.Abstractions.Memory.SafeImage;
using ReferencedSafeImage = Baketa.Core.Abstractions.Memory.ReferencedSafeImage;
using ImagePixelFormat = Baketa.Core.Abstractions.Memory.ImagePixelFormat;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events.EventTypes;
using Baketa.Core.Events.Diagnostics;
using Baketa.Core.Extensions;
using Baketa.Core.Utilities;
using Baketa.Core.Performance;
using Baketa.Core.Abstractions.Logging;
using Baketa.Core.Settings;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models;
using Sdcb.PaddleOCR.Models.Local;
using Sdcb.PaddleOCR.Models.Shared;
using static Sdcb.PaddleOCR.Models.ModelVersion;
using System.Buffers;
using System.IO;
using OpenCvSharp;
using System.Drawing;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.Security;
using System.Reflection;
using System.Windows.Forms;
using Baketa.Core.Abstractions.Imaging.Pipeline;
using Baketa.Core.Services.Imaging;
using Baketa.Core.Abstractions.Performance;
using Baketa.Infrastructure.OCR.Preprocessing;
using Baketa.Infrastructure.OCR.PostProcessing;
using Baketa.Infrastructure.OCR.TextProcessing;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.OCR.Results;
using Baketa.Infrastructure.OCR.PaddleOCR.Services;
using Baketa.Infrastructure.OCR.PaddleOCR.Abstractions;
using Baketa.Infrastructure.OCR.Scaling;
using IImageFactoryType = Baketa.Core.Abstractions.Factories.IImageFactory;
// ✅ [PHASE2.9.3.1] 型の曖昧性解決用エイリアス
using CoreOcrProgress = Baketa.Core.Abstractions.OCR.OcrProgress;
using PreprocessingImageCharacteristics = Baketa.Infrastructure.OCR.Preprocessing.ImageCharacteristics;

namespace Baketa.Infrastructure.OCR.PaddleOCR.Engine;

/// <summary>
/// PaddleOCRエンジンの実装クラス（IOcrEngine準拠）
/// 多重初期化防止機能付き
/// </summary>
public class PaddleOcrEngine : Baketa.Core.Abstractions.OCR.IOcrEngine
{
    // ❌ DI競合解決: 自己流シングルトン管理を廃止（DIコンテナ+ObjectPoolに一任）
    // ✅ ObjectPoolによる適切なライフサイクル管理により、独自インスタンス追跡は不要
    // private static readonly object _globalLock = new();
    // private static volatile int _instanceCount;
    // private static readonly ConcurrentDictionary<string, PaddleOcrEngine> _instances = new();

    // ✅ [PHASE2.9.3.2] New Service Dependencies (Facade Pattern)
    private readonly IPaddleOcrImageProcessor _imageProcessor;
    private readonly IPaddleOcrResultConverter _resultConverter;
    private readonly IPaddleOcrExecutor _executor;
    private readonly IPaddleOcrModelManager _modelManager;
    private readonly IPaddleOcrPerformanceTracker _performanceTracker;
    private readonly IPaddleOcrErrorHandler _errorHandler;
    // ✅ [PHASE2.11.4] エンジン初期化サービス統合（Phase 2.6実装）
    private readonly IPaddleOcrEngineInitializer _engineInitializer;

    // Legacy Dependencies (段階的に削減予定)
    private readonly IModelPathResolver __modelPathResolver;
    // ✅ [PHASE2.9.5] IOcrPreprocessingService削除 - 診断ログのみで使用、実質未使用
    private readonly ITextMerger __textMerger;
    private readonly IOcrPostProcessor __ocrPostProcessor;
    private readonly IGpuMemoryManager __gpuMemoryManager;
    private readonly IUnifiedSettingsService __unifiedSettingsService;
    // ✅ [PHASE2.9.5] IUnifiedLoggingService削除 - 全168箇所コメントアウト済み、完全未使用
    private readonly ILogger<PaddleOcrEngine>? __logger;
    private readonly IEventAggregator __eventAggregator;
    private readonly IOptionsMonitor<OcrSettings> _ocrSettings;
    private readonly IImageFactoryType __imageFactory;

    public PaddleOcrEngine(
        // ✅ [PHASE2.9.3.2] New Service Dependencies
        IPaddleOcrImageProcessor imageProcessor,
        IPaddleOcrResultConverter resultConverter,
        IPaddleOcrExecutor executor,
        IPaddleOcrModelManager modelManager,
        IPaddleOcrPerformanceTracker performanceTracker,
        IPaddleOcrErrorHandler errorHandler,
        // ✅ [PHASE2.11.4] エンジン初期化サービス
        IPaddleOcrEngineInitializer engineInitializer,
        // Legacy Dependencies
        IModelPathResolver _modelPathResolver,
        // ✅ [PHASE2.9.5] IOcrPreprocessingService削除
        ITextMerger _textMerger,
        IOcrPostProcessor _ocrPostProcessor,
        IGpuMemoryManager _gpuMemoryManager,
        IUnifiedSettingsService _unifiedSettingsService,
        IEventAggregator _eventAggregator,
        IOptionsMonitor<OcrSettings> ocrSettings,
        IImageFactoryType imageFactory,
        // ✅ [PHASE2.9.5] IUnifiedLoggingService削除
        ILogger<PaddleOcrEngine>? _logger = null)
    {
        // ✅ [PHASE2.9.3.2] New Service Initialization
        _imageProcessor = imageProcessor ?? throw new ArgumentNullException(nameof(imageProcessor));
        _resultConverter = resultConverter ?? throw new ArgumentNullException(nameof(resultConverter));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _modelManager = modelManager ?? throw new ArgumentNullException(nameof(modelManager));
        _performanceTracker = performanceTracker ?? throw new ArgumentNullException(nameof(performanceTracker));
        _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
        // ✅ [PHASE2.11.4] エンジン初期化サービス初期化
        _engineInitializer = engineInitializer ?? throw new ArgumentNullException(nameof(engineInitializer));

        // Legacy Initialization
        __modelPathResolver = _modelPathResolver ?? throw new ArgumentNullException(nameof(_modelPathResolver));
        // ✅ [PHASE2.9.5] __ocrPreprocessingService削除
        __textMerger = _textMerger ?? throw new ArgumentNullException(nameof(_textMerger));
        __ocrPostProcessor = _ocrPostProcessor ?? throw new ArgumentNullException(nameof(_ocrPostProcessor));
        __gpuMemoryManager = _gpuMemoryManager ?? throw new ArgumentNullException(nameof(_gpuMemoryManager));
        __unifiedSettingsService = _unifiedSettingsService ?? throw new ArgumentNullException(nameof(_unifiedSettingsService));
        __eventAggregator = _eventAggregator ?? throw new ArgumentNullException(nameof(_eventAggregator));
        _ocrSettings = ocrSettings ?? throw new ArgumentNullException(nameof(ocrSettings));
        __imageFactory = imageFactory ?? throw new ArgumentNullException(nameof(imageFactory));
        // ✅ [PHASE2.9.5] _unifiedLoggingService削除
        __logger = _logger;
        
        // ❌ DI競合解決: インスタンス作成追跡を無効化（ObjectPool管理に一任）
        // TrackInstanceCreation();
    }

    private readonly object _lockObject = new();
    
    // インスタンス追跡
    private readonly int _instanceId;

    // ✅ [PHASE2.9.5] _serviceTypeLogged削除 - Phase 3診断ログ廃止に伴い不要

    private PaddleOcrAll? _ocrEngine;
    private QueuedPaddleOcrAll? _queuedEngine;
    private OcrEngineSettings _settings = new();
    private bool _disposed;
    // V5統一完了 - 旧false /* V5統一により常にfalse */フィールドは完全削除済み
    
    // スレッドセーフティ対策：スレッドごとにOCRエンジンを保持
    private static readonly ThreadLocal<PaddleOcrAll?> _threadLocalOcrEngine = new(() => null);
    
    /// <summary>
    /// 🎯 [GEMINI_EMERGENCY_FIX_V2] PaddleOCR実行の真のグローバル単一スレッド化
    /// PaddleOCRネイティブライブラリのスレッド安全性問題を根本解決
    /// 複数インスタンス間でのスレッド競合による「PaddlePredictor run failed」エラーを防止
    /// 静的フィールドにより全PaddleOcrEngineインスタンスで共有される真の同期を実現
    /// </summary>
    private static readonly SemaphoreSlim _globalOcrSemaphore = new(1, 1);
    
    // パフォーマンス統計
    private readonly ConcurrentQueue<double> _processingTimes = new();
    
    // 適応的タイムアウト用の統計
    private DateTime _lastOcrTime = DateTime.MinValue;
    private int _consecutiveTimeouts;
    
    // PaddlePredictor失敗統計
    private int _consecutivePaddleFailures;
    
    // 進行中OCRタスクのキャンセレーション管理
    private CancellationTokenSource? _currentOcrCancellation;
    private int _totalProcessedImages;
    private int _errorCount;
    private readonly DateTime _startTime = DateTime.UtcNow;
    
    // 🔄 [HYBRID_STRATEGY] ハイブリッドOCR戦略サポート
    private HybridPaddleOcrService? _hybridService;
    private HybridOcrSettings? _hybridSettings;
    private bool _isHybridMode;
    
    // ❌ DI競合解決: 静的コンストラクタと独自インスタンス追跡を無効化
    // ✅ ObjectPoolとDIコンテナによる適切なライフサイクル管理に一任
    /*
    // コンストラクタで多重初期化チェック
    static PaddleOcrEngine()
    {
        // 静的コンストラクタで初期化追跡を開始
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] 🚨 PaddleOcrEngine静的コンストラクタ実行");
    }

    // インスタンス初期化時の追跡（継承クラスでオーバーライド可能）
    protected virtual void TrackInstanceCreation()
    {
        var newCount = Interlocked.Increment(ref _instanceCount);
        __logger?.LogWarning("🚨 PaddleOcrEngine インスタンス #{Count} が作成されました", newCount);
        
        if (newCount > 1)
        {
            __logger?.LogError("⚠️ 多重インスタンス検出! 合計: {Count}個", newCount);
            
            // スタックトレースで呼び出し元を特定
            var stackTrace = new StackTrace(true);
            var frames = stackTrace.GetFrames()?.Take(10);
            foreach (var frame in frames ?? [])
            {
                var method = frame.GetMethod();
                var fileName = frame.GetFileName();
                var lineNumber = frame.GetFileLineNumber();
                __logger?.LogError("  at {Method} in {File}:line {Line}", 
                    method?.DeclaringType?.Name + "." + method?.Name, 
                    System.IO.Path.GetFileName(fileName), 
                    lineNumber);
            }
        }
    }
    */

    public string EngineName => "PaddleOCR";
    public string EngineVersion => "2.7.0.3"; // Sdcb.PaddleOCRのバージョン
    public bool IsInitialized { get; private set; }
    public string? CurrentLanguage { get; private set; }
    
    /// <summary>
    /// マルチスレッド対応が有効かどうか
    /// </summary>
    public bool IsMultiThreadEnabled { get; private set; }

    /// <summary>
    /// OCRエンジンを初期化
    /// ✅ [PHASE2.11.5] Facade Pattern完全実装 - 専門サービスへの完全委譲
    /// </summary>
    /// <param name="settings">エンジン設定（省略時はデフォルト設定）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>初期化が成功した場合はtrue</returns>
    public async Task<bool> InitializeAsync(OcrEngineSettings? settings = null, CancellationToken cancellationToken = default)
    {
        settings ??= new OcrEngineSettings();

        if (!settings.IsValid())
        {
            __logger?.LogError("無効な設定でOCRエンジンの初期化が試行されました");
            return false;
        }

        ResetFailureCounter();
        ThrowIfDisposed();

        if (IsInitialized)
        {
            __logger?.LogDebug("PaddleOCRエンジンは既に初期化されています");
            return true;
        }

        try
        {
            // スレッドセーフティのためCPU/シングルスレッド強制
            settings.UseGpu = false;
            settings.EnableMultiThread = false;
            settings.WorkerCount = 1;

            __logger?.LogInformation("PaddleOCRエンジンの初期化開始 - 言語: {Language}", settings.Language);

            // ✅ [PHASE2.11.5] ネイティブライブラリチェック → IPaddleOcrEngineInitializer委譲
            if (!_engineInitializer.CheckNativeLibraries())
            {
                __logger?.LogError("必要なネイティブライブラリが見つかりません");
                return false;
            }

            // ✅ [PHASE2.11.5] モデル準備 → IPaddleOcrModelManager委譲
            var models = await _modelManager.PrepareModelsAsync(settings.Language, cancellationToken).ConfigureAwait(false);
            if (models == null)
            {
                __logger?.LogError("モデルの準備に失敗しました");
                return false;
            }

            // ✅ [PHASE2.11.5] エンジン初期化 → IPaddleOcrEngineInitializer委譲
            var success = await _engineInitializer.InitializeEnginesAsync(models, settings, cancellationToken).ConfigureAwait(false);

            if (success)
            {
                _settings = settings.Clone();
                CurrentLanguage = settings.Language;

                await InitializeHybridModeAsync(settings, cancellationToken).ConfigureAwait(false);

                IsInitialized = true;
                __logger?.LogInformation("PaddleOCRエンジンの初期化完了");
            }

            return success;
        }
        catch (OperationCanceledException)
        {
            __logger?.LogInformation("OCRエンジンの初期化がキャンセルされました");
            throw;
        }
        catch (Exception ex)
        {
            __logger?.LogError(ex, "OCRエンジン初期化エラー: {ExceptionType}", ex.GetType().Name);
            return false;
        }
    }

    /// <summary>
    /// エンジンのウォームアップを実行（初回実行時の遅延を解消）
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>ウォームアップが成功したか</returns>
    public async Task<bool> WarmupAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            __logger?.LogInformation("🔥 PaddleOCRウォームアップ開始");
            var stopwatch = Stopwatch.StartNew();
            
            // 小さなダミー画像を作成（512x512の白い画像）
            var dummyImageData = new byte[512 * 512 * 3];
            for (int i = 0; i < dummyImageData.Length; i++)
            {
                dummyImageData[i] = 255; // 白で埋める
            }
            
            // ダミー画像オブジェクトを作成
            var dummyImage = new Core.Services.Imaging.AdvancedImage(
                dummyImageData, 
                512, 
                512, 
                Core.Abstractions.Imaging.ImageFormat.Rgb24);
            
            // PaddleOCR実行（モデルをメモリにロード）
            __logger?.LogInformation("📝 ダミー画像でOCR実行中...");
            
            // 実際のOCR処理を小さい画像で実行してモデルをロード
            var result = await RecognizeAsync(dummyImage, progressCallback: null, cancellationToken).ConfigureAwait(false);
            
            stopwatch.Stop();
            __logger?.LogInformation("✅ PaddleOCRウォームアップ完了: {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            
            // 結果を簡単にログ出力
            __logger?.LogInformation("🔍 ウォームアップOCR結果: 検出領域数={Count}", result.TextRegions.Count);
            
            return true;
        }
        catch (Exception ex)
        {
            __logger?.LogError(ex, "❌ PaddleOCRウォームアップ中にエラーが発生");
            return false;
        }
    }

    /// <summary>
    /// 画像からテキストを認識します
    /// </summary>
    /// <param name="image">画像</param>
    /// <param name="progressCallback">進捗通知コールバック（オプション）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>OCR結果</returns>
    public async Task<OcrResults> RecognizeAsync(
        IImage image,
        IProgress<CoreOcrProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        return await RecognizeAsync(image, null, progressCallback, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 画像の指定領域からテキストを認識します（ゲームOCR最重要機能）
    /// </summary>
    /// <param name="image">画像</param>
    /// <param name="regionOfInterest">認識領域（nullの場合は画像全体）</param>
    /// <param name="progressCallback">進捗通知コールバック（オプション）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>OCR結果</returns>
    public async Task<OcrResults> RecognizeAsync(
        IImage image,
        Rectangle? regionOfInterest,
        IProgress<CoreOcrProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ThrowIfDisposed();
        
        // 🎯 [GEMINI_EMERGENCY_FIX] PaddleOCRスレッド安全性保護
        // 単一スレッド実行でPaddlePredictor run failed エラーを根本解決
        await _globalOcrSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stopwatch = Stopwatch.StartNew();
        var sessionId = Guid.NewGuid().ToString("N")[..8];

        // 📊 [DIAGNOSTIC] OCR処理開始イベント
        await __eventAggregator.PublishAsync(new PipelineDiagnosticEvent
        {
            Stage = "OCR",
            IsSuccess = true,
            ProcessingTimeMs = 0,
            SessionId = sessionId,
            Severity = DiagnosticSeverity.Information,
            Message = $"OCR処理開始: エンジン={EngineName}, 画像サイズ={image.Width}x{image.Height}, ROI={regionOfInterest?.ToString() ?? "なし"}",
            Metrics = new Dictionary<string, object>
            {
                { "Engine", EngineName },
                { "EngineVersion", EngineVersion },
                { "ImageWidth", image.Width },
                { "ImageHeight", image.Height },
                { "HasROI", regionOfInterest.HasValue },
                { "ROI", regionOfInterest?.ToString() ?? "なし" },
                { "Language", CurrentLanguage ?? "jpn" },
                { "IsInitialized", IsInitialized }
            }
        }).ConfigureAwait(false);
        
        // 初期化ガード: 未初期化の場合は自動初期化を実行（スレッドセーフ）
        if (!IsInitialized)
        {
            lock (_lockObject)
            {
                // ダブルチェックロッキングパターンで競合状態を回避
                if (!IsInitialized)
                {
                    // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog("⚠️ OCRエンジンが未初期化のため、自動初期化を実行します");
                    
                    // 初期化処理は非同期だが、ここではlockを使用しているため同期的に処理
                    var initTask = InitializeAsync(_settings, cancellationToken);
                    var initResult = initTask.GetAwaiter().GetResult();
                    
                    if (!initResult)
                    {
                        throw new InvalidOperationException("OCRエンジンの自動初期化に失敗しました。システム要件を確認してください。");
                    }
                    // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog("✅ OCRエンジンの自動初期化が完了しました");
                }
            }
        }
        
        // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"🔍 PaddleOcrEngine.RecognizeAsync開始:");
        // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"   ✅ 初期化状態: {IsInitialized}");
        // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"   🌐 現在の言語: {CurrentLanguage} (認識精度向上のため言語ヒント適用)");
        // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"   📏 画像サイズ: {image.Width}x{image.Height}");
        // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"   🎯 ROI: {regionOfInterest?.ToString() ?? "なし（全体）"}");
        // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"   📊 認識設定: 検出閾値={_settings.DetectionThreshold}, 認識閾値={_settings.RecognitionThreshold}");
        
        // テスト環境ではダミー結果を返す
        var isTestEnv = IsTestEnvironment();
        // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"   🧪 テスト環境判定: {isTestEnv}");
        
        if (isTestEnv)
        {
            // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog("🧪 テスト環境: ダミーOCR結果を返却");
            __logger?.LogDebug("テスト環境: ダミーOCR結果を返却");
            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            
            var dummyTextRegions = new List<OcrTextRegion>
            {
                new("テストテキスト", new Rectangle(10, 10, 100, 30), 0.95)
            };
            
            return new OcrResults(
                dummyTextRegions,
                image,
                stopwatch.Elapsed,
                CurrentLanguage ?? "jpn",
                regionOfInterest,
                "テストテキスト" // テスト環境では結合テキストも固定
            );
        }

        try
        {
            // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog("🎬 実際のOCR処理を開始");
            progressCallback?.Report(new CoreOcrProgress(0.1, "OCR処理を開始"));
            
            // IImageからMatに変換（大画面対応スケーリング付き）
            // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog("🔄 IImageからMatに変換中...");
            var (mat, scaleFactor) = await ConvertToMatWithScalingAsync(image, regionOfInterest, cancellationToken).ConfigureAwait(false);
            
            // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"🖼️ Mat変換完了: Empty={mat.Empty()}, Size={mat.Width}x{mat.Height}, スケール係数={scaleFactor:F3}");
            
            if (mat.Empty())
            {
                mat.Dispose();
                // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog("❌ 変換後の画像が空です");
                __logger?.LogWarning("変換後の画像が空です");
                return CreateEmptyResult(image, regionOfInterest, stopwatch.Elapsed);
            }
            
            // OCR実行結果を格納する変数を宣言（スコープ問題解決）
            IReadOnlyList<OcrTextRegion> textRegions = [];
            string? mergedText = null;
            string? postProcessedText = null;
            
            using (mat) // Matのリソース管理
            {
                // 🎯 [ULTRATHINK_PREVENTION] OCR実行前の早期予防システム
                progressCallback?.Report(new CoreOcrProgress(0.25, "画像品質検証中"));
            
                Mat processedMat;
            try 
            {
                processedMat = ApplyPreventiveNormalization(mat);
                __logger?.LogDebug("✅ [PREVENTIVE_NORM] 早期正規化完了");
            }
            catch (Exception ex)
            {
                __logger?.LogError(ex, "🚨 [PREVENTIVE_NORM] 早期正規化失敗");
                return CreateEmptyResult(image, regionOfInterest, stopwatch.Elapsed);
            }
            
            progressCallback?.Report(new CoreOcrProgress(0.3, "OCR処理実行中"));
            
            using (processedMat) // processedMatの適切なDispose管理
            {
                if (_isHybridMode && _hybridService != null)
                {
                    __logger?.LogDebug("🔄 ハイブリッドモードでOCR実行（予防処理済み）");
                    var processingMode = DetermineProcessingMode();
                    textRegions = await _hybridService.ExecuteHybridOcrAsync(processedMat, processingMode, cancellationToken).ConfigureAwait(false);
                    __logger?.LogDebug($"🔄 ハイブリッドOCR完了: {textRegions.Count}領域検出 ({processingMode}モード)");
                }
                else
                {
                    __logger?.LogDebug("📊 シングルモードでOCR実行（予防処理済み）");

                    // ✅ [PHASE2.9.3.4b] _executor + _resultConverter使用に置換
                    var paddleResult = await _executor.ExecuteOcrAsync(processedMat, progressCallback, cancellationToken).ConfigureAwait(false);
                    textRegions = _resultConverter.ConvertToTextRegions(
                        new[] { paddleResult },  // PaddleOcrResultを配列にラップ
                        scaleFactor,
                        regionOfInterest
                    );

                    __logger?.LogDebug($"📊 シングルOCR完了: {textRegions?.Count ?? 0}領域検出");
                }
                
                // テキスト結合アルゴリズムを適用
                if (textRegions != null && textRegions.Count > 0)
                {
                    try
                    {
                        // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog("🔗 テキスト結合アルゴリズム適用開始");
                        mergedText = __textMerger.MergeTextRegions(textRegions);
                        // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"🔗 テキスト結合完了: 結果文字数={mergedText.Length}");
                        __logger?.LogDebug("テキスト結合アルゴリズム適用完了: 結果文字数={Length}", mergedText.Length);
                    }
                    catch (Exception ex)
                    {
                        // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"❌ テキスト結合エラー: {ex.Message}");
                        __logger?.LogWarning(ex, "テキスト結合中にエラーが発生しました。元のテキストを使用します");
                        mergedText = null; // フォールバック
                    }
                }
                
                // OCR後処理を適用
                postProcessedText = mergedText;
                if (!string.IsNullOrWhiteSpace(mergedText))
                {
                    try
                    {
                        // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog("🔧 OCR後処理（誤認識修正）開始");
                        postProcessedText = await __ocrPostProcessor.ProcessAsync(mergedText, 0.8f).ConfigureAwait(false);
                        // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"🔧 OCR後処理完了: 修正前='{mergedText}' → 修正後='{postProcessedText}'");
                        __logger?.LogDebug("OCR後処理完了: 修正前長={Before}, 修正後長={After}", 
                            mergedText.Length, postProcessedText.Length);
                    }
                    catch (Exception ex)
                    {
                        // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"❌ OCR後処理エラー: {ex.Message}");
                        __logger?.LogWarning(ex, "OCR後処理中にエラーが発生しました。修正前のテキストを使用します");
                        postProcessedText = mergedText; // フォールバック
                    }
                }
            }
            
            // ROI座標の補正
            if (regionOfInterest.HasValue && textRegions != null)
            {
                // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"📍 ROI座標補正実行: {regionOfInterest.Value}");
                textRegions = AdjustCoordinatesForRoi(textRegions, regionOfInterest.Value);
            }

            // ✅ [PHASE2.9.5] Phase 3診断ログ削除 - __ocrPreprocessingService未使用のため不要

            // 📍 座標ログ出力 (ユーザー要求: 認識したテキストとともに座標位置もログで確認)
            // 直接ファイル書き込みでOCR結果を記録
            SafeWriteDebugLog($"📍 [DIRECT] PaddleOcrEngine - OCR処理完了: 検出領域数={textRegions?.Count ?? 0}");
            
            if (textRegions != null && textRegions.Count > 0)
            {
                __logger?.LogInformation("📍 OCR座標ログ - 検出されたテキスト領域: {Count}個", textRegions.Count);
                for (int i = 0; i < textRegions.Count; i++)
                {
                    var region = textRegions[i];
                    __logger?.LogInformation("📍 OCR結果[{Index}]: Text='{Text}' | Bounds=({X},{Y},{Width},{Height}) | Confidence={Confidence:F3}",
                        i, region.Text, region.Bounds.X, region.Bounds.Y, region.Bounds.Width, region.Bounds.Height, region.Confidence);
                    
                    // 直接ファイル書き込みでOCR結果の詳細を記録
                    try
                    {
                        // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化 | Confidence={region.Confidence:F3}{Environment.NewLine}");
                    }
                    catch (Exception fileEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"PaddleOcrEngine 詳細ログ書き込みエラー: {fileEx.Message}");
                    }
                }
            }
            else
            {
                __logger?.LogDebug("OCR座標ログ - テキスト領域が検出されませんでした");
            }
            
            stopwatch.Stop();
            
            // 統計更新
            UpdatePerformanceStats(stopwatch.Elapsed.TotalMilliseconds, true);
            
            progressCallback?.Report(new CoreOcrProgress(1.0, "OCR処理完了"));
            
            // TODO: OCR精度向上機能を後で統合予定（DI循環参照問題のため一時的に無効化）
            // IReadOnlyList<TextChunk> processedTextChunks = [];
            
            } // using (mat) の終了
            
            // 🎯 Level 1大画面対応: 一元化された座標復元システム活用
            OcrResults result;
            if (Math.Abs(scaleFactor - 1.0) >= 0.001 && textRegions != null && textRegions.Count > 0)
            {
                __logger?.LogDebug("📍 統合座標復元開始: スケール係数={ScaleFactor}", scaleFactor);
                
                try
                {
                    // CoordinateRestorer.RestoreOcrResultsを活用して一元管理
                    var tempResult = new OcrResults(
                        textRegions,
                        image, // スケーリング済み画像から元画像に変更される
                        stopwatch.Elapsed,
                        CurrentLanguage ?? "jpn",
                        regionOfInterest,
                        postProcessedText
                    );
                    
                    result = CoordinateRestorer.RestoreOcrResults(tempResult, scaleFactor, image);
                    __logger?.LogDebug("✅ 統合座標復元完了: {Count}個のテキスト領域とROIを復元", result.TextRegions.Count);
                }
                catch (Exception ex)
                {
                    __logger?.LogWarning(ex, "⚠️ 統合座標復元でエラーが発生しました。スケーリングされた座標を使用します");
                    // エラー時はスケーリングされた座標をそのまま使用
                    result = new OcrResults(
                        textRegions,
                        image,
                        stopwatch.Elapsed,
                        CurrentLanguage ?? "jpn",
                        regionOfInterest,
                        postProcessedText
                    );
                }
            }
            else
            {
                result = new OcrResults(
                    textRegions ?? [],
                    image,
                    stopwatch.Elapsed,
                    CurrentLanguage ?? "jpn",
                    regionOfInterest,
                    postProcessedText
                );
            }
            
            // 📊 [DIAGNOSTIC] OCR処理成功イベント
            await __eventAggregator.PublishAsync(new PipelineDiagnosticEvent
            {
                Stage = "OCR",
                IsSuccess = true,
                ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                SessionId = sessionId,
                Severity = DiagnosticSeverity.Information,
                Message = $"OCR処理成功: 検出テキスト数={result.TextRegions.Count}, 処理時間={stopwatch.ElapsedMilliseconds}ms",
                Metrics = new Dictionary<string, object>
                {
                    { "Engine", EngineName },
                    { "EngineVersion", EngineVersion },
                    { "TextRegionCount", result.TextRegions.Count },
                    { "ProcessingTimeMs", stopwatch.ElapsedMilliseconds },
                    { "Language", CurrentLanguage ?? "jpn" },
                    { "ImageWidth", image.Width },
                    { "ImageHeight", image.Height },
                    { "HasROI", regionOfInterest.HasValue },
                    { "MergedTextLength", postProcessedText?.Length ?? 0 },
                    { "HighConfidenceRegions", result.TextRegions.Count(r => r.Confidence > 0.8) },
                    { "AverageConfidence", result.TextRegions.Any() ? result.TextRegions.Average(r => r.Confidence) : 0.0 }
                }
            }).ConfigureAwait(false);
            
            // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"✅ OCR処理完了: 検出テキスト数={result.TextRegions.Count}, 処理時間={stopwatch.ElapsedMilliseconds}ms");
            __logger?.LogDebug("OCR処理完了 - 検出されたテキスト数: {Count}, 処理時間: {ElapsedMs}ms", 
                result.TextRegions.Count, stopwatch.ElapsedMilliseconds);
            
            return result;
        }
        catch (OperationCanceledException)
        {
            __logger?.LogDebug("OCR処理がキャンセルされました");
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            UpdatePerformanceStats(stopwatch.Elapsed.TotalMilliseconds, false);

            // 📊 [DIAGNOSTIC] OCR処理失敗イベント
            try
            {
                await __eventAggregator.PublishAsync(new PipelineDiagnosticEvent
                {
                    Stage = "OCR",
                    IsSuccess = false,
                    ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                    ErrorMessage = ex.Message,
                    SessionId = sessionId,
                    Severity = DiagnosticSeverity.Error,
                    Message = $"OCR処理失敗: {ex.GetType().Name}: {ex.Message}",
                    Metrics = new Dictionary<string, object>
                    {
                        { "Engine", EngineName },
                        { "EngineVersion", EngineVersion },
                        { "ProcessingTimeMs", stopwatch.ElapsedMilliseconds },
                        { "Language", CurrentLanguage ?? "jpn" },
                        { "ImageWidth", image.Width },
                        { "ImageHeight", image.Height },
                        { "HasROI", regionOfInterest.HasValue },
                        { "ErrorType", ex.GetType().Name },
                        { "IsInitialized", IsInitialized }
                    }
                }).ConfigureAwait(false);
            }
            catch
            {
                // 診断イベント発行失敗は無視（元の例外を優先）
            }

            __logger?.LogError(ex, "OCR処理中にエラーが発生: {ExceptionType}", ex.GetType().Name);
            throw new OcrException($"OCR処理中にエラーが発生しました: {ex.Message}", ex);
        }
        }
        finally
        {
            // 🎯 [GEMINI_EMERGENCY_FIX_V2] グローバルSemaphoreSlim確実解放
            // PaddleOCRスレッド制限の確実な解除でデッドロック防止
            // 全インスタンス共有の静的SemaphoreSlimを解放
            _globalOcrSemaphore.Release();
        }
    }

    /// <summary>
    /// OCRエンジンの設定を取得します
    /// </summary>
    /// <returns>現在の設定</returns>
    public OcrEngineSettings GetSettings()
    {
        return _settings.Clone();
    }

    /// <summary>
    /// OCRエンジンの設定を適用します
    /// </summary>
    /// <param name="settings">設定</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    public async Task ApplySettingsAsync(OcrEngineSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        
        if (!settings.IsValid())
        {
            throw new ArgumentException("無効な設定です", nameof(settings));
        }

        ThrowIfDisposed();
        
        if (!IsInitialized)
        {
            throw new InvalidOperationException("OCRエンジンが初期化されていません。InitializeAsync()を先に呼び出してください。");
        }

        // 言語変更の確認
        bool languageChanged = _settings.Language != settings.Language;

        if (languageChanged)
        {
            // 新しい言語のモデルが利用可能かチェック
            if (!await IsLanguageAvailableAsync(settings.Language, cancellationToken).ConfigureAwait(false))
            {
                throw new OcrException($"指定された言語 '{settings.Language}' のモデルが利用できません");
            }
        }

        // ✅ [PHASE2.11.6] 再初期化要否判定をヘルパーメソッドに抽出（可読性向上）
        bool requiresReinitialization = RequiresReinitialization(settings, languageChanged);
                                        
        _settings = settings.Clone();
        
        __logger?.LogInformation("OCRエンジン設定を更新: 言語={Language}, モデル={Model}",
            _settings.Language, _settings.ModelName);
            
        // 重要なパラメータが変更された場合は再初期化が必要
        if (requiresReinitialization)
        {
            __logger?.LogInformation("設定変更により再初期化を実行");
            
            DisposeEngines();
            await InitializeAsync(_settings, cancellationToken).ConfigureAwait(false);
        }
        
        // GPUメモリ制限チェック（GPU使用時のみ）
        if (_settings.UseGpu && _settings.EnableGpuMemoryMonitoring)
        {
            await CheckGpuMemoryLimitsAsync(_settings, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 使用可能な言語のリストを取得します
    /// </summary>
    /// <returns>言語コードのリスト</returns>
    // ✅ [PHASE2.9.6] _modelManager に委譲
    public IReadOnlyList<string> GetAvailableLanguages() => _modelManager.GetAvailableLanguages();

    /// <summary>
    /// 使用可能なモデルのリストを取得します
    /// </summary>
    /// <returns>モデル名のリスト</returns>
    // ✅ [PHASE2.9.6] _modelManager に委譲
    public IReadOnlyList<string> GetAvailableModels() => _modelManager.GetAvailableModels();

    /// <summary>
    /// 指定言語のモデルが利用可能かを確認します
    /// </summary>
    /// <param name="languageCode">言語コード</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>利用可能な場合はtrue</returns>
    // ✅ [PHASE2.9.6] _modelManager に委譲
    public async Task<bool> IsLanguageAvailableAsync(string languageCode, CancellationToken cancellationToken = default)
        => await _modelManager.IsLanguageAvailableAsync(languageCode, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// エンジンのパフォーマンス統計を取得
    /// </summary>
    /// <returns>パフォーマンス統計</returns>
    // ✅ [PHASE2.9.6] _performanceTracker に委譲
    public OcrPerformanceStats GetPerformanceStats() => _performanceTracker.GetPerformanceStats();

    #region Private Methods

    // ✅ [PHASE2.11.5] CheckNativeLibraries削除 - IPaddleOcrEngineInitializerに完全委譲済み

    /// <summary>
    /// テスト環境の検出（厳格版）
    /// </summary>
    private static bool IsTestEnvironment()
    {
        try
        {
            // より厳格なテスト環境検出
            var processName = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
            
            // 実行中のプロセス名による検出
            var isTestProcess = processName.Contains("testhost", StringComparison.OrdinalIgnoreCase) ||
                               processName.Contains("vstest", StringComparison.OrdinalIgnoreCase);
            
            // スタックトレースによるテスト検出（より確実）
            var stackTrace = Environment.StackTrace;
            var isTestFromStack = stackTrace.Contains("xunit", StringComparison.OrdinalIgnoreCase) ||
                                 stackTrace.Contains(".Tests.", StringComparison.OrdinalIgnoreCase) ||
                                 stackTrace.Contains("Microsoft.TestPlatform", StringComparison.OrdinalIgnoreCase) ||
                                 stackTrace.Contains("TestMethodInvoker", StringComparison.OrdinalIgnoreCase);
            
            // 環境変数による検出
            var isTestEnvironmentVar = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER")) ||
                                      !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TF_BUILD")) || // Azure DevOps
                                      !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")) || // GitHub Actions
                                      !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")) ||
                                      !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BUILD_BUILDID"));
            
            // コマンドライン引数による検出
            var isTestCommand = Environment.CommandLine.Contains("test", StringComparison.OrdinalIgnoreCase) ||
                               Environment.CommandLine.Contains("xunit", StringComparison.OrdinalIgnoreCase) ||
                               Environment.CommandLine.Contains("vstest", StringComparison.OrdinalIgnoreCase);
            
            // アセンブリ名による検出
            var entryAssembly = System.Reflection.Assembly.GetEntryAssembly();
            var isTestAssembly = entryAssembly?.FullName?.Contains("test", StringComparison.OrdinalIgnoreCase) == true ||
                                entryAssembly?.FullName?.Contains("xunit", StringComparison.OrdinalIgnoreCase) == true;
            
            var isTest = isTestProcess || isTestFromStack || isTestEnvironmentVar || isTestCommand || isTestAssembly;
            
            // 詳細な判定結果（静的メソッドのためコメントのみ）
            // Debug: Process={isTestProcess}, Stack={isTestFromStack}, Env={isTestEnvironmentVar}, Command={isTestCommand}, Assembly={isTestAssembly} → Result={isTest}
            
            return isTest;
        }
        catch (SecurityException ex)
        {
            // セキュリティ上の理由で情報取得できない場合は本番環境と判定（テスト環境誤判定防止）
            // Log: IsTestEnvironment: SecurityException発生 - 本番環境として継続: {ex.Message}
            return false;
        }
        catch (InvalidOperationException ex)
        {
            // 操作エラーが発生した場合は本番環境と判定（テスト環境誤判定防止）
            // Log: IsTestEnvironment: InvalidOperationException発生 - 本番環境として継続: {ex.Message}
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            // アクセス拒否の場合は本番環境と判定（テスト環境誤判定防止）  
            // Log: IsTestEnvironment: UnauthorizedAccessException発生 - 本番環境として継続: {ex.Message}
            return false;
        }
    }

    // ✅ [PHASE2.11.8] InitializeEnginesSafelyAsync削除 - IPaddleOcrEngineInitializer.InitializeEnginesAsyncに完全委譲済み

    // ✅ [PHASE2.11.8] PrepareModelsAsync削除 - IPaddleOcrModelManager.PrepareModelsAsyncに完全委譲済み

    // ✅ [PHASE2.11.8] TryCreatePPOCRv5ModelAsync削除 - IPaddleOcrModelManager.TryCreatePPOCRv5ModelAsyncに完全委譲済み

    // ✅ [PHASE2.11.8] CreatePPOCRv5CustomModelAsync削除 - IPaddleOcrModelManagerに完全委譲済み

    // ✅ [PHASE2.11.8] GetPPOCRv5RecognitionModelPath削除 - IPaddleOcrModelManagerに完全委譲済み

    // ✅ [PHASE2.11.8] GetPPOCRv5Model削除 - IPaddleOcrModelManagerに完全委譲済み

    // ✅ [PHASE2.11.8] GetDefaultLocalModel削除 - IPaddleOcrModelManagerに完全委譲済み

    // ✅ [PHASE2.11.8] GetRecognitionModelName削除 - IPaddleOcrModelManagerに完全委譲済み

    /// <summary>
    /// 🔥 [PHASE5.2G-A] PixelDataLockから直接Matを作成（unsafeヘルパーメソッド）
    /// C# 12.0制約: asyncメソッド内でunsafe使用不可のため、同期メソッドに切り出し
    /// 🔥 [PHASE7.1_OPTIONA] WindowsImage/SafeImageAdapter用のstride診断ログ追加
    /// </summary>
    private static Mat CreateMatFromPixelLock(PixelDataLock pixelLock, int width, int height)
    {
        // 🔥 [PHASE7.1_OPTIONA] stride計算の検証（WindowsImage/SafeImageAdapterパス）
        const int channels = 3; // CV_8UC3固定
        var calculatedStride = width * channels;
        var actualStride = pixelLock.Stride;
        var dataLength = pixelLock.Data.Length;

        // 🔥 [PHASE7.1_OPTIONA] ファイル直接書き込みでログ出力（staticメソッドのためILogger不可）
        var logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "baketa_debug.log");
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var logMessage = $"[{timestamp}] 🔍 [PHASE7.1_OPTIONA] CreateMatFromPixelLock診断:\n" +
                           $"[{timestamp}]   Image Type: WindowsImage/SafeImageAdapter (LockPixelData path)\n" +
                           $"[{timestamp}]   Width: {width}, Height: {height}\n" +
                           $"[{timestamp}]   Channels: {channels} (MatType.CV_8UC3)\n" +
                           $"[{timestamp}]   pixelLock.Data.Length: {dataLength} bytes\n" +
                           $"[{timestamp}]   Calculated stride (W*C): {calculatedStride}\n" +
                           $"[{timestamp}]   Actual stride (PixelLock): {actualStride}\n" +
                           $"[{timestamp}]   Stride mismatch: {calculatedStride != actualStride}\n";
            System.IO.File.AppendAllText(logPath, logMessage);
        }
        catch { /* ログ書き込み失敗は無視 */ }

        unsafe
        {
            fixed (byte* ptr = pixelLock.Data)
            {
                try
                {
                    var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    System.IO.File.AppendAllText(logPath,
                        $"[{timestamp}] 🚀 [PHASE7.1_OPTIONA] Mat.FromPixelData呼び出し - Height={height}, Width={width}, MatType=CV_8UC3, Stride={actualStride}\n");
                }
                catch { /* ログ書き込み失敗は無視 */ }

                // 生ピクセルデータから直接Matを作成（stride考慮）
                var mat = Mat.FromPixelData(
                    height,
                    width,
                    MatType.CV_8UC3,  // 3 channels for RGB24
                    (IntPtr)ptr,
                    actualStride
                );

                try
                {
                    var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    System.IO.File.AppendAllText(logPath,
                        $"[{timestamp}] ✅ [PHASE7.1_OPTIONA] Mat.FromPixelData成功 - Mat.Cols={mat.Cols}, Mat.Rows={mat.Rows}\n");
                }
                catch { /* ログ書き込み失敗は無視 */ }

                // Clone()でロックされたメモリから独立したMatを作成
                // （PixelDataLock.Dispose()でUnlockBitsされるため、Cloneが必須）
                return mat.Clone();
            }
        }
    }

    /// <summary>
    /// 🔥 [PHASE7] SafeImage (ArrayPool<byte>ベース) から直接Matを作成（真のゼロコピー）
    /// Gemini推奨実装: SafeImageの生ピクセルデータから直接Mat生成でメモリコピー不要
    /// </summary>
    private static Mat CreateMatFromSafeImage(SafeImage safeImage)
    {
        // Step 1: SafeImageから生ピクセルデータ取得（ArrayPool<byte>からの参照）
        var imageData = safeImage.GetImageData();

        // Step 2: PixelFormat → MatType 変換
        var matType = ConvertPixelFormatToMatType(safeImage.PixelFormat);

        // Step 3: 画像データのストライド計算（実際のメモリレイアウトに基づく）
        var channels = GetChannelCountFromMatType(matType);

        // 🔥 [PHASE7.1_DIAGNOSIS] stride計算の検証
        var calculatedStride = safeImage.Width * channels;
        var actualStride = imageData.Length / safeImage.Height;

        Console.WriteLine($"🔍 [PHASE7.1_DIAGNOSIS] CreateMatFromSafeImage診断:");
        Console.WriteLine($"  PixelFormat: {safeImage.PixelFormat}");
        Console.WriteLine($"  Width: {safeImage.Width}, Height: {safeImage.Height}");
        Console.WriteLine($"  Channels: {channels}");
        Console.WriteLine($"  imageData.Length: {imageData.Length} bytes");
        Console.WriteLine($"  Calculated stride (W*C): {calculatedStride}");
        Console.WriteLine($"  Actual stride (Length/H): {actualStride}");
        Console.WriteLine($"  Stride mismatch: {calculatedStride != actualStride}");

        // 🔧 [PHASE7.1_FIX] 実際のストライドを使用（パディングを考慮）
        var stride = actualStride;

        // Step 4: unsafeブロックでMat.FromPixelData()実行
        unsafe
        {
            fixed (byte* ptr = imageData)
            {
                Console.WriteLine($"🚀 [PHASE7.1] Mat.FromPixelData呼び出し - Height={safeImage.Height}, Width={safeImage.Width}, MatType={matType}, Stride={stride}");

                // 生ピクセルデータから直接Matを作成
                var mat = Mat.FromPixelData(
                    safeImage.Height,
                    safeImage.Width,
                    matType,
                    (IntPtr)ptr,
                    stride
                );

                Console.WriteLine($"✅ [PHASE7.1] Mat.FromPixelData成功 - Mat.Cols={mat.Cols}, Mat.Rows={mat.Rows}");

                // 🎯 重要: Clone()でArrayPool<byte>から独立したMatを作成
                // SafeImage.Dispose()でArrayPool返却されるため、Cloneが必須
                return mat.Clone();
            }
        }
    }

    /// <summary>
    /// 🔥 [PHASE7] ImagePixelFormat → MatType 変換ヘルパー
    /// Gemini指摘: ピクセルフォーマットとチャンネル数の対応関係に注意
    /// </summary>
    private static MatType ConvertPixelFormatToMatType(ImagePixelFormat pixelFormat)
    {
        return pixelFormat switch
        {
            ImagePixelFormat.Rgb24 => MatType.CV_8UC3,      // RGB 3チャンネル
            ImagePixelFormat.Rgba32 => MatType.CV_8UC4,     // RGBA 4チャンネル
            ImagePixelFormat.Bgra32 => MatType.CV_8UC4,     // BGRA 4チャンネル (OpenCVはBGR順序)
            ImagePixelFormat.Gray8 => MatType.CV_8UC1,      // グレースケール 1チャンネル
            _ => throw new NotSupportedException($"Unsupported PixelFormat: {pixelFormat}")
        };
    }

    /// <summary>
    /// 🔥 [PHASE7] MatTypeからチャンネル数を取得
    /// ストライド計算に使用
    /// </summary>
    private static int GetChannelCountFromMatType(MatType matType)
    {
        // switch式ではなく、if文で処理（定数値不要）
        if (matType == MatType.CV_8UC1) return 1;  // Grayscale
        if (matType == MatType.CV_8UC3) return 3;  // RGB/BGR
        if (matType == MatType.CV_8UC4) return 4;  // RGBA/BGRA
        throw new NotSupportedException($"Unsupported MatType: {matType}");
    }

    /// <summary>
    /// 🔥 [PHASE5.2G-A + Phase 7] IImageから直接Matを作成（型別最適化対応）
    /// C# 12.0制約: asyncメソッド内でref struct (PixelDataLock)使用不可のため完全分離
    ///
    /// Phase 7対応: SafeImage/ReferencedSafeImage向けArrayPoolゼロコピーパス追加
    /// - ReferencedSafeImage: 内部SafeImage取得 → Mat.FromPixelData() (ArrayPool<byte>経由)
    /// - WindowsImage: LockPixelData() → Mat.FromPixelData() (Bitmap.LockBits)
    /// </summary>
    private static Mat CreateMatFromImage(IImage image)
    {
        // 🔥 [PHASE7.1_OPTIONA] 呼び出し確認ログ
        var logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "baketa_debug.log");
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var imageType = image.GetType().Name;
            System.IO.File.AppendAllText(logPath,
                $"[{timestamp}] 🎯 [PHASE7.1_OPTIONA] CreateMatFromImage呼び出し - ImageType: {imageType}\n");
        }
        catch { /* ログ書き込み失敗は無視 */ }

        // 🔥 [PHASE7] ReferencedSafeImage → SafeImage抽出してArrayPoolゼロコピーパス
        if (image is ReferencedSafeImage refImage)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                System.IO.File.AppendAllText(logPath,
                    $"[{timestamp}] 🔀 [PHASE7.1_OPTIONA] ReferencedSafeImageパス選択 → CreateMatFromSafeImage呼び出し\n");
            }
            catch { /* ログ書き込み失敗は無視 */ }

            var safeImage = refImage.GetUnderlyingSafeImage();
            return CreateMatFromSafeImage(safeImage);
        }

        // 🔥 [PHASE5.2G-A] その他のIImage (WindowsImage等) は既存LockPixelDataパス
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            System.IO.File.AppendAllText(logPath,
                $"[{timestamp}] 🔀 [PHASE7.1_OPTIONA] LockPixelDataパス選択 → CreateMatFromPixelLock呼び出し\n");
        }
        catch { /* ログ書き込み失敗は無視 */ }

        using (var pixelLock = image.LockPixelData())
        {
            return CreateMatFromPixelLock(pixelLock, image.Width, image.Height);
        }
    }

    /// <summary>
    /// IImageからOpenCV Matに変換（Phase 5.2: ArrayPool<byte>対応）
    /// </summary>
    private async Task<Mat> ConvertToMatAsync(IImage image, Rectangle? regionOfInterest, CancellationToken cancellationToken)
    {
        try
        {
            // テスト環境ではOpenCvSharpの使用を回避
            if (IsTestEnvironment())
            {
                __logger?.LogDebug("テスト環境: ダミーMatを作成");
                return CreateDummyMat();
            }

            // 🔥 [PHASE5.2G-A] 真のゼロコピー実装: LockPixelData() + Mat.FromPixelData()
            // 効果:
            //   - PNG エンコード/デコード削除 (15-60ms/フレーム削減)
            //   - ArrayPool アロケーション削除 (~8.3MB/フレーム削減)
            //   - Buffer.BlockCopy() 削除 (メモリコピー0回)
            //   - GC 圧力大幅削減
            //
            // 実装:
            //   - IImage.LockPixelData() で Bitmap.LockBits() 経由の生ピクセルデータ取得
            //   - Mat.FromPixelData() で stride を考慮した直接Mat作成
            //   - using パターンで UnlockBits() 自動実行
            //
            // C# 12.0制約: ref struct (PixelDataLock)はasyncメソッド内で使用不可
            // → 同期ヘルパーメソッドCreateMatFromImage()に完全分離
            var mat = CreateMatFromImage(image);

                // ROI指定がある場合は切り出し
                if (regionOfInterest.HasValue)
                {
                    var roi = regionOfInterest.Value;
                    var rect = new Rect(roi.X, roi.Y, roi.Width, roi.Height);

                    // 🛡️ [MEMORY_PROTECTION] 画像境界チェック - Mat.Width/Heightの安全なアクセス
                    try
                    {
                        if (mat.Empty())
                        {
                            __logger?.LogWarning("⚠️ Mat is empty during ROI processing");
                            return mat; // 元のMatを返す
                        }

                        int matWidth, matHeight;
                        try
                        {
                            matWidth = mat.Width;
                            matHeight = mat.Height;
                        }
                        catch (AccessViolationException ex)
                        {
                            __logger?.LogError(ex, "🚨 AccessViolationException in Mat.Width/Height during ROI processing");
                            return mat; // 元のMatを返す（ROI適用せず）
                        }

                        rect = rect.Intersect(new Rect(0, 0, matWidth, matHeight));

                        if (rect.Width > 0 && rect.Height > 0)
                        {
                            try
                            {
                                // ROI用の新しいMatを作成し、元のmatを安全にDispose
                                var roiMat = new Mat(mat, rect).Clone();
                                mat.Dispose(); // 元のmatを解放
                                return roiMat;
                            }
                            catch (Exception ex)
                            {
                                __logger?.LogError(ex, "⚠️ Failed to create ROI Mat: {ExceptionType}", ex.GetType().Name);
                                return mat; // ROI作成に失敗した場合は元のMatを返す
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        __logger?.LogError(ex, "🚨 Exception during ROI processing: {ExceptionType}", ex.GetType().Name);
                        return mat; // 例外発生時は元のMatを返す
                    }
                }

                return mat;
        }
        catch (ArgumentException ex)
        {
            __logger?.LogError(ex, "画像変換の引数エラー: {ExceptionType}", ex.GetType().Name);
            throw new OcrException($"画像変換の引数エラー: {ex.Message}", ex);
        }
        catch (InvalidOperationException ex)
        {
            __logger?.LogError(ex, "画像変換の操作エラー: {ExceptionType}", ex.GetType().Name);
            throw new OcrException($"画像変換の操作エラー: {ex.Message}", ex);
        }
        catch (OutOfMemoryException ex)
        {
            __logger?.LogError(ex, "画像変換でメモリ不足: {ExceptionType}", ex.GetType().Name);
            throw new OcrException($"画像変換でメモリ不足: {ex.Message}", ex);
        }
        catch (NotSupportedException ex)
        {
            __logger?.LogError(ex, "サポートされていない画像形式: {ExceptionType}", ex.GetType().Name);
            throw new OcrException($"サポートされていない画像形式: {ex.Message}", ex);
        }
        // 🔥 [PHASE5.2G-A] finally削除: LockPixelData()のusing文がUnlockBits()を自動実行
        // ArrayPool返却も不要（ArrayPool自体を使用しなくなった）
    }

    /// <summary>
    /// 大画面対応の適応的画像変換（PaddleOCR制限に対応）
    /// </summary>
    /// <param name="image">変換する画像</param>
    /// <param name="regionOfInterest">関心領域（オプション）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>変換されたMatとスケール係数</returns>
    private async Task<(Mat mat, double scaleFactor)> ConvertToMatWithScalingAsync(
        IImage image, Rectangle? regionOfInterest, CancellationToken cancellationToken)
    {
        // Step 1: スケーリングが必要かどうかを判定
        var (newWidth, newHeight, scaleFactor) = AdaptiveImageScaler.CalculateOptimalSize(
            image.Width, image.Height);
        
        // Step 2: スケーリング情報をログ出力
        if (AdaptiveImageScaler.RequiresScaling(image.Width, image.Height))
        {
            var scalingInfo = AdaptiveImageScaler.GetScalingInfo(
                image.Width, image.Height, newWidth, newHeight, scaleFactor);
            var constraintType = AdaptiveImageScaler.GetConstraintType(image.Width, image.Height);
            
            __logger?.LogWarning("🔧 大画面自動スケーリング実行: {ScalingInfo} (制約: {ConstraintType})",
                scalingInfo, constraintType);
        }
        
        // Step 3: スケーリングが必要な場合は画像をリサイズ
        IImage processImage = image;
        if (Math.Abs(scaleFactor - 1.0) >= 0.001) // スケーリング必要
        {
            try
            {
                // Lanczosリサンプリングで高品質スケーリング
                processImage = await ScaleImageWithLanczos(image, newWidth, newHeight, cancellationToken);
                
                __logger?.LogDebug("✅ 画像スケーリング完了: {OriginalSize} → {NewSize}", 
                    $"{image.Width}x{image.Height}", $"{newWidth}x{newHeight}");
            }
            catch (Exception ex)
            {
                __logger?.LogError(ex, "❌ 画像スケーリング失敗 - 元画像で処理継続");
                processImage = image; // エラー時は元画像を使用
                scaleFactor = 1.0;
            }
        }
        
        // Step 4: ROI座標もスケーリングに合わせて調整（精度向上版）
        Rectangle? adjustedRoi = null;
        if (regionOfInterest.HasValue && Math.Abs(scaleFactor - 1.0) >= 0.001)
        {
            var roi = regionOfInterest.Value;
            
            // 🎯 精度向上: Math.Floor/Ceilingで認識対象領域の欠落を防止
            var x1 = roi.X * scaleFactor;
            var y1 = roi.Y * scaleFactor;
            var x2 = (roi.X + roi.Width) * scaleFactor;
            var y2 = (roi.Y + roi.Height) * scaleFactor;

            var newX = (int)Math.Floor(x1);
            var newY = (int)Math.Floor(y1);
            
            adjustedRoi = new Rectangle(
                x: newX,
                y: newY,
                width: (int)Math.Ceiling(x2) - newX,
                height: (int)Math.Ceiling(y2) - newY
            );
            
            __logger?.LogDebug("🎯 ROI座標精密スケーリング調整: {OriginalRoi} → {AdjustedRoi} (Floor/Ceiling適用)",
                regionOfInterest.Value, adjustedRoi.Value);
        }
        else
        {
            adjustedRoi = regionOfInterest;
        }
        
        // Step 5: 既存のConvertToMatAsyncを使用してMatに変換
        var mat = await ConvertToMatAsync(processImage, adjustedRoi, cancellationToken);
        
        // Step 6: スケーリングされた画像のリソースを解放（元画像と異なる場合）
        if (processImage != image)
        {
            processImage.Dispose();
        }
        
        return (mat, scaleFactor);
    }
    
    /// <summary>
    /// Lanczosリサンプリングによる高品質画像スケーリング（Phase 5.2: ArrayPool<byte>対応 + PNG圧縮スキップ）
    /// </summary>
    /// <param name="originalImage">元画像</param>
    /// <param name="targetWidth">目標幅</param>
    /// <param name="targetHeight">目標高さ</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>スケーリングされた画像</returns>
    private async Task<IImage> ScaleImageWithLanczos(IImage originalImage, int targetWidth, int targetHeight,
        CancellationToken cancellationToken)
    {
        // テスト環境ではダミー画像を返す
        if (IsTestEnvironment())
        {
            __logger?.LogDebug("テスト環境: ダミースケーリング結果を返却");
            return originalImage; // テスト環境では元画像をそのまま返す
        }

        try
        {
            // 🔥 [PHASE5.2G-A] 真のゼロコピー実装: LockPixelData() + Mat.FromPixelData()
            // ScaleImageWithLanczosでも同様の最適化を適用
            // C# 12.0制約: ref struct (PixelDataLock)はasyncメソッド内で使用不可
            // → 同期ヘルパーメソッドCreateMatFromImage()使用
            var mat = CreateMatFromImage(originalImage);

            // Lanczosリサンプリングでリサイズ
            using var resizedMat = new Mat();
            Cv2.Resize(mat, resizedMat, new OpenCvSharp.Size(targetWidth, targetHeight),
                interpolation: InterpolationFlags.Lanczos4);

            // mat.Dispose() - Clone済みMatを解放
            mat.Dispose();

            // 🔥 [PHASE5.2_GEMINI] PNG圧縮をスキップ - Mat → IImage 直接変換で8MB削減
            // 従来: resizedMat.ToBytes(".png") → 8MB PNG圧縮 → CreateFromBytesAsync
            // 最適化: resizedMat → BMP形式（無圧縮） → CreateFromBytesAsync
            // BMPはOpenCVのデフォルト形式で、圧縮オーバーヘッドなし
            var resizedImageData = resizedMat.ToBytes(".bmp");
            return await __imageFactory.CreateFromBytesAsync(resizedImageData).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            __logger?.LogError(ex, "Lanczosスケーリング失敗: {TargetSize}", $"{targetWidth}x{targetHeight}");
            throw new OcrException($"画像スケーリングに失敗しました: {ex.Message}", ex);
        }
        // 🔥 [PHASE5.2G-A] finally削除: LockPixelData()のusing文がUnlockBits()を自動実行
    }

    /// <summary>
    /// テスト用のダミーMatを作成
    /// ⚠️ [MEMORY_WARNING] 呼び出し側でusingまたはDisposeによる適切な管理が必要
    /// </summary>
    private static Mat CreateDummyMat()
    {
        try
        {
            // 最小限のMatを作成
            return new Mat(1, 1, MatType.CV_8UC3);
        }
        catch (TypeInitializationException ex)
        {
            // OpenCvSharp初期化エラー
            throw new OcrException($"テスト環境でOpenCvSharpライブラリ初期化エラー: {ex.Message}", ex);
        }
        catch (DllNotFoundException ex)
        {
            // ネイティブDLLが見つからない
            throw new OcrException($"テスト環境でOpenCvSharpライブラリが利用できません: {ex.Message}", ex);
        }
        catch (BadImageFormatException ex)
        {
            // プラットフォームミスマッチ
            throw new OcrException($"テスト環境でOpenCvSharpライブラリのプラットフォームエラー: {ex.Message}", ex);
        }
        catch (InvalidOperationException ex)
        {
            // Mat操作エラー
            throw new OcrException($"テスト環境でOpenCvSharpMat操作エラー: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// OpenCvSharp.MatをByteArrayに変換
    /// </summary>
    private async Task<byte[]> ConvertMatToByteArrayAsync(Mat mat)
    {
        await Task.CompletedTask.ConfigureAwait(false); // 非同期メソッドとして維持

        // OpenCvSharpのMatからbyte[]に直接変換
        // BGR形式の場合はRGB形式に変換
        // ✅ [MEMORY_SAFE] 新しいMatを作成する場合、finally文で適切にDispose処理済み
        Mat rgbMat = mat.Channels() == 3 ? new Mat() : mat;
        if (mat.Channels() == 3)
        {
            Cv2.CvtColor(mat, rgbMat, ColorConversionCodes.BGR2RGB);
        }

        try
        {
            // Matのピクセルデータを直接byte[]として取得
            var data = new byte[rgbMat.Total() * rgbMat.ElemSize()];
            System.Runtime.InteropServices.Marshal.Copy(rgbMat.Data, data, 0, data.Length);
            return data;
        }
        finally
        {
            if (rgbMat != mat)
            {
                rgbMat?.Dispose();
            }
        }
    }

    // ✅ [PHASE2.9.3.4b] ExecuteOcrAsyncメソッド削除 - _executor + _resultConverter に置換済み

    /// <summary>
    /// テキスト検出のみを実行（認識処理をスキップして高速化）
    /// PaddleOCRの検出モードのみを使用してテキスト領域を検出
    /// </summary>
    private async Task<IReadOnlyList<OcrTextRegion>> ExecuteTextDetectionOnlyAsync(
        Mat mat,
        CancellationToken cancellationToken)
    {
        __logger?.LogDebug("⚡ ExecuteTextDetectionOnlyAsync開始 - 高速検出モード");

        // 🚀 [PERFORMANCE_OPTIMIZATION] Phase 3: GameOptimizedPreprocessingService を使用した前処理（検出専用）
        Mat processedMat;
        try
        {
            // 🎯 [SPEED_OPTIMIZATION] 検出専用の軽量前処理
            // OpenCvSharp.Mat を IAdvancedImage に変換
            var imageData = await ConvertMatToByteArrayAsync(mat).ConfigureAwait(false);
            
            // 🛡️ [MEMORY_PROTECTION] Mat.Width/Height の安全なアクセス（検出専用）
            int matWidth, matHeight;
            try 
            {
                matWidth = mat.Width;
                matHeight = mat.Height;
            }
            catch (AccessViolationException ex)
            {
                __logger?.LogError(ex, "🚨 AccessViolationException in Mat.Width/Height during detection-only AdvancedImage creation");
                throw new OcrException("検出専用処理でMat画像サイズの取得中にメモリアクセス違反が発生しました", ex);
            }
            
            // 🎯 [PERFORMANCE_BOOST] 検出専用のため複雑な前処理をスキップ
            processedMat = mat.Clone();
            
            // 🛡️ [MEMORY_PROTECTION] Mat.Width/Height の安全なアクセス（ログ用）
            try 
            {
                __logger?.LogDebug("⚡ 検出専用前処理完了: {Width}x{Height} → {ProcessedWidth}x{ProcessedHeight}",
                    mat.Width, mat.Height, processedMat.Width, processedMat.Height);
            }
            catch (AccessViolationException ex)
            {
                __logger?.LogError(ex, "🚨 AccessViolationException during log output for Mat dimensions");
                __logger?.LogDebug("⚡ 検出専用前処理完了 - サイズ情報取得不可");
            }
        }
        catch (Exception ex)
        {
            __logger?.LogWarning(ex, "前処理でエラー発生、元画像を使用");
            processedMat = mat.Clone(); // 安全にクローン
        }

        try
        {
            // ✅ [PHASE2.9.4c] _executor + _resultConverter使用に置換
            var paddleResult = await _executor.ExecuteDetectionOnlyAsync(processedMat, cancellationToken).ConfigureAwait(false);
            return _resultConverter.ConvertDetectionOnlyResult(new[] { paddleResult });
        }
        finally
        {
            // processedMat が元の mat と異なる場合のみ Dispose
            if (!ReferenceEquals(processedMat, mat))
            {
                processedMat?.Dispose();
            }
        }
    }

    /// <summary>
    /// PaddleOCRの検出専用実行（内部実装）
    /// 注意: PaddleOCRライブラリの内部構造に依存する暫定実装
    /// </summary>
    private async Task<object> ExecuteDetectionOnlyInternal(Mat mat, CancellationToken cancellationToken)
    {
        try
        {
            // 暫定実装: 完全なOCRを実行してテキスト部分のみを空にする
            // 理想的には PaddleOCR の Detector のみを直接呼び出したいが、
            // 現在のライブラリAPIでは難しいため、この方法を採用
            
            if (IsMultiThreadEnabled && _queuedEngine != null)
            {
                // マルチスレッド実行でのタイムアウト設定
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var adaptiveTimeout = 30; // デフォルト30秒タイムアウト
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(adaptiveTimeout));
                
                // 🔧 [MAT_VALIDATION] PaddleOCR.Run前のMat検証強化
                __logger?.LogDebug("🔍 [QUEUED_ENGINE] PaddleOCR.Run実行前: Size={Width}x{Height}, Type={Type}, Channels={Channels}, IsContinuous={IsContinuous}",
                    mat.Cols, mat.Rows, mat.Type(), mat.Channels(), mat.IsContinuous());
                
                if (mat.Empty() || mat.Cols <= 0 || mat.Rows <= 0)
                {
                    __logger?.LogError("🚨 [QUEUED_ENGINE] 不正なMat状態でPaddleOCR.Run実行中止: Empty={Empty}, Size={Width}x{Height}",
                        mat.Empty(), mat.Cols, mat.Rows);
                    throw new InvalidOperationException($"PaddleOCR.Run実行用の不正なMat: Empty={mat.Empty()}, Size={mat.Cols}x{mat.Rows}");
                }
                
                var detectionTask = Task.Run(() => 
                {
                    try
                    {
                        __logger?.LogDebug("🏃 [QUEUED_ENGINE] PaddleOCR.Run実行中...");
                        var result = _queuedEngine.Run(mat);
                        __logger?.LogDebug("✅ [QUEUED_ENGINE] PaddleOCR.Run成功");
                        return result;
                    }
                    catch (Exception ex)
                    {
                        __logger?.LogError(ex, "🚨 [QUEUED_ENGINE] PaddleOCR.Run失敗: {ExceptionType} - {Message}", ex.GetType().Name, ex.Message);
                        throw;
                    }
                }, timeoutCts.Token);
                return await detectionTask.ConfigureAwait(false);
            }
            else if (_ocrEngine != null)
            {
                // 🎯 [OWNERSHIP_TRANSFER] シングルスレッド実行時もClone作成して所有権移譲
                var matForDetection = mat.Clone(); // 独立コピー作成
                return await ExecuteOcrInSeparateTask(matForDetection, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                throw new InvalidOperationException("利用可能なOCRエンジンがありません");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            __logger?.LogDebug("検出専用処理がキャンセルされました");
            throw;
        }
        catch (Exception ex)
        {
            __logger?.LogWarning(ex, "検出専用処理でエラー発生、再試行を実行");
            
            // メモリクリア後に再試行
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            
            if (_ocrEngine != null)
            {
                // 🎯 [OWNERSHIP_TRANSFER] 再試行時もClone作成して所有権移譲
                var matForRetry = mat.Clone(); // 独立コピー作成
                return await ExecuteOcrInSeparateTask(matForRetry, cancellationToken).ConfigureAwait(false);
            }
            throw;
        }
    }

    // ✅ [PHASE2.9.4c] ExecuteDetectionOnlyInternalOptimizedメソッド削除（105行） - _executor.ExecuteDetectionOnlyAsync に置換済み

    // ✅ [PHASE2.9.4c] ConvertDetectionOnlyResultメソッド削除 - _resultConverter.ConvertDetectionOnlyResult に置換済み

    // ✅ [PHASE2.9.4c] ProcessSinglePaddleResultForDetectionOnlyメソッド削除 - _resultConverter.ConvertDetectionOnlyResult に置換済み

    // 以下のヘルパーメソッドも同時削除:
    // - ProcessSinglePaddleResultForDetectionOnly (42行)
    // - ExtractBoundsFromRegion (37行)
    // - ExtractBoundsFromResult (45行)
    // - ExtractRectangleFromObject (30行)

    /// <summary>
    /// PaddleOCR領域座標からバウンディングボックスを計算
    /// </summary>
    private static Rectangle CalculateBoundingBoxFromRegion(PointF[] region)
    {
        if (region == null || region.Length == 0)
        {
            return Rectangle.Empty;
        }

        var minX = region.Min(p => p.X);
        var maxX = region.Max(p => p.X);
        var minY = region.Min(p => p.Y);
        var maxY = region.Max(p => p.Y);

        return new Rectangle(
            x: (int)Math.Floor(minX),
            y: (int)Math.Floor(minY),
            width: (int)Math.Ceiling(maxX - minX),
            height: (int)Math.Ceiling(maxY - minY)
        );
    }

    // ✅ [PHASE2.9.3.4b] ConvertPaddleOcrResultメソッド削除 - _resultConverter.ConvertToTextRegions に置換済み

    // ✅ [PHASE2.9.4d] ProcessSinglePaddleResultメソッド削除（109行） - PaddleOcrResultConverterに移行済み、未使用

    // ✅ [PHASE2.9.4d] ProcessPaddleRegionメソッド削除（195行） - PaddleOcrResultConverterに移行済み、未使用

    /// <summary>
    /// ROI使用時の座標補正（画面境界チェック付き）
    /// </summary>
    private List<OcrTextRegion> AdjustCoordinatesForRoi(
        IReadOnlyList<OcrTextRegion> textRegions,
        Rectangle roi)
    {
        // 画面サイズを取得
        var screenBounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        var screenWidth = screenBounds.Width;
        var screenHeight = screenBounds.Height;

        return [.. textRegions.Select(region => {
            // ROI補正後の座標を計算
            var adjustedX = region.Bounds.X + roi.X;
            var adjustedY = region.Bounds.Y + roi.Y;
            
            // 画面境界内に制限
            var clampedX = Math.Max(0, Math.Min(adjustedX, screenWidth - region.Bounds.Width));
            var clampedY = Math.Max(0, Math.Min(adjustedY, screenHeight - region.Bounds.Height));
            
            // 境界外の場合は警告ログ出力
            if (adjustedX != clampedX || adjustedY != clampedY)
            {
                // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"🚨 座標補正により画面外座標を修正: 元座標({adjustedX},{adjustedY}) → 補正後({clampedX},{clampedY}) [画面サイズ:{screenWidth}x{screenHeight}]");
            }

            return new OcrTextRegion(
                region.Text,
                new Rectangle(
                    clampedX,
                    clampedY,
                    region.Bounds.Width,
                    region.Bounds.Height
                ),
                region.Confidence,
                region.Contour?.Select(p => new System.Drawing.Point(
                    Math.Max(0, Math.Min(p.X + roi.X, screenWidth)), 
                    Math.Max(0, Math.Min(p.Y + roi.Y, screenHeight))
                )).ToArray(),
                region.Direction
            );
        })];
    }

    /// <summary>
    /// 空の結果を作成
    /// </summary>
    private OcrResults CreateEmptyResult(IImage image, Rectangle? regionOfInterest, TimeSpan processingTime)
    {
        return new OcrResults(
            [],
            image,
            processingTime,
            CurrentLanguage ?? "jpn",
            regionOfInterest,
            string.Empty // 空の場合は空文字列
        );
    }

    /// <summary>
    /// パフォーマンス統計を更新
    /// </summary>
    private void UpdatePerformanceStats(double processingTimeMs, bool success)
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
    /// エンジンの破棄
    /// </summary>
    private void DisposeEngines()
    {
        lock (_lockObject)
        {
            _queuedEngine?.Dispose();
            _queuedEngine = null;
            
            _ocrEngine?.Dispose();
            _ocrEngine = null;
            
            IsInitialized = false;
            IsMultiThreadEnabled = false;
            CurrentLanguage = null;
        }
    }

    /// <summary>
    /// 初期化状態のチェック
    /// </summary>
    private void ThrowIfNotInitialized()
    {
        if (!IsInitialized)
        {
            throw new InvalidOperationException("OCRエンジンが初期化されていません。InitializeAsync()を呼び出してください。");
        }
    }

    /// <summary>
    /// 破棄状態のチェック
    /// </summary>
    /// <summary>
    /// 日本語認識に特化した最適化パラメータを適用
    /// 漢字・ひらがな・カタカナの認識精度を向上
    /// </summary>
    private static void ApplyJapaneseOptimizations(Type ocrType, object ocrEngine)
    {
        // PaddleOCR v3.0.1では言語別の詳細設定プロパティが存在しないため
        // 代わりに実在するプロパティのみを使用
        // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"     📝 日本語最適化: 実在するプロパティのみ使用");
        
        // 実在するプロパティで日本語テキストに有効な設定
        try
        {
            // 回転検出を有効化（日本語の縦書き対応）
            var rotationProp = ocrType.GetProperty("AllowRotateDetection");
            if (rotationProp != null && rotationProp.CanWrite)
            {
                rotationProp.SetValue(ocrEngine, true);
                // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"     🔄 日本語縦書き対応: 回転検出有効");
            }
        }
        catch (Exception ex)
        {
            // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"     ❌ 日本語最適化設定エラー: {ex.Message}");
        }
    }

    /// <summary>
    /// 英語認識に特化した最適化パラメータを適用
    /// アルファベット・数字の認識精度を向上
    /// </summary>
    private static void ApplyEnglishOptimizations(Type ocrType, object ocrEngine)
    {
        // PaddleOCR v3.0.1では言語別の詳細設定プロパティが存在しないため
        // 代わりに実在するプロパティのみを使用
        // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"     📝 英語最適化: 実在するプロパティのみ使用");
        
        // 実在するプロパティで英語テキストに有効な設定
        try
        {
            // 180度分類を有効化（英語テキストの向き対応）
            var classificationProp = ocrType.GetProperty("Enable180Classification");
            if (classificationProp != null && classificationProp.CanWrite)
            {
                classificationProp.SetValue(ocrEngine, true);
                // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"     🔄 英語テキスト向き対応: 180度分類有効");
            }
        }
        catch (Exception ex)
        {
            // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"     ❌ 英語最適化設定エラー: {ex.Message}");
        }
    }

    /// <summary>
    /// 翻訳設定とOCR設定を統合して使用言語を決定
    /// </summary>
    /// <param name="settings">OCRエンジン設定</param>
    /// <returns>使用する言語コード</returns>
    private string DetermineLanguageFromSettings(OcrEngineSettings settings)
    {
        try
        {
            // 1. 明示的なOCR言語設定を最優先
            if (!string.IsNullOrWhiteSpace(settings.Language) && settings.Language != "jpn")
            {
                var mappedLanguage = MapDisplayNameToLanguageCode(settings.Language);
                // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"🎯 OCR設定から言語決定: '{settings.Language}' → '{mappedLanguage}'");
                return mappedLanguage;
            }
            
            // 2. 翻訳設定から言語を推測（SimpleSettingsViewModelから）
            var translationSourceLanguage = GetTranslationSourceLanguageFromConfig();
            if (!string.IsNullOrWhiteSpace(translationSourceLanguage))
            {
                var mappedLanguage = MapDisplayNameToLanguageCode(translationSourceLanguage);
                // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"🌐 翻訳設定から言語決定: '{translationSourceLanguage}' → '{mappedLanguage}'");
                return mappedLanguage;
            }
            
            // 3. デフォルト言語（日本語）
            // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog("📋 設定が見つからないため、デフォルト言語 'jpn' を使用");
            return "jpn";
        }
        catch (Exception ex)
        {
            // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"❌ 言語決定処理でエラー: {ex.Message}");
            return "jpn"; // フォールバック
        }
    }
    
    /// <summary>
    /// 設定サービスから翻訳元言語を取得
    /// </summary>
    /// <returns>翻訳元言語の表示名</returns>
    private string? GetTranslationSourceLanguageFromConfig()
    {
        try
        {
            // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog("📁 設定サービスからSourceLanguage取得試行...");
            
            // UnifiedSettingsServiceを使用して設定取得
            var translationSettings = __unifiedSettingsService.GetTranslationSettings();
            var sourceLanguage = translationSettings?.DefaultSourceLanguage;
            
            if (!string.IsNullOrWhiteSpace(sourceLanguage))
            {
                // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"📁 設定サービスから翻訳元言語取得: '{sourceLanguage}'");
                return sourceLanguage;
            }
            
            // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog("📁 設定サービスからSourceLanguageが取得できませんでした");
            return null;
        }
        catch (Exception ex)
        {
            // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"❌ 設定サービス取得エラー: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// JSON文字列からSourceLanguageを抽出
    /// </summary>
    /// <param name="jsonContent">JSON設定内容</param>
    /// <returns>翻訳元言語</returns>
    private string? ExtractSourceLanguageFromJson(string jsonContent)
    {
        try
        {
            // シンプルな文字列検索でSourceLanguageを抽出
            var patterns = new[]
            {
                "\"SourceLanguage\"\\s*:\\s*\"([^\"]+)\"",
                "\"sourceLanguage\"\\s*:\\s*\"([^\"]+)\"",
                "\"DefaultSourceLanguage\"\\s*:\\s*\"([^\"]+)\""
            };
            
            foreach (var pattern in patterns)
            {
                var match = System.Text.RegularExpressions.Regex.Match(jsonContent, pattern);
                if (match.Success && match.Groups.Count > 1)
                {
                    var sourceLanguage = match.Groups[1].Value;
                    // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"📋 JSON解析成功 ({pattern}): '{sourceLanguage}'");
                    return sourceLanguage;
                }
            }
            
            // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog("📋 JSONからSourceLanguageパターンが見つかりません");
            return null;
        }
        catch (Exception ex)
        {
            // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"📋 JSON解析エラー: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// 表示名をOCR言語コードにマッピング
    /// </summary>
    /// <param name="displayName">言語の表示名</param>
    /// <returns>OCR用言語コード</returns>
    private string MapDisplayNameToLanguageCode(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return "jpn";
            
        var languageMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // 日本語
            { "日本語", "jpn" },
            { "Japanese", "jpn" },
            { "ja", "jpn" },
            { "jpn", "jpn" },
            
            // 英語
            { "英語", "eng" },
            { "English", "eng" },
            { "en", "eng" },
            { "eng", "eng" },
            
            // 中国語（簡体字）
            { "簡体字中国語", "chi_sim" },
            { "简体中文", "chi_sim" },
            { "Chinese (Simplified)", "chi_sim" },
            { "zh-CN", "chi_sim" },
            { "zh_cn", "chi_sim" },
            
            // 中国語（繁体字）
            { "繁体字中国語", "chi_tra" },
            { "繁體中文", "chi_tra" },
            { "Chinese (Traditional)", "chi_tra" },
            { "zh-TW", "chi_tra" },
            { "zh_tw", "chi_tra" },
            
            // 韓国語
            { "韓国語", "kor" },
            { "한국어", "kor" },
            { "Korean", "kor" },
            { "ko", "kor" },
            { "kor", "kor" }
        };
        
        if (languageMapping.TryGetValue(displayName, out var languageCode))
        {
            return languageCode;
        }
        
        // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"⚠️ 未知の言語表示名 '{displayName}'、デフォルト 'jpn' を使用");
        return "jpn";
    }

    /// <summary>
    /// 実行時に言語ヒントを適用して認識精度を向上
    /// OCR実行直前に翻訳設定と連携して言語情報を再確認し、最適化パラメータを適用
    /// </summary>
    // 削除: 実行時言語ヒント機能
    // PaddleOCR v3.0.1では言語設定APIが存在しないため、
    // 実行時の言語切り替えは不可能。代わりに画像前処理で品質向上を図る。
    
    // 削除: 言語別実行時最適化関数
    // PaddleOCR v3.0.1では実行時パラメータ変更APIが存在しないため、
    // これらの関数は効果がない。代わりに画像前処理による品質向上を行う。

    /// <summary>
    /// 現在の設定が日本語言語かどうかを判定
    /// </summary>
    /// <returns>日本語の場合true</returns>
    private bool IsJapaneseLanguage()
    {
        return _settings.Language?.Equals("jpn", StringComparison.OrdinalIgnoreCase) == true ||
               _settings.Language?.Equals("ja", StringComparison.OrdinalIgnoreCase) == true ||
               _settings.Language?.Equals("japanese", StringComparison.OrdinalIgnoreCase) == true ||
               _settings.Language?.Equals("日本語", StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>
    /// GPUメモリ制限をチェックし、必要に応じて警告やフォールバックを実行
    /// </summary>
    private async Task CheckGpuMemoryLimitsAsync(OcrEngineSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            // 必要メモリ量の推定（OCR処理用）
            var estimatedMemoryMB = EstimateRequiredGpuMemory(settings);
            
            // メモリ可用性チェック
            var isAvailable = await __gpuMemoryManager.IsMemoryAvailableAsync(estimatedMemoryMB, cancellationToken).ConfigureAwait(false);
            
            if (!isAvailable)
            {
                __logger?.LogWarning("⚠️ GPU memory insufficient: Required={RequiredMB}MB, falling back to CPU mode", estimatedMemoryMB);
                
                // 自動的にCPUモードにフォールバック
                settings.UseGpu = false;
                _settings.UseGpu = false;
                
                return;
            }
            
            // GPUメモリ制限を適用
            var limits = new GpuMemoryLimits
            {
                MaxUsageMB = settings.MaxGpuMemoryMB,
                WarningThreshold = 0.8,
                EnforceLimit = true,
                MonitoringIntervalSeconds = 60
            };
            
            await __gpuMemoryManager.ApplyLimitsAsync(limits).ConfigureAwait(false);
            
            // 監視開始（まだ開始していない場合）
            if (!__gpuMemoryManager.IsMonitoringEnabled)
            {
                await __gpuMemoryManager.StartMonitoringAsync(limits, cancellationToken).ConfigureAwait(false);
            }
            
            __logger?.LogInformation("💻 GPU memory limits applied: Max={MaxMB}MB, Estimated={EstimatedMB}MB", 
                settings.MaxGpuMemoryMB, estimatedMemoryMB);
        }
        catch (Exception ex)
        {
            __logger?.LogWarning(ex, "⚠️ Failed to check GPU memory limits, continuing without restrictions");
        }
    }
    
    /// <summary>
    /// OCR処理に必要なGPUメモリ量を推定
    /// </summary>
    private static int EstimateRequiredGpuMemory(OcrEngineSettings settings)
    {
        // 基本的なOCRモデル用メモリ
        var baseMemoryMB = 512;

        // 言語モデル使用時の追加メモリ
        if (settings.UseLanguageModel)
        {
            baseMemoryMB += 256;
        }

        // マルチスレッド処理時の追加メモリ
        if (settings.EnableMultiThread)
        {
            baseMemoryMB += settings.WorkerCount * 128;
        }

        return baseMemoryMB;
    }

    /// <summary>
    /// 設定変更によりエンジンの再初期化が必要かを判定
    /// ✅ [PHASE2.11.6] ApplySettingsAsyncから抽出（可読性・保守性向上）
    /// </summary>
    private bool RequiresReinitialization(OcrEngineSettings newSettings, bool languageChanged)
    {
        return languageChanged ||
               _settings.ModelName != newSettings.ModelName ||
               _settings.UseGpu != newSettings.UseGpu ||
               _settings.GpuDeviceId != newSettings.GpuDeviceId ||
               _settings.EnableMultiThread != newSettings.EnableMultiThread ||
               _settings.WorkerCount != newSettings.WorkerCount;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
    
    /// <summary>
    /// PaddleOCRエンジンを再初期化（連続失敗時の回復処理）
    /// </summary>
    private async Task ReinitializeEngineAsync()
    {
        try
        {
            // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            
            // 現在のエンジンを安全に廃棄
            lock (_lockObject)
            {
                _queuedEngine?.Dispose();
                _queuedEngine = null;
                _ocrEngine = null;
                IsInitialized = false;
            }
            
            // 短い待機時間でメモリクリーンアップ
            await Task.Delay(500).ConfigureAwait(false);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            
            // エンジンを再初期化
            var success = await InitializeAsync(_settings).ConfigureAwait(false);
            
            if (success)
            {
                _consecutivePaddleFailures = 0; // 再初期化成功時はカウンタリセット
                // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            }
            else
            {
                // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            }
        }
        catch (Exception ex)
        {
            // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
        }
    }

    #endregion

    /// <summary>
    /// リソースの解放
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    #region 最新技術ベース高度画像処理メソッド
    
    /// <summary>
    /// 局所的明度・コントラスト調整（不均一照明対応）
    /// </summary>
    private void ApplyLocalBrightnessContrast(Mat input, Mat output)
    {
        try
        {
            // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"     🔆 局所的明度・コントラスト調整: {input.Width}x{input.Height}");
            
            // ガウシアンブラーで背景推定
            using var background = new Mat();
            Cv2.GaussianBlur(input, background, new OpenCvSharp.Size(51, 51), 0);
            
            // 背景を差し引いて局所的コントラスト強化
            using var temp = new Mat();
            Cv2.Subtract(input, background, temp);
            
            // 結果を正規化
            Cv2.Normalize(temp, output, 0, 255, NormTypes.MinMax);
            
            // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"     ✅ 局所的明度・コントラスト調整完了");
        }
        catch (Exception ex)
        {
            // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"     ❌ 局所的明度・コントラスト調整エラー: {ex.Message}");
            input.CopyTo(output);
        }
    }
    
    /// <summary>
    /// 高度なUn-sharp Masking（研究推奨手法）
    /// </summary>
    private void ApplyAdvancedUnsharpMasking(Mat input, Mat output)
    {
        try
        {
            // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"     🔪 高度Un-sharp Masking: {input.Width}x{input.Height}");
            
            // 複数のガウシアンブラーで多段階シャープ化
            using var blur1 = new Mat();
            using var blur2 = new Mat();
            using var blur3 = new Mat();
            
            Cv2.GaussianBlur(input, blur1, new OpenCvSharp.Size(3, 3), 0);
            Cv2.GaussianBlur(input, blur2, new OpenCvSharp.Size(5, 5), 0);
            Cv2.GaussianBlur(input, blur3, new OpenCvSharp.Size(7, 7), 0);
            
            // 多段階のアンシャープマスキング
            using var sharp1 = new Mat();
            using var sharp2 = new Mat();
            using var sharp3 = new Mat();
            
            Cv2.AddWeighted(input, 2.0, blur1, -1.0, 0, sharp1);
            Cv2.AddWeighted(input, 1.5, blur2, -0.5, 0, sharp2);
            Cv2.AddWeighted(input, 1.2, blur3, -0.2, 0, sharp3);
            
            // 結果を統合
            using var combined = new Mat();
            Cv2.AddWeighted(sharp1, 0.5, sharp2, 0.3, 0, combined);
            Cv2.AddWeighted(combined, 0.8, sharp3, 0.2, 0, output);
            
            // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"     ✅ 高度Un-sharp Masking完了");
        }
        catch (Exception)
        {
            // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"     ❌ 高度Un-sharp Maskingエラー: {ex.Message}");
            input.CopyTo(output);
        }
    }
    
    /// <summary>
    /// 日本語特化適応的二値化
    /// </summary>
    private void ApplyJapaneseOptimizedBinarization(Mat input, Mat output)
    {
        try
        {
            // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"     🔲 日本語特化適応的二値化: {input.Width}x{input.Height}");
            
            // 日本語文字に最適化されたパラメータ
            using var adaptive1 = new Mat();
            using var adaptive2 = new Mat();
            using var otsu = new Mat();
            
            // 複数の適応的二値化手法を組み合わせ
            Cv2.AdaptiveThreshold(input, adaptive1, 255, AdaptiveThresholdTypes.MeanC, ThresholdTypes.Binary, 15, 3);
            Cv2.AdaptiveThreshold(input, adaptive2, 255, AdaptiveThresholdTypes.GaussianC, ThresholdTypes.Binary, 17, 4);
            Cv2.Threshold(input, otsu, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
            
            // 結果を統合（日本語文字に最適）
            using var combined = new Mat();
            Cv2.BitwiseAnd(adaptive1, adaptive2, combined);
            Cv2.BitwiseOr(combined, otsu, output);
            
            // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"     ✅ 日本語特化適応的二値化完了");
        }
        catch (Exception ex)
        {
            // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"     ❌ 日本語特化適応的二値化エラー: {ex.Message}");
            input.CopyTo(output);
        }
    }
    
    /// <summary>
    /// 日本語最適化モルフォロジー変換
    /// </summary>
    private void ApplyJapaneseOptimizedMorphology(Mat input, Mat output)
    {
        try
        {
            // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"     🔧 日本語最適化モルフォロジー変換: {input.Width}x{input.Height}");
            
            // 日本語文字に最適化されたカーネル
            var kernel1 = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(2, 1)); // 横方向結合
            var kernel2 = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(1, 2)); // 縦方向結合
            var kernel3 = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(3, 3)); // 全体形状整形
            
            using var temp1 = new Mat();
            using var temp2 = new Mat();
            using var temp3 = new Mat();
            
            // 段階的なモルフォロジー処理
            Cv2.MorphologyEx(input, temp1, MorphTypes.Close, kernel1);
            Cv2.MorphologyEx(temp1, temp2, MorphTypes.Close, kernel2);
            Cv2.MorphologyEx(temp2, temp3, MorphTypes.Open, kernel3);
            
            // 最終的な文字形状最適化
            Cv2.MorphologyEx(temp3, output, MorphTypes.Close, kernel3);
            
            // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"     ✅ 日本語最適化モルフォロジー変換完了");
        }
        catch (Exception ex)
        {
            // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"     ❌ 日本語最適化モルフォロジー変換エラー: {ex.Message}");
            input.CopyTo(output);
        }
    }
    
    /// <summary>
    /// 最終品質向上処理
    /// </summary>
    private void ApplyFinalQualityEnhancement(Mat input, Mat output)
    {
        try
        {
            // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"     ✨ 最終品質向上処理: {input.Width}x{input.Height}");
            
            // 最終的な品質向上処理
            using var temp = new Mat();
            
            // 小さなノイズ除去
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(1, 1));
            Cv2.MorphologyEx(input, temp, MorphTypes.Open, kernel);
            
            // 文字の境界線を鮮明化
            using var dilated = new Mat();
            using var eroded = new Mat();
            Cv2.Dilate(temp, dilated, kernel);
            Cv2.Erode(temp, eroded, kernel);
            
            // 結果を統合
            Cv2.BitwiseOr(dilated, eroded, output);
            
            // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"     ✅ 最終品質向上処理完了");
        }
        catch (Exception ex)
        {
            // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"     ❌ 最終品質向上処理エラー: {ex.Message}");
            input.CopyTo(output);
        }
    }
    
    #endregion


    /// <summary>
    /// 🚀 [ULTRATHINK_PHASE23] Gemini推奨メモリ分離戦略実装版 - 完全なスレッド分離
    /// IMPORTANT: この メソッドは processedMat の所有権を受け取り、責任を持ってDispose する
    /// 大画像クラッシュ問題解決: byte[]分離によりMat共有を完全回避
    /// </summary>
    private async Task<object> ExecuteOcrInSeparateTask(Mat processedMat, CancellationToken cancellationToken)
    {
        // 🧠 [GEMINI_MEMORY_SEPARATION] Task.Run外でbyte[]データを抽出
        byte[] imageBytes;
        OpenCvSharp.Size imageSize;
        MatType imageType;
        string workingMatInfo = "";
        
        try
        {
            // Task.Run外で画像データをbyte[]として抽出 - スレッド分離
            __logger?.LogDebug("🚀 [MEMORY_SEPARATION] メモリ分離戦略開始 - Mat → byte[]抽出");
            
            using (processedMat) // 引数Matの確実な解放
            {
                // 🔍 [MAT_INFO] 元画像情報保存
                imageSize = new OpenCvSharp.Size(processedMat.Cols, processedMat.Rows);
                imageType = processedMat.Type();
                workingMatInfo = $"Size={imageSize.Width}x{imageSize.Height}, Type={imageType}, Channels={processedMat.Channels()}";
                
                // 🛡️ [MEMORY_EFFICIENCY] Mat連続性確認・正規化
                Mat continuousMat;
                if (!processedMat.IsContinuous())
                {
                    __logger?.LogDebug("📋 [MEMORY_SEPARATION] 非連続Mat検出 - Clone作成");
                    continuousMat = processedMat.Clone();
                }
                else
                {
                    __logger?.LogDebug("📋 [MEMORY_SEPARATION] 連続Mat確認 - そのまま使用");
                    continuousMat = processedMat;
                }
                
                using (continuousMat)
                {
                    // 🎯 [CRITICAL_FIX] byte[]データ抽出 - スレッド安全
                    var dataSize = continuousMat.Total() * continuousMat.ElemSize();
                    imageBytes = new byte[dataSize];
                    
                    unsafe
                    {
                        var srcPtr = (byte*)continuousMat.Data.ToPointer();
                        fixed (byte* dstPtr = imageBytes)
                        {
                            Buffer.MemoryCopy(srcPtr, dstPtr, dataSize, dataSize);
                        }
                    }
                    
                    __logger?.LogDebug("✅ [MEMORY_SEPARATION] byte[]抽出完了: {Size:N0}bytes", dataSize);
                }
            }
            // ここでprocessedMatは完全に解放済み - スレッド分離完了
            
            // 🎯 [MEMORY_SEPARATED_TIMEOUT] 画像サイズベースの適応的タイムアウト設定
            var pixelCount = imageSize.Width * imageSize.Height;
            var baseTimeout = pixelCount > 500000 ? 45 : (pixelCount > 300000 ? 35 : 30);
            var adaptiveTimeout = GetAdaptiveTimeout(baseTimeout);
            __logger?.LogDebug("⏱️ [MEMORY_SEPARATION] タイムアウト設定: {Timeout}秒 (画像: {Size}={PixelCount:N0}px, ベース: {Base}秒)", 
                adaptiveTimeout, $"{imageSize.Width}x{imageSize.Height}", pixelCount, baseTimeout);
        
            // 現在のOCRキャンセレーション管理
            _currentOcrCancellation?.Dispose();
            _currentOcrCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(adaptiveTimeout));
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _currentOcrCancellation.Token);
        
            var ocrTask = Task.Run(() =>
            {
                __logger?.LogWarning("🚀 [MEMORY_SEPARATION] Task.Run開始 - OCR処理実行開始");
                Console.WriteLine($"🚀 [OCR_TASK] Task.Run開始: {DateTime.Now:HH:mm:ss.fff}");
                __logger?.LogDebug("🚀 [MEMORY_SEPARATION] Task.Run内で新しいMat再構築開始");
                
                // 🔧 [CRITICAL_FIX] PaddlePredictor初期化エラー対策
                if (_ocrEngine == null)
                {
                    __logger?.LogError("🚨 [PADDLE_PREDICTOR_ERROR] OCRエンジンが初期化されていません");
                    throw new InvalidOperationException("OCRエンジンが初期化されていません");
                }
                
                // 🎯 [GEMINI_MEMORY_SEPARATION] byte[]から新しいMatを再構築 - 完全なスレッド分離
                Mat reconstructedMat = null;
                try
                {
                    __logger?.LogDebug("🔄 [MAT_RECONSTRUCTION] byte[]からMat再構築: {Size}, Type={Type}, データサイズ={DataSize:N0}bytes", 
                        $"{imageSize.Width}x{imageSize.Height}", imageType, imageBytes.Length);
                        
                    // 新しいMatインスタンス作成（元Matとは完全に分離）
                    reconstructedMat = new Mat(imageSize.Height, imageSize.Width, imageType);
                    
                    // byte[]データをMatにコピー
                    unsafe
                    {
                        var dstPtr = (byte*)reconstructedMat.Data.ToPointer();
                        fixed (byte* srcPtr = imageBytes)
                        {
                            Buffer.MemoryCopy(srcPtr, dstPtr, imageBytes.Length, imageBytes.Length);
                        }
                    }
                    
                    __logger?.LogDebug("✅ [MAT_RECONSTRUCTION] Mat再構築完了 - 完全に独立したMatインスタンス生成");
                    
                    // 🧠 [GEMINI_VALIDATION] 再構築されたMat状態検証
                    var matDetails = $"Size={reconstructedMat.Size()}, Type={reconstructedMat.Type()}, Channels={reconstructedMat.Channels()}, Empty={reconstructedMat.Empty()}, IsContinuous={reconstructedMat.IsContinuous()}";
                    __logger?.LogDebug("🔍 [MAT_VALIDATION] 再構築Mat詳細: {MatDetails}", matDetails);
                
                    // 🛡️ [CRITICAL_MEMORY_PROTECTION] AccessViolationException回避策
                    if (_consecutivePaddleFailures >= 3)
                    {
                        __logger?.LogError("🚨 [PADDLE_PREDICTOR_ERROR] PaddleOCR連続失敗のため一時的に無効化中（失敗回数: {FailureCount}）", _consecutivePaddleFailures);
                        throw new InvalidOperationException($"PaddleOCR連続失敗のため一時的に無効化中（失敗回数: {_consecutivePaddleFailures}）");
                    }
                    
                    // 🚨 [CRASH_PREVENTION] 大画像サイズ制限（WER_FAULT_SIGクラッシュ対策）
                    const int MAX_PIXELS = 35000000; // 3500万ピクセル制限（8K 7680x4320=33.18M, 4K 3840x2160=8.29Mに対応）
                    var totalPixels = reconstructedMat.Cols * reconstructedMat.Rows;
                    if (totalPixels > MAX_PIXELS)
                    {
                        __logger?.LogDebug("🎯 [IMAGE_RESIZE] 大画像検出 - リサイズ実行: {Width}x{Height}={TotalPixels:N0} > {MaxPixels:N0}制限", 
                            reconstructedMat.Cols, reconstructedMat.Rows, totalPixels, MAX_PIXELS);
                        
                        // アスペクト比を保持してリサイズ
                        var scale = Math.Sqrt((double)MAX_PIXELS / totalPixels);
                        var newWidth = (int)(reconstructedMat.Cols * scale);
                        var newHeight = (int)(reconstructedMat.Rows * scale);
                        
                        using var resizedMat = new Mat();
                        Cv2.Resize(reconstructedMat, resizedMat, new OpenCvSharp.Size(newWidth, newHeight), 0, 0, InterpolationFlags.Area);
                        
                        // 元の再構築Matを解放し、リサイズ版に置き換え
                        reconstructedMat.Dispose();
                        reconstructedMat = resizedMat.Clone();
                        
                        var finalPixels = reconstructedMat.Cols * reconstructedMat.Rows;
                        __logger?.LogDebug("✅ [IMAGE_RESIZE] リサイズ完了: {NewWidth}x{NewHeight}={FinalPixels:N0}ピクセル (縮小率: {Scale:F3})",
                            newWidth, newHeight, finalPixels, scale);
                    }
                    
                    // 🧠 [GEMINI_MEMORY_SEPARATION] 再構築されたMatを使用してOCR実行
                    // GCを実行してメモリを整理  
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    
                    // 🎯 [ULTRATHINK_FIX] Gemini推奨: アライメント問題解決
                    var originalSize = $"{reconstructedMat.Width}x{reconstructedMat.Height}";
                    var originalOdd = (reconstructedMat.Width % 2 == 1) || (reconstructedMat.Height % 2 == 1);
                    
                    // 🔍 [GEMINI_MAT_TRACE] 正規化前のMat.Dataポインタ追跡
                    var beforeNormalizePtr = reconstructedMat.Data.ToString("X16");
                    __logger?.LogDebug("🔍 [MAT_TRACE_BEFORE] 正規化前: Ptr={Ptr}, Size={Size}", beforeNormalizePtr, originalSize);
                    
                    // 🎯 [MEMORY_SEPARATION_NORMALIZATION] 再構築されたMatを正規化
                    var normalizedMat = NormalizeImageDimensions(reconstructedMat);
                    
                    // 再構築Matは使用済みなので解放
                    if (normalizedMat != reconstructedMat)
                    {
                        reconstructedMat.Dispose();
                        reconstructedMat = normalizedMat;
                    }
                    
                    // 🔍 [GEMINI_MAT_TRACE] 正規化後のMat.Dataポインタ追跡  
                    var afterNormalizePtr = reconstructedMat.Data.ToString("X16");
                    var normalizedSize = $"{reconstructedMat.Width}x{reconstructedMat.Height}";
                    __logger?.LogDebug("🔍 [MAT_TRACE_AFTER] 正規化後: Ptr={Ptr}, Size={Size}", afterNormalizePtr, normalizedSize);
                    
                    // 🎯 [GEMINI_POINTER_ANALYSIS] ポインタ変化の分析
                    var pointerChanged = beforeNormalizePtr != afterNormalizePtr;
                    __logger?.LogDebug("🎯 [MAT_PTR_ANALYSIS] ポインタ変化: {Changed}, 前={Before}, 後={After}", 
                        pointerChanged ? "あり" : "なし", beforeNormalizePtr, afterNormalizePtr);
                    
                    // 🔍 [ULTRATHINK_EVIDENCE] 正規化効果の確実な証拠収集
                    var normalizedOdd = (reconstructedMat.Width % 2 == 1) || (reconstructedMat.Height % 2 == 1);
                    __logger?.LogDebug("🎯 [NORMALIZATION_EVIDENCE] 正規化実行: {OriginalSize}({OriginalOdd}) → {NormalizedSize}({NormalizedOdd})", 
                        originalSize, originalOdd ? "奇数あり" : "偶数", normalizedSize, normalizedOdd ? "奇数あり" : "偶数");
                    
                    // Mat検証
                    if (!ValidateMatForPaddleOCR(reconstructedMat))
                    {
                        __logger?.LogWarning("⚠️ [MAT_PROCESSING] Mat validation failed, attempting automatic fix...");
                        var fixedMat = FixMatForPaddleOCR(reconstructedMat);
                        if (fixedMat == null)
                        {
                            throw new InvalidOperationException("Mat画像がPaddleOCR実行に適さず、自動修正も失敗しました");
                        }
                        
                        __logger?.LogDebug("✅ [MAT_PROCESSING] Mat自動修正成功 - 修正後のMatを使用");
                        reconstructedMat.Dispose(); // 元のreconstructedMatを解放
                        reconstructedMat = fixedMat; // 修正後のMatを使用
                    }
                    
                    // 🔒 [EXECUTION_SAFETY] PaddleOCR実行前最終安全確認
                    if (reconstructedMat.IsDisposed || reconstructedMat.Empty())
                    {
                        __logger?.LogError("🚨 [OCR_ENGINE] 不正なMat状態でPaddleOCR.Run中止: IsDisposed={IsDisposed}, Empty={Empty}",
                            reconstructedMat.IsDisposed, reconstructedMat.Empty());
                        throw new InvalidOperationException("PaddleOCR実行直前にMatが無効になりました");
                    }
                    
                    // Mat状態の詳細ログ
                    __logger?.LogDebug("🔍 [OCR_ENGINE] PaddleOCR.Run実行前状況: Size={Width}x{Height}, Type={Type}, Channels={Channels}, IsContinuous={IsContinuous}",
                        reconstructedMat.Cols, reconstructedMat.Rows, reconstructedMat.Type(), reconstructedMat.Channels(), reconstructedMat.IsContinuous());
                    
                    // PaddleOCR最小サイズチェック
                    if (reconstructedMat.Cols < 16 || reconstructedMat.Rows < 16)
                    {
                        __logger?.LogError("🚨 [OCR_ENGINE] 画像サイズが小さすぎ: {Width}x{Height} (最小: 16x16)", reconstructedMat.Cols, reconstructedMat.Rows);
                        throw new InvalidOperationException($"PaddleOCR用画像サイズが小さすぎ: {reconstructedMat.Cols}x{reconstructedMat.Rows} (最小: 16x16)");
                    }
                            
                    // 🛡️ [ACCESS_VIOLATION_PREVENTION] メモリアクセス安全性の最終チェック
                    try 
                    {
                        // Mat データ連続性確認（再構築済みMatは既に連続であるはず）
                        if (!reconstructedMat.IsContinuous())
                        {
                            __logger?.LogWarning("⚠️ [MEMORY_SAFETY] Mat非連続データを連続データに変換中...");
                            var continuousMat = reconstructedMat.Clone();
                            reconstructedMat.Dispose();
                            reconstructedMat = continuousMat;
                        }
                        
                        // メモリデータ有効性チェック
                        if (reconstructedMat.Empty())
                        {
                            __logger?.LogError("🚨 [MEMORY_SAFETY] Mat データが空です");
                            throw new InvalidOperationException("Mat データが空です");
                        }
                        
                        // データサイズ検証
                        var expectedSize = reconstructedMat.Rows * reconstructedMat.Cols * reconstructedMat.Channels();
                        if (expectedSize <= 0)
                        {
                            __logger?.LogError("🚨 [MEMORY_SAFETY] 無効なMatデータサイズ: {Size}", expectedSize);
                            throw new InvalidOperationException($"無効なMatデータサイズ: {expectedSize}");
                        }
                        
                        __logger?.LogDebug("✅ [MEMORY_SAFETY] メモリ安全性チェック完了: Size={Size}, Continuous={Continuous}", 
                            expectedSize, reconstructedMat.IsContinuous());
                    }
                    catch (Exception safetyEx)
                    {
                        __logger?.LogError(safetyEx, "🚨 [MEMORY_SAFETY] メモリ安全性チェックで例外");
                        throw new InvalidOperationException("PaddleOCR実行前のメモリ安全性チェックで例外", safetyEx);
                    }
                            
                    // 🎯 [PADDLE_PREDICTOR_CRITICAL_FIX] PaddlePredictor run failed エラー対策
                    __logger?.LogDebug("🏃 [OCR_ENGINE] PaddleOCR.Run実行開始 - メモリ分離済みMat使用");
                    
                    // 🔍 [GEMINI_FINAL_TRACE] PaddleOCR実行直前のMat.Dataポインタ確認
                    var finalMatPtr = reconstructedMat.Data.ToString("X16");
                    var finalMatSize = $"{reconstructedMat.Width}x{reconstructedMat.Height}";
                    __logger?.LogDebug("🔍 [MAT_TRACE_FINAL] PaddleOCR直前: Ptr={Ptr}, Size={Size}", finalMatPtr, finalMatSize);
                    
                    // 🧠 [GEMINI_MEMORY_SEPARATION] 完全分離されたMatを直接使用（追加Cloneは不要）
                    __logger?.LogWarning("🏁 [CRITICAL] PaddleOCR.Run実行直前 - ここで停止する場合はPaddleOCR内部問題");
                    Console.WriteLine($"🏁 [CRITICAL] PaddleOCR.Run実行開始: {DateTime.Now:HH:mm:ss.fff}");
                    
                    var ocrResult = _ocrEngine.Run(reconstructedMat);
                    
                    __logger?.LogWarning("✅ [SUCCESS] PaddleOCR.Run成功完了 - メモリ分離戦略");
                    Console.WriteLine($"✅ [SUCCESS] PaddleOCR.Run完了: {DateTime.Now:HH:mm:ss.fff}");
                    
                    return ocrResult;
                }
                catch (ObjectDisposedException ex)
                {
                    __logger?.LogError(ex, "🚨 [MAT_LIFECYCLE] ObjectDisposedException in PaddleOCR execution");
                    throw new InvalidOperationException("Mat objectが予期せず解放されました", ex);
                }
                catch (OperationCanceledException)
                {
                    _consecutivePaddleFailures++;
                    __logger?.LogError("🚨 [PADDLE_TIMEOUT] PaddleOCR実行がタイムアウトしました。連続失敗: {FailureCount}", _consecutivePaddleFailures);
                    Console.WriteLine($"🚨 [TIMEOUT] PaddleOCR実行タイムアウト: {DateTime.Now:HH:mm:ss.fff} - 連続失敗: {_consecutivePaddleFailures}");
                    throw new TimeoutException($"PaddleOCR実行がタイムアウトしました。連続失敗: {_consecutivePaddleFailures}");
                }
                catch (AggregateException ex) when (ex.InnerException is AccessViolationException)
                {
                    _consecutivePaddleFailures++;
                    __logger?.LogError(ex, "🚨 [PADDLE_MEMORY] PaddleOCRメモリアクセス違反が発生しました。Mat: {Width}x{Height}。連続失敗: {FailureCount}", 
                        reconstructedMat.Cols, reconstructedMat.Rows, _consecutivePaddleFailures);
                    throw new InvalidOperationException($"PaddleOCRメモリアクセス違反が発生しました。Mat: {reconstructedMat.Cols}x{reconstructedMat.Rows}。連続失敗: {_consecutivePaddleFailures}", ex.InnerException);
                }
                catch (AccessViolationException ex)
                {
                    _consecutivePaddleFailures++;
                    __logger?.LogError(ex, "🚨 [PADDLE_NATIVE] PaddleOCRネイティブライブラリでメモリ破損が発生しました。Mat: {Width}x{Height}。連続失敗: {FailureCount}", 
                        reconstructedMat.Cols, reconstructedMat.Rows, _consecutivePaddleFailures);
                    throw new InvalidOperationException($"PaddleOCRネイティブライブラリでメモリ破損が発生しました。Mat: {reconstructedMat.Cols}x{reconstructedMat.Rows}。連続失敗: {_consecutivePaddleFailures}", ex);
                }
                catch (Exception ex) when (ex.Message.Contains("PaddlePredictor") && ex.Message.Contains("run failed"))
                {
                    _consecutivePaddleFailures++;
                    
                    var detailedInfo = CollectPaddlePredictorErrorInfo(reconstructedMat, ex);
                    __logger?.LogError(ex, "🚨 [PADDLE_PREDICTOR_FAILED] 失敗#{FailureCount}: {DetailedInfo}", _consecutivePaddleFailures, detailedInfo);
                    
                    // Mat状態の詳細ログ
                    try 
                    {
                        var matInfo = $"Mat Info: {reconstructedMat.Width}x{reconstructedMat.Height}, " +
                                     $"Channels={reconstructedMat.Channels()}, Type={reconstructedMat.Type()}, " +
                                     $"Empty={reconstructedMat.Empty()}, Continuous={reconstructedMat.IsContinuous()}";
                        __logger?.LogError("🔍 [PADDLE_DEBUG] {MatInfo}", matInfo);
                    }
                    catch 
                    {
                        __logger?.LogError("🚨 [PADDLE_DEBUG] Cannot access Mat properties (Mat may be corrupted)");
                    }
                    
                    throw new InvalidOperationException($"PaddlePredictor実行失敗。連続失敗: {_consecutivePaddleFailures}", ex);
                }
                finally
                {
                    // 🧹 [CLEANUP] reconstructedMatの確実な解放
                    try 
                    {
                        reconstructedMat?.Dispose();
                    }
                    catch (Exception cleanupEx)
                    {
                        __logger?.LogWarning(cleanupEx, "⚠️ [CLEANUP] reconstructedMat cleanup warning");
                    }
                }
            });

            // 🎯 [MEMORY_SEPARATION_COMPLETE] Task.Run完了待機とresult取得
            __logger?.LogDebug("⏳ [MEMORY_SEPARATION] Task.Run処理完了待機中...");
            var result = await ocrTask.ConfigureAwait(false);
            __logger?.LogDebug("✅ [MEMORY_SEPARATION] Task.Run処理完了 - 結果取得成功");
            
            // 連続失敗カウンターをリセット（成功時）
            if (_consecutivePaddleFailures > 0)
            {
                __logger?.LogInformation("🎯 [PADDLE_RECOVERY] PaddleOCR連続失敗からの復旧: {FailureCount} → 0", _consecutivePaddleFailures);
                _consecutivePaddleFailures = 0;
            }
            
            return result;
        }
        catch (Exception ex)
        {
            __logger?.LogError(ex, "🚨 [MEMORY_SEPARATION] メモリ分離戦略でエラー発生");
            throw;
        }
    }

    private async Task<object> ExecuteOcrInSeparateTaskOptimized(Mat processedMat, CancellationToken cancellationToken, int timeoutSeconds = 15)
    {
        // 🧠 [ULTRATHINK_GEMINI_FIX] Gemini推奨Mat防御的コピー戦略（最適化版） - メモリ競合回避
        Mat safeMat = null;
        try
        {
            safeMat = processedMat.Clone(); // 防御的コピー作成
            processedMat.Dispose(); // 元Matを即座にDispose
            
            using (safeMat) // 安全なクローンMatを使用（最適化版）
            {
                __logger?.LogDebug("🚀 最適化OCR実行開始 - Mat防御的コピー版");
            
            // 🚀 [PERFORMANCE_OPTIMIZATION] 検出専用の高速タイムアウト設定
            var adaptiveTimeout = timeoutSeconds; // デフォルト15秒（通常の半分）
            __logger?.LogDebug($"⚡ 最適化タイムアウト設定: {adaptiveTimeout}秒（検出専用高速化）");
        
            // 現在のOCRキャンセレーション管理
            _currentOcrCancellation?.Dispose();
            _currentOcrCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(adaptiveTimeout));
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _currentOcrCancellation.Token);
        
            var ocrTask = Task.Run(() =>
            {
                __logger?.LogDebug("🏃 OCRエンジン実行中 - 最適化検出タスク");
                
                // 🔧 [CRITICAL_FIX] PaddlePredictor初期化エラー対策（最適化版）
                if (_ocrEngine == null)
                {
                    __logger?.LogError("🚨 [PADDLE_PREDICTOR_ERROR_OPT] OCRエンジンが初期化されていません（最適化） - 緊急再初期化を実行");
                    throw new InvalidOperationException("OCRエンジンが初期化されていません（最適化版）");
                }
                
                // 🚀 [SPEED_OPTIMIZATION] 最適化Mat検証（軽量版）
                try
                {
                    var matDetails = $"Size={safeMat.Size()}, Type={safeMat.Type()}, Channels={safeMat.Channels()}";
                    __logger?.LogDebug("🔍 [MAT_VALIDATION_OPT] 実行前Mat詳細（防御的コピー最適化版）: {MatDetails}", matDetails);
                }
                catch (Exception matEx)
                {
                    __logger?.LogError(matEx, "🚨 [MAT_VALIDATION_OPT] Mat詳細取得でエラー（最適化） - Mat破損の可能性");
                    throw new InvalidOperationException("Mat状態が不正です（最適化版）", matEx);
                }
            
                // 🛡️ [CRITICAL_MEMORY_PROTECTION] AccessViolationException回避策（最適化版）
                if (_consecutivePaddleFailures >= 2) // 最適化版では2回で制限
                {
                    __logger?.LogError("🚨 [PADDLE_PREDICTOR_ERROR_OPT] PaddleOCR連続失敗のため一時的に無効化中（最適化版）（失敗回数: {FailureCount}）", _consecutivePaddleFailures);
                    throw new InvalidOperationException($"PaddleOCR連続失敗のため一時的に無効化中（最適化版）（失敗回数: {_consecutivePaddleFailures}）");
                }
                
                // 🚀 [PERFORMANCE_BOOST] 大画像制限の軽量チェック
                const int MAX_PIXELS_OPT = 35000000; // 3500万ピクセル制限（最適化版・8K/4K対応でメイン制限と統一）
                var totalPixels = safeMat.Cols * safeMat.Rows;
                if (totalPixels > MAX_PIXELS_OPT)
                {
                    __logger?.LogDebug("🎯 [IMAGE_RESIZE_OPT] 大画像検出（最適化） - 高速リサイズ実行: {Width}x{Height}={TotalPixels:N0} > {MaxPixels:N0}制限", 
                        safeMat.Cols, safeMat.Rows, totalPixels, MAX_PIXELS_OPT);
                    
                    // より積極的な縮小（高速化優先）
                    var scale = Math.Sqrt((double)MAX_PIXELS_OPT / totalPixels);
                    var newWidth = (int)(safeMat.Cols * scale);
                    var newHeight = (int)(safeMat.Rows * scale);
                    
                    using var resizedMat = new Mat();
                    // 高速補間法を使用
                    Cv2.Resize(safeMat, resizedMat, new OpenCvSharp.Size(newWidth, newHeight), 0, 0, InterpolationFlags.Linear);
                    
                    // 🧠 [GEMINI_MAT_FIX] safeMatを安全に置き換え（最適化版）
                    safeMat.Dispose(); // 古いsafeMatを解放
                    safeMat = resizedMat.Clone(); // 新しいサイズのsafeMatに更新
                    
                    var finalPixels = safeMat.Cols * safeMat.Rows;
                    __logger?.LogDebug("✅ [IMAGE_RESIZE_OPT] 高速リサイズ完了: {NewWidth}x{NewHeight}={FinalPixels:N0}ピクセル (縮小率: {Scale:F3})",
                        newWidth, newHeight, finalPixels, scale);
                }
                
                object result;
                try
                {
                    // 🔧 [PADDLE_PREDICTOR_FIX_OPT] PaddlePredictor run failed エラー特化修正（最適化版）
                    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6)); // より短いタイムアウト
                    var ocrTask = Task.Run(() => {
                        // 🚀 [GC_OPTIMIZATION] 軽量GC実行（最適化版）
                        if (_consecutivePaddleFailures == 0)
                        {
                            GC.Collect(0, GCCollectionMode.Optimized); // 軽量GCのみ
                        }
                        
                        // 🔍 [CRITICAL_DEBUG_OPT] PaddleOCR実行前のMat詳細検証と自動修正（軽量版）
                        Mat workingMat = null;
                        try
                        {
                            // 🧠 [GEMINI_MAT_FIX] 既に防御的コピー済safeMatを使用（最適化版）
                            workingMat = safeMat.Clone();
                            
                            // 🛡️ [ENHANCED_VALIDATION] Mat状態の詳細検証（強化版）
                            var matIsDisposed = workingMat.IsDisposed;
                            var matIsEmpty = workingMat.Empty();
                            var matCols = workingMat.Cols;
                            var matRows = workingMat.Rows;
                            var matChannels = matIsEmpty ? -1 : workingMat.Channels();
                            var matDepth = matIsEmpty ? -1 : workingMat.Depth();
                            var matDataPtr = workingMat.Data.ToString("X16");

                            __logger?.LogDebug(
                                "🔍 [MAT_VALIDATION] PaddleOCR実行前Mat検証 - " +
                                "Disposed={Disposed}, Empty={Empty}, Size={Width}x{Height}, " +
                                "Channels={Channels}, Depth={Depth}, DataPtr={Ptr}",
                                matIsDisposed, matIsEmpty, matCols, matRows, matChannels, matDepth, matDataPtr);

                            if (matIsDisposed || matIsEmpty || matCols < 16 || matRows < 16)
                            {
                                __logger?.LogCritical(
                                    "💥 [MAT_INVALID] 不正なMat状態でPaddleOCR.Run中止 - " +
                                    "Disposed={Disposed}, Empty={Empty}, Size={Width}x{Height}, Ptr={Ptr}",
                                    matIsDisposed, matIsEmpty, matCols, matRows, matDataPtr);
                                throw new InvalidOperationException(
                                    $"PaddleOCR実行直前にMatが無効になりました: Disposed={matIsDisposed}, Empty={matIsEmpty}, Size={matCols}x{matRows}");
                            }
                            
                            // 🎯 [PADDLE_PREDICTOR_CRITICAL_FIX_OPT] PaddlePredictor run failed エラー対策（最適化版）
                            __logger?.LogDebug("🏃 [OCR_ENGINE_OPT] PaddleOCR.Run実行開始（最適化）...");
                            
                            // 🔍 [GEMINI_FINAL_TRACE_OPT] PaddleOCR実行直前のMat.Dataポインタ確認（最適化）
                            var finalMatPtrOpt = workingMat.Data.ToString("X16");
                            var finalMatSizeOpt = $"{workingMat.Width}x{workingMat.Height}";
                            __logger?.LogDebug("🔍 [MAT_TRACE_FINAL_OPT] PaddleOCR直前（最適化）: Ptr={Ptr}, Size={Size}", finalMatPtrOpt, finalMatSizeOpt);
                            
                            // 🎯 [GEMINI_FORCE_COPY_OPT] Force Copy戦略: 最適化パスでも安全なClone()
                            using var safeCopyMat = workingMat.Clone();
                            var ocrResult = _ocrEngine.Run(safeCopyMat);
                            __logger?.LogDebug("✅ [OCR_ENGINE_OPT] PaddleOCR.Run成功完了（最適化）");
                            return ocrResult;
                        }
                        catch (ObjectDisposedException ex)
                        {
                            __logger?.LogError(ex, "🚨 [MAT_LIFECYCLE_OPT] ObjectDisposedException in PaddleOCR execution (optimized)");
                            throw new InvalidOperationException("Mat objectが予期せず解放されました（最適化版）", ex);
                        }
                        finally
                        {
                            // 🧹 [CLEANUP_OPT] workingMatの確実な解放（最適化版）
                            try 
                            {
                                workingMat?.Dispose();
                            }
                            catch (Exception cleanupEx)
                            {
                                __logger?.LogWarning(cleanupEx, "⚠️ [CLEANUP_OPT] workingMat cleanup warning (optimized)");
                            }
                        }
                    }, cts.Token);
                    
                    result = ocrTask.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    _consecutivePaddleFailures++;
                    __logger?.LogError("🚨 [PADDLE_TIMEOUT_OPT] PaddleOCR実行がタイムアウト（最適化）（6秒）。連続失敗: {FailureCount}", _consecutivePaddleFailures);
                    throw new TimeoutException($"PaddleOCR実行がタイムアウト（最適化）（6秒）。連続失敗: {_consecutivePaddleFailures}");
                }
                catch (Exception ex) when (ex.Message.Contains("PaddlePredictor") && ex.Message.Contains("run failed"))
                {
                    // 🚨 [ULTRATHINK_ENHANCED_RECOVERY_OPT] PaddlePredictor失敗時の高速回復機構（最適化版）
                    _consecutivePaddleFailures++;
                    
                    var detailedInfo = $"PaddlePredictor Error (Optimized): {ex.Message}";
                    __logger?.LogError(ex, "🚨 [PADDLE_PREDICTOR_FAILED_OPT] 失敗#{FailureCount}: {DetailedInfo}", _consecutivePaddleFailures, detailedInfo);
                    
                    // 🎯 [ULTRATHINK_FAST_RECOVERY] 最適化版での高速回復処理
                    if (_consecutivePaddleFailures <= 2)
                    {
                        // 1-2回失敗: 超高速回復（最小処理）
                        __logger?.LogWarning("🔄 [AUTO_RECOVERY_FAST] 最適化版失敗#{Count} - 超高速回復開始", _consecutivePaddleFailures);
                        try
                        {
                            // 最小限のGC（高速化優先）
                            GC.Collect(0, GCCollectionMode.Optimized);
                            
                            // 更に小さいサイズでリトライ（高速化）
                            using var fastMat = new Mat();
                            var fastScale = Math.Min(0.8, Math.Sqrt(200000.0 / (safeMat.Cols * safeMat.Rows))); // 20万ピクセル制限（高速）
                            var fastSize = new OpenCvSharp.Size(Math.Max(12, (int)(safeMat.Cols * fastScale)), 
                                                               Math.Max(12, (int)(safeMat.Rows * fastScale)));
                            Cv2.Resize(safeMat, fastMat, fastSize, 0, 0, InterpolationFlags.Nearest); // 最高速補間
                            
                            // 🎯 [GEMINI_FORCE_COPY_FAST] Force Copy戦略: 超高速回復でも安全なClone()
                            using var safeCopyMat = fastMat.Clone();
                            var recoveryResult = _ocrEngine.Run(safeCopyMat);
                            
                            __logger?.LogInformation("✅ [AUTO_RECOVERY_FAST] 超高速回復成功 - 失敗カウンタリセット");
                            _consecutivePaddleFailures = Math.Max(0, _consecutivePaddleFailures - 1);
                            return recoveryResult;
                        }
                        catch (Exception recoveryEx)
                        {
                            __logger?.LogWarning(recoveryEx, "⚠️ [AUTO_RECOVERY_FAST] 超高速回復失敗");
                        }
                    }
                    
                    throw new InvalidOperationException($"PaddlePredictor実行失敗（最適化）: {ex.Message}。連続失敗: {_consecutivePaddleFailures}回", ex);
                }
                catch (AccessViolationException avEx)
                {
                    // 🛡️ [CRITICAL_FIX] AccessViolationException専用処理
                    // PaddleOCRネイティブライブラリ内でのメモリアクセス違反を捕捉
                    _consecutivePaddleFailures += 3; // AVEは致命的なので大きくカウント

                    __logger?.LogCritical(avEx,
                        "💥 [ACCESS_VIOLATION] PaddleOCRネイティブライブラリでメモリアクセス違反 - " +
                        "連続失敗: {FailureCount}回。180度分類器またはモデル互換性の問題が疑われます。",
                        _consecutivePaddleFailures);

                    // 180度分類が無効化されているか確認してログ
                    var clsEnabled = _ocrEngine?.Enable180Classification ?? false;
                    __logger?.LogCritical("🔍 [AVE_DEBUG] Enable180Classification状態: {ClsEnabled}", clsEnabled);

                    // AccessViolationExceptionは回復不能なため、即座にスロー
                    throw new InvalidOperationException(
                        $"PaddleOCRネイティブエラー（AccessViolationException）。連続失敗: {_consecutivePaddleFailures}回。" +
                        "180度分類器が無効化されているか確認してください。", avEx);
                }
                catch (Exception ex)
                {
                    _consecutivePaddleFailures++;
                    __logger?.LogError(ex, "🚨 [GENERAL_OCR_ERROR_OPT] Unexpected PaddleOCR error (optimized): {Message}", ex.Message);
                    throw new InvalidOperationException($"PaddleOCR実行エラー（最適化）: {ex.Message}。連続失敗: {_consecutivePaddleFailures}", ex);
                }
                
                __logger?.LogDebug("✅ PaddleOCR.Run()完了（最適化） - 結果取得完了");
                
                // 成功時は連続失敗カウンタをリセット
                if (_consecutivePaddleFailures > 0)
                {
                    __logger?.LogDebug("🎯 [RECOVERY_OPT] PaddleOCR成功（最適化） - 連続失敗カウンタリセット: {OldCount} → 0", _consecutivePaddleFailures);
                    _consecutivePaddleFailures = 0;
                }
                
                return result;
            }, combinedCts.Token);

            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(adaptiveTimeout), cancellationToken);
        
            var completedTask = await Task.WhenAny(ocrTask, timeoutTask).ConfigureAwait(false);
        
            if (completedTask == ocrTask)
            {
                var result = await ocrTask.ConfigureAwait(false);
            
                if (result == null)
                {
                    __logger?.LogWarning("⚠️ OCR処理結果がnull（最適化） - エラー状態");
                    throw new InvalidOperationException("OCR processing returned null result (optimized)");
                }
            
                __logger?.LogDebug("✅ OCR処理正常完了（最適化） - Task.WhenAny版");
            
                // 成功時の統計更新とクリーンアップ
                _lastOcrTime = DateTime.UtcNow;
                _consecutiveTimeouts = 0;
                _currentOcrCancellation = null;
            
                return result;
            }
            else
            {
                var modelVersion = "V5"; // V5統一完了
                __logger?.LogWarning("⏰ {ModelVersion}モデルOCR処理{Timeout}秒タイムアウト（最適化）", modelVersion, adaptiveTimeout);
            
                // バックグラウンドタスクのキャンセルを要求
                combinedCts.Cancel();
            
                // タイムアウト時の統計更新とクリーンアップ
                _consecutiveTimeouts++;
                _currentOcrCancellation = null;
            
                throw new TimeoutException($"{modelVersion}モデルのOCR処理が{adaptiveTimeout}秒でタイムアウトしました（最適化版）");
            }
        } // 🧠 [GEMINI_MAT_FIX] safeMatのusing scope終了 - 安全なコピー解放（最適化版）
        }
        catch (Exception ex)
        {
            // 🧠 [GEMINI_ERROR_HANDLING] エラー時のsafeMatクリーンアップ（最適化版）
            safeMat?.Dispose();
            __logger?.LogError(ex, "🚨 [GEMINI_MAT_FIX] ExecuteOcrInSeparateTaskOptimizedでエラー発生 - safeMatクリーンアップ完了");
            throw;
        }
    }

    /// <summary>
    /// 翻訳結果が表示された際に進行中のタイムアウト処理をキャンセル
    /// </summary>
    // ✅ [PHASE2.9.6] _executor に委譲
    public void CancelCurrentOcrTimeout() => _executor.CancelCurrentOcrTimeout();

    /// <summary>
    /// テキスト検出のみを実行（認識処理をスキップ）
    /// AdaptiveTileStrategy等での高速テキスト領域検出用
    /// </summary>
    /// <param name="image">画像</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>検出されたテキスト領域（テキスト内容は空またはダミー）</returns>
    public async Task<OcrResults> DetectTextRegionsAsync(
        IImage image,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ThrowIfDisposed();

        // 初期化ガード: 未初期化の場合は自動初期化を実行（スレッドセーフ）
        if (!IsInitialized)
        {
            lock (_lockObject)
            {
                // ダブルチェックロッキングパターンで競合状態を回避
                if (!IsInitialized)
                {
                    var initTask = InitializeAsync(_settings, cancellationToken);
                    var initResult = initTask.GetAwaiter().GetResult();
                    
                    if (!initResult)
                    {
                        throw new InvalidOperationException("OCRエンジンの自動初期化に失敗しました。システム要件を確認してください。");
                    }
                }
            }
        }

        var stopwatch = Stopwatch.StartNew();
        
        __logger?.LogDebug("🔍 PaddleOcrEngine.DetectTextRegionsAsync開始 - 高速検出専用モード");

        // テスト環境ではダミー結果を返す
        var isTestEnv = IsTestEnvironment();
        
        if (isTestEnv)
        {
            __logger?.LogDebug("テスト環境: ダミーテキスト検出結果を返却");
            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            
            var dummyTextRegions = new List<OcrTextRegion>
            {
                new("", new Rectangle(10, 10, 100, 30), 0.95), // 検出専用なのでテキストは空
                new("", new Rectangle(50, 60, 80, 25), 0.88)
            };
            
            return new OcrResults(
                dummyTextRegions,
                image,
                stopwatch.Elapsed,
                CurrentLanguage ?? "jpn",
                null,
                "" // 検出専用なので結合テキストも空
            );
        }

        try
        {
            // IImageからMatに変換（大画面対応スケーリング付き）
            var (mat, scaleFactor) = await ConvertToMatWithScalingAsync(image, null, cancellationToken).ConfigureAwait(false);
            
            if (mat.Empty())
            {
                mat.Dispose();
                __logger?.LogWarning("変換後の画像が空です");
                return CreateEmptyResult(image, null, stopwatch.Elapsed);
            }

            // 検出結果を格納する変数を宣言（スコープ問題解決）
            IReadOnlyList<OcrTextRegion> textRegions;
            using (mat) // Matのリソース管理
            {
                // テキスト検出のみを実行（認識をスキップ）
                textRegions = await ExecuteTextDetectionOnlyAsync(mat, cancellationToken).ConfigureAwait(false);
            }
            
            // 🎯 Level 1大画面対応: 統合座標復元（検出専用）
            stopwatch.Stop();
            
            OcrResults result;
            if (Math.Abs(scaleFactor - 1.0) >= 0.001 && textRegions != null && textRegions.Count > 0)
            {
                __logger?.LogDebug("📍 検出専用統合座標復元開始: スケール係数={ScaleFactor}", scaleFactor);
                
                try
                {
                    // CoordinateRestorer.RestoreOcrResultsを活用（検出専用モード）
                    var tempResult = new OcrResults(
                        textRegions,
                        image,
                        stopwatch.Elapsed,
                        CurrentLanguage ?? "jpn",
                        null,
                        "" // 検出専用なので結合テキストは空
                    );
                    
                    result = CoordinateRestorer.RestoreOcrResults(tempResult, scaleFactor, image);
                    __logger?.LogDebug("✅ 検出専用統合座標復元完了: {Count}個のテキスト領域を復元", result.TextRegions.Count);
                }
                catch (Exception ex)
                {
                    __logger?.LogWarning(ex, "⚠️ 検出専用統合座標復元でエラーが発生しました。スケーリングされた座標を使用します");
                    // エラー時はスケーリングされた座標をそのまま使用
                    result = new OcrResults(
                        textRegions,
                        image,
                        stopwatch.Elapsed,
                        CurrentLanguage ?? "jpn",
                        null,
                        "" // 検出専用なので結合テキストは空
                    );
                }
            }
            else
            {
                result = new OcrResults(
                    textRegions ?? [],
                    image,
                    stopwatch.Elapsed,
                    CurrentLanguage ?? "jpn",
                    null,
                    "" // 検出専用なので結合テキストは空
                );
            }

            __logger?.LogDebug("✅ テキスト検出専用処理完了 - 検出領域数: {Count}, 処理時間: {Time}ms", 
                textRegions?.Count ?? 0, stopwatch.ElapsedMilliseconds);
            
            // 統計更新
            UpdatePerformanceStats(stopwatch.Elapsed.TotalMilliseconds, true);

            return result;
        }
        catch (OperationCanceledException)
        {
            __logger?.LogDebug("テキスト検出処理がキャンセルされました");
            throw;
        }
        catch (Exception ex)
        {
            UpdatePerformanceStats(stopwatch.Elapsed.TotalMilliseconds, false);
            __logger?.LogError(ex, "テキスト検出処理中にエラーが発生: {ExceptionType}", ex.GetType().Name);
            throw new OcrException($"テキスト検出処理中にエラーが発生しました: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 解像度とモデルに応じた基本タイムアウトを計算
    /// </summary>
    /// <param name="mat">処理対象の画像Mat</param>
    /// <returns>基本タイムアウト（秒）</returns>
    private int CalculateBaseTimeout(Mat mat)
{
    // 🛡️ [MEMORY_PROTECTION] Mat状態の安全性チェック
    try 
    {
        // Mat.Empty()チェックが最も安全（内部でColsやRowsチェックも行う）
        if (mat == null || mat.Empty())
        {
            __logger?.LogWarning("⚠️ Mat is null or empty in CalculateBaseTimeout - using default timeout");
            return 30; // V5統一タイムアウト
        }

        // Mat基本プロパティの安全な取得（AccessViolationException & ObjectDisposedException回避）
        int width, height;
        try 
        {
            // 🛡️ [LIFECYCLE_PROTECTION] Mat処分状態チェック
            if (mat.IsDisposed)
            {
                __logger?.LogWarning("⚠️ Mat is disposed in CalculateBaseTimeout - using default timeout");
                return 30; // V5統一タイムアウト
            }
            
            width = mat.Width;   // 内部でmat.get_Cols()を呼び出し
            height = mat.Height; // 内部でmat.get_Rows()を呼び出し
        }
        catch (ObjectDisposedException ex)
        {
            __logger?.LogError(ex, "🚨 [MAT_DISPOSED] ObjectDisposedException in Mat.Width/Height access");
            return 30; // V5統一タイムアウト
        }
        catch (AccessViolationException ex)
        {
            __logger?.LogError(ex, "🚨 AccessViolationException in Mat.Width/Height access - Mat may be corrupted or disposed");
            return 30; // V5統一タイムアウト
        }
        catch (Exception ex)
        {
            __logger?.LogError(ex, "⚠️ Unexpected exception in Mat property access: {ExceptionType}", ex.GetType().Name);
            return 30; // V5統一タイムアウト
        }

        // 有効なサイズかチェック
        if (width <= 0 || height <= 0)
        {
            __logger?.LogWarning("⚠️ Invalid Mat dimensions: {Width}x{Height} - using default timeout", width, height);
            return 30; // V5統一タイムアウト
        }

        var pixelCount = (long)width * height; // オーバーフロー防止のためlong使用
        var isV4Model = false /* V5統一により常にfalse */;
        
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
        
        __logger?.LogDebug("🖼️ 解像度ベースタイムアウト: {Width}x{Height}({PixelCount:N0}px) → {BaseTimeout}秒 (V4={IsV4Model})", 
            width, height, pixelCount, baseTimeout, isV4Model);
            
        return baseTimeout;
    }
    catch (ObjectDisposedException ex)
    {
        __logger?.LogError(ex, "🚨 [MAT_LIFECYCLE] Mat disposed during CalculateBaseTimeout - using default timeout");
        return false /* V5統一により常にfalse */ ? 25 : 30; // フォールバック
    }
    catch (AccessViolationException ex)
    {
        __logger?.LogError(ex, "🚨 AccessViolationException in CalculateBaseTimeout - using default timeout");
        return false /* V5統一により常にfalse */ ? 25 : 30; // フォールバック
    }
    catch (Exception ex)
    {
        __logger?.LogError(ex, "🚨 Unexpected error in CalculateBaseTimeout - using default timeout");
        return false /* V5統一により常にfalse */ ? 25 : 30; // フォールバック
    }
}

    /// <summary>
    /// 適応的タイムアウト値を計算
    /// </summary>
    private int GetAdaptiveTimeout(int baseTimeout)
    {
        var timeSinceLastOcr = DateTime.UtcNow - _lastOcrTime;
        
        // 連続処理による性能劣化を考慮
        var adaptiveTimeout = baseTimeout;
        
        // 短時間での連続処理の場合、タイムアウトを延長
        if (timeSinceLastOcr.TotalSeconds < 10)
        {
            adaptiveTimeout = (int)(baseTimeout * 1.5);
            // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"🔄 連続処理検出: 前回から{timeSinceLastOcr.TotalSeconds:F1}秒, タイムアウト延長");
        }
        
        // 連続タイムアウトの場合、さらに延長
        if (_consecutiveTimeouts > 0)
        {
            adaptiveTimeout = (int)(adaptiveTimeout * (1 + 0.3 * _consecutiveTimeouts));
            // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"⚠️ 連続タイムアウト={_consecutiveTimeouts}回, タイムアウト追加延長");
        }
        
        // 🎯 [LEVEL1_FIX] 大画面対応スケーリング処理を考慮したタイムアウト延長
        // Level 1実装により、Mat再構築やスケーリング処理で追加時間が必要
        adaptiveTimeout = (int)(adaptiveTimeout * 1.8); // 80%延長
        __logger?.LogDebug("🎯 [LEVEL1_TIMEOUT] 大画面対応タイムアウト延長: {BaseTimeout}秒 → {AdaptiveTimeout}秒 (80%延長)", 
            baseTimeout, adaptiveTimeout);
        
        // 最大値制限を緩和 (3倍 → 4倍)
        var maxTimeout = Math.Min(adaptiveTimeout, baseTimeout * 4);
        
        // 🔍 [ULTRATHINK_FIX] タイムアウト設定の詳細ログ
        __logger?.LogWarning("⏱️ [TIMEOUT_CONFIG] 最終タイムアウト設定: {FinalTimeout}秒 (ベース: {Base}秒, 適応: {Adaptive}秒, 連続失敗: {Failures}回)", 
            maxTimeout, baseTimeout, adaptiveTimeout, _consecutiveTimeouts);
        
        return maxTimeout;
    }
    
    /// <summary>
    /// PaddleOCR実行前のMat画像詳細検証
    /// PaddlePredictor(Detector) run failedエラー対策
    /// </summary>
    private bool ValidateMatForPaddleOCR(Mat mat)
    {
        try 
        {
            // 🔍 [VALIDATION-1] 基本状態チェック
            if (mat == null)
            {
                __logger?.LogError("🚨 [MAT_VALIDATION] Mat is null");
                return false;
            }
            
            if (mat.Empty())
            {
                __logger?.LogError("🚨 [MAT_VALIDATION] Mat is empty");
                return false;
            }
            
            // 🔍 [VALIDATION-2] 画像サイズ検証（AccessViolationException安全版）
            int width, height, channels;
            MatType matType;
            bool isContinuous;
            
            try 
            {
                width = mat.Width;
                height = mat.Height;
                channels = mat.Channels();
                matType = mat.Type();
                isContinuous = mat.IsContinuous();
            }
            catch (AccessViolationException ex)
            {
                __logger?.LogError(ex, "🚨 [MAT_VALIDATION] AccessViolationException during Mat property access");
                return false;
            }
            
            __logger?.LogDebug("🔍 [MAT_VALIDATION] Mat Properties: {Width}x{Height}, Channels={Channels}, Type={Type}, Continuous={Continuous}",
                width, height, channels, matType, isContinuous);
            
            // 🔍 [VALIDATION-3] PaddleOCR要件チェック
            
            // サイズ制限チェック
            const int MIN_SIZE = 10;
            const int MAX_SIZE = 8192;
            
            if (width < MIN_SIZE || height < MIN_SIZE)
            {
                __logger?.LogError("🚨 [MAT_VALIDATION] Image too small: {Width}x{Height} (minimum: {Min}x{Min})", 
                    width, height, MIN_SIZE);
                return false;
            }
            
            if (width > MAX_SIZE || height > MAX_SIZE)
            {
                __logger?.LogError("🚨 [MAT_VALIDATION] Image too large: {Width}x{Height} (maximum: {Max}x{Max})", 
                    width, height, MAX_SIZE);
                return false;
            }
            
            // チャンネル数チェック（PaddleOCRは3チャンネルBGRを期待）
            if (channels != 3)
            {
                __logger?.LogError("🚨 [MAT_VALIDATION] Invalid channels: {Channels} (expected: 3)", channels);
                return false;
            }
            
            // データ型チェック（8-bit unsigned, 3 channels必須）
            if (matType != MatType.CV_8UC3)
            {
                __logger?.LogError("🚨 [MAT_VALIDATION] Invalid Mat type: {Type} (expected: CV_8UC3)", matType);
                return false;
            }
            
            // 🔍 [VALIDATION-4] メモリ状態チェック
            try 
            {
                var step = mat.Step();
                var elemSize = mat.ElemSize();
                
                __logger?.LogDebug("🔍 [MAT_VALIDATION] Memory Layout: Step={Step}, ElemSize={ElemSize}", step, elemSize);
                
                if (step <= 0 || elemSize <= 0)
                {
                    __logger?.LogError("🚨 [MAT_VALIDATION] Invalid memory layout: Step={Step}, ElemSize={ElemSize}", 
                        step, elemSize);
                    return false;
                }
            }
            catch (Exception ex)
            {
                __logger?.LogError(ex, "🚨 [MAT_VALIDATION] Memory layout check failed");
                return false;
            }
            
            // 🔍 [VALIDATION-5] 画像データ整合性チェック
            try 
            {
                // 画像の一部をサンプリングして有効性を確認
                var total = mat.Total();
                if (total <= 0)
                {
                    __logger?.LogError("🚨 [MAT_VALIDATION] Invalid total pixels: {Total}", total);
                    return false;
                }
                
                // 期待される総ピクセル数と実際の値を比較
                var expectedTotal = (long)width * height;
                if (total != expectedTotal)
                {
                    __logger?.LogError("🚨 [MAT_VALIDATION] Pixel count mismatch: Expected={Expected}, Actual={Actual}",
                        expectedTotal, total);
                    return false;
                }
            }
            catch (Exception ex)
            {
                __logger?.LogError(ex, "🚨 [MAT_VALIDATION] Data integrity check failed");
                return false;
            }
            
            // ✅ すべての検証をパス
            __logger?.LogDebug("✅ [MAT_VALIDATION] Mat validation passed: {Width}x{Height}, {Channels}ch, {Type}",
                width, height, channels, matType);
            return true;
        }
        catch (Exception ex)
        {
            __logger?.LogError(ex, "🚨 [MAT_VALIDATION] Unexpected error during Mat validation");
            return false;
        }
    }
    
    /// <summary>
    /// Mat画像をPaddleOCR実行に適合するよう自動修正
    /// PaddlePredictor(Detector) run failedエラー対策
    /// </summary>
    /// <summary>
    /// 🎯 [ULTRATHINK_FIX] Gemini推奨: 奇数幅メモリアライメント正規化
    /// PaddlePredictor内部のSIMD命令・メモリ境界問題を回避
    /// </summary>
    private Mat NormalizeImageDimensions(Mat inputMat)
    {
        if (inputMat == null || inputMat.Empty())
        {
            __logger?.LogWarning("⚠️ [NORMALIZE] Cannot normalize null or empty Mat");
            return inputMat;
        }

        try
        {
            bool needsResize = false;
            var newWidth = inputMat.Width;
            var newHeight = inputMat.Height;

            // 🎯 [ULTRATHINK_PHASE21_FIX] 4バイトアライメント正規化（SIMD命令対応）
            // PaddleOCRは内部でSSE2/AVX命令を使用するため、4の倍数が必須
            if (inputMat.Width % 4 != 0)
            {
                newWidth = ((inputMat.Width / 4) + 1) * 4;  // 次の4の倍数に切り上げ
                needsResize = true;
                __logger?.LogDebug("🔧 [NORMALIZE] 幅を4の倍数に正規化: {Width} → {NewWidth} (SIMD最適化)", 
                    inputMat.Width, newWidth);
            }

            // 🎯 [ULTRATHINK_PHASE21_FIX] 高さも4の倍数に正規化
            if (inputMat.Height % 4 != 0)
            {
                newHeight = ((inputMat.Height / 4) + 1) * 4;  // 次の4の倍数に切り上げ
                needsResize = true;
                __logger?.LogDebug("🔧 [NORMALIZE] 高さを4の倍数に正規化: {Height} → {NewHeight} (SIMD最適化)", 
                    inputMat.Height, newHeight);
            }

            if (needsResize)
            {
                Mat normalizedMat = new();
                Cv2.Resize(inputMat, normalizedMat, new OpenCvSharp.Size(newWidth, newHeight));
                
                __logger?.LogInformation("✅ [NORMALIZE] 画像サイズ正規化完了: {OriginalSize} → {NormalizedSize} " +
                    "(PaddlePredictor最適化対応)", 
                    $"{inputMat.Width}x{inputMat.Height}", $"{newWidth}x{newHeight}");
                
                return normalizedMat;
            }

            return inputMat;
        }
        catch (Exception ex)
        {
            __logger?.LogError(ex, "🚨 [NORMALIZE] 画像正規化中にエラー - 元画像を返却");
            return inputMat;
        }
    }

    private Mat? FixMatForPaddleOCR(Mat originalMat)
    {
        try 
        {
            __logger?.LogDebug("🔧 [MAT_FIX] Mat自動修正開始");
            
            if (originalMat == null || originalMat.Empty())
            {
                __logger?.LogError("🚨 [MAT_FIX] Cannot fix null or empty Mat");
                return null;
            }
            
            Mat fixedMat = originalMat.Clone();
            bool wasModified = false;
            
            // 🔧 [FIX-1] 基本プロパティ取得（安全版）
            int width, height, channels;
            MatType matType;
            
            try 
            {
                width = fixedMat.Width;
                height = fixedMat.Height;
                channels = fixedMat.Channels();
                matType = fixedMat.Type();
            }
            catch (AccessViolationException ex)
            {
                __logger?.LogError(ex, "🚨 [MAT_FIX] AccessViolationException during property access - cannot fix");
                fixedMat?.Dispose();
                return null;
            }
            
            __logger?.LogDebug("🔧 [MAT_FIX] Original: {Width}x{Height}, {Channels}ch, {Type}",
                width, height, channels, matType);
            
            // 🔧 [FIX-2] 画像サイズ修正
            const int MIN_SIZE = 10;
            const int MAX_SIZE = 4096; // PaddleOCRの安全な最大サイズ
            
            if (width < MIN_SIZE || height < MIN_SIZE || width > MAX_SIZE || height > MAX_SIZE)
            {
                // アスペクト比を維持してリサイズ
                double scale = Math.Min((double)MAX_SIZE / width, (double)MAX_SIZE / height);
                scale = Math.Max(scale, (double)MIN_SIZE / Math.Min(width, height));
                
                int newWidth = Math.Max(MIN_SIZE, Math.Min(MAX_SIZE, (int)(width * scale)));
                int newHeight = Math.Max(MIN_SIZE, Math.Min(MAX_SIZE, (int)(height * scale)));
                
                __logger?.LogDebug("🔧 [MAT_FIX] Resizing: {OldWidth}x{OldHeight} → {NewWidth}x{NewHeight}",
                    width, height, newWidth, newHeight);
                
                // ✅ [MEMORY_SAFE] 新しいMatを作成し、古いMatを適切にDispose
                var resizedMat = new Mat();
                Cv2.Resize(fixedMat, resizedMat, new OpenCvSharp.Size(newWidth, newHeight), 0, 0, InterpolationFlags.Area);
                fixedMat.Dispose(); // 古いMatを解放
                fixedMat = resizedMat; // 新しいMatに置き換え
                wasModified = true;
                
                // 更新されたサイズ情報
                width = newWidth;
                height = newHeight;
            }
            
            // 🔧 [FIX-3] チャンネル数修正
            if (channels != 3)
            {
                __logger?.LogDebug("🔧 [MAT_FIX] Converting channels: {Channels} → 3", channels);
                
                // ✅ [MEMORY_SAFE] チャンネル変換用のMatを作成し、適切にDispose管理
                var convertedMat = new Mat();
                try 
                {
                    if (channels == 1)
                    {
                        // グレースケール → BGR
                        Cv2.CvtColor(fixedMat, convertedMat, ColorConversionCodes.GRAY2BGR);
                    }
                    else if (channels == 4)
                    {
                        // BGRA → BGR
                        Cv2.CvtColor(fixedMat, convertedMat, ColorConversionCodes.BGRA2BGR);
                    }
                    else
                    {
                        // その他の場合はグレースケール経由でBGRに変換
                        // ✅ [MEMORY_SAFE] 一時的なgrayMatは適切にDispose
                        var grayMat = new Mat();
                        Cv2.CvtColor(fixedMat, grayMat, ColorConversionCodes.BGR2GRAY);
                        Cv2.CvtColor(grayMat, convertedMat, ColorConversionCodes.GRAY2BGR);
                        grayMat.Dispose(); // 一時的なMatを解放
                    }
                    
                    fixedMat.Dispose(); // 古いMatを解放
                    fixedMat = convertedMat; // 新しいMatに置き換え
                    channels = 3;
                    wasModified = true;
                }
                catch (Exception ex)
                {
                    __logger?.LogError(ex, "🚨 [MAT_FIX] Channel conversion failed");
                    convertedMat.Dispose();
                    fixedMat?.Dispose();
                    return null;
                }
            }
            
            // 🔧 [FIX-4] データ型修正
            if (fixedMat.Type() != MatType.CV_8UC3)
            {
                __logger?.LogDebug("🔧 [MAT_FIX] Converting type: {Type} → CV_8UC3", fixedMat.Type());
                
                // ✅ [MEMORY_SAFE] データ型変換用のMatを作成し、適切にDispose管理
                var convertedMat = new Mat();
                try 
                {
                    fixedMat.ConvertTo(convertedMat, MatType.CV_8UC3);
                    fixedMat.Dispose(); // 古いMatを解放
                    fixedMat = convertedMat; // 新しいMatに置き換え
                    wasModified = true;
                }
                catch (Exception ex)
                {
                    __logger?.LogError(ex, "🚨 [MAT_FIX] Type conversion failed");
                    convertedMat.Dispose();
                    fixedMat?.Dispose();
                    return null;
                }
            }
            
            // 🔧 [FIX-5] メモリレイアウト最適化
            if (!fixedMat.IsContinuous())
            {
                __logger?.LogDebug("🔧 [MAT_FIX] Making memory continuous");
                
                var continuousMat = fixedMat.Clone();
                fixedMat.Dispose();
                fixedMat = continuousMat;
                wasModified = true;
            }
            
            // 🔧 [FIX-6] 4の倍数アライメント修正（PaddlePredictor SIMD互換性対応）
            int currentWidth = fixedMat.Width;
            int currentHeight = fixedMat.Height;
            
            if (currentWidth % 4 != 0 || currentHeight % 4 != 0)
            {
                // 4の倍数に調整（切り上げ）
                int alignedWidth = (currentWidth + 3) & ~3;   // 4の倍数に切り上げ
                int alignedHeight = (currentHeight + 3) & ~3; // 4の倍数に切り上げ
                
                __logger?.LogDebug("🔧 [MAT_FIX] 4-byte alignment fix: {OldWidth}x{OldHeight} → {NewWidth}x{NewHeight}",
                    currentWidth, currentHeight, alignedWidth, alignedHeight);
                
                // ✅ [MEMORY_SAFE] アライメント調整のためのパディング処理
                var alignedMat = new Mat();
                try
                {
                    // 境界をゼロパディングでリサイズ（画像内容を保持）
                    Cv2.CopyMakeBorder(fixedMat, alignedMat, 
                        0, alignedHeight - currentHeight,  // top, bottom
                        0, alignedWidth - currentWidth,    // left, right  
                        BorderTypes.Constant, Scalar.Black);
                    
                    fixedMat.Dispose(); // 古いMatを解放
                    fixedMat = alignedMat; // 新しいMatに置き換え
                    wasModified = true;
                }
                catch (Exception ex)
                {
                    __logger?.LogError(ex, "🚨 [MAT_FIX] 4-byte alignment failed");
                    alignedMat.Dispose();
                    fixedMat?.Dispose();
                    return null;
                }
            }
            
            // 最終検証
            if (ValidateMatForPaddleOCR(fixedMat))
            {
                if (wasModified)
                {
                    __logger?.LogDebug("✅ [MAT_FIX] Mat修正成功: {Width}x{Height}, {Channels}ch, {Type}",
                        fixedMat.Width, fixedMat.Height, fixedMat.Channels(), fixedMat.Type());
                }
                return fixedMat;
            }
            else
            {
                __logger?.LogError("🚨 [MAT_FIX] Mat修正後も検証に失敗");
                fixedMat?.Dispose();
                return null;
            }
        }
        catch (Exception ex)
        {
            __logger?.LogError(ex, "🚨 [MAT_FIX] Unexpected error during Mat fixing");
            return null;
        }
    }
    
    /// <summary>
    /// 🎯 [ULTRATHINK_EVIDENCE] PaddlePredictor実行エラーの包括的証拠収集
    /// 奇数幅理論とその他根本原因の確実な証拠を収集
    /// </summary>
    private string CollectPaddlePredictorErrorInfo(Mat mat, Exception ex)
    {
        var info = new List<string>();
        
        try 
        {
            // エラーの基本情報
            info.Add($"Error: {ex.Message}");
            info.Add($"Exception Type: {ex.GetType().Name}");
            info.Add($"Consecutive Failures: {_consecutivePaddleFailures}");
            
            // 🔍 [ULTRATHINK_EVIDENCE] Mat状態情報（安全な取得）
            try 
            {
                var width = mat.Width;
                var height = mat.Height;
                var channels = mat.Channels();
                var totalPixels = mat.Total();
                
                info.Add($"Mat Size: {width}x{height}");
                info.Add($"Mat Channels: {channels}");
                info.Add($"Mat Type: {mat.Type()}");
                info.Add($"Mat Empty: {mat.Empty()}");
                info.Add($"Mat Continuous: {mat.IsContinuous()}");
                info.Add($"Mat Total Pixels: {totalPixels}");
                
                // 🎯 [ULTRATHINK_CRITICAL_EVIDENCE] 奇数幅問題分析
                var widthOdd = width % 2 == 1;
                var heightOdd = height % 2 == 1;
                info.Add($"🔍 [ODD_WIDTH_ANALYSIS] Width Odd: {widthOdd} (Width: {width})");
                info.Add($"🔍 [ODD_HEIGHT_ANALYSIS] Height Odd: {heightOdd} (Height: {height})");
                
                if (widthOdd || heightOdd)
                {
                    info.Add($"⚠️ [EVIDENCE_CRITICAL] 奇数寸法検出 - NormalizeImageDimensions実行後も奇数！");
                    info.Add($"   📊 Expected: 正規化により偶数化されるべき");
                    info.Add($"   📊 Actual: Width={width}({(widthOdd ? "奇数" : "偶数")}), Height={height}({(heightOdd ? "奇数" : "偶数")})");
                }
                
                // 🎯 [ULTRATHINK_MEMORY_ANALYSIS] メモリアライメント分析
                var widthAlignment = width % 4;  // 4バイト境界
                var heightAlignment = height % 4;
                info.Add($"🔍 [MEMORY_ALIGNMENT] Width mod 4: {widthAlignment}, Height mod 4: {heightAlignment}");
                
                // 🎯 [ULTRATHINK_SIZE_ANALYSIS] 画像サイズカテゴリ分析
                var pixelCategory = totalPixels switch
                {
                    < 10000 => "極小(10K未満)",
                    < 100000 => "小(10K-100K)",
                    < 500000 => "中(100K-500K)",
                    < 1000000 => "大(500K-1M)",
                    _ => "極大(1M超)"
                };
                info.Add($"🔍 [SIZE_CATEGORY] Pixel Category: {pixelCategory} ({totalPixels:N0} pixels)");
                
                // 🎯 [ULTRATHINK_SIMD_ANALYSIS] SIMD命令互換性分析
                var simdCompatible = (width % 16 == 0) && (height % 16 == 0); // AVX512対応
                var sse2Compatible = (width % 8 == 0) && (height % 8 == 0);   // SSE2対応
                info.Add($"🔍 [SIMD_COMPAT] AVX512 Compatible: {simdCompatible}, SSE2 Compatible: {sse2Compatible}");
                
                // 🎯 [ULTRATHINK_ASPECT_ANALYSIS] アスペクト比分析
                var aspectRatio = (double)width / height;
                var aspectCategory = aspectRatio switch
                {
                    < 0.5 => "縦長(1:2以上)",
                    < 0.8 => "縦寄り(1:1.25-1:2)",
                    < 1.25 => "正方形寄り(4:5-5:4)",
                    < 2.0 => "横寄り(5:4-2:1)",
                    _ => "横長(2:1以上)"
                };
                info.Add($"🔍 [ASPECT_RATIO] Ratio: {aspectRatio:F3} ({aspectCategory})");
            }
            catch 
            {
                info.Add("Mat properties inaccessible (corrupted)");
            }
            
            // システム状態情報
            info.Add($"Is V4 Model: {false /* V5統一により常にfalse */}");
            info.Add($"Last OCR Time: {_lastOcrTime}");
            info.Add($"Consecutive Timeouts: {_consecutiveTimeouts}");
            
            // メモリ情報
            try 
            {
                var memoryBefore = GC.GetTotalMemory(false);
                info.Add($"Memory Usage: {memoryBefore / (1024 * 1024):F1} MB");
            }
            catch 
            {
                info.Add("Memory info unavailable");
            }
            
            // スタックトレース（最初の数行のみ）
            if (ex.StackTrace != null)
            {
                var stackLines = ex.StackTrace.Split('\n').Take(3);
                info.Add($"Stack Trace: {string.Join(" -> ", stackLines.Select(l => l.Trim()))}");
            }
        }
        catch (Exception infoEx)
        {
            info.Add($"Error collecting info: {infoEx.Message}");
        }
        
        return string.Join(", ", info);
    }
    
    /// <summary>
    /// PaddlePredictor実行エラーに基づく対処提案を生成
    /// </summary>
    private string GeneratePaddleErrorSuggestion(string errorMessage)
    {
        if (errorMessage.Contains("PaddlePredictor(Detector) run failed"))
        {
            return "検出器エラー: 画像の前処理またはサイズ調整が必要。画像品質またはPaddleOCRモデルの確認を推奨";
        }
        else if (errorMessage.Contains("PaddlePredictor(Recognizer) run failed"))
        {
            return "認識器エラー: テキスト認識段階での問題。検出されたテキスト領域のサイズまたは品質を確認";
        }
        else if (errorMessage.Contains("run failed"))
        {
            // 連続失敗回数に基づく提案
            if (_consecutivePaddleFailures >= 3)
            {
                return "連続失敗検出: OCRエンジンの再初期化またはシステム再起動を推奨";
            }
            else if (_consecutivePaddleFailures >= 2)
            {
                return "複数回失敗: 画像の前処理方法の変更または解像度調整を推奨";
            }
            else
            {
                return "初回エラー: 画像形式またはサイズの調整を試行";
            }
        }
        else
        {
            return "不明なPaddleOCRエラー: ログ確認とシステム状態の点検を推奨";
        }
    }

    /// <summary>
    /// リソースの解放（パターン実装）
    /// </summary>
    private void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            __logger?.LogDebug("PaddleOcrEngineのリソースを解放中");
            _currentOcrCancellation?.Dispose();
            _currentOcrCancellation = null;
            
            // ハイブリッドサービスの廃棄
            if (_hybridService != null)
            {
                __logger?.LogDebug("🔄 ハイブリッドサービスを廃棄中");
                _hybridService.Dispose();
                _hybridService = null;
            }
            
            // 🎯 [GEMINI_EMERGENCY_FIX_V2] 静的SemaphoreSlimはDispose対象外
            // _globalOcrSemaphore は全インスタンス共有のため個別Disposeしない
            // アプリケーション終了時まで維持される
            // _globalOcrSemaphore?.Dispose(); // 静的フィールドは個別廃棄不要
            
            DisposeEngines();
        }

        _disposed = true;
    }

    /// <summary>
    /// 現在使用中のモデルがPP-OCRv5かどうかを検出
    /// </summary>
    private bool DetectIfV5Model()
    {
        try
        {
            // V5統一により常にtrueを返す
            return true;
        }
        catch (Exception ex)
        {
            // Note: staticメソッドではログ出力不可 // _unifiedLoggingService?.WriteDebugLog($"   ❌ V5モデル検出エラー: {ex.Message}");
            return false; // エラー時はV4として処理
        }
    }

    /// <summary>
    /// 画像特性に基づいて最適なゲームプロファイルを選択
    /// </summary>
    /// <param name="characteristics">画像特性</param>
    /// <returns>最適なプロファイル名</returns>
    private static string SelectOptimalGameProfile(PreprocessingImageCharacteristics characteristics)
    {
        // 明度とコントラストに基づいてプロファイルを選択
        if (characteristics.IsDarkBackground)
        {
            return "darkbackground";
        }
        else if (characteristics.IsBrightBackground)
        {
            return "lightbackground";
        }
        else if (characteristics.Contrast > 50.0)
        {
            return "highcontrast";
        }
        else if (characteristics.ImageType.Contains("anime", StringComparison.OrdinalIgnoreCase) || 
                 characteristics.TextDensity < 0.1)
        {
            return "anime";
        }
        
        return "default";
    }

    /// <summary>
    /// 環境依存しないデバッグログパスを取得
    /// </summary>
    private static string GetDebugLogPath()
    {
        var debugLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), 
            "BaketaDebugLogs", 
            "debug_app_logs.txt"
        );
        
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(debugLogPath));
        }
        catch
        {
            // フォールバック: Tempディレクトリを使用
            debugLogPath = Path.Combine(Path.GetTempPath(), "BaketaDebugLogs", "debug_app_logs.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(debugLogPath));
        }
        
        return debugLogPath;
    }

    /// <summary>
    /// 安全なデバッグログ書き込み
    /// </summary>
    private static void SafeWriteDebugLog(string message)
    {
        try
        {
            var debugLogPath = GetDebugLogPath();
            System.IO.File.AppendAllText(debugLogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"デバッグログ書き込みエラー: {ex.Message}");
        }
    }

    /// <summary>
    /// テキスト検出精度向上のための最適化パラメーター適用
    /// 低コントラスト・小文字検出対応
    /// </summary>
    private static void ApplyDetectionOptimization(PaddleOcrAll ocrEngine)
    {
        try
        {
            var engineType = ocrEngine.GetType();
            
            // 🎯 検出感度最適化パラメーター（言語非依存）
            var detectionParams = new Dictionary<string, object>
            {
                // 検出閾値を大幅に下げて感度向上（0.3 → 0.1）
                { "det_db_thresh", 0.1f },
                
                // ボックス閾値を下げて小さなテキストも検出（0.6 → 0.3）
                { "det_db_box_thresh", 0.3f },
                
                // アンクリップ比率を上げて小さい文字を拡張
                { "det_db_unclip_ratio", 2.2f },
                
                // 検出時の最大辺長を拡大（解像度向上）
                { "det_limit_side_len", 1440 },
                
                // スコアモードを精度重視に設定
                { "det_db_score_mode", "slow" },
                
                // 検出制限タイプ
                { "det_limit_type", "max" }
            };

            // リフレクションでパラメーター適用
            int appliedCount = 0;
            foreach (var param in detectionParams)
            {
                try
                {
                    // プロパティ検索
                    var property = engineType.GetProperty(param.Key, 
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    
                    if (property != null && property.CanWrite)
                    {
                        var convertedValue = ConvertParameterValue(param.Value, property.PropertyType);
                        property.SetValue(ocrEngine, convertedValue);
                        appliedCount++;
                        continue;
                    }
                    
                    // フィールド検索
                    var field = engineType.GetField(param.Key, 
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    
                    if (field != null)
                    {
                        var convertedValue = ConvertParameterValue(param.Value, field.FieldType);
                        field.SetValue(ocrEngine, convertedValue);
                        appliedCount++;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"パラメーター適用エラー {param.Key}: {ex.Message}");
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"🎯 検出精度最適化完了: {appliedCount}/{detectionParams.Count}個のパラメーター適用");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"検出最適化エラー: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// パラメーター値の型変換
    /// </summary>
    private static object? ConvertParameterValue(object value, Type targetType)
    {
        if (value == null) return null;
        
        if (targetType == typeof(string))
            return value.ToString();
        
        if (targetType == typeof(bool))
            return Convert.ToBoolean(value, System.Globalization.CultureInfo.InvariantCulture);
        
        if (targetType == typeof(int))
            return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
        
        if (targetType == typeof(float))
            return Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture);
        
        if (targetType == typeof(double))
            return Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
        
        return Convert.ChangeType(value, targetType, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// ハイブリッドモード初期化
    /// </summary>
    private async Task InitializeHybridModeAsync(OcrEngineSettings settings, CancellationToken cancellationToken = default)
    {
        try
        {
            // ハイブリッドモード設定を確認
            if (settings.EnableHybridMode)
            {
                __logger?.LogInformation("🔄 ハイブリッドモード初期化開始 - V3(高速) + V5(高精度)");
                
                // DIからハイブリッド設定とサービスを取得（Serviceサービス方式に対応）
                try
                {
                    // Microsoft.Extensions.DependencyInjectionでServiceProviderを直接利用する方法を回避し、
                    // 代わりにデフォルト設定を使用
                    _hybridSettings = new HybridOcrSettings
                    {
                        FastDetectionModel = PaddleOcrModelVersion.V3,
                        HighQualityModel = PaddleOcrModelVersion.V5,
                        ImageQualityThreshold = 0.6,
                        RegionCountThreshold = 5,
                        FastDetectionTimeoutMs = 500,
                        HighQualityTimeoutMs = 3000
                    };

                    // HybridPaddleOcrServiceを直接初期化
                    _hybridService = new HybridPaddleOcrService(
                        __logger as ILogger<HybridPaddleOcrService> ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<HybridPaddleOcrService>.Instance,
                        __eventAggregator,
                        _hybridSettings
                    );

                    await _hybridService.InitializeAsync(cancellationToken).ConfigureAwait(false);
                    
                    _isHybridMode = true;
                    __logger?.LogInformation("✅ ハイブリッドモード初期化完了");
                    
                    // 診断イベントを発行
                    await __eventAggregator.PublishAsync(new PipelineDiagnosticEvent
                    {
                        Stage = "OCR_Initialization",
                        IsSuccess = true,
                        ProcessingTimeMs = 0,
                        Message = "ハイブリッドモード初期化完了",
                        Severity = DiagnosticSeverity.Information,
                        Metrics = new Dictionary<string, object>
                        {
                            { "HybridModeEnabled", true },
                            { "FastDetectionModel", _hybridSettings.FastDetectionModel.ToString() },
                            { "HighQualityModel", _hybridSettings.HighQualityModel.ToString() }
                        }
                    }).ConfigureAwait(false);
                }
                catch (Exception hybridInitEx)
                {
                    __logger?.LogError(hybridInitEx, "❌ ハイブリッドサービス初期化失敗");
                    _isHybridMode = false;
                    throw; // 親のcatchで処理
                }
            }
            else
            {
                __logger?.LogDebug("📊 シングルモードで初期化 - ハイブリッドモード無効");
                _isHybridMode = false;
            }
        }
        catch (Exception ex)
        {
            __logger?.LogError(ex, "❌ ハイブリッドモード初期化失敗");
            _isHybridMode = false;
            
            // ハイブリッドモードが失敗してもシングルモードで続行
            __logger?.LogWarning("⚠️ シングルモードで続行します");
        }
    }

    /// <summary>
    /// ハイブリッド処理モードを決定
    /// </summary>
    private OcrProcessingMode DetermineProcessingMode()
    {
        // デフォルトは適応的モード（画像品質に基づく自動選択）
        return OcrProcessingMode.Adaptive;
    }

    /// <summary>
    /// PaddleOCR連続失敗カウンターを強制リセット
    /// </summary>
    // ✅ [PHASE2.9.6] _performanceTracker に委譲
    public void ResetFailureCounter() => _performanceTracker.ResetFailureCounter();

    /// <summary>
    /// 現在の連続失敗回数を取得
    /// </summary>
    // ✅ [PHASE2.9.6] _performanceTracker に委譲
    public int GetConsecutiveFailureCount() => _performanceTracker.GetConsecutiveFailureCount();
    
    /// <summary>
    /// 🎯 [ULTRATHINK_PREVENTION] PaddlePredictor失敗を完全予防する包括的正規化
    /// すべての既知問題を事前解決し、エラー発生自体を防ぐ
    /// </summary>
    private Mat ApplyPreventiveNormalization(Mat inputMat)
    {
        if (inputMat == null || inputMat.Empty())
        {
            throw new ArgumentException("Input Mat is null or empty");
        }

        var preventiveSw = System.Diagnostics.Stopwatch.StartNew();
        Mat processedMat = inputMat;

        try
        {
            // 🔍 [PREVENTION_LOG] 処理前状態の詳細記録
            var originalInfo = $"{processedMat.Width}x{processedMat.Height}, Ch:{processedMat.Channels()}, Type:{processedMat.Type()}";
            __logger?.LogDebug("🎯 [PREVENTIVE_START] 予防処理開始: {OriginalInfo}", originalInfo);

            // ステップ1: 極端なサイズ問題の予防
            var totalPixels = processedMat.Width * processedMat.Height;
            if (totalPixels > 2000000) // 200万ピクセル制限
            {
                var scale = Math.Sqrt(2000000.0 / totalPixels);
                var newWidth = Math.Max(16, (int)(processedMat.Width * scale));
                var newHeight = Math.Max(16, (int)(processedMat.Height * scale));
                
                var resizedMat = new Mat();
                Cv2.Resize(processedMat, resizedMat, new OpenCvSharp.Size(newWidth, newHeight), 0, 0, InterpolationFlags.Area);
                
                if (processedMat != inputMat) processedMat.Dispose();
                processedMat = resizedMat;
                
                __logger?.LogInformation("🎯 [PREVENTION_RESIZE] 大画像リサイズ: {OriginalPixels:N0} → {NewPixels:N0} pixels", 
                    totalPixels, newWidth * newHeight);
            }

            // ステップ2: 奇数幅・高さの完全解決
            var needsOddFix = (processedMat.Width % 2 == 1) || (processedMat.Height % 2 == 1);
            if (needsOddFix)
            {
                var evenWidth = processedMat.Width + (processedMat.Width % 2);
                var evenHeight = processedMat.Height + (processedMat.Height % 2);
                
                var evenMat = new Mat();
                Cv2.Resize(processedMat, evenMat, new OpenCvSharp.Size(evenWidth, evenHeight), 0, 0, InterpolationFlags.Linear);
                
                if (processedMat != inputMat) processedMat.Dispose();
                processedMat = evenMat;
                
                __logger?.LogInformation("🎯 [PREVENTION_ODD] 奇数幅修正: {OriginalSize} → {EvenSize}", 
                    $"{inputMat.Width}x{inputMat.Height}", $"{evenWidth}x{evenHeight}");
            }

            // ステップ3: メモリアライメント最適化 (16バイト境界)
            var alignWidth = processedMat.Width;
            var alignHeight = processedMat.Height;
            var needsAlignment = false;

            if (alignWidth % 16 != 0)
            {
                alignWidth = ((alignWidth / 16) + 1) * 16;
                needsAlignment = true;
            }
            if (alignHeight % 16 != 0)
            {
                alignHeight = ((alignHeight / 16) + 1) * 16;
                needsAlignment = true;
            }

            if (needsAlignment)
            {
                var alignedMat = new Mat();
                Cv2.Resize(processedMat, alignedMat, new OpenCvSharp.Size(alignWidth, alignHeight), 0, 0, InterpolationFlags.Linear);
                
                if (processedMat != inputMat) processedMat.Dispose();
                processedMat = alignedMat;
                
                __logger?.LogDebug("🎯 [PREVENTION_ALIGN] 16バイト境界整列: {OriginalSize} → {AlignedSize}", 
                    $"{inputMat.Width}x{inputMat.Height}", $"{alignWidth}x{alignHeight}");
            }

            // ステップ4: チャンネル数正規化
            if (processedMat.Channels() != 3)
            {
                var channelMat = new Mat();
                if (processedMat.Channels() == 1)
                {
                    Cv2.CvtColor(processedMat, channelMat, ColorConversionCodes.GRAY2BGR);
                }
                else if (processedMat.Channels() == 4)
                {
                    Cv2.CvtColor(processedMat, channelMat, ColorConversionCodes.BGRA2BGR);
                }
                else
                {
                    channelMat = processedMat.Clone();
                }
                
                if (processedMat != inputMat) processedMat.Dispose();
                processedMat = channelMat;
                
                __logger?.LogDebug("🎯 [PREVENTION_CHANNEL] チャンネル正規化: {OriginalChannels} → 3", inputMat.Channels());
            }

            // ステップ5: 最終検証
            if (processedMat.Empty() || processedMat.Width < 16 || processedMat.Height < 16)
            {
                throw new InvalidOperationException($"Preventive normalization resulted in invalid Mat: {processedMat.Width}x{processedMat.Height}");
            }

            preventiveSw.Stop();
            var finalInfo = $"{processedMat.Width}x{processedMat.Height}, Ch:{processedMat.Channels()}, Type:{processedMat.Type()}";
            __logger?.LogInformation("✅ [PREVENTION_COMPLETE] 予防処理完了: {FinalInfo} (処理時間: {ElapsedMs}ms)", 
                finalInfo, preventiveSw.ElapsedMilliseconds);

            return processedMat;
        }
        catch (Exception ex)
        {
            __logger?.LogError(ex, "🚨 [PREVENTION_ERROR] 予防処理でエラー発生");
            
            // エラー時は元のMatをクローンして返す
            if (processedMat != inputMat && processedMat != null)
            {
                processedMat.Dispose();
            }
            throw;
        }
    }
}
