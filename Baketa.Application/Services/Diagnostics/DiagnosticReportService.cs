using Baketa.Core.Abstractions.Diagnostics;
using Baketa.Core.Abstractions.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace Baketa.Application.Services.Diagnostics;

/// <summary>
/// 診断レポート生成統一サービス実装
/// MainOverlayViewModelから抽出された診断レポートロジックを統一化
/// IHostedService実装によりアプリケーションライフサイクルと連動
/// </summary>
public sealed class DiagnosticReportService : IDiagnosticReportService, IHostedService, IDisposable
{
    private readonly IDiagnosticCollectionService _diagnosticCollectionService;
    private readonly ILogger<DiagnosticReportService> _logger;
    
    private readonly Subject<DiagnosticReportGenerated> _reportGeneratedSubject = new();
    private readonly Subject<PerformanceMetrics> _metricsSubject = new();
    private System.Threading.Timer? _metricsTimer;
    private bool _disposed;

    public DiagnosticReportService(
        IDiagnosticCollectionService diagnosticCollectionService,
        ILogger<DiagnosticReportService> logger)
    {
        _diagnosticCollectionService = diagnosticCollectionService ?? throw new ArgumentNullException(nameof(diagnosticCollectionService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // タイマーはStartAsyncで初期化（IHostedServiceパターン）
    }

    /// <inheritdoc />
    public async Task<string?> GenerateReportAsync(string trigger, string? context = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(trigger))
            throw new ArgumentException("Trigger cannot be null or empty", nameof(trigger));

        var reportStartTime = DateTime.UtcNow;
        
        try
        {
            _logger.LogInformation("📊 診断レポート生成開始: Trigger={Trigger}, Context={Context}", trigger, context);
            
            var reportPath = await _diagnosticCollectionService.GenerateReportAsync(trigger).ConfigureAwait(false);
            
            if (!string.IsNullOrEmpty(reportPath))
            {
                _logger.LogInformation("✅ 診断レポート生成成功: {ReportPath}", reportPath);
                
                // 成功イベントの発行
                _reportGeneratedSubject.OnNext(new DiagnosticReportGenerated(
                    reportPath, 
                    trigger, 
                    reportStartTime, 
                    true
                ));
                
                return reportPath;
            }
            else
            {
                _logger.LogWarning("⚠️ 診断レポート生成: 蓄積されたデータなし");
                
                // 警告イベントの発行
                _reportGeneratedSubject.OnNext(new DiagnosticReportGenerated(
                    string.Empty, 
                    trigger, 
                    reportStartTime, 
                    false, 
                    "蓄積されたデータが見つかりません"
                ));
                
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 診断レポート生成エラー: Trigger={Trigger}", trigger);
            
            // エラーイベントの発行
            _reportGeneratedSubject.OnNext(new DiagnosticReportGenerated(
                string.Empty, 
                trigger, 
                reportStartTime, 
                false, 
                ex.Message
            ));
            
            // 呼び出し側での例外処理を可能にするため、nullを返す
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<SystemHealthStatus> GetSystemHealthAsync()
    {
        try
        {
            var process = System.Diagnostics.Process.GetCurrentProcess();
            var uptime = DateTime.UtcNow - process.StartTime.ToUniversalTime();
            
            // システムメトリクスの収集
            var cpuUsage = GetCpuUsage();
            var memoryUsage = process.WorkingSet64;
            
            // アクティブサービスの確認（簡易実装）
            var activeServices = new[] { "TranslationService", "OCRService", "DiagnosticService" };
            
            // 警告・エラーの収集（実装は要調整）
            var warnings = Array.Empty<string>();
            var errors = Array.Empty<string>();
            
            var isHealthy = cpuUsage < 80.0 && memoryUsage < 1_000_000_000; // 1GB以下
            
            return new SystemHealthStatus(
                isHealthy,
                uptime,
                cpuUsage,
                memoryUsage,
                activeServices,
                warnings,
                errors
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "システムヘルス状態取得エラー");
            
            return new SystemHealthStatus(
                false,
                TimeSpan.Zero,
                0,
                0,
                Array.Empty<string>(),
                new[] { "ヘルス状態取得失敗" },
                new[] { ex.Message }
            );
        }
    }

    /// <inheritdoc />
    public IObservable<PerformanceMetrics> MetricsStream => _metricsSubject.AsObservable();

    /// <inheritdoc />
    public IObservable<DiagnosticReportGenerated> ReportGenerated => _reportGeneratedSubject.AsObservable();

    /// <summary>
    /// パフォーマンスメトリクスを収集します
    /// </summary>
    private void CollectMetrics(object? state)
    {
        if (_disposed) return;

        try
        {
            var process = System.Diagnostics.Process.GetCurrentProcess();
            var cpuUsage = GetCpuUsage();
            var memoryUsage = process.WorkingSet64;
            
            var metrics = new PerformanceMetrics(
                DateTime.UtcNow,
                cpuUsage,
                memoryUsage,
                0, // アクティブ翻訳数（実装要調整）
                TimeSpan.FromMilliseconds(100) // 平均応答時間（実装要調整）
            );
            
            _metricsSubject.OnNext(metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "パフォーマンスメトリクス収集エラー");
        }
    }

    /// <summary>
    /// CPU使用率を取得します（簡易実装）
    /// </summary>
    private double GetCpuUsage()
    {
        try
        {
            // 簡易的なCPU使用率取得
            // 実装環境に応じてより精密な実装に変更可能
            var process = System.Diagnostics.Process.GetCurrentProcess();
            return Math.Min(100.0, process.TotalProcessorTime.TotalMilliseconds / Environment.ProcessorCount);
        }
        catch
        {
            return 0.0;
        }
    }

    /// <summary>
    /// IHostedService: サービス開始時の処理
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("診断レポートサービスを開始します");
        
        // パフォーマンスメトリクス収集タイマーを開始
        _metricsTimer = new System.Threading.Timer(
            CollectMetrics, 
            null, 
            TimeSpan.FromSeconds(10), // 初回は10秒後
            TimeSpan.FromSeconds(30)  // 以降30秒間隔
        );
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// IHostedService: サービス停止時の処理
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("診断レポートサービスを停止します");
        
        // タイマーを停止
        if (_metricsTimer != null)
        {
            await _metricsTimer.DisposeAsync();
            _metricsTimer = null;
        }
        
        // 最終レポートを生成（オプション）
        try
        {
            await GenerateReportAsync("application_shutdown", "Final report on shutdown", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "シャットダウン時の最終レポート生成に失敗");
        }
    }

    /// <summary>
    /// リソースを解放します
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        _metricsTimer?.Dispose();
        _reportGeneratedSubject?.OnCompleted();
        _reportGeneratedSubject?.Dispose();
        _metricsSubject?.OnCompleted();
        _metricsSubject?.Dispose();

        _disposed = true;
    }
}