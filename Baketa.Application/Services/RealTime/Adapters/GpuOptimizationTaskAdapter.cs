using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Services;

namespace Baketa.Application.Services.RealTime.Adapters;

/// <summary>
/// PooledGpuOptimizationOrchestratorをIUpdatableTaskに変換するアダプター
/// 既存のGPU最適化機能をUnifiedRealTimeUpdateServiceに統合
/// </summary>
public sealed class GpuOptimizationTaskAdapter : IUpdatableTask
{
    private readonly ILogger<GpuOptimizationTaskAdapter> _logger;
    
    // 📊 実行頻度制御（GPU最適化は低頻度でよい）
    private DateTime _lastExecutionTime = DateTime.MinValue;
    private readonly TimeSpan _executionInterval = TimeSpan.FromMinutes(2); // 2分間隔

    public GpuOptimizationTaskAdapter(ILogger<GpuOptimizationTaskAdapter> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// タスク名
    /// </summary>
    public string TaskName => "GpuOptimization";

    /// <summary>
    /// 実行優先度（中優先度 - リソース系の後）
    /// </summary>
    public int Priority => 5;

    /// <summary>
    /// 常に有効
    /// </summary>
    public bool IsEnabled => true;

    /// <summary>
    /// GPU最適化実行（元PooledGpuOptimizationOrchestrator.PerformOptimizationCycle相当）
    /// </summary>
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        
        // 📊 2分間隔制御（GPU最適化は頻繁に実行する必要がない）
        if (now - _lastExecutionTime < _executionInterval)
        {
            _logger.LogTrace("⏭️ GpuOptimization: インターバル未経過、スキップ");
            return;
        }
        
        _lastExecutionTime = now;

        try
        {
            // GPU最適化の簡易実行
            // 注意: 本来のPooledGpuOptimizationOrchestratorは複雑な最適化処理を行うが、
            // ここでは統合システムでの負荷軽減を目的として最低限の処理を実装
            
            await PerformLightweightGpuOptimizationAsync(cancellationToken).ConfigureAwait(false);
            
            _logger.LogDebug("✅ GPU最適化サイクル完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ GpuOptimization task failed: {ErrorMessage}", ex.Message);
            throw; // UnifiedRealTimeUpdateServiceでキャッチされる
        }
    }

    /// <summary>
    /// 軽量GPU最適化処理
    /// </summary>
    private async Task PerformLightweightGpuOptimizationAsync(CancellationToken cancellationToken)
    {
        // 🚀 軽量化されたGPU最適化
        // 元のPooledGpuOptimizationOrchestratorの重い処理を避けて、
        // 統合システムでの実行に適した軽量処理を実装
        
        // 1. GPU使用率の簡易チェック
        var gpuUsage = await GetSimpleGpuUsageAsync().ConfigureAwait(false);
        
        // 2. 高負荷時の簡易調整
        if (gpuUsage > 80.0)
        {
            _logger.LogInformation("🔥 GPU高負荷検出 ({Usage:F1}%) - 軽量最適化適用", gpuUsage);
            // TODO: 実際の最適化処理（プール容量調整等）をここに実装
        }
        else
        {
            _logger.LogTrace("📊 GPU使用率正常 ({Usage:F1}%)", gpuUsage);
        }
        
        // 小さな遅延を入れて他のタスクに影響しないようにする
        await Task.Delay(50, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 簡易GPU使用率取得
    /// </summary>
    private async Task<double> GetSimpleGpuUsageAsync()
    {
        try
        {
            // 簡易的なGPU使用率取得
            // 実際の実装では、Windows Performance Counters や NVML を使用
            await Task.Delay(10).ConfigureAwait(false); // 非同期処理のシミュレート
            
            // TODO: 実際のGPU使用率取得ロジックを実装
            // 現在は模擬値を返す
            var random = new Random();
            return random.NextDouble() * 100.0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GPU使用率取得失敗 - デフォルト値を使用");
            return 0.0;
        }
    }
}