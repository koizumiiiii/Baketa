using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Imaging;
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

    public PooledOcrService(
        ObjectPool<IOcrEngine> enginePool,
        ILogger<PooledOcrService> logger)
    {
        _enginePool = enginePool ?? throw new ArgumentNullException(nameof(enginePool));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
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
        
        _logger.LogInformation("🔥 PooledOcrServiceウォームアップ開始");
        
        // プールから最初のエンジンを取得してウォームアップ
        var engine = _enginePool.Get();
        if (engine == null)
        {
            _logger.LogError("❌ PooledOcrService: ウォームアップ用エンジンを取得できませんでした");
            return false;
        }
        
        try
        {
            var result = await engine.WarmupAsync(cancellationToken);
            _logger.LogInformation($"✅ PooledOcrServiceウォームアップ結果: {result}");
            return result;
        }
        finally
        {
            _enginePool.Return(engine);
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

    public OcrEngineSettings GetSettings()
    {
        ThrowIfDisposed();
        
        // プール化環境では、統一設定を返す
        // 各エンジンインスタンスは同じ設定で初期化されるため
        return new OcrEngineSettings
        {
            Language = "jpn",
            DetectionThreshold = 0.3, // 現在の設定値
            RecognitionThreshold = 0.6, // 現在の設定値
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
    /// テキスト検出のみを実行（認識処理をスキップ）
    /// </summary>
    public async Task<OcrResults> DetectTextRegionsAsync(IImage image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        ThrowIfDisposed();

        _logger.LogDebug("🔍 PooledOcrService: DetectTextRegionsAsync実行");

        // プールから一時的にエンジンを取得して検出専用処理を実行
        // TODO: 実際のプール実装時により効率的な方法に改善
        
        // 現在は基本実装として、RecognizeAsyncでテキスト部分を空にする方式を採用
        var fullResult = await RecognizeAsync(image, null, cancellationToken);
        
        var detectionOnlyRegions = fullResult.TextRegions.Select(region => 
            new OcrTextRegion("", region.Bounds, region.Confidence, region.Contour, region.Direction))
            .ToList();

        return new OcrResults(
            detectionOnlyRegions,
            image,
            fullResult.ProcessingTime,
            fullResult.LanguageCode,
            fullResult.RegionOfInterest,
            ""
        );
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
