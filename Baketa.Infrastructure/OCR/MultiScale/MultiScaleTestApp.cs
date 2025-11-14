using System;
using System.Threading.Tasks;
using Baketa.Infrastructure.DI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.OCR.MultiScale;

/// <summary>
/// マルチスケールOCR処理のテストアプリケーション
/// </summary>
public static class MultiScaleTestApp
{
    /// <summary>
    /// メインテストエントリーポイント
    /// </summary>
    public static async Task RunTestAsync()
    {
        Console.WriteLine("🔍 マルチスケールOCR処理テスト開始");

        try
        {
            // DIコンテナを構築
            var services = new ServiceCollection();

            // ログ設定
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            // 必要なサービスを登録
            var paddleOcrModule = new PaddleOcrModule();
            paddleOcrModule.RegisterServices(services);

            var serviceProvider = services.BuildServiceProvider();

            // マルチスケールテストランナーを取得
            var testRunner = serviceProvider.GetRequiredService<MultiScaleTestRunner>();

            // テスト実行
            Console.WriteLine("📊 小文字テキスト認識テスト実行中...");
            await testRunner.TestSmallTextRecognitionAsync().ConfigureAwait(false);

            Console.WriteLine("✅ マルチスケールOCRテスト完了");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ テスト実行エラー: {ex.Message}");
            Console.WriteLine($"詳細: {ex}");
        }
    }

    /// <summary>
    /// 既存の画像でマルチスケール処理をテスト
    /// </summary>
    public static async Task TestWithRealImageAsync(string imagePath)
    {
        Console.WriteLine($"🖼️ 実画像でのマルチスケールテスト: {imagePath}");

        try
        {
            // DIコンテナを構築
            var services = new ServiceCollection();

            // ログ設定
            services.AddLogging(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Debug);
            });

            // 必要なサービスを登録
            var paddleOcrModule = new PaddleOcrModule();
            paddleOcrModule.RegisterServices(services);

            var serviceProvider = services.BuildServiceProvider();

            // サービスを取得
            var multiScaleProcessor = serviceProvider.GetRequiredService<IMultiScaleOcrProcessor>();
            var ocrEngine = serviceProvider.GetRequiredService<Baketa.Core.Abstractions.OCR.IOcrEngine>();
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger("MultiScaleTestApp");

            // 画像ファイルが存在するかチェック
            if (!System.IO.File.Exists(imagePath))
            {
                Console.WriteLine($"❌ 画像ファイルが見つかりません: {imagePath}");
                return;
            }

            // 画像をロード（簡易実装）
            var imageBytes = await System.IO.File.ReadAllBytesAsync(imagePath).ConfigureAwait(false);
            var image = new Baketa.Core.Services.Imaging.AdvancedImage(
                imageBytes, 800, 600, Baketa.Core.Abstractions.Imaging.ImageFormat.Png);

            logger.LogInformation("📷 画像ロード完了: {Path}", imagePath);

            // 通常のOCR処理
            var normalStart = DateTime.Now;
            var normalResult = await ocrEngine.RecognizeAsync(image).ConfigureAwait(false);
            var normalTime = DateTime.Now - normalStart;

            logger.LogInformation("⚪ 通常OCR: {Regions}リージョン, {Time}ms",
                normalResult.TextRegions.Count, normalTime.TotalMilliseconds);

            // マルチスケールOCR処理
            var multiStart = DateTime.Now;
            var multiResult = await multiScaleProcessor.ProcessWithDetailsAsync(image, ocrEngine).ConfigureAwait(false);
            var multiTime = DateTime.Now - multiStart;

            logger.LogInformation("🔍 マルチスケール: {Regions}リージョン, {Time}ms",
                multiResult.MergedResult.TextRegions.Count, multiTime.TotalMilliseconds);

            // 改善効果を表示
            var improvement = multiResult.MergedResult.TextRegions.Count - normalResult.TextRegions.Count;
            var timeRatio = multiTime.TotalMilliseconds / normalTime.TotalMilliseconds;

            Console.WriteLine($"📈 結果比較:");
            Console.WriteLine($"   検出リージョン: {normalResult.TextRegions.Count} → {multiResult.MergedResult.TextRegions.Count} (差分: {improvement})");
            Console.WriteLine($"   処理時間: {normalTime.TotalMilliseconds:F0}ms → {multiTime.TotalMilliseconds:F0}ms (比率: {timeRatio:F1}x)");
            Console.WriteLine($"   改善スコア: {multiResult.Stats.ImprovementScore:F2}");

            if (improvement > 0)
            {
                Console.WriteLine("✅ マルチスケール処理により追加のテキストが検出されました");
            }

        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 実画像テストエラー: {ex.Message}");
        }
    }
}
