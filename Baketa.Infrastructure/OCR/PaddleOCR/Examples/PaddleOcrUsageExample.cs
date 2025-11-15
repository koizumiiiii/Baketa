using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Infrastructure.DI;
using Baketa.Infrastructure.OCR.PaddleOCR.Engine;
using Baketa.Infrastructure.OCR.PaddleOCR.Initialization;
using Baketa.Infrastructure.OCR.PaddleOCR.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.OCR.PaddleOCR.Examples;

/// <summary>
/// PaddleOCR使用例のサンプルコード
/// </summary>
[SuppressMessage("Globalization", "CA1303:Do not pass literals as localized parameters",
    Justification = "このクラスは例文・デモ用コードであり、ローカライゼーションは不要")]
public static class PaddleOcrUsageExample
{
    /// <summary>
    /// 基本的なOCR処理の実行例
    /// </summary>
    public static async Task<bool> BasicOcrExampleAsync()
    {
        // 1. DIコンテナの設定
        var services = new ServiceCollection();

        // ログ設定（シンプルな実装）
        services.AddLogging(builder =>
        {
            // コンソールログの代わりにデバッグログを使用
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // PaddleOCRサービスの登録
        var paddleOcrModule = new PaddleOcrModule();
        paddleOcrModule.RegisterServices(services);

#pragma warning disable CA2007 // ServiceProvider.DisposeAsyncはUIコンテキストではないためConfigureAwait不要
        await using var serviceProvider = services.BuildServiceProvider();
#pragma warning restore CA2007

        try
        {
            // 2. PaddleOCR初期化システムの取得と初期化
            var initializer = serviceProvider.GetRequiredService<PaddleOcrInitializer>();
            var success = await initializer.InitializeAsync().ConfigureAwait(false);

            if (!success)
            {
                Console.WriteLine("PaddleOCR初期化に失敗しました");
                return false;
            }

            Console.WriteLine("PaddleOCR初期化完了");

            // 3. OCRエンジンの取得と初期化
            var ocrEngine = serviceProvider.GetRequiredService<PaddleOcrEngine>();

            // 英語モデルで初期化
            var settings = new OcrEngineSettings
            {
                Language = "eng",
                UseGpu = false,
                EnableMultiThread = true,
                WorkerCount = 2
            };

            var engineInitSuccess = await ocrEngine.InitializeAsync(
                settings
            ).ConfigureAwait(false);

            if (!engineInitSuccess)
            {
                Console.WriteLine("OCRエンジン初期化に失敗しました");
                return false;
            }

            Console.WriteLine("OCRエンジン初期化完了");

            // 4. サンプル画像でのOCR実行（実際の画像があれば）
            // var image = LoadSampleImage(); // 実装は省略
            // var results = await ocrEngine.RecognizeAsync(image);
            // DisplayResults(results);

            Console.WriteLine("OCR処理例の実行完了");

            return true;
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"引数エラーが発生しました: {ex.Message}");
            return false;
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"操作エラーが発生しました: {ex.Message}");
            return false;
        }
        catch (System.IO.IOException ex)
        {
            Console.WriteLine($"I/Oエラーが発生しました: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 言語切り替えの実行例
    /// </summary>
    public static async Task<bool> LanguageSwitchExampleAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug));

        var paddleOcrModule = new PaddleOcrModule();
        paddleOcrModule.RegisterServices(services);

#pragma warning disable CA2007 // ServiceProvider.DisposeAsyncはUIコンテキストではないためConfigureAwait不要
        await using var serviceProvider = services.BuildServiceProvider();
#pragma warning restore CA2007

        try
        {
            var initializer = serviceProvider.GetRequiredService<PaddleOcrInitializer>();
            await initializer.InitializeAsync().ConfigureAwait(false);

            var ocrEngine = serviceProvider.GetRequiredService<PaddleOcrEngine>();

            // 英語で初期化
            var engSettings = new OcrEngineSettings { Language = "eng" };
            await ocrEngine.InitializeAsync(engSettings).ConfigureAwait(false);
            Console.WriteLine($"現在の言語: {ocrEngine.CurrentLanguage}");

            // 日本語に切り替え
            var jpnSettings = new OcrEngineSettings { Language = "jpn" };
            await ocrEngine.ApplySettingsAsync(jpnSettings).ConfigureAwait(false);
            Console.WriteLine($"切り替え後の言語: {ocrEngine.CurrentLanguage}");

            return true;
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"言語切り替え例で引数エラー: {ex.Message}");
            return false;
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"言語切り替え例で操作エラー: {ex.Message}");
            return false;
        }
        catch (System.IO.IOException ex)
        {
            Console.WriteLine($"言語切り替え例でI/Oエラー: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// ROI（関心領域）指定の実行例
    /// </summary>
    public static async Task<bool> RoiOcrExampleAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug));

        var paddleOcrModule = new PaddleOcrModule();
        paddleOcrModule.RegisterServices(services);

#pragma warning disable CA2007 // ServiceProvider.DisposeAsyncはUIコンテキストではないためConfigureAwait不要
        await using var serviceProvider = services.BuildServiceProvider();
#pragma warning restore CA2007

        try
        {
            var initializer = serviceProvider.GetRequiredService<PaddleOcrInitializer>();
            await initializer.InitializeAsync().ConfigureAwait(false);

            var ocrEngine = serviceProvider.GetRequiredService<PaddleOcrEngine>();
            var roiSettings = new OcrEngineSettings { Language = "eng" };
            await ocrEngine.InitializeAsync(roiSettings).ConfigureAwait(false);

            // ROI指定でのOCR実行例
            // var image = LoadSampleImage(); // 実装は省略
            // var roi = new Rectangle(100, 100, 300, 200); // 関心領域
            // var results = await ocrEngine.RecognizeAsync(image, roi);

            Console.WriteLine("ROI指定OCR例の実行完了");
            return true;
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"ROI指定OCR例で引数エラー: {ex.Message}");
            return false;
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"ROI指定OCR例で操作エラー: {ex.Message}");
            return false;
        }
        catch (System.IO.IOException ex)
        {
            Console.WriteLine($"ROI指定OCR例でI/Oエラー: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// OCR結果の表示例
    /// </summary>
    private static void DisplayResults(Sdcb.PaddleOCR.PaddleOcrResult[] paddleResults)
    {
        if (paddleResults == null || paddleResults.Length == 0)
        {
            Console.WriteLine("認識されたテキストはありません");
            return;
        }

        var results = Baketa.Infrastructure.OCR.PaddleOCR.Results.OcrResult.FromPaddleResults(paddleResults);

        Console.WriteLine($"認識されたテキスト数: {results.Length}");

        foreach (var result in results)
        {
            Console.WriteLine($"テキスト: '{result.Text}'");
            Console.WriteLine($"信頼度: {result.Confidence:F3}");
            Console.WriteLine($"位置: {result.BoundingBox}");
            Console.WriteLine("---");
        }

        // var collection = new Baketa.Core.Abstractions.OCR.OcrResults(
        //     results.Select(r => r.ToTextRegion()).ToList(),
        //     サンプル画像パラメータ（実際の実装では適切な値を設定）
        //     testImage, // IImageの実装が必要
        //     TimeSpan.FromMilliseconds(0), // 実際の処理時間は測定が必要
        //     "eng"
        // );

        // Console.WriteLine($"統計情報: {collection}");
        // Console.WriteLine($"結合テキスト: '{collection.Text}'");
    }

    /// <summary>
    /// 全ての使用例を実行
    /// </summary>
    public static async Task RunAllExamplesAsync()
    {
        Console.WriteLine("=== PaddleOCR使用例の実行開始 ===");

        Console.WriteLine("\n1. 基本的なOCR処理例");
        await BasicOcrExampleAsync().ConfigureAwait(false);

        Console.WriteLine("\n2. 言語切り替え例");
        await LanguageSwitchExampleAsync().ConfigureAwait(false);

        Console.WriteLine("\n3. ROI指定OCR例");
        await RoiOcrExampleAsync().ConfigureAwait(false);

        Console.WriteLine("\n=== 全ての使用例実行完了 ===");
    }
}
