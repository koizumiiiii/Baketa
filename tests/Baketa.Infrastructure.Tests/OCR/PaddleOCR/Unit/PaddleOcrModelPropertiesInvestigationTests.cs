using System.Reflection;
using Sdcb.PaddleOCR.Models;
using Sdcb.PaddleOCR.Models.Local;
using Sdcb.PaddleOCR.Models.Shared;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Tests.OCR.PaddleOCR.Unit;

/// <summary>
/// PaddleOCRモデルプロパティの詳細調査テスト
/// V4とV3モデルの違いを検出する方法を調査
/// </summary>
public class PaddleOcrModelPropertiesInvestigationTests(ITestOutputHelper output)
{
    [Fact]
    public void InvestigateLocalFullModelsJapanV4Properties()
    {
        // Arrange & Act
        var japanV4 = LocalFullModels.JapanV4;

        output.WriteLine("=== LocalFullModels.JapanV4 詳細調査 ===");
        output.WriteLine($"型: {japanV4?.GetType()?.FullName ?? "null"}");
        output.WriteLine($"基底型: {japanV4?.GetType()?.BaseType?.FullName ?? "null"}");

        if (japanV4 != null)
        {
            // FullOcrModelのプロパティを調査
            output.WriteLine("\n--- FullOcrModel レベルのプロパティ ---");
            var fullOcrModelType = typeof(FullOcrModel);
            var properties = fullOcrModelType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in properties)
            {
                try
                {
                    var value = prop.GetValue(japanV4);
                    output.WriteLine($"{prop.Name}: {value?.GetType()?.Name ?? "null"} = {value}");
                }
                catch (Exception ex)
                {
                    output.WriteLine($"{prop.Name}: アクセス不可 ({ex.Message})");
                }
            }

            // RecognizationModelの詳細調査
            output.WriteLine("\n--- RecognizationModel プロパティ ---");
            InvestigateModelProperties(japanV4.RecognizationModel, "RecognizationModel");

            // DetectionModelの詳細調査
            output.WriteLine("\n--- DetectionModel プロパティ ---");
            InvestigateModelProperties(japanV4.DetectionModel, "DetectionModel");

            // ClassificationModelの詳細調査
            output.WriteLine("\n--- ClassificationModel プロパティ ---");
            InvestigateModelProperties(japanV4.ClassificationModel, "ClassificationModel");
        }
    }

    [Fact]
    public void InvestigateLocalFullModelsJapanV3Properties()
    {
        // Arrange & Act
        var japanV3 = LocalFullModels.JapanV3;

        output.WriteLine("=== LocalFullModels.JapanV3 詳細調査 ===");
        output.WriteLine($"型: {japanV3?.GetType()?.FullName ?? "null"}");
        output.WriteLine($"基底型: {japanV3?.GetType()?.BaseType?.FullName ?? "null"}");

        if (japanV3 != null)
        {
            // FullOcrModelのプロパティを調査
            output.WriteLine("\n--- FullOcrModel レベルのプロパティ ---");
            var fullOcrModelType = typeof(FullOcrModel);
            var properties = fullOcrModelType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in properties)
            {
                try
                {
                    var value = prop.GetValue(japanV3);
                    output.WriteLine($"{prop.Name}: {value?.GetType()?.Name ?? "null"} = {value}");
                }
                catch (Exception ex)
                {
                    output.WriteLine($"{prop.Name}: アクセス不可 ({ex.Message})");
                }
            }

            // RecognizationModelの詳細調査
            output.WriteLine("\n--- RecognizationModel プロパティ ---");
            InvestigateModelProperties(japanV3.RecognizationModel, "RecognizationModel");

            // DetectionModelの詳細調査
            output.WriteLine("\n--- DetectionModel プロパティ ---");
            InvestigateModelProperties(japanV3.DetectionModel, "DetectionModel");

            // ClassificationModelの詳細調査
            output.WriteLine("\n--- ClassificationModel プロパティ ---");
            InvestigateModelProperties(japanV3.ClassificationModel, "ClassificationModel");
        }
    }

    [Fact]
    public void CompareJapanV4VsV3Models()
    {
        // Arrange
        var japanV4 = LocalFullModels.JapanV4;
        var japanV3 = LocalFullModels.JapanV3;

        output.WriteLine("=== V4 vs V3 モデル比較 ===");

        // 基本型比較
        output.WriteLine($"V4型: {japanV4?.GetType()?.Name ?? "null"}");
        output.WriteLine($"V3型: {japanV3?.GetType()?.Name ?? "null"}");
        output.WriteLine($"型が同じ: {japanV4?.GetType() == japanV3?.GetType()}");

        if (japanV4 != null && japanV3 != null)
        {
            // RecognizationModel比較
            output.WriteLine("\n--- RecognizationModel 比較 ---");
            CompareModelDetails(japanV4.RecognizationModel, japanV3.RecognizationModel, "Recognization");

            // DetectionModel比較
            output.WriteLine("\n--- DetectionModel 比較 ---");
            CompareModelDetails(japanV4.DetectionModel, japanV3.DetectionModel, "Detection");

            // ClassificationModel比較
            output.WriteLine("\n--- ClassificationModel 比較 ---");
            CompareModelDetails(japanV4.ClassificationModel, japanV3.ClassificationModel, "Classification");
        }
    }

    [Fact]
    public void TestV4DetectionMethods()
    {
        // Arrange
        var japanV4 = LocalFullModels.JapanV4;
        var japanV3 = LocalFullModels.JapanV3;

        output.WriteLine("=== V4検出メソッドのテスト ===");

        if (japanV4 != null)
        {
            output.WriteLine("\n--- V4モデルでの検出テスト ---");
            TestDetectionMethod(japanV4, "V4");
        }

        if (japanV3 != null)
        {
            output.WriteLine("\n--- V3モデルでの検出テスト ---");
            TestDetectionMethod(japanV3, "V3");
        }

        // 推奨検出メソッドの提案
        output.WriteLine("\n--- 推奨V4検出メソッド ---");
        if (japanV4 != null && japanV3 != null)
        {
            var recommendedMethod = GetRecommendedV4DetectionMethod(japanV4, japanV3);
            output.WriteLine($"推奨メソッド: {recommendedMethod}");
        }
    }

    [Fact]
    public void InvestigateAllLocalFullModels()
    {
        output.WriteLine("=== 全LocalFullModels調査 ===");

        var localFullModelsType = typeof(LocalFullModels);
        var staticProperties = localFullModelsType.GetProperties(BindingFlags.Public | BindingFlags.Static);

        foreach (var prop in staticProperties)
        {
            try
            {
                var model = prop.GetValue(null) as FullOcrModel;
                output.WriteLine($"\n--- {prop.Name} ---");
                output.WriteLine($"型: {model?.GetType()?.Name ?? "null"}");

                if (model != null)
                {
                    output.WriteLine($"認識モデル型: {model.RecognizationModel?.GetType()?.Name ?? "null"}");
                    output.WriteLine($"検出モデル型: {model.DetectionModel?.GetType()?.Name ?? "null"}");
                    output.WriteLine($"分類モデル型: {model.ClassificationModel?.GetType()?.Name ?? "null"}");

                    // 可能な識別子の探索
                    if (HasProperty(model.RecognizationModel, "Name"))
                    {
                        output.WriteLine($"認識モデル名: {GetPropertyValue(model.RecognizationModel, "Name")}");
                    }
                    if (HasProperty(model.RecognizationModel, "Version"))
                    {
                        output.WriteLine($"認識モデルバージョン: {GetPropertyValue(model.RecognizationModel, "Version")}");
                    }
                    if (HasProperty(model.DetectionModel, "Name"))
                    {
                        output.WriteLine($"検出モデル名: {GetPropertyValue(model.DetectionModel, "Name")}");
                    }
                    if (HasProperty(model.DetectionModel, "Version"))
                    {
                        output.WriteLine($"検出モデルバージョン: {GetPropertyValue(model.DetectionModel, "Version")}");
                    }
                }
            }
            catch (Exception ex)
            {
                output.WriteLine($"--- {prop.Name} (エラー) ---");
                output.WriteLine($"エラー: {ex.Message}");
            }
        }
    }

    private void InvestigateModelProperties(object? model, string modelType)
    {
        if (model == null)
        {
            output.WriteLine($"{modelType}: null");
            return;
        }

        output.WriteLine($"{modelType}型: {model.GetType().FullName}");

        var properties = model.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var prop in properties)
        {
            try
            {
                var value = prop.GetValue(model);
                output.WriteLine($"  {prop.Name}: {prop.PropertyType.Name} = {value}");
            }
            catch (Exception ex)
            {
                output.WriteLine($"  {prop.Name}: アクセス不可 ({ex.Message})");
            }
        }

        // フィールドも調査
        var fields = model.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);
        if (fields.Length > 0)
        {
            output.WriteLine("  Fields:");
            foreach (var field in fields)
            {
                try
                {
                    var value = field.GetValue(model);
                    output.WriteLine($"    {field.Name}: {field.FieldType.Name} = {value}");
                }
                catch (Exception ex)
                {
                    output.WriteLine($"    {field.Name}: アクセス不可 ({ex.Message})");
                }
            }
        }
    }

    private void CompareModelDetails(object? model1, object? model2, string modelType)
    {
        output.WriteLine($"{modelType}モデル1型: {model1?.GetType()?.Name ?? "null"}");
        output.WriteLine($"{modelType}モデル2型: {model2?.GetType()?.Name ?? "null"}");
        output.WriteLine($"型が同じ: {model1?.GetType() == model2?.GetType()}");

        if (model1 != null && model2 != null)
        {
            var properties = model1.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in properties)
            {
                try
                {
                    var value1 = prop.GetValue(model1);
                    var value2 = prop.GetValue(model2);
                    var isEqual = Equals(value1, value2);
                    output.WriteLine($"  {prop.Name}: {value1} vs {value2} (同じ: {isEqual})");
                }
                catch (Exception ex)
                {
                    output.WriteLine($"  {prop.Name}: 比較不可 ({ex.Message})");
                }
            }
        }
    }

    private void TestDetectionMethod(FullOcrModel model, string version)
    {
        // 現在のコードで使用されている検出方法をテスト
        var recognizationModel = model.RecognizationModel;
        var detectionModel = model.DetectionModel;

        // テスト1: Name プロパティでPP-OCRv4を検出
        var test1Result = false;
        var test1Value = "不明";
        if (HasProperty(recognizationModel, "Name"))
        {
            test1Value = GetPropertyValue(recognizationModel, "Name")?.ToString() ?? "null";
            test1Result = test1Value.Contains("PP-OCRv4", StringComparison.OrdinalIgnoreCase);
        }
        output.WriteLine($"認識モデル名PP-OCRv4検出: {test1Result} (値: {test1Value})");

        // テスト2: Version プロパティでV4を検出
        var test2Result = false;
        var test2Value = "不明";
        if (HasProperty(recognizationModel, "Version"))
        {
            test2Value = GetPropertyValue(recognizationModel, "Version")?.ToString() ?? "null";
            test2Result = test2Value == "V4";
        }
        output.WriteLine($"認識モデルバージョンV4検出: {test2Result} (値: {test2Value})");

        // テスト3: DetectionModel Name でPP-OCRv4を検出
        var test3Result = false;
        var test3Value = "不明";
        if (HasProperty(detectionModel, "Name"))
        {
            test3Value = GetPropertyValue(detectionModel, "Name")?.ToString() ?? "null";
            test3Result = test3Value.Contains("PP-OCRv4", StringComparison.OrdinalIgnoreCase);
        }
        output.WriteLine($"検出モデル名PP-OCRv4検出: {test3Result} (値: {test3Value})");

        // テスト4: 型名による検出
        var recognizationTypeName = recognizationModel?.GetType().Name ?? "null";
        var detectionTypeName = detectionModel?.GetType().Name ?? "null";
        var test4Result = recognizationTypeName.Contains("V4") || detectionTypeName.Contains("V4");
        output.WriteLine($"型名V4検出: {test4Result} (認識型: {recognizationTypeName}, 検出型: {detectionTypeName})");

        // テスト5: アセンブリ名による検出
        var recognizationAssembly = recognizationModel?.GetType().Assembly.GetName().Name ?? "null";
        var detectionAssembly = detectionModel?.GetType().Assembly.GetName().Name ?? "null";
        var test5Result = recognizationAssembly.Contains("V4") || detectionAssembly.Contains("V4");
        output.WriteLine($"アセンブリ名V4検出: {test5Result} (認識: {recognizationAssembly}, 検出: {detectionAssembly})");

        output.WriteLine($"総合判定({version}): {test1Result || test2Result || test3Result || test4Result || test5Result}");
    }

    private string GetRecommendedV4DetectionMethod(FullOcrModel v4Model, FullOcrModel v3Model)
    {
        var methods = new List<string>();

        // アセンブリ名による検出が最も確実
        var v4Assembly = v4Model.RecognizationModel?.GetType().Assembly.GetName().Name ?? "";
        var v3Assembly = v3Model.RecognizationModel?.GetType().Assembly.GetName().Name ?? "";

        if (v4Assembly != v3Assembly && v4Assembly.Contains("V4"))
        {
            methods.Add("アセンブリ名による検出 (推奨)");
        }

        // 型名による検出
        var v4TypeName = v4Model.RecognizationModel?.GetType().Name ?? "";
        var v3TypeName = v3Model.RecognizationModel?.GetType().Name ?? "";

        if (v4TypeName != v3TypeName)
        {
            methods.Add("型名による検出");
        }

        // プロパティによる検出
        if (HasProperty(v4Model.RecognizationModel, "Name") || HasProperty(v4Model.RecognizationModel, "Version"))
        {
            methods.Add("プロパティによる検出");
        }

        return methods.Count > 0 ? string.Join(", ", methods) : "検出方法が見つかりません";
    }

    private bool HasProperty(object? obj, string propertyName)
    {
        if (obj == null) return false;
        return obj.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance) != null;
    }

    private object? GetPropertyValue(object? obj, string propertyName)
    {
        if (obj == null) return null;
        var prop = obj.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        return prop?.GetValue(obj);
    }
}
