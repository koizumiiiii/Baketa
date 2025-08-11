using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.OCR.Measurement;

/// <summary>
/// アプリ起動時にOCR精度測定テストを実行するサービス
/// </summary>
public sealed class OcrAccuracyStartupService(
    IServiceProvider serviceProvider,
    ILogger<OcrAccuracyStartupService> logger) : BackgroundService
{
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    private readonly ILogger<OcrAccuracyStartupService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // アプリ起動後少し待機
            await Task.Delay(3000, stoppingToken).ConfigureAwait(false);

            _logger.LogInformation("🚀 OCR精度測定システムのスタートアップテストを開始します");

            using var scope = _serviceProvider.CreateScope();
            var testRunner = scope.ServiceProvider.GetRequiredService<OcrAccuracyTestRunner>();

            // 基本的な精度測定テストを実行
            var reportPath = await testRunner.RunBasicAccuracyTestAsync().ConfigureAwait(false);

            if (!string.IsNullOrEmpty(reportPath))
            {
                _logger.LogInformation("✅ OCR精度測定テスト完了 - レポート生成: {ReportPath}", reportPath);
                _logger.LogInformation("📄 生成されたレポートを確認してOCR精度改善の効果を評価してください");
            }
            else
            {
                _logger.LogWarning("⚠️ OCR精度測定テストは完了しましたが、レポートが生成されませんでした");
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("OCR精度測定スタートアップテストがキャンセルされました");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OCR精度測定スタートアップテスト中にエラーが発生しました");
        }
    }
}

/// <summary>
/// OCR精度測定スタートアップサービスの拡張メソッド
/// </summary>
public static class OcrAccuracyStartupServiceExtensions
{
    /// <summary>
    /// OCR精度測定スタートアップサービスを追加
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <returns>サービスコレクション</returns>
    public static IServiceCollection AddOcrAccuracyStartupService(this IServiceCollection services)
    {
        services.AddHostedService<OcrAccuracyStartupService>();
        return services;
    }
}