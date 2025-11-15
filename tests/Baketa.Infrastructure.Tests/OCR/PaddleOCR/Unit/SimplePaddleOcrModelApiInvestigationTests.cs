using System.Reflection;
using Sdcb.PaddleOCR.Models.Local;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Tests.OCR.PaddleOCR.Unit;

/// <summary>
/// PaddleOCRモデルAPIの簡単な調査テスト
/// V4とV3モデルの基本的な違いを確認
/// </summary>
public class SimplePaddleOcrModelApiInvestigationTests(ITestOutputHelper output)
{
    [Fact]
    public void Basic_JapanV4_Model_Investigation()
    {
        output.WriteLine("=== JapanV4 基本調査 ===");

        try
        {
            var japanV4 = LocalFullModels.JapanV4;
            output.WriteLine($"JapanV4は利用可能: {japanV4 != null}");

            if (japanV4 != null)
            {
                output.WriteLine($"型: {japanV4.GetType().FullName}");
                output.WriteLine($"基底型: {japanV4.GetType().BaseType?.FullName}");
                output.WriteLine($"アセンブリ: {japanV4.GetType().Assembly.GetName().Name}");

                output.WriteLine($"RecognizationModel型: {japanV4.RecognizationModel?.GetType().FullName ?? "null"}");
                output.WriteLine($"DetectionModel型: {japanV4.DetectionModel?.GetType().FullName ?? "null"}");
                output.WriteLine($"ClassificationModel型: {japanV4.ClassificationModel?.GetType().FullName ?? "null"}");
            }
        }
        catch (Exception ex)
        {
            output.WriteLine($"JapanV4取得エラー: {ex.Message}");
        }
    }

    [Fact]
    public void Basic_JapanV3_Model_Investigation()
    {
        output.WriteLine("=== JapanV3 基本調査 ===");

        try
        {
            var japanV3 = LocalFullModels.JapanV3;
            output.WriteLine($"JapanV3は利用可能: {japanV3 != null}");

            if (japanV3 != null)
            {
                output.WriteLine($"型: {japanV3.GetType().FullName}");
                output.WriteLine($"基底型: {japanV3.GetType().BaseType?.FullName}");
                output.WriteLine($"アセンブリ: {japanV3.GetType().Assembly.GetName().Name}");

                output.WriteLine($"RecognizationModel型: {japanV3.RecognizationModel?.GetType().FullName ?? "null"}");
                output.WriteLine($"DetectionModel型: {japanV3.DetectionModel?.GetType().FullName ?? "null"}");
                output.WriteLine($"ClassificationModel型: {japanV3.ClassificationModel?.GetType().FullName ?? "null"}");
            }
        }
        catch (Exception ex)
        {
            output.WriteLine($"JapanV3取得エラー: {ex.Message}");
        }
    }

    [Fact]
    public void Investigate_RecognizationModel_Properties()
    {
        output.WriteLine("=== RecognizationModel プロパティ調査 ===");

        try
        {
            var japanV4 = LocalFullModels.JapanV4;
            var japanV3 = LocalFullModels.JapanV3;

            output.WriteLine("--- V4 RecognizationModel ---");
            if (japanV4?.RecognizationModel != null)
            {
                InvestigateObjectProperties(japanV4.RecognizationModel, "V4 認識");
            }

            output.WriteLine("\n--- V3 RecognizationModel ---");
            if (japanV3?.RecognizationModel != null)
            {
                InvestigateObjectProperties(japanV3.RecognizationModel, "V3 認識");
            }
        }
        catch (Exception ex)
        {
            output.WriteLine($"RecognizationModel調査エラー: {ex.Message}");
        }
    }

    [Fact]
    public void Investigate_DetectionModel_Properties()
    {
        output.WriteLine("=== DetectionModel プロパティ調査 ===");

        try
        {
            var japanV4 = LocalFullModels.JapanV4;
            var japanV3 = LocalFullModels.JapanV3;

            output.WriteLine("--- V4 DetectionModel ---");
            if (japanV4?.DetectionModel != null)
            {
                InvestigateObjectProperties(japanV4.DetectionModel, "V4 検出");
            }

            output.WriteLine("\n--- V3 DetectionModel ---");
            if (japanV3?.DetectionModel != null)
            {
                InvestigateObjectProperties(japanV3.DetectionModel, "V3 検出");
            }
        }
        catch (Exception ex)
        {
            output.WriteLine($"DetectionModel調査エラー: {ex.Message}");
        }
    }

    [Fact]
    public void Compare_V4_vs_V3_Assembly_Names()
    {
        output.WriteLine("=== V4 vs V3 アセンブリ名比較 ===");

        try
        {
            var japanV4 = LocalFullModels.JapanV4;
            var japanV3 = LocalFullModels.JapanV3;

            if (japanV4?.RecognizationModel != null && japanV3?.RecognizationModel != null)
            {
                var v4Assembly = japanV4.RecognizationModel.GetType().Assembly.GetName().Name;
                var v3Assembly = japanV3.RecognizationModel.GetType().Assembly.GetName().Name;

                output.WriteLine($"V4認識モデルアセンブリ: {v4Assembly}");
                output.WriteLine($"V3認識モデルアセンブリ: {v3Assembly}");
                output.WriteLine($"アセンブリが異なる: {v4Assembly != v3Assembly}");

                if (v4Assembly != null)
                {
                    output.WriteLine($"V4アセンブリにV4が含まれる: {v4Assembly.Contains("V4")}");
                    output.WriteLine($"V4アセンブリにLocalV4が含まれる: {v4Assembly.Contains("LocalV4")}");
                }
            }

            if (japanV4?.DetectionModel != null && japanV3?.DetectionModel != null)
            {
                var v4DetectionAssembly = japanV4.DetectionModel.GetType().Assembly.GetName().Name;
                var v3DetectionAssembly = japanV3.DetectionModel.GetType().Assembly.GetName().Name;

                output.WriteLine($"V4検出モデルアセンブリ: {v4DetectionAssembly}");
                output.WriteLine($"V3検出モデルアセンブリ: {v3DetectionAssembly}");
                output.WriteLine($"検出モデルアセンブリが異なる: {v4DetectionAssembly != v3DetectionAssembly}");
            }
        }
        catch (Exception ex)
        {
            output.WriteLine($"アセンブリ比較エラー: {ex.Message}");
        }
    }

    [Fact]
    public void Test_Practical_V4_Detection_Methods()
    {
        output.WriteLine("=== 実用的なV4検出方法テスト ===");

        try
        {
            var japanV4 = LocalFullModels.JapanV4;
            var japanV3 = LocalFullModels.JapanV3;

            if (japanV4 != null && japanV3 != null)
            {
                // 方法1: アセンブリ名による検出
                var v4AssemblyName = japanV4.RecognizationModel?.GetType().Assembly.GetName().Name ?? "";
                var v3AssemblyName = japanV3.RecognizationModel?.GetType().Assembly.GetName().Name ?? "";
                var method1Works = v4AssemblyName.Contains("V4") && !v3AssemblyName.Contains("V4");

                output.WriteLine($"方法1 - アセンブリ名検出: {method1Works}");
                output.WriteLine($"  V4アセンブリ: {v4AssemblyName}");
                output.WriteLine($"  V3アセンブリ: {v3AssemblyName}");

                // 方法2: 型名による検出
                var v4TypeName = japanV4.RecognizationModel?.GetType().Name ?? "";
                var v3TypeName = japanV3.RecognizationModel?.GetType().Name ?? "";
                var method2Works = v4TypeName != v3TypeName;

                output.WriteLine($"方法2 - 型名による検出: {method2Works}");
                output.WriteLine($"  V4型名: {v4TypeName}");
                output.WriteLine($"  V3型名: {v3TypeName}");

                // 方法3: FullName による検出
                var v4FullName = japanV4.RecognizationModel?.GetType().FullName ?? "";
                var v3FullName = japanV3.RecognizationModel?.GetType().FullName ?? "";
                var method3Works = v4FullName.Contains("V4") && !v3FullName.Contains("V4");

                output.WriteLine($"方法3 - FullName検出: {method3Works}");
                output.WriteLine($"  V4FullName: {v4FullName}");
                output.WriteLine($"  V3FullName: {v3FullName}");

                // 推奨メソッドの提案
                if (method1Works)
                {
                    output.WriteLine("推奨: アセンブリ名による検出が利用可能");
                    output.WriteLine($"実装例: model.RecognizationModel?.GetType().Assembly.GetName().Name?.Contains(\"V4\") == true");
                }
                else if (method2Works)
                {
                    output.WriteLine("推奨: 型名による検出が利用可能");
                    output.WriteLine($"実装例: V4型='{v4TypeName}', V3型='{v3TypeName}'");
                }
                else if (method3Works)
                {
                    output.WriteLine("推奨: FullName検出が利用可能");
                }
                else
                {
                    output.WriteLine("注意: 自動検出方法が見つかりません");
                }
            }
        }
        catch (Exception ex)
        {
            output.WriteLine($"V4検出方法テストエラー: {ex.Message}");
        }
    }

    private void InvestigateObjectProperties(object obj, string prefix)
    {
        if (obj == null)
        {
            output.WriteLine($"{prefix}: null");
            return;
        }

        var type = obj.GetType();
        output.WriteLine($"{prefix}型: {type.FullName}");

        // プロパティ一覧
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        output.WriteLine($"{prefix}プロパティ数: {properties.Length}");

        foreach (var prop in properties.Take(10)) // 最初の10個のみ表示
        {
            try
            {
                var value = prop.GetValue(obj);
                var valueStr = value?.ToString() ?? "null";
                if (valueStr.Length > 50) valueStr = valueStr[..50] + "...";
                output.WriteLine($"  {prop.Name}: {prop.PropertyType.Name} = {valueStr}");
            }
            catch (Exception ex)
            {
                output.WriteLine($"  {prop.Name}: アクセス不可 ({ex.GetType().Name})");
            }
        }

        if (properties.Length > 10)
        {
            output.WriteLine($"  ... 他 {properties.Length - 10} 個のプロパティ");
        }
    }
}
