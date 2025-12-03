using System.Buffers;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using Baketa.Core.Abstractions.GPU;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Models.OCR;
using Baketa.Infrastructure.OCR.GPU;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using Baketa.Infrastructure.OCR.Preprocessing;

// 型エイリアス: System.DrawingとOpenCvSharpの曖昧さ解消
using DrawingPoint = System.Drawing.Point;
using CvPoint = OpenCvSharp.Point;

namespace Baketa.Infrastructure.OCR.ONNX;

/// <summary>
/// ONNX Runtime ベースの OCR エンジン実装
/// Issue #181: GPU/CPU 自動切り替え対応
/// PP-OCRv5 ONNX モデルを使用した推論エンジン
/// </summary>
public sealed class OnnxOcrEngine : IOcrEngine
{
    #region PP-OCRv5 モデル定数

    // === 検出モデル（DBNet）前処理定数 ===
    /// <summary>検出モデルの入力画像サイズ（正方形）</summary>
    private const int DetectionTargetSize = 960;

    /// <summary>パディング用アライメント（32の倍数に調整）</summary>
    private const int PaddingAlignment = 32;

    // ImageNet正規化パラメータ（RGB順）
    /// <summary>ImageNet正規化 - 平均値 (R, G, B)</summary>
    private static readonly float[] ImageNetMean = [0.485f, 0.456f, 0.406f];

    /// <summary>ImageNet正規化 - 標準偏差 (R, G, B)</summary>
    private static readonly float[] ImageNetStd = [0.229f, 0.224f, 0.225f];

    // === 認識モデル前処理定数 ===
    /// <summary>認識モデルの入力高さ（固定） - PP-OCRv5は48を使用</summary>
    private const int RecognitionTargetHeight = 48;

    /// <summary>認識モデルの最大入力幅</summary>
    private const int RecognitionMaxWidth = 320;

    /// <summary>認識モデル正規化パラメータ（平均・標準偏差共通）</summary>
    private const float RecognitionNormFactor = 0.5f;

    // === 後処理定数 ===
    /// <summary>検出領域の最小面積（ピクセル^2）</summary>
    private const double MinContourArea = 100.0;

    /// <summary>縦書き判定の高さ/幅比閾値</summary>
    private const double VerticalTextRatio = 1.5;

    #endregion

    private readonly IUnifiedGpuOptimizer _gpuOptimizer;
    private readonly IPpOcrv5ModelConfiguration _modelConfig;
    private readonly ILogger<OnnxOcrEngine> _logger;

    private InferenceSession? _detectionSession;
    private InferenceSession? _recognitionSession;
    private OcrEngineSettings _settings = new();

    private readonly object _sessionLock = new();
    private bool _isInitialized;
    private bool _disposed;

    // パフォーマンス統計
    private int _totalProcessedImages;
    private double _totalProcessingTimeMs;
    private double _minProcessingTimeMs = double.MaxValue;
    private double _maxProcessingTimeMs;
    private int _errorCount;
    private int _consecutiveFailureCount;
    private DateTime _startTime = DateTime.UtcNow;

    // 辞書（認識用文字リスト）
    private string[]? _characterDictionary;

    public string EngineName => "ONNX PP-OCRv5";
    public string EngineVersion => "1.0.0";
    public bool IsInitialized => _isInitialized;
    public string? CurrentLanguage => _settings.Language;

    public OnnxOcrEngine(
        IUnifiedGpuOptimizer gpuOptimizer,
        IPpOcrv5ModelConfiguration modelConfig,
        ILogger<OnnxOcrEngine> logger)
    {
        _gpuOptimizer = gpuOptimizer ?? throw new ArgumentNullException(nameof(gpuOptimizer));
        _modelConfig = modelConfig ?? throw new ArgumentNullException(nameof(modelConfig));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> InitializeAsync(OcrEngineSettings? settings = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_isInitialized)
        {
            _logger.LogDebug("ONNX OCR エンジンは既に初期化されています");
            return true;
        }

        _settings = settings?.Clone() ?? new OcrEngineSettings();

        try
        {
            _logger.LogInformation("ONNX OCR エンジン初期化開始");

            // GPU環境に応じた最適なSessionOptionsを取得
            // Issue #181: GPU初期化失敗時はCPUにフォールバック
            SessionOptions sessionOptions;
            try
            {
                sessionOptions = await _gpuOptimizer.CreateOptimalSessionOptionsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception gpuEx)
            {
                _logger.LogWarning(gpuEx, "GPU SessionOptions作成失敗、CPUフォールバックを使用");
                Console.WriteLine($"⚠️ [Issue #181] GPU初期化失敗: {gpuEx.Message}");
                Console.WriteLine("   → CPUフォールバックを使用します");
                sessionOptions = CreateCpuFallbackSessionOptions();
            }

            // 検出モデルのロード
            var detModelPath = _modelConfig.GetDetectionModelPath();
            if (!File.Exists(detModelPath))
            {
                _logger.LogWarning("検出モデルが見つかりません: {Path}。モデルをダウンロードしてください。", detModelPath);
                // モデルがない場合はダミー初期化（後でモデルダウンロード機能を追加）
                _isInitialized = false;
                return false;
            }

            _detectionSession = new InferenceSession(detModelPath, sessionOptions);
            _logger.LogInformation("検出モデルをロードしました: {Path}", detModelPath);

            // 認識モデルのロード
            var recModelPath = _modelConfig.GetRecognitionModelPath(_settings.Language);
            if (!File.Exists(recModelPath))
            {
                _logger.LogWarning("認識モデルが見つかりません: {Path}", recModelPath);
                _isInitialized = false;
                return false;
            }

            _recognitionSession = new InferenceSession(recModelPath, sessionOptions);
            _logger.LogInformation("認識モデルをロードしました: {Path}", recModelPath);

            // 文字辞書のロード
            var dictPath = _modelConfig.GetDictionaryPath(_settings.Language);
            if (File.Exists(dictPath))
            {
                _characterDictionary = await File.ReadAllLinesAsync(dictPath, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("文字辞書をロードしました: {Count} 文字", _characterDictionary.Length);
            }
            else
            {
                _logger.LogWarning("文字辞書が見つかりません: {Path}", dictPath);
                _characterDictionary = [];
            }

            _isInitialized = true;
            _startTime = DateTime.UtcNow;
            _logger.LogInformation("ONNX OCR エンジン初期化完了");

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ONNX OCR エンジン初期化失敗");
            _isInitialized = false;
            return false;
        }
    }

    public async Task<bool> WarmupAsync(CancellationToken cancellationToken = default)
    {
        if (!_isInitialized)
        {
            _logger.LogWarning("エンジンが初期化されていないためウォームアップをスキップします");
            return false;
        }

        try
        {
            _logger.LogInformation("ONNX OCR エンジンウォームアップ開始");

            // ダミー画像でウォームアップ
            // PP-OCRv5 認識モデルは高さ48pxを期待する（CRNNアーキテクチャ標準）
            using var dummyImage = new Mat(48, 100, MatType.CV_8UC3, Scalar.White);
            var inputTensor = PreprocessImageForRecognition(dummyImage);

            if (_recognitionSession != null)
            {
                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("x", inputTensor)
                };

                using var results = _recognitionSession.Run(inputs);
                _logger.LogInformation("ウォームアップ完了");
            }

            await Task.CompletedTask.ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ウォームアップ中にエラーが発生しました");
            return false;
        }
    }

    public async Task<OcrResults> RecognizeAsync(
        IImage image,
        IProgress<OcrProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        return await RecognizeAsync(image, null, progressCallback, cancellationToken).ConfigureAwait(false);
    }

    public async Task<OcrResults> RecognizeAsync(
        IImage image,
        Rectangle? regionOfInterest,
        IProgress<OcrProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        var context = new OcrContext(image, IntPtr.Zero, regionOfInterest, cancellationToken);
        return await RecognizeAsync(context, progressCallback).ConfigureAwait(false);
    }

    public async Task<OcrResults> RecognizeAsync(
        OcrContext context,
        IProgress<OcrProgress>? progressCallback = null)
    {
        // 🔍 [ONNX_DIAG] RecognizeAsync呼び出し確認
        Console.WriteLine($"🔍 [ONNX_DIAG] OnnxOcrEngine.RecognizeAsync 開始 - IsInitialized: {_isInitialized}, Time: {DateTime.Now:HH:mm:ss.fff}");
        _logger.LogInformation("🔍 [ONNX_DIAG] OnnxOcrEngine.RecognizeAsync 開始 - IsInitialized: {IsInit}", _isInitialized);

        if (!_isInitialized)
            throw new InvalidOperationException("OCR エンジンが初期化されていません");

        ObjectDisposedException.ThrowIf(_disposed, this);

        var stopwatch = Stopwatch.StartNew();
        var textRegions = new List<OcrTextRegion>();

        try
        {
            progressCallback?.Report(new OcrProgress(0.1, "前処理中") { Phase = OcrPhase.Preprocessing });

            // 画像をMatに変換
            using var mat = ConvertToMat(context.Image, context.CaptureRegion);

            if (mat.Empty())
            {
                _logger.LogWarning("変換後の画像が空です");
                return CreateEmptyResult(context.Image, context.CaptureRegion, stopwatch.Elapsed);
            }

            // 🇯🇵 [PREPROCESS] PP-OCRv5日本語最適化前処理（現在無効化 - 精度検証中）
            Mat? matForOcr = mat;  // 前処理なしで元画像を使用
            var shouldDisposeMatForOcr = false;

            _logger.LogInformation("🔍 [PREPROCESS_DISABLED] 前処理無効化中 - 素の画像でOCR実行 (ONNX)");
            Console.WriteLine($"🔍 [PREPROCESS_DISABLED] 前処理無効化中 - 素の画像でOCR実行 (ONNX) {DateTime.Now:HH:mm:ss.fff}");

            // TODO: 前処理の効果を検証後、以下を有効化または調整
            /*
            _logger.LogInformation("🇯🇵 [PREPROCESS] PP-OCRv5日本語最適化前処理を適用中 (ONNX)...");
            Console.WriteLine($"🇯🇵 [PREPROCESS] PP-OCRv5日本語最適化前処理を適用中 (ONNX)... {DateTime.Now:HH:mm:ss.fff}");

            try
            {
                var preprocessedMat = PPOCRv5Preprocessor.ProcessGameImageForV5(mat, "jpn");
                if (preprocessedMat != null && !preprocessedMat.Empty())
                {
                    matForOcr = preprocessedMat;
                    shouldDisposeMatForOcr = true;
                    _logger.LogInformation("🇯🇵 [PREPROCESS] 前処理完了 (ONNX) - サイズ: {Width}x{Height}", preprocessedMat.Width, preprocessedMat.Height);
                    Console.WriteLine($"🇯🇵 [PREPROCESS] 前処理完了 (ONNX) - サイズ: {preprocessedMat.Width}x{preprocessedMat.Height}");
                }
                else
                {
                    _logger.LogWarning("🇯🇵 [PREPROCESS] 前処理結果が無効、元画像でOCR実行 (ONNX)");
                    matForOcr = mat;
                }
            }
            catch (Exception preprocessEx)
            {
                _logger.LogWarning(preprocessEx, "🇯🇵 [PREPROCESS] 前処理でエラー、元画像でOCR実行 (ONNX)");
                matForOcr = mat;
            }
            */

            progressCallback?.Report(new OcrProgress(0.3, "テキスト検出中") { Phase = OcrPhase.TextDetection });

            try
            {
                // テキスト検出（前処理済み画像を使用）
                var detectedBoxes = await DetectTextAsync(matForOcr!, context.CancellationToken).ConfigureAwait(false);

                if (detectedBoxes.Count == 0)
                {
                    _logger.LogDebug("テキスト領域が検出されませんでした");
                    return CreateEmptyResult(context.Image, context.CaptureRegion, stopwatch.Elapsed);
                }

                _logger.LogDebug("{Count} 個のテキスト領域を検出", detectedBoxes.Count);

                progressCallback?.Report(new OcrProgress(0.5, "テキスト認識中") { Phase = OcrPhase.TextRecognition });

                // テキスト認識（前処理済み画像を使用）
                for (int i = 0; i < detectedBoxes.Count; i++)
                {
                    context.CancellationToken.ThrowIfCancellationRequested();

                    var box = detectedBoxes[i];
                    var (text, confidence) = await RecognizeTextInRegionAsync(matForOcr!, box, context.CancellationToken).ConfigureAwait(false);

                    if (!string.IsNullOrWhiteSpace(text) && confidence >= _settings.RecognitionThreshold)
                    {
                        var bounds = GetBoundingRect(box);

                        // ROI座標変換
                        if (context.HasCaptureRegion)
                        {
                            bounds = new Rectangle(
                                bounds.X + context.CaptureRegion!.Value.X,
                                bounds.Y + context.CaptureRegion!.Value.Y,
                                bounds.Width,
                                bounds.Height);
                        }

                        textRegions.Add(new OcrTextRegion(
                            text,
                            bounds,
                            confidence,
                            box,
                            DetectTextDirection(box)));
                    }

                    progressCallback?.Report(new OcrProgress(
                        0.5 + (0.4 * (i + 1) / detectedBoxes.Count),
                        $"認識中 ({i + 1}/{detectedBoxes.Count})")
                    { Phase = OcrPhase.TextRecognition });
                }

                progressCallback?.Report(new OcrProgress(0.95, "後処理中") { Phase = OcrPhase.PostProcessing });

                stopwatch.Stop();
                UpdatePerformanceStats(stopwatch.Elapsed.TotalMilliseconds, true);

                progressCallback?.Report(new OcrProgress(1.0, "完了") { Phase = OcrPhase.Completed });

                _logger.LogInformation("OCR完了: {Count} 領域, {Time}ms", textRegions.Count, stopwatch.ElapsedMilliseconds);

                return new OcrResults(
                    textRegions,
                    context.Image,
                    stopwatch.Elapsed,
                    _settings.Language,
                    context.CaptureRegion);
            }
            finally
            {
                // 前処理画像のリソース破棄
                if (shouldDisposeMatForOcr && matForOcr != null)
                {
                    matForOcr.Dispose();
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("OCR処理がキャンセルされました");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OCR処理中にエラーが発生");
            UpdatePerformanceStats(stopwatch.Elapsed.TotalMilliseconds, false);
            throw new OcrException("OCR処理に失敗しました", ex);
        }
    }

    public Task<OcrResults> DetectTextRegionsAsync(IImage image, CancellationToken cancellationToken = default)
    {
        // テキスト検出のみ（認識なし）
        return RecognizeAsync(image, null, null, cancellationToken);
    }

    public OcrEngineSettings GetSettings() => _settings.Clone();

    public async Task ApplySettingsAsync(OcrEngineSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.IsValid())
            throw new ArgumentException("無効な設定です", nameof(settings));

        var languageChanged = _settings.Language != settings.Language;
        _settings = settings.Clone();

        if (languageChanged && _isInitialized)
        {
            _logger.LogInformation("言語設定が変更されたため、認識モデルを再ロードします");

            // 認識モデルの再ロード
            var recModelPath = _modelConfig.GetRecognitionModelPath(_settings.Language);
            if (File.Exists(recModelPath))
            {
                var sessionOptions = await _gpuOptimizer.CreateOptimalSessionOptionsAsync(cancellationToken).ConfigureAwait(false);

                lock (_sessionLock)
                {
                    _recognitionSession?.Dispose();
                    _recognitionSession = new InferenceSession(recModelPath, sessionOptions);
                }

                // 辞書の再ロード
                var dictPath = _modelConfig.GetDictionaryPath(_settings.Language);
                if (File.Exists(dictPath))
                {
                    _characterDictionary = await File.ReadAllLinesAsync(dictPath, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    public IReadOnlyList<string> GetAvailableLanguages()
    {
        return ["jpn", "eng", "chi_sim"];
    }

    public IReadOnlyList<string> GetAvailableModels()
    {
        return ["ppocrv5-onnx"];
    }

    public async Task<bool> IsLanguageAvailableAsync(string languageCode, CancellationToken cancellationToken = default)
    {
        var modelPath = _modelConfig.GetRecognitionModelPath(languageCode);
        await Task.CompletedTask.ConfigureAwait(false);
        return File.Exists(modelPath);
    }

    public OcrPerformanceStats GetPerformanceStats()
    {
        var totalImages = _totalProcessedImages + _errorCount;
        return new OcrPerformanceStats
        {
            TotalProcessedImages = totalImages,
            AverageProcessingTimeMs = totalImages > 0 ? _totalProcessingTimeMs / totalImages : 0,
            MinProcessingTimeMs = _minProcessingTimeMs == double.MaxValue ? 0 : _minProcessingTimeMs,
            MaxProcessingTimeMs = _maxProcessingTimeMs,
            ErrorCount = _errorCount,
            SuccessRate = totalImages > 0 ? (double)_totalProcessedImages / totalImages : 0,
            StartTime = _startTime,
            LastUpdateTime = DateTime.UtcNow
        };
    }

    public void CancelCurrentOcrTimeout()
    {
        // ONNX Runtimeはタイムアウト管理が異なるため、ここでは何もしない
    }

    public int GetConsecutiveFailureCount() => _consecutiveFailureCount;

    public void ResetFailureCounter()
    {
        _consecutiveFailureCount = 0;
    }

    #region Private Methods

    /// <summary>
    /// GPU初期化失敗時のCPUフォールバック用SessionOptionsを作成
    /// Issue #181: ONNX Runtime パッケージ競合時の安全なフォールバック
    /// </summary>
    private static SessionOptions CreateCpuFallbackSessionOptions()
    {
        var options = new SessionOptions();

        // CPU最適化設定
        options.EnableCpuMemArena = true;
        options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
        options.IntraOpNumThreads = Environment.ProcessorCount;
        options.EnableMemoryPattern = true;
        options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

        Console.WriteLine($"✅ [Issue #181] CPU SessionOptions作成完了 (Threads: {Environment.ProcessorCount})");

        return options;
    }

    private Mat ConvertToMat(IImage image, Rectangle? roi)
    {
        // LockPixelDataを使用してゼロコピーでピクセルデータにアクセス
        using var pixelLock = image.LockPixelData();
        var pixelData = pixelLock.Data;
        var stride = pixelLock.Stride;

        var mat = new Mat(image.Height, image.Width, MatType.CV_8UC4);

        // SpanからMatへコピー
        unsafe
        {
            fixed (byte* srcPtr = pixelData)
            {
                for (int y = 0; y < image.Height; y++)
                {
                    var srcRow = srcPtr + y * stride;
                    var dstRow = mat.Ptr(y);
                    Buffer.MemoryCopy(srcRow, (void*)dstRow, image.Width * 4, image.Width * 4);
                }
            }
        }

        // BGRA to BGR
        Cv2.CvtColor(mat, mat, ColorConversionCodes.BGRA2BGR);

        if (roi.HasValue)
        {
            var roiRect = new OpenCvSharp.Rect(roi.Value.X, roi.Value.Y, roi.Value.Width, roi.Value.Height);
            return new Mat(mat, roiRect);
        }

        return mat;
    }

    private async Task<List<DrawingPoint[]>> DetectTextAsync(Mat image, CancellationToken cancellationToken)
    {
        if (_detectionSession == null)
            return [];

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 前処理
            var inputTensor = PreprocessImageForDetection(image);

            // 推論
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("x", inputTensor)
            };

            using var results = _detectionSession.Run(inputs);
            var output = results.First().AsTensor<float>();

            // 後処理（DBNet出力からバウンディングボックス抽出）
            return PostprocessDetection(output, image.Width, image.Height);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<(string text, double confidence)> RecognizeTextInRegionAsync(
        Mat image,
        DrawingPoint[] box,
        CancellationToken cancellationToken)
    {
        if (_recognitionSession == null || _characterDictionary == null || _characterDictionary.Length == 0)
            return (string.Empty, 0);

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            // テキスト領域を切り出し
            using var cropped = CropAndRotateRegion(image, box);
            if (cropped.Empty())
                return (string.Empty, 0);

            // 前処理
            var inputTensor = PreprocessImageForRecognition(cropped);

            // 推論
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("x", inputTensor)
            };

            using var results = _recognitionSession.Run(inputs);
            var output = results.First().AsTensor<float>();

            // CTCデコード
            return CTCDecode(output);
        }, cancellationToken).ConfigureAwait(false);
    }

    private DenseTensor<float> PreprocessImageForDetection(Mat image)
    {
        // 検出モデル用の前処理
        // 1. リサイズ（DetectionTargetSize x DetectionTargetSize を推奨）
        var scale = Math.Min((double)DetectionTargetSize / image.Width, (double)DetectionTargetSize / image.Height);
        var newWidth = (int)(image.Width * scale);
        var newHeight = (int)(image.Height * scale);

        // PaddingAlignment の倍数にパディング
        newWidth = ((newWidth + PaddingAlignment - 1) / PaddingAlignment) * PaddingAlignment;
        newHeight = ((newHeight + PaddingAlignment - 1) / PaddingAlignment) * PaddingAlignment;

        using var resized = new Mat();
        Cv2.Resize(image, resized, new OpenCvSharp.Size(newWidth, newHeight));

        // 2. ImageNet正規化
        var tensor = new DenseTensor<float>([1, 3, newHeight, newWidth]);

        for (int y = 0; y < newHeight; y++)
        {
            for (int x = 0; x < newWidth; x++)
            {
                var pixel = resized.At<Vec3b>(y, x);
                // BGR -> RGB変換しながら正規化
                tensor[0, 0, y, x] = (pixel[2] / 255.0f - ImageNetMean[0]) / ImageNetStd[0]; // R
                tensor[0, 1, y, x] = (pixel[1] / 255.0f - ImageNetMean[1]) / ImageNetStd[1]; // G
                tensor[0, 2, y, x] = (pixel[0] / 255.0f - ImageNetMean[2]) / ImageNetStd[2]; // B
            }
        }

        return tensor;
    }

    private DenseTensor<float> PreprocessImageForRecognition(Mat image)
    {
        // 認識モデル用の前処理
        // 高さをRecognitionTargetHeightに正規化、幅はアスペクト比を維持
        var scale = (double)RecognitionTargetHeight / image.Height;
        var newWidth = Math.Max(1, (int)(image.Width * scale));

        // 最大幅制限
        if (newWidth > RecognitionMaxWidth)
        {
            newWidth = RecognitionMaxWidth;
        }

        using var resized = new Mat();
        Cv2.Resize(image, resized, new OpenCvSharp.Size(newWidth, RecognitionTargetHeight));

        // 正規化（mean=0.5, std=0.5）
        var tensor = new DenseTensor<float>([1, 3, RecognitionTargetHeight, newWidth]);

        for (int y = 0; y < RecognitionTargetHeight; y++)
        {
            for (int x = 0; x < newWidth; x++)
            {
                var pixel = resized.At<Vec3b>(y, x);
                // BGR -> RGB変換しながら正規化
                tensor[0, 0, y, x] = (pixel[2] / 255.0f - RecognitionNormFactor) / RecognitionNormFactor; // R
                tensor[0, 1, y, x] = (pixel[1] / 255.0f - RecognitionNormFactor) / RecognitionNormFactor; // G
                tensor[0, 2, y, x] = (pixel[0] / 255.0f - RecognitionNormFactor) / RecognitionNormFactor; // B
            }
        }

        return tensor;
    }

    private List<DrawingPoint[]> PostprocessDetection(Tensor<float> output, int originalWidth, int originalHeight)
    {
        var boxes = new List<DrawingPoint[]>();

        // DBNet出力をバイナリマップに変換
        var dims = output.Dimensions;
        var height = dims[2];
        var width = dims[3];

        using var binaryMap = new Mat(height, width, MatType.CV_8UC1);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var value = output[0, 0, y, x];
                binaryMap.Set(y, x, (byte)(value > _settings.DetectionThreshold ? 255 : 0));
            }
        }

        // 輪郭検出
        Cv2.FindContours(binaryMap, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        // スケール計算
        var scaleX = (double)originalWidth / width;
        var scaleY = (double)originalHeight / height;

        foreach (var contour in contours)
        {
            var area = Cv2.ContourArea(contour);
            if (area < MinContourArea) continue; // 小さすぎる領域を除外

            // 最小外接矩形
            var rect = Cv2.MinAreaRect(contour);
            var points2f = Cv2.BoxPoints(rect);

            // DrawingPoint[]に変換しスケーリング
            var points = points2f.Select(p => new DrawingPoint(
                (int)(p.X * scaleX),
                (int)(p.Y * scaleY)
            )).ToArray();

            boxes.Add(points);
        }

        return boxes;
    }

    private (string text, double confidence) CTCDecode(Tensor<float> output)
    {
        if (_characterDictionary == null || _characterDictionary.Length == 0)
            return (string.Empty, 0);

        var dims = output.Dimensions;
        var timeSteps = dims[1];
        var numClasses = dims[2];

        var result = new System.Text.StringBuilder();
        var confidences = new List<double>();
        int lastIndex = 0;

        for (int t = 0; t < timeSteps; t++)
        {
            // 最大確率のインデックスを取得
            var maxProb = float.MinValue;
            var maxIndex = 0;

            for (int c = 0; c < numClasses; c++)
            {
                var prob = output[0, t, c];
                if (prob > maxProb)
                {
                    maxProb = prob;
                    maxIndex = c;
                }
            }

            // CTCブランクでなく、前のインデックスと異なる場合に文字を追加
            // 通常、インデックス0がブランク
            if (maxIndex != 0 && maxIndex != lastIndex)
            {
                if (maxIndex - 1 < _characterDictionary.Length)
                {
                    result.Append(_characterDictionary[maxIndex - 1]);
                    // PP-OCRv5 認識モデルはlog-softmax出力のため、expで確率に変換
                    // 注意: モデルがsoftmax出力の場合はMath.Expを削除する必要あり
                    confidences.Add(Math.Exp(maxProb));
                }
            }

            lastIndex = maxIndex;
        }

        var avgConfidence = confidences.Count > 0 ? confidences.Average() : 0;
        return (result.ToString(), avgConfidence);
    }

    private Mat CropAndRotateRegion(Mat image, DrawingPoint[] box)
    {
        if (box.Length != 4)
            return new Mat();

        // 四角形の頂点をPoint2f配列に変換
        var srcPoints = box.Select(p => new Point2f(p.X, p.Y)).ToArray();

        // 幅と高さを計算
        var width = (int)Math.Max(
            Distance(srcPoints[0], srcPoints[1]),
            Distance(srcPoints[2], srcPoints[3]));
        var height = (int)Math.Max(
            Distance(srcPoints[0], srcPoints[3]),
            Distance(srcPoints[1], srcPoints[2]));

        if (width <= 0 || height <= 0)
            return new Mat();

        // 出力先の四角形
        var dstPoints = new Point2f[]
        {
            new(0, 0),
            new(width, 0),
            new(width, height),
            new(0, height)
        };

        // 透視変換
        using var transform = Cv2.GetPerspectiveTransform(srcPoints, dstPoints);
        var result = new Mat();
        Cv2.WarpPerspective(image, result, transform, new OpenCvSharp.Size(width, height));

        return result;
    }

    private static double Distance(Point2f p1, Point2f p2)
    {
        return Math.Sqrt(Math.Pow(p2.X - p1.X, 2) + Math.Pow(p2.Y - p1.Y, 2));
    }

    private static Rectangle GetBoundingRect(DrawingPoint[] box)
    {
        var minX = box.Min(p => p.X);
        var minY = box.Min(p => p.Y);
        var maxX = box.Max(p => p.X);
        var maxY = box.Max(p => p.Y);

        return new Rectangle(minX, minY, maxX - minX, maxY - minY);
    }

    private static TextDirection DetectTextDirection(DrawingPoint[] box)
    {
        if (box.Length < 4)
            return TextDirection.Unknown;

        // 簡易的な方向判定
        var width = Math.Max(
            Distance(new Point2f(box[0].X, box[0].Y), new Point2f(box[1].X, box[1].Y)),
            Distance(new Point2f(box[2].X, box[2].Y), new Point2f(box[3].X, box[3].Y)));
        var height = Math.Max(
            Distance(new Point2f(box[0].X, box[0].Y), new Point2f(box[3].X, box[3].Y)),
            Distance(new Point2f(box[1].X, box[1].Y), new Point2f(box[2].X, box[2].Y)));

        return height > width * VerticalTextRatio ? TextDirection.Vertical : TextDirection.Horizontal;
    }

    private OcrResults CreateEmptyResult(IImage image, Rectangle? roi, TimeSpan elapsed)
    {
        return new OcrResults(
            [],
            image,
            elapsed,
            _settings.Language,
            roi);
    }

    private void UpdatePerformanceStats(double elapsedMs, bool success)
    {
        if (success)
        {
            _totalProcessedImages++;
            _consecutiveFailureCount = 0;
        }
        else
        {
            _errorCount++;
            _consecutiveFailureCount++;
        }

        _totalProcessingTimeMs += elapsedMs;
        _minProcessingTimeMs = Math.Min(_minProcessingTimeMs, elapsedMs);
        _maxProcessingTimeMs = Math.Max(_maxProcessingTimeMs, elapsedMs);
    }

    #endregion

    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_sessionLock)
        {
            _detectionSession?.Dispose();
            _recognitionSession?.Dispose();
            _detectionSession = null;
            _recognitionSession = null;
        }

        _disposed = true;
        _isInitialized = false;

        _logger.LogInformation("ONNX OCR エンジンを破棄しました");
    }
}
