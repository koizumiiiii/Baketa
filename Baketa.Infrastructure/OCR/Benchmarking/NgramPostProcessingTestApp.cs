using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Baketa.Infrastructure.DI;
using Baketa.Infrastructure.OCR.PostProcessing;
using Baketa.Infrastructure.OCR.PostProcessing.NgramModels;

namespace Baketa.Infrastructure.OCR.Benchmarking;

/// <summary>
/// N-gramベース後処理のテストアプリケーション
/// </summary>
public static class NgramPostProcessingTestApp
{
    /// <summary>
    /// N-gramベース後処理の包括的テスト
    /// </summary>
    public static async Task RunComprehensiveTestAsync()
    {
        Console.WriteLine("=== N-gramベース後処理 包括的テスト ===");
        
        // DIコンテナの設定
        var services = new ServiceCollection();
        
        // ロギング設定
        services.AddLogging(builder =>
        {
            builder.AddConsole()
                   .SetMinimumLevel(LogLevel.Information);
        });
        
        // 必要なモジュールの登録
        var paddleOcrModule = new PaddleOcrModule();
        paddleOcrModule.RegisterServices(services);
        
        var serviceProvider = services.BuildServiceProvider();
        
        try
        {
            // N-gramベンチマークの実行
            var benchmark = serviceProvider.GetRequiredService<NgramPostProcessingBenchmark>();
            var result = await benchmark.RunComprehensiveBenchmarkAsync();
            
            // 結果の表示
            Console.WriteLine("\n=== ベンチマーク結果 ===");
            Console.WriteLine($"最適手法: {result.BestMethod.MethodName}");
            Console.WriteLine($"精度: {result.BestMethod.AccuracyScore:F2}%");
            Console.WriteLine($"修正率: {result.BestMethod.CorrectionRate:F2}%");
            Console.WriteLine($"処理時間: {result.BestMethod.ProcessingTime.TotalMilliseconds:F1}ms");
            
            Console.WriteLine("\n=== 各手法の詳細結果 ===");
            foreach (var method in result.Results)
            {
                Console.WriteLine($"{method.MethodName}:");
                Console.WriteLine($"  精度: {method.AccuracyScore:F2}%");
                Console.WriteLine($"  修正率: {method.CorrectionRate:F2}%");
                Console.WriteLine($"  処理時間: {method.ProcessingTime.TotalMilliseconds:F1}ms");
                Console.WriteLine();
            }
            
            Console.WriteLine("=== 推奨設定 ===");
            Console.WriteLine(result.Recommendations);
            
        }
        catch (Exception ex)
        {
            Console.WriteLine($"テスト実行エラー: {ex.Message}");
            Console.WriteLine($"詳細: {ex.StackTrace}");
        }
        finally
        {
            serviceProvider.Dispose();
        }
    }
    
    /// <summary>
    /// 個別後処理テスト
    /// </summary>
    public static async Task RunIndividualProcessingTestAsync()
    {
        Console.WriteLine("=== 個別後処理テスト ===");
        
        var services = new ServiceCollection();
        
        services.AddLogging(builder =>
        {
            builder.AddConsole()
                   .SetMinimumLevel(LogLevel.Warning);
        });
        
        var paddleOcrModule = new PaddleOcrModule();
        paddleOcrModule.RegisterServices(services);
        
        var serviceProvider = services.BuildServiceProvider();
        
        try
        {
            // ハイブリッドプロセッサを取得
            var hybridFactory = serviceProvider.GetRequiredService<HybridOcrPostProcessorFactory>();
            var processor = await hybridFactory.CreateAsync();
            
            // テストケース
            var testCases = new[]
            {
                "車体テスト",
                "オンボーデイシグ (院法体勝)の恐計",
                "役計書",
                "データベース", // 正しいケース
                "APIの応答時間",
                "バージョン1.O.O"
            };
            
            Console.WriteLine("入力テキスト → 修正後テキスト");
            Console.WriteLine("==========================");
            
            foreach (var testCase in testCases)
            {
                var result = await processor.ProcessAsync(testCase);
                var status = testCase == result ? "(変更なし)" : "(修正済み)";
                Console.WriteLine($"{testCase} → {result} {status}");
            }
            
            // 処理順序比較テスト
            Console.WriteLine("\n=== 処理順序比較テスト ===");
            var hybridProcessor = await hybridFactory.CreateAsync();
            var comparisonResult = await hybridProcessor.CompareProcessingOrdersAsync("車体テストの役計");
            
            Console.WriteLine($"元テキスト: {comparisonResult.OriginalText}");
            Console.WriteLine($"N-gram → Dictionary: {comparisonResult.NgramFirstResult}");
            Console.WriteLine($"Dictionary → N-gram: {comparisonResult.DictionaryFirstResult}");
            Console.WriteLine($"N-gramのみ: {comparisonResult.NgramOnlyResult}");
            Console.WriteLine($"Dictionaryのみ: {comparisonResult.DictionaryOnlyResult}");
            Console.WriteLine($"全て同じ結果: {comparisonResult.AllResultsMatch}");
            
        }
        catch (Exception ex)
        {
            Console.WriteLine($"個別テスト実行エラー: {ex.Message}");
            Console.WriteLine($"詳細: {ex.StackTrace}");
        }
        finally
        {
            serviceProvider.Dispose();
        }
    }
    
    /// <summary>
    /// N-gramモデル品質評価テスト
    /// </summary>
    public static async Task RunModelEvaluationTestAsync()
    {
        Console.WriteLine("=== N-gramモデル品質評価テスト ===");
        
        var services = new ServiceCollection();
        
        services.AddLogging(builder =>
        {
            builder.AddConsole()
                   .SetMinimumLevel(LogLevel.Information);
        });
        
        var paddleOcrModule = new PaddleOcrModule();
        paddleOcrModule.RegisterServices(services);
        
        var serviceProvider = services.BuildServiceProvider();
        
        try
        {
            var trainingService = serviceProvider.GetRequiredService<NgramTrainingService>();
            
            // モデルの学習
            Console.WriteLine("Bigramモデルを学習中...");
            var model = await trainingService.TrainJapaneseBigramModelAsync();
            
            // モデルの評価
            Console.WriteLine("モデルの品質を評価中...");
            var evaluationResult = await trainingService.EvaluateModelAsync(model);
            
            Console.WriteLine($"モデル精度: {evaluationResult.Accuracy:F2}%");
            Console.WriteLine($"評価ケース数: {evaluationResult.Cases.Count}");
            
            Console.WriteLine("\n=== 詳細評価結果 ===");
            foreach (var testCase in evaluationResult.Cases)
            {
                var status = testCase.IsCorrect ? "✓" : "✗";
                Console.WriteLine($"{status} 正解: {testCase.CorrectText} (尤度: {testCase.CorrectLikelihood:F3})");
                Console.WriteLine($"  誤答: {testCase.CorruptedText} (尤度: {testCase.CorruptedLikelihood:F3})");
            }
            
        }
        catch (Exception ex)
        {
            Console.WriteLine($"モデル評価エラー: {ex.Message}");
            Console.WriteLine($"詳細: {ex.StackTrace}");
        }
        finally
        {
            serviceProvider.Dispose();
        }
    }
}

/// <summary>
/// テストアプリケーションのエントリーポイント
/// </summary>
public class NgramTestProgram
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("N-gramベース後処理テストアプリケーション");
        Console.WriteLine("1. 包括的ベンチマークテスト");
        Console.WriteLine("2. 個別後処理テスト");
        Console.WriteLine("3. N-gramモデル品質評価テスト");
        Console.WriteLine("実行する番号を入力してください: ");
        
        var input = Console.ReadLine();
        
        switch (input)
        {
            case "1":
                await NgramPostProcessingTestApp.RunComprehensiveTestAsync();
                break;
            case "2":
                await NgramPostProcessingTestApp.RunIndividualProcessingTestAsync();
                break;
            case "3":
                await NgramPostProcessingTestApp.RunModelEvaluationTestAsync();
                break;
            default:
                Console.WriteLine("無効な選択です。包括的テストを実行します。");
                await NgramPostProcessingTestApp.RunComprehensiveTestAsync();
                break;
        }
        
        Console.WriteLine("\nEnterキーを押して終了...");
        Console.ReadLine();
    }
}