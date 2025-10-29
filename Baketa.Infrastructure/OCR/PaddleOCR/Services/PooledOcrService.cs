using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Models.OCR;
using Baketa.Core.Settings;
using Baketa.Infrastructure.OCR.PaddleOCR.Factory;

namespace Baketa.Infrastructure.OCR.PaddleOCR.Services;

/// <summary>
/// プール化されたOCRサービス実装
/// ObjectPoolを使用して複数のOCRエンジンインスタンスを効率管理
/// 並列処理での競合問題を根本解決
/// </summary>
public sealed class PooledOcrService : IOcrEngine
{
    private readonly ObjectPool<IOcrEngine> _enginePool;
    private readonly ILogger<PooledOcrService> _logger;
    private readonly IOptionsMonitor<OcrSettings> _ocrSettings;

    public PooledOcrService(
        ObjectPool<IOcrEngine> enginePool,
        ILogger<PooledOcrService> logger,
        IOptionsMonitor<OcrSettings> ocrSettings)
    {
        _enginePool = enginePool ?? throw new ArgumentNullException(nameof(enginePool));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _ocrSettings = ocrSettings ?? throw new ArgumentNullException(nameof(ocrSettings));
        
        _logger.LogInformation("🏊 PooledOcrService初期化完了 - プール化OCRサービス開始");
    }

    public bool IsDisposed { get; private set; }

    // IOcrEngine インターフェース実装
    public string EngineName => "PooledPaddleOCR";
    public string EngineVersion => "2.7.0.3-Pooled";
    public bool IsInitialized => true; // プール化環境では常に初期化済み
    public string? CurrentLanguage => "jpn"; // 固定言語

    public async Task<bool> InitializeAsync(OcrEngineSettings? settings = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        // プール化されたサービスでは、各エンジンインスタンスが個別に初期化される
        // このメソッドは互換性のためのスタブ実装
        _logger.LogDebug("📋 PooledOcrService.InitializeAsync: プール化環境では個別エンジンが初期化されます");
        
        return await Task.FromResult(true);
    }
    
    public async Task<bool> WarmupAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        _logger?.LogDebug("🔥🔥🔥 [PHASE13.2.25] PooledOcrService.WarmupAsync開始");
        _logger.LogInformation("🔥 PooledOcrServiceウォームアップ開始");

        // プールから最初のエンジンを取得してウォームアップ
        var engine = _enginePool.Get();
        _logger?.LogDebug($"🔥 [PHASE13.2.25] enginePool.Get()完了 - engine型: {engine?.GetType().Name ?? "NULL"}");
        if (engine == null)
        {
            _logger?.LogDebug("❌❌❌ [PHASE13.2.25] engine == null");
            _logger.LogError("❌ PooledOcrService: ウォームアップ用エンジンを取得できませんでした");
            return false;
        }

        try
        {
            _logger?.LogDebug("🚨 [PHASE13.2.25] engine.WarmupAsync()呼び出し直前");
            var result = await engine.WarmupAsync(cancellationToken);
            _logger?.LogDebug($"✅ [PHASE13.2.25] engine.WarmupAsync()完了 - 結果: {result}");
            _logger.LogInformation($"✅ PooledOcrServiceウォームアップ結果: {result}");
            return result;
        }
        finally
        {
            _enginePool.Return(engine);
            _logger?.LogDebug("🔍 [PHASE13.2.25] enginePool.Return()完了");
        }
    }

    public async Task<OcrResults> RecognizeAsync(
        IImage image,
        IProgress<OcrProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        return await RecognizeAsync(image, null, progressCallback, cancellationToken);
    }

    public async Task<OcrResults> RecognizeAsync(
        IImage image,
        Rectangle? regionOfInterest = null,
        IProgress<OcrProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        ArgumentNullException.ThrowIfNull(image);

        var engine = _enginePool.Get();
        if (engine == null)
        {
            _logger.LogError("❌ PooledOcrService: エンジンインスタンスをプールから取得できませんでした");
            throw new InvalidOperationException("OCRエンジンプールからインスタンスを取得できませんでした");
        }

        try
        {
            _logger.LogDebug("🔄 PooledOcrService: エンジンプールから取得 - 型: {EngineType}, Hash: {EngineHash}", 
                engine.GetType().Name, engine.GetHashCode());
            
            var startTime = DateTime.UtcNow;
            var results = await engine.RecognizeAsync(image, regionOfInterest, progressCallback, cancellationToken);
            var duration = DateTime.UtcNow - startTime;
            
            _logger.LogDebug("✅ PooledOcrService: OCR処理完了 - 処理時間: {Duration}ms, 結果数: {ResultCount}", 
                duration.TotalMilliseconds, results.TextRegions.Count);
            
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ PooledOcrService: OCR処理でエラーが発生 - エンジン: {EngineType}", 
                engine.GetType().Name);
            throw;
        }
        finally
        {
            // エンジンをプールに返却
            try
            {
                _enginePool.Return(engine);
                _logger.LogDebug("♻️ PooledOcrService: エンジンをプールに返却 - Hash: {EngineHash}", 
                    engine.GetHashCode());
            }
            catch (Exception returnEx)
            {
                _logger.LogWarning(returnEx, "⚠️ PooledOcrService: エンジン返却時にエラー - Hash: {EngineHash}", 
                    engine.GetHashCode());
                // 返却エラーは処理を中断しない
            }
        }
    }

    /// <summary>
    /// [Option B] OcrContextを使用してテキストを認識します（座標問題恒久対応）
    /// </summary>
    public async Task<OcrResults> RecognizeAsync(OcrContext context, IProgress<OcrProgress>? progressCallback = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        _logger.LogInformation("🎯 [OPTION_B] PooledOcrService - OcrContext使用のRecognizeAsync呼び出し");

        // 既存メソッドに委譲
        return await RecognizeAsync(
            context.Image,
            context.CaptureRegion,
            progressCallback,
            context.CancellationToken).ConfigureAwait(false);
    }

    public OcrEngineSettings GetSettings()
    {
        ThrowIfDisposed();
        
        var settings = _ocrSettings.CurrentValue;
        
        // appsettings.jsonから統一設定を取得
        return new OcrEngineSettings
        {
            Language = "jpn",
            DetectionThreshold = settings.DetectionThreshold, // 統一設定: appsettings.json から読み込み
            RecognitionThreshold = 0.6, // 現在の設定値（今後統一化対象）
            UseGpu = true,
            MaxDetections = 1000,
            EnablePreprocessing = true
        };
    }

    public async Task ApplySettingsAsync(OcrEngineSettings settings, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        // プール化環境では設定変更は複雑なため、現在はサポート外
        _logger.LogWarning("⚠️ PooledOcrService: 設定変更はプール化環境でサポートされていません");
        await Task.CompletedTask;
    }

    public IReadOnlyList<string> GetAvailableLanguages()
    {
        ThrowIfDisposed();
        return ["jpn", "japanese"]; // 現在は日本語のみサポート
    }

    public IReadOnlyList<string> GetAvailableModels()
    {
        ThrowIfDisposed();
        return ["PaddleOCR-v4-jpn"]; // 利用可能なモデル名のリスト
    }

    public async Task<bool> IsLanguageAvailableAsync(string languageCode, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await Task.FromResult(languageCode == "jpn" || languageCode == "japanese");
    }

    public OcrPerformanceStats GetPerformanceStats()
    {
        ThrowIfDisposed();
        // プール化環境では個別統計は複雑なため、ダミー値を返す
        return new OcrPerformanceStats
        {
            TotalProcessedImages = 0,
            AverageProcessingTimeMs = 0.0,
            MinProcessingTimeMs = 0.0,
            MaxProcessingTimeMs = 0.0,
            SuccessRate = 1.0,
            ErrorCount = 0,
            StartTime = DateTime.UtcNow,
            LastUpdateTime = DateTime.UtcNow
        };
    }

    public void CancelCurrentOcrTimeout()
    {
        ThrowIfDisposed();
        // プール化環境では個別エンジンのキャンセル制御は複雑
        _logger.LogDebug("🔄 PooledOcrService: OCRタイムアウトキャンセル要求");
    }

    /// <summary>
    /// 連続失敗回数を取得（診断・フォールバック判定用）
    /// </summary>
    /// <returns>連続失敗回数</returns>
    public int GetConsecutiveFailureCount()
    {
        ThrowIfDisposed();

        // プール化環境では個別エンジンの失敗カウントを追跡しないため、常に0を返す
        // 各エンジンインスタンスが独自にカウントを保持している可能性はあるが、
        // プールレベルでの統合カウントは複雑なため実装しない
        return 0;
    }

    /// <summary>
    /// 失敗カウンタをリセット（緊急時復旧用）
    /// </summary>
    public void ResetFailureCounter()
    {
        ThrowIfDisposed();

        // プール化環境では個別エンジンの失敗カウントを追跡しないため、何もしない
        _logger.LogDebug("🔄 PooledOcrService: 失敗カウンタリセット要求（プール化環境では効果なし）");
    }

    /// <summary>
    /// テキスト検出のみを実行（認識処理をスキップ）
    /// </summary>
    public async Task<OcrResults> DetectTextRegionsAsync(IImage image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ThrowIfDisposed();

        _logger.LogDebug("🔍 PooledOcrService: DetectTextRegionsAsync実行（効率的な検出専用実装）");

        var engine = _enginePool.Get();
        if (engine == null)
        {
            _logger.LogError("❌ PooledOcrService: 検出専用エンジンインスタンスをプールから取得できませんでした");
            throw new InvalidOperationException("OCR検出専用エンジンプールからインスタンスを取得できませんでした");
        }

        try
        {
            _logger.LogDebug("🔄 PooledOcrService: 検出専用エンジンプールから取得 - 型: {EngineType}, Hash: {EngineHash}", 
                engine.GetType().Name, engine.GetHashCode());
            
            var startTime = DateTime.UtcNow;
            
            // ✅ 効率的な検出専用処理: 認識処理をスキップしてリソースを大幅節約
            var results = await engine.DetectTextRegionsAsync(image, cancellationToken).ConfigureAwait(false);
            
            var duration = DateTime.UtcNow - startTime;
            
            _logger.LogDebug("✅ PooledOcrService: 効率的検出専用処理完了 - 処理時間: {Duration}ms, 結果数: {ResultCount}", 
                duration.TotalMilliseconds, results.TextRegions.Count);
            
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ PooledOcrService: 検出専用処理でエラーが発生 - エンジン: {EngineType}", 
                engine.GetType().Name);
            throw;
        }
        finally
        {
            // エンジンをプールに返却
            try
            {
                _enginePool.Return(engine);
                _logger.LogDebug("♻️ PooledOcrService: 検出専用エンジンをプールに返却 - Hash: {EngineHash}", 
                    engine.GetHashCode());
            }
            catch (Exception returnEx)
            {
                _logger.LogWarning(returnEx, "⚠️ PooledOcrService: 検出専用エンジン返却時にエラー - Hash: {EngineHash}", 
                    engine.GetHashCode());
                // 返却エラーは処理を中断しない
            }
        }
    }

    public async Task<bool> SwitchLanguageAsync(string language, CancellationToken _ = default)
    {
        ThrowIfDisposed();
        
        _logger.LogDebug("🔄 PooledOcrService: 言語切り替え要求 - {Language}", language);
        
        // プール化環境での言語切り替えは複雑なため、現在は固定言語（日本語）のみサポート
        if (language == "jpn" || language == "japanese")
        {
            return await Task.FromResult(true);
        }
        
        _logger.LogWarning("⚠️ PooledOcrService: プール化環境では日本語のみサポート - 要求言語: {Language}", language);
        return await Task.FromResult(false);
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        
        try
        {
            _logger.LogInformation("🧹 PooledOcrService: リソース解放開始");
            
            // ObjectPoolは自動的にクリーンアップされるため、明示的な処理は不要
            // 各エンジンインスタンスのDisposeはObjectPoolPolicyで管理される
            
            IsDisposed = true;
            _logger.LogInformation("✅ PooledOcrService: リソース解放完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ PooledOcrService: リソース解放でエラー");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
    }
}
