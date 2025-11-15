using System.Drawing;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Models.OCR; // 🎯 [OPTION_B] OcrContext用
using Baketa.Infrastructure.OCR.PaddleOCR.Engine;
using Baketa.Infrastructure.OCR.PaddleOCR.Models;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Tests.OCR.PaddleOCR.TestData;

/// <summary>
/// テスト用の安全なPaddleOcrEngineラッパー
/// 実際のPaddleOCRライブラリを使用せずに、引数検証と基本的な動作をテストします
/// IOcrEngineインターフェースに完全準拠
/// </summary>
/// <param name="modelPathResolver">モデルパスリゾルバー</param>
/// <param name="logger">ロガーインスタンス</param>
/// <param name="skipRealInitialization">実際の初期化をスキップするかどうか</param>
public class SafeTestPaddleOcrEngine(
    IModelPathResolver modelPathResolver,
    ILogger<PaddleOcrEngine>? logger = null,
    bool skipRealInitialization = true) : IOcrEngine
{
    private readonly IModelPathResolver _modelPathResolver = modelPathResolver ?? throw new ArgumentNullException(nameof(modelPathResolver));
    private readonly ILogger<PaddleOcrEngine>? _logger = logger;
    private readonly bool _skipRealInitialization = skipRealInitialization;
    private bool _disposed;

    // 設定管理
    private OcrEngineSettings _settings = new();

    // パフォーマンス統計
    private int _totalProcessedImages;
    private readonly List<double> _processingTimes = [];
    private int _errorCount;
    private DateTime _startTime = DateTime.UtcNow;

    #region IOcrEngine実装

    /// <summary>
    /// OCRエンジンの名前
    /// </summary>
    public string EngineName => "PaddleOCR (Test)";

    /// <summary>
    /// OCRエンジンのバージョン
    /// </summary>
    public string EngineVersion => "2.7.0.3";

    /// <summary>
    /// エンジンが初期化済みかどうか
    /// </summary>
    public bool IsInitialized { get; private set; }

    /// <summary>
    /// 現在の言語設定
    /// </summary>
    public string? CurrentLanguage { get; private set; }

    /// <summary>
    /// OCRエンジンを初期化します
    /// </summary>
    public async Task<bool> InitializeAsync(OcrEngineSettings? settings = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (_skipRealInitialization)
        {
            settings ??= new OcrEngineSettings();

            // 厳密なパラメータ検証を実装（例外を投げる）
            ValidateInitializationSettings(settings);

            if (!settings.IsValid())
            {
                _logger?.LogError("無効な設定でOCRエンジンの初期化が失敗しました");
                return false;
            }

            return await SimulateInitializationAsync(settings, cancellationToken).ConfigureAwait(false);
        }

        // 実際のPaddleOcrEngineは使用しない（テスト環境では危険）
        throw new NotSupportedException("実際のPaddleOCRエンジンの初期化はテスト環境では無効化されています");
    }

    /// <summary>
    /// ウォームアップを実行します（テスト用）
    /// </summary>
    public async Task<bool> WarmupAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger?.LogInformation("SafeTestPaddleOcrEngineウォームアップ開始");

            if (_skipRealInitialization)
            {
                // テストモードではダミーのウォームアップ
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                _logger?.LogInformation("SafeTestPaddleOcrEngineウォームアップ完了（テスト用）");
                return true;
            }

            // 実際のウォームアップはスキップ（内部エンジンがnullの場合）
            _logger?.LogWarning("SafeTestPaddleOcrEngineウォームアップスキップ（実エンジン未実装）");
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "SafeTestPaddleOcrEngineウォームアップ中にエラーが発生");
            return false;
        }
    }

    /// <summary>
    /// 画像からテキストを認識します
    /// </summary>
    public async Task<OcrResults> RecognizeAsync(
        IImage image,
        IProgress<OcrProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        return await RecognizeAsync(image, null, progressCallback, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 画像の指定領域からテキストを認識します
    /// </summary>
    public async Task<OcrResults> RecognizeAsync(
        IImage image,
        Rectangle? regionOfInterest,
        IProgress<OcrProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ThrowIfDisposed();
        ThrowIfNotInitialized();

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // 進捗通知
            progressCallback?.Report(new OcrProgress(0.0, "OCR処理を開始（テスト用）"));

            // テスト用の最短遅延
            await Task.Delay(1, cancellationToken).ConfigureAwait(false);

            progressCallback?.Report(new OcrProgress(0.5, "テキスト検出中（テスト用）"));
            await Task.Delay(1, cancellationToken).ConfigureAwait(false);

            progressCallback?.Report(new OcrProgress(1.0, "OCR処理完了（テスト用）"));

            stopwatch.Stop();

            // 統計を更新
            _totalProcessedImages++;
            _processingTimes.Add(stopwatch.Elapsed.TotalMilliseconds);

            _logger?.LogDebug("テスト用OCR実行完了 - 処理時間: {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);

            // 空の結果を返す（テスト用）
            return new OcrResults(
                [],
                image,
                stopwatch.Elapsed,
                CurrentLanguage ?? _settings.Language,
                regionOfInterest
            );
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            _logger?.LogInformation("OCR処理がキャンセルされました（テスト用）");
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _errorCount++;
            _logger?.LogError(ex, "OCR処理中にエラーが発生（テスト用）");
            throw new OcrException("OCR処理に失敗しました（テスト用）", ex);
        }
    }

    /// <summary>
    /// [Option B] OcrContextを使用してテキストを認識します（座標問題恒久対応）
    /// </summary>
    /// <param name="context">OCRコンテキスト（画像、ウィンドウハンドル、キャプチャ領域を含む）</param>
    /// <param name="progressCallback">進捗通知コールバック（オプション）</param>
    /// <returns>OCR結果</returns>
    /// <remarks>
    /// テスト用の実装。既存のRecognizeAsyncメソッドに委譲します。
    /// </remarks>
    public async Task<OcrResults> RecognizeAsync(
        OcrContext context,
        IProgress<OcrProgress>? progressCallback = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        _logger?.LogDebug("🎯 [OPTION_B] SafeTestPaddleOcrEngine - OcrContext使用 - HasCaptureRegion: {HasCaptureRegion}",
            context.HasCaptureRegion);

        // 既存メソッドに委譲
        return await RecognizeAsync(
            context.Image,
            context.CaptureRegion,
            progressCallback,
            context.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// OCRエンジンの設定を取得します
    /// </summary>
    public OcrEngineSettings GetSettings()
    {
        return _settings.Clone();
    }

    /// <summary>
    /// OCRエンジンの設定を適用します
    /// </summary>
    public async Task ApplySettingsAsync(OcrEngineSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ThrowIfDisposed();

        if (!settings.IsValid())
        {
            throw new ArgumentException("無効な設定です", nameof(settings));
        }

        if (!IsInitialized)
        {
            throw new InvalidOperationException("OCRエンジンが初期化されていません。InitializeAsync()を先に呼び出してください。");
        }

        await Task.Delay(1, cancellationToken).ConfigureAwait(false);

        // 言語変更を検出
        bool languageChanged = _settings.Language != settings.Language;

        // 設定をコピー
        _settings = settings.Clone();

        // 言語が変更された場合は更新
        if (languageChanged)
        {
            CurrentLanguage = _settings.Language;
            _logger?.LogInformation("言語を変更しました: {Language}（テスト用）", _settings.Language);
        }

        _logger?.LogInformation("OCRエンジン設定を更新: 言語={Language}, モデル={Model}（テスト用）",
            _settings.Language, _settings.ModelName);
    }

    /// <summary>
    /// 使用可能な言語のリストを取得します
    /// </summary>
    public IReadOnlyList<string> GetAvailableLanguages()
    {
        return ["eng", "jpn"];
    }

    /// <summary>
    /// 使用可能なモデルのリストを取得します
    /// </summary>
    public IReadOnlyList<string> GetAvailableModels()
    {
        return ["standard"];
    }

    /// <summary>
    /// 指定言語のモデルが利用可能かを確認します
    /// </summary>
    public async Task<bool> IsLanguageAvailableAsync(string languageCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(languageCode))
            return false;

        var availableLanguages = GetAvailableLanguages();
        if (!availableLanguages.Contains(languageCode))
            return false;

        await Task.Delay(1, cancellationToken).ConfigureAwait(false);

        // テスト環境ではモデルファイルの存在確認は行わない
        return true; // テスト用では常に利用可能とする
    }

    /// <summary>
    /// エンジンのパフォーマンス統計を取得
    /// </summary>
    public OcrPerformanceStats GetPerformanceStats()
    {
        double averageTime = _processingTimes.Count > 0 ? _processingTimes.Average() : 0.0;
        double minTime = _processingTimes.Count > 0 ? _processingTimes.Min() : 0.0;
        double maxTime = _processingTimes.Count > 0 ? _processingTimes.Max() : 0.0;
        double successRate = _totalProcessedImages > 0 ?
            (double)(_totalProcessedImages - _errorCount) / _totalProcessedImages : 1.0;

        return new OcrPerformanceStats
        {
            TotalProcessedImages = _totalProcessedImages,
            AverageProcessingTimeMs = averageTime,
            MinProcessingTimeMs = minTime,
            MaxProcessingTimeMs = maxTime,
            ErrorCount = _errorCount,
            SuccessRate = successRate,
            StartTime = _startTime,
            LastUpdateTime = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 進行中のOCRタイムアウト処理をキャンセル
    /// テスト用エンジンではスタブ実装
    /// </summary>
    public void CancelCurrentOcrTimeout()
    {
        // テストエンジンではタイムアウト処理がないため何もしない
        _logger?.LogDebug("SafeTestPaddleOcrEngine: CancelCurrentOcrTimeout呼び出し（スタブ実装）");
    }

    /// <summary>
    /// 連続失敗回数を取得（診断・フォールバック判定用）
    /// </summary>
    /// <returns>連続失敗回数</returns>
    public int GetConsecutiveFailureCount()
    {
        // テストエンジンは失敗カウントを追跡しないため、常に0を返す
        return 0;
    }

    /// <summary>
    /// 失敗カウンタをリセット（緊急時復旧用）
    /// </summary>
    public void ResetFailureCounter()
    {
        // テストエンジンは失敗カウントを追跡しないため、何もしない
        _logger?.LogDebug("SafeTestPaddleOcrEngine: ResetFailureCounter呼び出し（スタブ実装）");
    }

    /// <summary>
    /// テキスト検出のみを実行（認識処理をスキップ）
    /// テスト用の簡易実装
    /// </summary>
    public async Task<OcrResults> DetectTextRegionsAsync(IImage image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ThrowIfDisposed();
        ThrowIfNotInitialized();

        _logger?.LogDebug("SafeTestPaddleOcrEngine: DetectTextRegionsAsync実行（テスト用）");

        // テスト用の最短遅延
        await Task.Delay(1, cancellationToken).ConfigureAwait(false);

        // 空の結果を返す（テスト用）
        return new OcrResults(
            [],
            image,
            TimeSpan.FromMilliseconds(1),
            CurrentLanguage ?? _settings.Language,
            null,
            ""
        );
    }

    #endregion

    #region 言語切り替え支援メソッド（テスト用）

    /// <summary>
    /// 言語を切り替えます（テスト用の簡易メソッド）
    /// </summary>
    public async Task<bool> SwitchLanguageAsync(string language, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ThrowIfNotInitialized();

        if (string.IsNullOrWhiteSpace(language))
        {
            throw new ArgumentException("言語コードが無効です", nameof(language));
        }

        if (language == "invalid")
        {
            throw new ArgumentException($"サポートされていない言語: {language}", nameof(language));
        }

        if (!GetAvailableLanguages().Contains(language))
        {
            throw new ArgumentException($"サポートされていない言語: {language}", nameof(language));
        }

        if (CurrentLanguage == language)
        {
            _logger?.LogDebug("既に指定された言語で初期化されています: {Language}（テスト用）", language);
            return true;
        }

        // 設定を更新
        var newSettings = _settings.Clone();
        newSettings.Language = language;

        await ApplySettingsAsync(newSettings, cancellationToken).ConfigureAwait(false);

        _logger?.LogInformation("言語切り替え完了: {Language}（テスト用）", language);
        return true;
    }

    #endregion

    #region バリデーションメソッド

    /// <summary>
    /// 初期化設定の厳密な検証
    /// </summary>
    private static void ValidateInitializationSettings(OcrEngineSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // 言語の検証
        if (string.IsNullOrWhiteSpace(settings.Language))
        {
            throw new ArgumentException("言語コードが無効です", nameof(settings));
        }

        if (settings.Language == "invalid")
        {
            throw new ArgumentException($"サポートされていない言語: {settings.Language}", nameof(settings));
        }

        // ワーカー数の検証
        if (settings.WorkerCount <= 0 || settings.WorkerCount > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), settings.WorkerCount,
                "ワーカー数は1から10の間で指定してください");
        }

        // 閾値の検証
        if (settings.DetectionThreshold < 0.0 || settings.DetectionThreshold > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), settings.DetectionThreshold,
                "検出閾値は0.0から1.0の間で指定してください");
        }

        if (settings.RecognitionThreshold < 0.0 || settings.RecognitionThreshold > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), settings.RecognitionThreshold,
                "認識閾値は0.0から1.0の間で指定してください");
        }

        // 最大検出数の検証
        if (settings.MaxDetections <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), settings.MaxDetections,
                "最大検出数は正の値で指定してください");
        }

        // GPUデバイスIDの検証
        if (settings.GpuDeviceId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), settings.GpuDeviceId,
                "GPUデバイスIDは0以上で指定してください");
        }

        // モデル名の検証
        if (string.IsNullOrWhiteSpace(settings.ModelName))
        {
            throw new ArgumentException("モデル名が無効です", nameof(settings));
        }
    }

    #endregion

    #region プライベートメソッド

    /// <summary>
    /// テスト用の初期化シミュレーション
    /// </summary>
    private async Task<bool> SimulateInitializationAsync(OcrEngineSettings settings, CancellationToken cancellationToken)
    {
        await Task.Delay(1, cancellationToken).ConfigureAwait(false);

        // 無効なパス設定を検出
        if (IsInvalidPathConfiguration())
        {
            _logger?.LogError("無効なパス設定で初期化が失敗しました（テスト用）");
            return false;
        }

        if (IsInitialized)
        {
            _logger?.LogDebug("PaddleOCRエンジンは既に初期化されています（テスト用）");
            return true;
        }

        try
        {
            // テスト用のディレクトリ作成シミュレーション
            CreateTestDirectories();

            // 設定を適用
            _settings = settings.Clone();

            // 成功をシミュレート
            IsInitialized = true;
            CurrentLanguage = settings.Language;
            _startTime = DateTime.UtcNow;

            _logger?.LogInformation("PaddleOCRエンジンの初期化完了（テスト用）");
            return true;
        }
        catch (ArgumentException ex)
        {
            _logger?.LogError(ex, "無効な引数でPaddleOCRエンジンの初期化に失敗（テスト用）");
            return false;
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogError(ex, "無効な操作でPaddleOCRエンジンの初期化に失敗（テスト用）");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger?.LogError(ex, "アクセス権限不足でPaddleOCRエンジンの初期化に失敗（テスト用）");
            return false;
        }
        catch (IOException ex)
        {
            _logger?.LogError(ex, "I/Oエラーでディレクトリ作成に失敗（テスト用）");
            return false;
        }
    }

    /// <summary>
    /// 無効なパス設定を検出
    /// </summary>
    private bool IsInvalidPathConfiguration()
    {
        try
        {
            var modelsDirectory = _modelPathResolver.GetModelsRootDirectory();
            var detectionDirectory = _modelPathResolver.GetDetectionModelsDirectory();

            // ネットワークパスを検出
            if (modelsDirectory.StartsWith(@"\\", StringComparison.Ordinal) ||
                detectionDirectory.StartsWith(@"\\", StringComparison.Ordinal))
            {
                _logger?.LogWarning("ネットワークパスが検出されました: {ModelsDir}", modelsDirectory);
                return true;
            }

            // 空のパスを検出
            if (string.IsNullOrWhiteSpace(modelsDirectory) || string.IsNullOrWhiteSpace(detectionDirectory))
            {
                _logger?.LogWarning("空のパスが検出されました");
                return true;
            }

            return false;
        }
        catch (ArgumentException ex)
        {
            _logger?.LogWarning(ex, "パス設定の引数が無効です");
            return true; // エラーが発生した場合は無効とみなす
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger?.LogWarning(ex, "パス設定の確認中にアクセス権限エラーが発生");
            return true; // エラーが発生した場合は無効とみなす
        }
        catch (IOException ex)
        {
            _logger?.LogWarning(ex, "パス設定の確認中にI/Oエラーが発生");
            return true; // エラーが発生した場合は無効とみなす
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
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>
    /// テスト用のディレクトリ作成シミュレーション
    /// </summary>
    private void CreateTestDirectories()
    {
        try
        {
            // テスト環境でのディレクトリ作成をシミュレート
            string[] testDirectories =
            [
                _modelPathResolver.GetDetectionModelsDirectory(),
                _modelPathResolver.GetRecognitionModelsDirectory("eng"),
                _modelPathResolver.GetRecognitionModelsDirectory("jpn")
            ];

            foreach (var directory in testDirectories)
            {
                try
                {
                    _modelPathResolver.EnsureDirectoryExists(directory);
                    _logger?.LogDebug("テスト用ディレクトリ作成: {Directory}", directory);
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger?.LogWarning(ex, "テスト用ディレクトリ作成でアクセス権限エラー: {Directory}", directory);
                    // テスト環境では継続
                }
                catch (IOException ex)
                {
                    _logger?.LogWarning(ex, "テスト用ディレクトリ作成でI/Oエラー: {Directory}", directory);
                    // テスト環境では継続
                }
                catch (ArgumentException ex)
                {
                    _logger?.LogWarning(ex, "テスト用ディレクトリ作成で引数エラー: {Directory}", directory);
                    // テスト環境では継続
                }
            }
        }
        catch (ArgumentException ex)
        {
            _logger?.LogError(ex, "テスト用ディレクトリ作成の初期化で引数エラー");
            // テスト環境ではエラーを再スローしない
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogError(ex, "テスト用ディレクトリ作成の初期化で操作エラー");
            // テスト環境ではエラーを再スローしない
        }
    }

    #endregion

    #region IDisposable実装

    /// <summary>
    /// リソースの解放
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// リソースの解放（パターン実装）
    /// </summary>
    /// <param name="disposing">マネージドリソースも解放するかどうか</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            _logger?.LogDebug("SafeTestPaddleOcrEngineのリソースを解放中");

            IsInitialized = false;
            CurrentLanguage = null;
            _processingTimes.Clear();
        }

        _disposed = true;
    }

    #endregion
}
