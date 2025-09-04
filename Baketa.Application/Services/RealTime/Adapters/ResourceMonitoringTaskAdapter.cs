using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Services;
using Baketa.Infrastructure.ResourceManagement;

namespace Baketa.Application.Services.RealTime.Adapters;

/// <summary>
/// ResourceMonitoringHostedServiceをIUpdatableTaskに変換するアダプター
/// 既存のVRAM監視機能をUnifiedRealTimeUpdateServiceに統合
/// </summary>
public sealed class ResourceMonitoringTaskAdapter : IUpdatableTask
{
    private readonly IResourceManager _resourceManager;
    private readonly ILogger<ResourceMonitoringTaskAdapter> _logger;

    public ResourceMonitoringTaskAdapter(
        IResourceManager resourceManager,
        ILogger<ResourceMonitoringTaskAdapter> logger)
    {
        _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// タスク名
    /// </summary>
    public string TaskName => "ResourceMonitoring";

    /// <summary>
    /// 実行優先度（最高優先度 - VRAM監視は重要）
    /// </summary>
    public int Priority => 1;

    /// <summary>
    /// 常に有効
    /// </summary>
    public bool IsEnabled => true;

    /// <summary>
    /// VRAM監視実行（元ResourceMonitoringHostedService.ExecuteVramMonitoring相当）
    /// </summary>
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            // HybridResourceManager.AdjustParallelismAsync実行
            // 内部でMonitorVramDynamicallyAsync → CalculateVramPressureLevelが呼び出される
            await _resourceManager.AdjustParallelismAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogDebug("✅ Phase 3.2: VRAM監視5-tier判定実行完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ResourceMonitoring task failed: {ErrorMessage}", ex.Message);
            throw; // UnifiedRealTimeUpdateServiceでキャッチされる
        }
    }
}