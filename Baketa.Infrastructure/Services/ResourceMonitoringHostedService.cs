using Baketa.Core.Abstractions.Monitoring;
using Baketa.Infrastructure.ResourceManagement;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.Services;

/// <summary>
/// VRAM監視5-tier圧迫度レベル判定を定期実行するホステッドサービス
/// Phase 3.2完全実装のための核心コンポーネント
/// </summary>
public sealed class ResourceMonitoringHostedService : IHostedService, IDisposable
{
    private readonly IResourceManager _resourceManager;
    private readonly ResourceMonitoringSettings _settings;
    private readonly ILogger<ResourceMonitoringHostedService> _logger;
    private System.Threading.Timer? _monitoringTimer;
    private bool _disposed;

    public ResourceMonitoringHostedService(
        IResourceManager resourceManager,
        IOptions<ResourceMonitoringSettings> settings,
        ILogger<ResourceMonitoringHostedService> logger)
    {
        _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        _settings = settings.Value ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_disposed) return Task.CompletedTask;

        if (!_settings.IsValid)
        {
            _logger.LogError("❌ ResourceMonitoringSettings設定が無効です - VRAM監視を開始できません");
            return Task.CompletedTask;
        }

        if (!_settings.EnableGpuMonitoring)
        {
            _logger.LogInformation("ℹ️ GPU監視が無効化されています - VRAM監視をスキップ");
            return Task.CompletedTask;
        }

        _logger.LogInformation("🚀 Phase 3.2: VRAM監視5-tier圧迫度レベル判定システム開始");
        _logger.LogInformation("⚙️ 監視間隔: {IntervalMs}ms, GPU監視: {EnableGpu}", 
            _settings.MonitoringIntervalMs, _settings.EnableGpuMonitoring);

        // 定期実行タイマー開始
        _monitoringTimer = new System.Threading.Timer(
            ExecuteVramMonitoring, 
            null, 
            TimeSpan.FromSeconds(1), // 1秒後に最初の実行
            TimeSpan.FromMilliseconds(_settings.MonitoringIntervalMs));

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_disposed) return;

        _logger.LogInformation("⏹️ Phase 3.2: VRAM監視システム停止中...");

        if (_monitoringTimer != null)
        {
            await _monitoringTimer.DisposeAsync();
            _monitoringTimer = null;
        }

        _logger.LogInformation("✅ Phase 3.2: VRAM監視システム停止完了");
    }

    /// <summary>
    /// VRAM監視実行 - HybridResourceManager.AdjustParallelismAsyncを呼び出し
    /// </summary>
    private async void ExecuteVramMonitoring(object? state)
    {
        if (_disposed || _monitoringTimer == null) return;

        try
        {
            // HybridResourceManager.AdjustParallelismAsync実行
            // 内部でMonitorVramDynamicallyAsync → CalculateVramPressureLevelが呼び出される
            await _resourceManager.AdjustParallelismAsync(CancellationToken.None);

            _logger.LogDebug("✅ Phase 3.2: VRAM監視5-tier判定実行完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Phase 3.2: VRAM監視実行中にエラーが発生");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _monitoringTimer?.Dispose();
        _disposed = true;

        _logger.LogDebug("🗑️ ResourceMonitoringHostedService: リソース解放完了");
    }
}