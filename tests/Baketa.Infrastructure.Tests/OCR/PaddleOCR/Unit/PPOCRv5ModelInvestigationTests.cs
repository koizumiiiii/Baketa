using System;
using System.Reflection;
using Baketa.Infrastructure.OCR.PaddleOCR.Models;
using Sdcb.PaddleOCR.Models.Local;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Tests.OCR.PaddleOCR.Unit;

/// <summary>
/// PP-OCRv5モデルの詳細調査テスト
/// 初期化失敗の根本原因を特定するための包括的テスト
/// </summary>
public class PPOCRv5ModelInvestigationTests(ITestOutputHelper output)
{
    [Fact]
    public void Investigation_LocalFullModels_ChineseV5_Basic()
    {
        output.WriteLine("=== LocalFullModels.ChineseV5 基本調査 ===");

        try
        {
            // 1. LocalFullModels.ChineseV5へのアクセステスト
            output.WriteLine("1. LocalFullModels.ChineseV5アクセステスト");
            var chineseV5 = LocalFullModels.ChineseV5;
            output.WriteLine($"   ChineseV5は利用可能: {chineseV5 != null}");

            if (chineseV5 != null)
            {
                output.WriteLine($"   型: {chineseV5.GetType().FullName}");
                output.WriteLine($"   基底型: {chineseV5.GetType().BaseType?.FullName}");
                output.WriteLine($"   アセンブリ: {chineseV5.GetType().Assembly.GetName().Name}");

                // 2. 各モデルコンポーネントの詳細調査
                output.WriteLine("\n2. モデルコンポーネント詳細");
                InvestigateModelComponent(chineseV5.RecognizationModel, "認識モデル");
                InvestigateModelComponent(chineseV5.DetectionModel, "検出モデル");
                InvestigateModelComponent(chineseV5.ClassificationModel, "分類モデル");
            }
        }
        catch (Exception ex)
        {
            output.WriteLine($"❌ LocalFullModels.ChineseV5 アクセスエラー: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
            {
                output.WriteLine($"   内部例外: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            }
            output.WriteLine($"   スタックトレース: {ex.StackTrace}");
        }
    }

    [Fact]
    public void Investigation_PPOCRv5ModelProvider_IsAvailable()
    {
        output.WriteLine("=== PPOCRv5ModelProvider.IsAvailable() 調査 ===");

        try
        {
            output.WriteLine("1. PPOCRv5ModelProvider.IsAvailable()呼び出し");
            var isAvailable = PPOCRv5ModelProvider.IsAvailable();
            output.WriteLine($"   結果: {isAvailable}");

            output.WriteLine("\n2. IsAvailable()内部での例外確認");
            // IsAvailable()メソッドの実装を詳細に確認
            try
            {
                var chineseV5 = LocalFullModels.ChineseV5;
                output.WriteLine($"   LocalFullModels.ChineseV5直接アクセス: {chineseV5 != null}");

                if (chineseV5 != null)
                {
                    output.WriteLine($"   型情報: {chineseV5.GetType().FullName}");
                }
            }
            catch (Exception innerEx)
            {
                output.WriteLine($"   ❌ LocalFullModels.ChineseV5内部エラー: {innerEx.GetType().Name}: {innerEx.Message}");
            }
        }
        catch (Exception ex)
        {
            output.WriteLine($"❌ PPOCRv5ModelProvider.IsAvailable()エラー: {ex.GetType().Name}: {ex.Message}");
            output.WriteLine($"   スタックトレース: {ex.StackTrace}");
        }
    }

    [Fact]
    public void Investigation_PPOCRv5ModelProvider_GetPPOCRv5MultilingualModel()
    {
        output.WriteLine("=== PPOCRv5ModelProvider.GetPPOCRv5MultilingualModel() 調査 ===");

        try
        {
            output.WriteLine("1. GetPPOCRv5MultilingualModel()呼び出し");
            var model = PPOCRv5ModelProvider.GetPPOCRv5MultilingualModel();
            output.WriteLine($"   結果: {model != null}");

            if (model != null)
            {
                output.WriteLine($"   型: {model.GetType().FullName}");
                output.WriteLine($"   RecognizationModel: {model.RecognizationModel?.GetType().FullName ?? "null"}");
                output.WriteLine($"   DetectionModel: {model.DetectionModel?.GetType().FullName ?? "null"}");
                output.WriteLine($"   ClassificationModel: {model.ClassificationModel?.GetType().FullName ?? "null"}");
            }
            else
            {
                output.WriteLine("   ❌ モデルはnullが返されました");

                // モデル取得失敗の詳細調査
                output.WriteLine("\n2. 失敗原因の詳細調査");
                InvestigateModelPathsAndFiles();
            }
        }
        catch (Exception ex)
        {
            output.WriteLine($"❌ GetPPOCRv5MultilingualModel()エラー: {ex.GetType().Name}: {ex.Message}");
            output.WriteLine($"   スタックトレース: {ex.StackTrace}");

            // 詳細な失敗原因調査
            InvestigateModelPathsAndFiles();
        }
    }

    [Fact]
    public void Investigation_Compare_V4_vs_V5_Models()
    {
        output.WriteLine("=== V4 vs V5 モデル比較調査 ===");

        try
        {
            // V5統一モデル調査（旧V4から移行）
            output.WriteLine("1. V5統一モデル調査（旧V4から統一移行）");
            var japanV5 = LocalFullModels.ChineseV5; // V5統一モデル
            var englishV5 = LocalFullModels.ChineseV5; // V5統一モデル
            var chineseV5 = LocalFullModels.ChineseV5;

            output.WriteLine($"   JapanV5（V5統一）: {japanV5 != null}");
            output.WriteLine($"   EnglishV5（V5統一）: {englishV5 != null}");
            output.WriteLine($"   ChineseV5: {chineseV5 != null}");

            // V5モデル調査
            output.WriteLine("\n2. V5モデル調査");
            try
            {
                // 既にchineseV5は上で定義済みなので削除
                output.WriteLine($"   ChineseV5: {chineseV5 != null}");

                if (chineseV5 != null && japanV5 != null)
                {
                    output.WriteLine("\n3. V5統一モデルの詳細比較");
                    CompareModels(japanV5, "JapanV5（V5統一）", chineseV5, "ChineseV5");
                }
            }
            catch (Exception v5Ex)
            {
                output.WriteLine($"   ❌ V5モデルアクセスエラー: {v5Ex.GetType().Name}: {v5Ex.Message}");
            }
        }
        catch (Exception ex)
        {
            output.WriteLine($"❌ V4/V5モデル比較エラー: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [Fact]
    public void Investigation_Assembly_Dependencies()
    {
        output.WriteLine("=== アセンブリ依存関係調査 ===");

        try
        {
            // 現在読み込まれているSdcb関連アセンブリ
            var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name?.Contains("Sdcb") == true)
                .ToList();

            output.WriteLine($"1. 読み込み済みSdcbアセンブリ数: {loadedAssemblies.Count}");

            foreach (var assembly in loadedAssemblies)
            {
                var name = assembly.GetName();
                output.WriteLine($"   - {name.Name} (Version: {name.Version})");
            }

            // LocalFullModelsの定義アセンブリ
            output.WriteLine("\n2. LocalFullModelsアセンブリ情報");
            var localFullModelsType = typeof(LocalFullModels);
            var localFullModelsAssembly = localFullModelsType.Assembly;
            var assemblyName = localFullModelsAssembly.GetName();

            output.WriteLine($"   アセンブリ名: {assemblyName.Name}");
            output.WriteLine($"   バージョン: {assemblyName.Version}");
            output.WriteLine($"   場所: {localFullModelsAssembly.Location}");

            // LocalFullModelsの静的プロパティ一覧
            output.WriteLine("\n3. LocalFullModels利用可能プロパティ");
            var properties = localFullModelsType.GetProperties(BindingFlags.Public | BindingFlags.Static);
            foreach (var prop in properties)
            {
                try
                {
                    var value = prop.GetValue(null);
                    output.WriteLine($"   - {prop.Name}: {value != null}");
                }
                catch (Exception propEx)
                {
                    output.WriteLine($"   - {prop.Name}: ❌ エラー ({propEx.GetType().Name})");
                }
            }
        }
        catch (Exception ex)
        {
            output.WriteLine($"❌ アセンブリ依存関係調査エラー: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void InvestigateModelComponent(object? component, string componentName)
    {
        output.WriteLine($"\n   {componentName}:");
        if (component == null)
        {
            output.WriteLine($"     null");
            return;
        }

        output.WriteLine($"     型: {component.GetType().FullName}");
        output.WriteLine($"     アセンブリ: {component.GetType().Assembly.GetName().Name}");

        // 主要プロパティの調査
        var properties = component.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var importantProps = properties.Take(5);

        foreach (var prop in importantProps)
        {
            try
            {
                var value = prop.GetValue(component);
                var valueStr = value?.ToString() ?? "null";
                if (valueStr.Length > 30) valueStr = valueStr[..30] + "...";
                output.WriteLine($"     {prop.Name}: {valueStr}");
            }
            catch (Exception)
            {
                output.WriteLine($"     {prop.Name}: アクセス不可");
            }
        }
    }

    private void CompareModels(object model1, string name1, object model2, string name2)
    {
        output.WriteLine($"   {name1} vs {name2} 比較:");

        // 型比較
        output.WriteLine($"     型一致: {model1.GetType() == model2.GetType()}");
        output.WriteLine($"     {name1}型: {model1.GetType().FullName}");
        output.WriteLine($"     {name2}型: {model2.GetType().FullName}");

        // アセンブリ比較
        var assembly1 = model1.GetType().Assembly.GetName().Name;
        var assembly2 = model2.GetType().Assembly.GetName().Name;
        output.WriteLine($"     アセンブリ一致: {assembly1 == assembly2}");
        output.WriteLine($"     {name1}アセンブリ: {assembly1}");
        output.WriteLine($"     {name2}アセンブリ: {assembly2}");
    }

    private void InvestigateModelPathsAndFiles()
    {
        output.WriteLine("\n   モデルパスとファイル存在確認:");

        try
        {
            // PPOCRv5ModelProviderの内部パス情報を取得（リフレクション使用）
            var providerType = typeof(PPOCRv5ModelProvider);
            var modelsBasePathField = providerType.GetField("ModelsBasePath", BindingFlags.NonPublic | BindingFlags.Static);

            if (modelsBasePathField != null)
            {
                var basePath = modelsBasePathField.GetValue(null) as string;
                output.WriteLine($"     ModelsBasePath: {basePath}");

                if (!string.IsNullOrEmpty(basePath))
                {
                    output.WriteLine($"     BasePath存在: {System.IO.Directory.Exists(basePath)}");

                    var expectedPaths = new[]
                    {
                        System.IO.Path.Combine(basePath, "PP-OCRv5_server_det"),
                        System.IO.Path.Combine(basePath, "PP-OCRv5_server_rec"),
                        System.IO.Path.Combine(basePath, "ch_ppocr_mobile_v2.0_cls_infer")
                    };

                    foreach (var path in expectedPaths)
                    {
                        output.WriteLine($"     {System.IO.Path.GetFileName(path)}: {System.IO.Directory.Exists(path)}");
                    }
                }
            }
            else
            {
                output.WriteLine("     ModelsBasePathフィールドが見つかりません");
            }
        }
        catch (Exception ex)
        {
            output.WriteLine($"     ❌ パス調査エラー: {ex.Message}");
        }
    }
}
