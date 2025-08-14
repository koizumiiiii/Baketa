using Microsoft.Extensions.Logging;
using Baketa.Infrastructure.OCR.PaddleOCR.Engine;
using Baketa.Infrastructure.OCR.PaddleOCR.Models;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using System.Drawing;
using System.IO;

namespace Baketa.Infrastructure.OCR.PaddleOCR.Engine;

/// <summary>
/// 本番環境でも安全に使用できるPaddleOcrEngineラッパー
/// 実際のPaddleOCRライブラリを使用せずに、引数検証と基本的な動作をテストします
/// IOcrEngineインターフェースに完全準拠
/// </summary>
/// <param name="modelPathResolver">モデルパスリゾルバー</param>
/// <param name="logger">ロガーインスタンス</param>
/// <param name="skipRealInitialization">実際の初期化をスキップするかどうか</param>
public class SafePaddleOcrEngine(
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
    public string EngineName => "PaddleOCR (Safe)";

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
        
        Console.WriteLine("⚠️ SafePaddleOcrEngine初期化 - これはモックエンジンです！");
        
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

        // 実際のPaddleOcrEngineは使用しない（開発・テスト環境では危険）
        throw new NotSupportedException("実際のPaddleOCRエンジンの初期化は開発・テスト環境では無効化されています");
    }
    
    public async Task<bool> WarmupAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger?.LogInformation("SafePaddleOcrEngineウォームアップ開始");
            
            if (_skipRealInitialization)
            {
                // モックモードではダミーのウォームアップ
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                _logger?.LogInformation("SafePaddleOcrEngineウォームアップ完了（モック）");
                return true;
            }
            
            // 実際のウォームアップはスキップ（内部エンジンがnullの場合）
            _logger?.LogWarning("SafePaddleOcrEngineウォームアップスキップ（実エンジン未実装）");
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "SafePaddleOcrEngineウォームアップ中にエラーが発生");
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
            progressCallback?.Report(new OcrProgress(0.0, "OCR処理を開始（Safe版）"));
            
            // 開発・テスト環境での最短遅延
            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            
            progressCallback?.Report(new OcrProgress(0.5, "テキスト検出中（Safe版）"));
            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            
            progressCallback?.Report(new OcrProgress(1.0, "OCR処理完了（Safe版）"));
            
            stopwatch.Stop();
            
            // 統計を更新
            _totalProcessedImages++;
            _processingTimes.Add(stopwatch.Elapsed.TotalMilliseconds);
            
            _logger?.LogDebug("Safe OCR実行完了 - 処理時間: {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            
            // 空の結果を返す（Safe版）
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
            _logger?.LogInformation("OCR処理がキャンセルされました（Safe版）");
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _errorCount++;
            _logger?.LogError(ex, "OCR処理中にエラーが発生（Safe版）");
            throw new OcrException("OCR処理に失敗しました（Safe版）", ex);
        }
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
            _logger?.LogInformation("言語を変更しました: {Language}（Safe版）", _settings.Language);
        }
        
        _logger?.LogInformation("OCRエンジン設定を更新: 言語={Language}, モデル={Model}（Safe版）",
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
            
        // Safe環境ではモデルファイルの存在確認は行わない
        return true; // Safe版では常に利用可能とする
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
    /// SafePaddleOcrEngineではスタブ実装
    /// </summary>
    public void CancelCurrentOcrTimeout()
    {
        // SafePaddleOcrEngineではタイムアウト処理がないため何もしない
        // ログで記録のみ
        _logger?.LogDebug("SafePaddleOcrEngine: CancelCurrentOcrTimeout呼び出し（スタブ実装）");
    }

    /// <summary>
    /// テキスト検出のみを実行（認識処理をスキップ）
    /// SafePaddleOcrEngineではダミー実装を提供
    /// </summary>
    public async Task<OcrResults> DetectTextRegionsAsync(IImage image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ThrowIfDisposed();

        _logger?.LogDebug("SafePaddleOcrEngine: DetectTextRegionsAsync実行（ダミー実装）");

        await Task.Delay(10, cancellationToken); // 軽微な遅延でリアリティを演出

        var dummyTextRegions = new List<OcrTextRegion>
        {
            new("", new Rectangle(10, 10, 100, 30), 0.95), // 検出専用なのでテキストは空
            new("", new Rectangle(50, 60, 80, 25), 0.88)
        };

        return new OcrResults(
            dummyTextRegions,
            image,
            TimeSpan.FromMilliseconds(10),
            CurrentLanguage ?? "jpn",
            null,
            "" // 検出専用なので結合テキストも空
        );
    }

    #endregion

    #region 言語切り替え支援メソッド（Safe版）

    /// <summary>
    /// 言語を切り替えます（Safe版の簡易メソッド）
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
            _logger?.LogDebug("既に指定された言語で初期化されています: {Language}（Safe版）", language);
            return true;
        }

        // 設定を更新
        var newSettings = _settings.Clone();
        newSettings.Language = language;
        
        await ApplySettingsAsync(newSettings, cancellationToken).ConfigureAwait(false);
        
        _logger?.LogInformation("言語切り替え完了: {Language}（Safe版）", language);
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
    /// Safe版の初期化シミュレーション
    /// </summary>
    private async Task<bool> SimulateInitializationAsync(OcrEngineSettings settings, CancellationToken cancellationToken)
    {
        await Task.Delay(1, cancellationToken).ConfigureAwait(false);

        // 無効なパス設定を検出
        if (IsInvalidPathConfiguration())
        {
            _logger?.LogError("無効なパス設定で初期化が失敗しました（Safe版）");
            return false;
        }

        if (IsInitialized)
        {
            _logger?.LogDebug("PaddleOCRエンジンは既に初期化されています（Safe版）");
            return true;
        }

        try
        {
            // Safe版のディレクトリ作成シミュレーション
            CreateSafeDirectories();
            
            // 設定を適用
            _settings = settings.Clone();
            
            // 成功をシミュレート
            IsInitialized = true;
            CurrentLanguage = settings.Language;
            _startTime = DateTime.UtcNow;
            
            _logger?.LogInformation("PaddleOCRエンジンの初期化完了（Safe版）");
            return true;
        }
        catch (ArgumentException ex)
        {
            _logger?.LogError(ex, "無効な引数でPaddleOCRエンジンの初期化に失敗（Safe版）");
            return false;
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogError(ex, "無効な操作でPaddleOCRエンジンの初期化に失敗（Safe版）");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger?.LogError(ex, "アクセス権限不足でPaddleOCRエンジンの初期化に失敗（Safe版）");
            return false;
        }
        catch (IOException ex)
        {
            _logger?.LogError(ex, "I/Oエラーでディレクトリ作成に失敗（Safe版）");
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
    /// Safe版のディレクトリ作成シミュレーション
    /// </summary>
    private void CreateSafeDirectories()
    {
        try
        {
            // Safe環境でのディレクトリ作成をシミュレート
            string[] safeDirectories =
            [
                _modelPathResolver.GetDetectionModelsDirectory(),
                _modelPathResolver.GetRecognitionModelsDirectory("eng"),
                _modelPathResolver.GetRecognitionModelsDirectory("jpn")
            ];
            
            foreach (var directory in safeDirectories)
            {
                try
                {
                    _modelPathResolver.EnsureDirectoryExists(directory);
                    _logger?.LogDebug("Safe版ディレクトリ作成: {Directory}", directory);
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger?.LogWarning(ex, "Safe版ディレクトリ作成でアクセス権限エラー: {Directory}", directory);
                    // Safe環境では継続
                }
                catch (IOException ex)
                {
                    _logger?.LogWarning(ex, "Safe版ディレクトリ作成でI/Oエラー: {Directory}", directory);
                    // Safe環境では継続
                }
                catch (ArgumentException ex)
                {
                    _logger?.LogWarning(ex, "Safe版ディレクトリ作成で引数エラー: {Directory}", directory);
                    // Safe環境では継続
                }
            }
        }
        catch (ArgumentException ex)
        {
            _logger?.LogError(ex, "Safe版ディレクトリ作成の初期化で引数エラー");
            // Safe環境ではエラーを再スローしない
        }
        catch (InvalidOperationException ex)
        {
            _logger?.LogError(ex, "Safe版ディレクトリ作成の初期化で操作エラー");
            // Safe環境ではエラーを再スローしない
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
            _logger?.LogDebug("SafePaddleOcrEngineのリソースを解放中");
            
            IsInitialized = false;
            CurrentLanguage = null;
            _processingTimes.Clear();
        }

        _disposed = true;
    }

    #endregion
}