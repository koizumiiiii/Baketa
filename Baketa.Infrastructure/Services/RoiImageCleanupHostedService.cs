using System.IO;
using Baketa.Core.Settings;
using Baketa.Infrastructure.OCR.PaddleOCR.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.Services;

/// <summary>
/// ROI画像の自動クリーンアップサービス
/// 起動時 + 定期的に古いROI画像を削除してディスク容量を節約
/// </summary>
public sealed class RoiImageCleanupHostedService : BackgroundService
{
    private readonly ILogger<RoiImageCleanupHostedService> _logger;
    private readonly RoiDiagnosticsSettings _settings;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(24); // 24時間ごとにクリーンアップ

    public RoiImageCleanupHostedService(
        ILogger<RoiImageCleanupHostedService> logger,
        IOptions<RoiDiagnosticsSettings> settings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("🧹 ROI画像自動クリーンアップサービス開始 - 保持期間: {Days}日", _settings.MaxImageFileRetentionDays);

            // ROI画像出力が無効化されている場合はクリーンアップのみ実行
            if (!_settings.EnableRoiImageOutput)
            {
                _logger.LogInformation("ROI画像出力は無効化されています（本番環境）- クリーンアップのみ実行");
            }

            // 起動直後に少し待機（他のサービスの初期化完了を待つ）
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);

            // 起動時に即座にクリーンアップ実行
            await CleanupRoiImagesAsync(stoppingToken).ConfigureAwait(false);

            // 定期的なクリーンアップループ
            while (!stoppingToken.IsCancellationRequested)
            {
                // 次のクリーンアップまで待機（24時間）
                await Task.Delay(_cleanupInterval, stoppingToken).ConfigureAwait(false);

                // クリーンアップ実行
                await CleanupRoiImagesAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("ROI画像クリーンアップサービスが正常に停止しました");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ROI画像クリーンアップサービスでエラーが発生しました");
        }
    }

    /// <summary>
    /// ROI画像のクリーンアップを実行
    /// </summary>
    private async Task CleanupRoiImagesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var outputPath = _settings.GetExpandedOutputPath();

            if (!Directory.Exists(outputPath))
            {
                _logger.LogDebug("ROI画像ディレクトリが存在しません: {Path}", outputPath);
                return;
            }

            _logger.LogInformation("🧹 ROI画像クリーンアップ開始: {Path}", outputPath);

            // ImageDiagnosticsSaverを使用してクリーンアップ（loggerはnullを渡す）
            using var saver = new ImageDiagnosticsSaver(outputPath, logger: null);
            var maxAge = TimeSpan.FromDays(_settings.MaxImageFileRetentionDays);

            await saver.CleanupOldFilesAsync(maxAge).ConfigureAwait(false);

            // クリーンアップ後の統計情報
            var remainingFiles = Directory.GetFiles(outputPath, "*.*", SearchOption.AllDirectories);
            var totalSizeMB = remainingFiles.Sum(f => new FileInfo(f).Length) / (1024.0 * 1024.0);

            _logger.LogInformation(
                "✅ ROI画像クリーンアップ完了 - 残存ファイル数: {Count}, 合計サイズ: {Size:F2}MB",
                remainingFiles.Length,
                totalSizeMB);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ROI画像クリーンアップ中にエラーが発生しました");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ROI画像クリーンアップサービスを停止しています...");
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }
}
