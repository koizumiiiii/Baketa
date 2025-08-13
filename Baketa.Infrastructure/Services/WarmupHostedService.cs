using Baketa.Core.Abstractions.GPU;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Services;

/// <summary>
/// ウォームアップホストサービス（Issue #143: アプリケーション起動時の自動ウォームアップ）
/// BackgroundServiceとして動作し、アプリケーション開始時に非同期ウォームアップを自動実行
/// </summary>
public sealed class WarmupHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WarmupHostedService> _logger;

    public WarmupHostedService(
        IServiceProvider serviceProvider,
        ILogger<WarmupHostedService> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("🚀 ウォームアップホストサービス開始 - Issue #143 コールドスタート遅延根絶");

            // アプリケーション起動後、少し待機してからウォームアップ開始
            // UI初期化完了を待つため
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);

            // IWarmupServiceを取得してウォームアップ実行
            using var scope = _serviceProvider.CreateScope();
            var warmupService = scope.ServiceProvider.GetService<IWarmupService>();
            
            if (warmupService == null)
            {
                _logger.LogWarning("IWarmupServiceが見つかりません");
                return;
            }

            _logger.LogInformation("バックグラウンドウォームアップを開始します");
            
            // 進捗通知の購読
            warmupService.WarmupProgressChanged += OnWarmupProgressChanged;
            
            try
            {
                // ウォームアップ開始（非同期実行）
                await warmupService.StartWarmupAsync(stoppingToken).ConfigureAwait(false);
                
                // ウォームアップ完了を待機（最大5分）
                var waitTimeout = TimeSpan.FromMinutes(5);
                var success = await warmupService.WaitForWarmupAsync(waitTimeout, stoppingToken).ConfigureAwait(false);
                
                if (success)
                {
                    _logger.LogInformation("🎉 バックグラウンドウォームアップが正常に完了しました");
                }
                else
                {
                    _logger.LogWarning("⚠️ バックグラウンドウォームアップがタイムアウトしました（{Timeout}分）", waitTimeout.TotalMinutes);
                }
            }
            finally
            {
                // 進捗通知の購読解除
                warmupService.WarmupProgressChanged -= OnWarmupProgressChanged;
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("ウォームアップホストサービスがキャンセルされました");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ウォームアップホストサービスでエラーが発生しました");
        }
    }

    private void OnWarmupProgressChanged(object? sender, WarmupProgressEventArgs e)
    {
        switch (e.Phase)
        {
            case WarmupPhase.Starting:
                _logger.LogDebug("ウォームアップ開始: {Status}", e.Status);
                break;
            
            case WarmupPhase.GpuDetection:
                _logger.LogDebug("GPU環境検出: {Progress:P1} - {Status}", e.Progress, e.Status);
                break;
            
            case WarmupPhase.OcrInitialization:
            case WarmupPhase.OcrWarmup:
                _logger.LogDebug("OCRウォームアップ: {Progress:P1} - {Status}", e.Progress, e.Status);
                break;
            
            case WarmupPhase.TranslationInitialization:
            case WarmupPhase.TranslationWarmup:
                _logger.LogDebug("翻訳ウォームアップ: {Progress:P1} - {Status}", e.Progress, e.Status);
                break;
            
            case WarmupPhase.Completed:
                _logger.LogInformation("🎯 ウォームアップ完了: {Progress:P1} - {Status}", e.Progress, e.Status);
                break;
            
            default:
                _logger.LogDebug("ウォームアップ進捗: {Progress:P1} - {Status}", e.Progress, e.Status);
                break;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ウォームアップホストサービスを停止します");
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}