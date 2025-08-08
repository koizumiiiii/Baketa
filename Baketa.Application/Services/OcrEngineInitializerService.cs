using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Baketa.Core.Abstractions.OCR;
using Baketa.Infrastructure.OCR.PaddleOCR.Factory;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Baketa.Application.Services;

/// <summary>
/// OCRエンジンのバックグラウンド初期化サービス
/// Gemini推奨のシングルトン化+非同期初期化戦略を実装
/// </summary>
public sealed class OcrEngineInitializerService : IHostedService
{
    private readonly ILogger<OcrEngineInitializerService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private IOcrEngine? _initializedEngine;
    private readonly object _lockObject = new();
    private volatile bool _isInitialized;
    private volatile bool _isInitializing;
    
    public OcrEngineInitializerService(
        ILogger<OcrEngineInitializerService> logger, 
        IServiceProvider serviceProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    /// <summary>
    /// 初期化されたOCRエンジンを取得（ブロッキングなし）
    /// </summary>
    public IOcrEngine? GetInitializedEngine()
    {
        lock (_lockObject)
        {
            return _initializedEngine;
        }
    }
    
    /// <summary>
    /// OCRエンジンが初期化済みかどうか
    /// </summary>
    public bool IsInitialized => _isInitialized;
    
    /// <summary>
    /// OCRエンジンの初期化中かどうか
    /// </summary>
    public bool IsInitializing => _isInitializing;

    /// <summary>
    /// サービス開始時にバックグラウンド初期化を開始
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🚀 OCRエンジンのバックグラウンド初期化開始");

        // Task.Runでバックグラウンドスレッドで実行し、UIスレッドをブロックしない
        _ = Task.Run(async () =>
        {
            try
            {
                _isInitializing = true;
                _logger.LogInformation("⚡ PaddleOCRエンジンの段階的初期化開始...");
                
                var totalWatch = System.Diagnostics.Stopwatch.StartNew();
                
                // 🚨 Gemini推奨：段階的初期化アプローチ
                await InitializeEngineProgressivelyAsync(cancellationToken);
                
                totalWatch.Stop();
                _logger.LogInformation("✅ バックグラウンドOCRエンジン初期化完了: {ElapsedMs}ms", totalWatch.ElapsedMilliseconds);
                
                _isInitialized = true;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("⏹️ OCRエンジン初期化がキャンセルされました");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ バックグラウンドOCRエンジン初期化エラー");
            }
            finally
            {
                _isInitializing = false;
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Gemini推奨の段階的初期化アプローチ
    /// Stage 1: 基本初期化 → Stage 2: 検出モデル → Stage 3: 認識モデル
    /// </summary>
    private async Task InitializeEngineProgressivelyAsync(CancellationToken cancellationToken)
    {
        var stageWatch = System.Diagnostics.Stopwatch.StartNew();
        
        // Stage 1: 基本初期化（設定、パス解決のみ）
        stageWatch.Restart();
        _logger.LogInformation("🔄 Stage 1: 基本設定とパス解決");
        // 軽量設定のみ - 実際のモデル読み込みは遅延
        await Task.Delay(1, cancellationToken); // 非同期メソッドのダミー
        _logger.LogInformation("✅ Stage 1完了: {ElapsedMs}ms", stageWatch.ElapsedMilliseconds);
        
        // Stage 2: 検出モデルのプリロード
        stageWatch.Restart();
        _logger.LogInformation("🔄 Stage 2: 検出モデル読み込み");
        
        // 🔥 実際のOCRエンジン初期化をここで実行
        using var scope = _serviceProvider.CreateScope();
        var ocrEngineFactory = scope.ServiceProvider.GetRequiredService<IPaddleOcrEngineFactory>();
        
        // ここで17秒のボトルネックが発生するが、バックグラウンドで実行されるためUIは応答性を保つ
        var engine = await ocrEngineFactory.CreateAsync();
        
        lock (_lockObject)
        {
            _initializedEngine = engine;
        }
        
        _logger.LogInformation("✅ Stage 2完了（モデル読み込み含む）: {ElapsedMs}ms", stageWatch.ElapsedMilliseconds);
        
        // 🔥 Stage 2.5: ウォームアップ実行
        stageWatch.Restart();
        _logger.LogInformation("🔥 Stage 2.5: OCRエンジンのウォームアップ開始");
        
        try
        {
            var warmupResult = await engine.WarmupAsync(cancellationToken);
            if (warmupResult)
            {
                _logger.LogInformation("✅ Stage 2.5完了（ウォームアップ成功）: {ElapsedMs}ms", stageWatch.ElapsedMilliseconds);
            }
            else
            {
                _logger.LogWarning("⚠️ Stage 2.5: ウォームアップ失敗（処理は継続）: {ElapsedMs}ms", stageWatch.ElapsedMilliseconds);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Stage 2.5: ウォームアップ中にエラー（処理は継続）");
        }
        
        // Stage 3: 言語別認識モデルの遅延読み込み準備
        stageWatch.Restart();
        _logger.LogInformation("🔄 Stage 3: 認識モデル準備完了");
        await Task.Delay(1, cancellationToken); // 将来の拡張用
        _logger.LogInformation("✅ Stage 3完了: {ElapsedMs}ms", stageWatch.ElapsedMilliseconds);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🛑 OCRエンジン初期化サービス停止");
        
        // リソースクリーンアップ
        lock (_lockObject)
        {
            _initializedEngine?.Dispose();
            _initializedEngine = null;
        }
        
        return Task.CompletedTask;
    }
}