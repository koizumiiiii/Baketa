using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Services;
using Baketa.Application.Services.Diagnostics;

namespace Baketa.Application.Services.RealTime.Adapters;

/// <summary>
/// DiagnosticReportServiceのメトリクス収集をIUpdatableTaskに変換するアダプター
/// 既存の30秒間隔パフォーマンス収集機能をUnifiedRealTimeUpdateServiceに統合
/// </summary>
public sealed class DiagnosticMetricsTaskAdapter : IUpdatableTask
{
    private readonly IDiagnosticReportService _diagnosticReportService;
    private readonly ILogger<DiagnosticMetricsTaskAdapter> _logger;
    
    // 📊 実行頻度制御（元30秒間隔を維持）
    private DateTime _lastExecutionTime = DateTime.MinValue;
    private readonly TimeSpan _executionInterval = TimeSpan.FromSeconds(30);

    public DiagnosticMetricsTaskAdapter(
        IDiagnosticReportService diagnosticReportService,
        ILogger<DiagnosticMetricsTaskAdapter> logger)
    {
        _diagnosticReportService = diagnosticReportService ?? throw new ArgumentNullException(nameof(diagnosticReportService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// タスク名
    /// </summary>
    public string TaskName => "DiagnosticMetrics";

    /// <summary>
    /// 実行優先度（低優先度 - 診断は最後でよい）
    /// </summary>
    public int Priority => 8;

    /// <summary>
    /// 常に有効
    /// </summary>
    public bool IsEnabled => true;

    /// <summary>
    /// 診断メトリクス収集実行（元DiagnosticReportService.CollectMetrics相当）
    /// </summary>
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        
        // 📊 30秒間隔制御（元Timerの動作を再現）
        if (now - _lastExecutionTime < _executionInterval)
        {
            _logger.LogTrace("⏭️ DiagnosticMetrics: インターバル未経過、スキップ");
            return;
        }
        
        _lastExecutionTime = now;

        try
        {
            // システムヘルス状態取得（非同期版）
            var healthStatus = await _diagnosticReportService.GetSystemHealthAsync().ConfigureAwait(false);
            
            _logger.LogDebug("✅ DiagnosticMetrics収集完了: Health={IsHealthy}, CPU={CpuUsage:F1}%, Memory={MemoryMB:F1}MB", 
                healthStatus.IsHealthy, 
                healthStatus.CpuUsage, 
                healthStatus.MemoryUsageBytes / (1024.0 * 1024.0));
                
            // メトリクスストリーム通知（既存のReactiveXストリーム連携）
            // DiagnosticReportServiceの内部MetricsSubjectが自動的に通知済み
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ DiagnosticMetrics task failed: {ErrorMessage}", ex.Message);
            throw; // UnifiedRealTimeUpdateServiceでキャッチされる
        }
    }
}