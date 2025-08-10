using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Abstractions.Performance;
using Baketa.Core.Abstractions.Logging;
using Baketa.Infrastructure.OCR.PaddleOCR.Engine;
using Baketa.Infrastructure.OCR.PaddleOCR.Models;
using Baketa.Infrastructure.OCR.TextProcessing;
using Baketa.Infrastructure.OCR.PostProcessing;

namespace Baketa.Infrastructure.OCR.PaddleOCR.Factory;

/// <summary>
/// PaddleOCRエンジンファクトリー実装
/// プール化されたOCRエンジンインスタンスの作成・管理を担当
/// </summary>
public sealed class PaddleOcrEngineFactory : IPaddleOcrEngineFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PaddleOcrEngineFactory> _logger;
    
    public PaddleOcrEngineFactory(
        IServiceProvider serviceProvider,
        ILogger<PaddleOcrEngineFactory> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 新しいPaddleOCRエンジンインスタンスを作成します
    /// </summary>
    public async Task<IOcrEngine> CreateAsync()
    {
        try
        {
            _logger.LogDebug("🏭 PaddleOcrEngineFactory: 新しいエンジンインスタンス作成開始");
            
            // 必要な依存関係を解決（PaddleOcrModuleと同じロジック）
            var modelPathResolver = _serviceProvider.GetRequiredService<IModelPathResolver>();
            var ocrPreprocessingService = _serviceProvider.GetRequiredService<IOcrPreprocessingService>();
            var textMerger = _serviceProvider.GetRequiredService<ITextMerger>();
            var ocrPostProcessor = _serviceProvider.GetRequiredService<IOcrPostProcessor>();
            var gpuMemoryManager = _serviceProvider.GetRequiredService<IGpuMemoryManager>();
            var engineLogger = _serviceProvider.GetService<ILogger<PaddleOcrEngine>>();
            
            // 環境判定（PaddleOcrModuleと同じロジック）
            string? envValue = Environment.GetEnvironmentVariable("BAKETA_FORCE_PRODUCTION_OCR");
            bool forceProduction = envValue == "true";
            
            IOcrEngine engine;
            
            // 🔥 プール化環境では実際のOCRを使用（SafePaddleOcrEngineは空結果を返すため）
            _logger.LogDebug("🏊 プール化環境でのエンジン選択 - 環境変数: '{EnvValue}', 強制本番: {ForceProduction}", envValue ?? "null", forceProduction);
            
            if (forceProduction || true) // 🚨 緊急修正: プール化では常に実際のOCRエンジンを使用
            {
                _logger.LogDebug("⚡ 実際のPaddleOCRエンジン作成（プール化対応）");
                
                // 🔥 重要: シングルトンパターンを無効化するため、直接インスタンス作成
                var unifiedSettingsService = _serviceProvider.GetRequiredService<IUnifiedSettingsService>();
                var unifiedLoggingService = _serviceProvider.GetService<IUnifiedLoggingService>();
                engine = new NonSingletonPaddleOcrEngine(
                    modelPathResolver, 
                    ocrPreprocessingService, 
                    textMerger, 
                    ocrPostProcessor, 
                    gpuMemoryManager,
                    unifiedSettingsService,
                    unifiedLoggingService,
                    engineLogger);
            }
            else
            {
                _logger.LogDebug("🔒 SafePaddleOcrEngine作成（開発・テスト環境）");
                engine = new SafePaddleOcrEngine(modelPathResolver, engineLogger, skipRealInitialization: false);
            }
            
            _logger.LogDebug("🔧 PaddleOcrEngineFactory: エンジン初期化開始 - 型: {EngineType}", engine.GetType().Name);
            
            // プール化されたエンジンを初期化
            var initialized = await engine.InitializeAsync();
            if (!initialized)
            {
                _logger.LogWarning("⚠️ PaddleOcrEngineFactory: エンジン初期化失敗 - 型: {EngineType}", engine.GetType().Name);
                engine.Dispose();
                throw new InvalidOperationException($"OCRエンジンの初期化に失敗しました: {engine.GetType().Name}");
            }
            
            _logger.LogDebug("✅ PaddleOcrEngineFactory: エンジン作成・初期化完了 - 型: {EngineType}", engine.GetType().Name);
            return engine;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ PaddleOcrEngineFactory: エンジンインスタンス作成エラー");
            throw;
        }
    }

    /// <summary>
    /// エンジンインスタンスをクリーンアップします
    /// </summary>
    public async Task CleanupAsync(IOcrEngine engine)
    {
        if (engine == null) return;
        
        try
        {
            _logger.LogDebug("🧹 PaddleOcrEngineFactory: エンジンクリーンアップ開始 - 型: {EngineType}", engine.GetType().Name);
            
            // OCRエンジンの状態をリセット（必要に応じて）
            // 現在の実装では特別なクリーンアップは不要
            
            _logger.LogDebug("✅ PaddleOcrEngineFactory: エンジンクリーンアップ完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "⚠️ PaddleOcrEngineFactory: エンジンクリーンアップでエラー");
            // クリーンアップエラーは致命的ではないため、例外をthrowしない
        }
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// エンジンインスタンスが再利用可能かどうかを判定します
    /// </summary>
    public bool IsReusable(IOcrEngine engine)
    {
        if (engine == null) return false;
        
        try
        {
            // エンジンの基本状態をチェック
            // IsInitializedプロパティで生存状態を判定
            var isInitialized = engine.IsInitialized;
            
            // 追加の健全性チェック: 設定取得が可能かテスト
            var settings = engine.GetSettings();
            
            return isInitialized && settings != null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ PaddleOcrEngineFactory: エンジン再利用性判定エラー - 再利用不可として処理");
            return false;
        }
    }
}

/// <summary>
/// シングルトンパターンを無効化したPaddleOcrEngine
/// プール化で複数インスタンスを許可するため
/// </summary>
internal sealed class NonSingletonPaddleOcrEngine : PaddleOcrEngine
{
    public NonSingletonPaddleOcrEngine(
        IModelPathResolver modelPathResolver,
        IOcrPreprocessingService ocrPreprocessingService,
        ITextMerger textMerger,
        IOcrPostProcessor ocrPostProcessor,
        IGpuMemoryManager gpuMemoryManager,
        IUnifiedSettingsService unifiedSettingsService,
        IUnifiedLoggingService? unifiedLoggingService = null,
        ILogger<PaddleOcrEngine>? logger = null)
        : base(modelPathResolver, ocrPreprocessingService, textMerger, ocrPostProcessor, gpuMemoryManager, unifiedSettingsService, unifiedLoggingService, logger)
    {
        // 🚨 重要: 親クラスのシングルトンチェックを意図的にバイパス
        // プール環境では複数インスタンスが必要
    }

    /// <summary>
    /// プール化環境ではシングルトンチェックをスキップ
    /// </summary>
    protected override void TrackInstanceCreation()
    {
        // シングルトンチェックを無効化 - プール環境では複数インスタンスが正常
        // ログのみ出力してエラーチェックはスキップ
        Console.WriteLine($"🏊 NonSingletonPaddleOcrEngine: プール用インスタンス作成 - Hash: {this.GetHashCode()}");
    }
}