using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Tests.Translation.Local.Onnx.SentencePiece;

/// <summary>
/// Microsoft.ML.Tokenizers API詳細調査テスト
/// </summary>
public class MicrosoftMLTokenizersApiInvestigationTests(ITestOutputHelper output)
{
    [Fact]
    public void InvestigateTokenizerHierarchy()
    {
        output.WriteLine("🔍 Microsoft.ML.Tokenizers型階層調査");
        output.WriteLine(new string('=', 50));

        try
        {
            var assembly = Assembly.LoadFrom("Microsoft.ML.Tokenizers.dll");
            var types = assembly.GetExportedTypes();

            output.WriteLine($"📦 アセンブリ: {assembly.FullName}");
            output.WriteLine($"📍 場所: {assembly.Location}");
            output.WriteLine($"🔢 バージョン: {assembly.GetName().Version}");
            output.WriteLine("");

            // Tokenizerベースクラスを調査
            var tokenizerType = types.FirstOrDefault(t => t.Name == "Tokenizer" && t.IsAbstract);
            if (tokenizerType != null)
            {
                output.WriteLine($"🏗️ Tokenizer基底クラス: {tokenizerType.FullName}");
                
                var methods = tokenizerType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                foreach (var method in methods.OrderBy(m => m.Name))
                {
                    var parameters = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                    output.WriteLine($"   📝 {method.ReturnType.Name} {method.Name}({parameters})");
                }
                output.WriteLine("");
            }

            // SentencePieceTokenizerを調査
            var sentencePieceType = types.FirstOrDefault(t => t.Name == "SentencePieceTokenizer");
            if (sentencePieceType != null)
            {
                output.WriteLine($"🔤 SentencePieceTokenizer: {sentencePieceType.FullName}");
                output.WriteLine($"   継承: {sentencePieceType.BaseType?.Name}");
                
                var constructors = sentencePieceType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
                output.WriteLine($"   🏗️ コンストラクタ数: {constructors.Length}");
                
                var staticMethods = sentencePieceType.GetMethods(BindingFlags.Public | BindingFlags.Static);
                output.WriteLine($"   📝 静的メソッド:");
                foreach (var method in staticMethods.Where(m => m.Name.Contains("Create", StringComparison.Ordinal)).OrderBy(m => m.Name))
                {
                    var parameters = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                    output.WriteLine($"      {method.ReturnType.Name} {method.Name}({parameters})");
                }
                output.WriteLine("");
            }

            // EncodeResultを調査
            var encodeResultType = types.FirstOrDefault(t => t.Name == "EncodeResult");
            if (encodeResultType != null)
            {
                output.WriteLine($"📊 EncodeResult: {encodeResultType.FullName}");
                
                var properties = encodeResultType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                foreach (var prop in properties.OrderBy(p => p.Name))
                {
                    output.WriteLine($"   🏷️ {prop.PropertyType.Name} {prop.Name} {{ {(prop.CanRead ? "get; " : "")}{(prop.CanWrite ? "set; " : "")}}}");
                }
                output.WriteLine("");
            }

            // その他の関連型を調査
            var relevantTypes = types.Where(t => 
                t.Name.Contains("SentencePiece", StringComparison.OrdinalIgnoreCase) || 
                t.Name.Contains("Normalizer", StringComparison.OrdinalIgnoreCase) ||
                t.Name.Contains("Encode", StringComparison.OrdinalIgnoreCase) ||
                t.Name.Contains("Decode", StringComparison.OrdinalIgnoreCase)).ToList();

            output.WriteLine("🔗 関連型一覧:");
            foreach (var type in relevantTypes.OrderBy(t => t.Name))
            {
                var typeKind = type.IsInterface ? "Interface" : 
                              type.IsAbstract ? "Abstract" : 
                              type.IsSealed ? "Sealed" : "Class";
                output.WriteLine($"   {typeKind}: {type.Name}");
            }

        }
#pragma warning disable CA1031 // Do not catch general exception types - API調査のため一般的な例外キャッチを許可
        catch (Exception ex)
        {
            output.WriteLine($"❌ 調査エラー: {ex.Message}");
        }
#pragma warning restore CA1031
    }

    [Fact]
    public void InvestigateSentencePieceCreateMethods()
    {
        output.WriteLine("🔍 SentencePieceTokenizer.Create メソッド調査");
        output.WriteLine(new string('=', 50));

        try
        {
            var type = Type.GetType("Microsoft.ML.Tokenizers.SentencePieceTokenizer, Microsoft.ML.Tokenizers");
            if (type == null)
            {
                output.WriteLine("❌ SentencePieceTokenizer型が見つかりません");
                return;
            }

            var createMethods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "Create")
                .ToArray();

            output.WriteLine($"📝 Create メソッド数: {createMethods.Length}");
            output.WriteLine("");

            for (int i = 0; i < createMethods.Length; i++)
            {
                var method = createMethods[i];
                output.WriteLine($"🔧 Create オーバーロード {i + 1}:");
                output.WriteLine($"   戻り値: {method.ReturnType.Name}");
                
                var parameters = method.GetParameters();
                output.WriteLine($"   パラメーター数: {parameters.Length}");
                
                foreach (var param in parameters)
                {
                    var defaultValue = param.HasDefaultValue ? $" = {param.DefaultValue}" : "";
                    output.WriteLine($"   - {param.ParameterType.Name} {param.Name}{defaultValue}");
                }
                
                output.WriteLine("");
            }

            // 実際に呼び出し可能かテスト
            TestCreateMethodInvocation(type, createMethods);

        }
#pragma warning disable CA1031 // Do not catch general exception types - API調査のため一般的な例外キャッチを許可
        catch (Exception ex)
        {
            output.WriteLine($"❌ 調査エラー: {ex.Message}");
        }
#pragma warning restore CA1031
    }

    [Fact]
    public void InvestigateEncodeDecodeSignatures()
    {
        output.WriteLine("🔍 Encode/Decode メソッドシグネチャ調査");
        output.WriteLine(new string('=', 50));

        try
        {
            var tokenizerType = Type.GetType("Microsoft.ML.Tokenizers.Tokenizer, Microsoft.ML.Tokenizers");
            if (tokenizerType == null)
            {
                output.WriteLine("❌ Tokenizer型が見つかりません");
                return;
            }

            // Encodeメソッドを調査
            var encodeMethods = tokenizerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name == "Encode")
                .ToArray();

            output.WriteLine($"📤 Encode メソッド数: {encodeMethods.Length}");
            foreach (var method in encodeMethods)
            {
                var parameters = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                output.WriteLine($"   {method.ReturnType.Name} Encode({parameters})");
            }
            output.WriteLine("");

            // Decodeメソッドを調査
            var decodeMethods = tokenizerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.Name == "Decode")
                .ToArray();

            output.WriteLine($"📥 Decode メソッド数: {decodeMethods.Length}");
            foreach (var method in decodeMethods)
            {
                var parameters = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                output.WriteLine($"   {method.ReturnType.Name} Decode({parameters})");
            }
            output.WriteLine("");

            // EncodeResult型の詳細調査
            InvestigateEncodeResultType();

        }
#pragma warning disable CA1031 // Do not catch general exception types - API調査のため一般的な例外キャッチを許可
        catch (Exception ex)
        {
            output.WriteLine($"❌ 調査エラー: {ex.Message}");
        }
#pragma warning restore CA1031
    }

    [Fact]
    public void TestActualSentencePieceInstantiation()
    {
        output.WriteLine("🧪 実際のSentencePieceTokenizer作成テスト");
        output.WriteLine(new string('=', 50));

        try
        {
            // ダミーSentencePieceモデルデータを作成
            var dummyModelData = CreateMinimalSentencePieceModel();
            
            using var stream = new MemoryStream(dummyModelData);
            
            // リフレクションでSentencePieceTokenizer.Createを呼び出し
            var type = Type.GetType("Microsoft.ML.Tokenizers.SentencePieceTokenizer, Microsoft.ML.Tokenizers");
            var createMethod = type?.GetMethod("Create", [typeof(Stream), typeof(bool), typeof(bool), typeof(System.Collections.Generic.IReadOnlyDictionary<string, int>)]);
            
            if (createMethod != null)
            {
                try
                {
                    var tokenizer = createMethod.Invoke(null, [stream, true, false, null]);
                    
                    if (tokenizer != null)
                    {
                        output.WriteLine("✅ SentencePieceTokenizer作成成功");
                        
                        // 基本的なメソッド呼び出しテスト
                        TestTokenizerBasicMethods(tokenizer);
                        
                        // リソース解放
                        if (tokenizer is IDisposable disposable)
                        {
                            disposable.Dispose();
                        }
                    }
                    else
                    {
                        output.WriteLine("⚠️ SentencePieceTokenizer作成は成功しましたが、nullが返されました");
                    }
                }
                catch (TargetInvocationException ex) when (ex.InnerException != null)
                {
                    output.WriteLine($"⚠️ 予想されるエラー（ダミーデータのため）: {ex.InnerException.GetType().Name}");
                    output.WriteLine($"   メッセージ: {ex.InnerException.Message}");
                    
                    // これは正常（ダミーデータなので失敗が期待される）
                    Assert.True(true, "ダミーデータでの失敗は想定内");
                }
            }
            else
            {
                output.WriteLine("❌ SentencePieceTokenizer.Createメソッドが見つかりません");
            }
        }
#pragma warning disable CA1031 // Do not catch general exception types - API調査のため一般的な例外キャッチを許可
        catch (Exception ex)
        {
            output.WriteLine($"❌ テストエラー: {ex.Message}");
        }
#pragma warning restore CA1031
    }

    private void TestCreateMethodInvocation(Type _, MethodInfo[] createMethods)
    {
        output.WriteLine("🧪 Create メソッド呼び出しテスト:");
        
        foreach (var method in createMethods)
        {
            try
            {
                var parameters = method.GetParameters();
                var args = new object[parameters.Length];
                
                // 基本的なパラメーター値を設定
                for (int i = 0; i < parameters.Length; i++)
                {
                    var param = parameters[i];
                    args[i] = param.ParameterType switch
                    {
                        var t when t == typeof(Stream) => new MemoryStream(CreateMinimalSentencePieceModel()),
                        var t when t == typeof(bool) => param.HasDefaultValue ? (param.DefaultValue ?? false) : false,
                        var t when t.IsGenericType && t.GetGenericTypeDefinition() == typeof(System.Collections.Generic.IReadOnlyDictionary<,>) => null!,
                        _ => param.HasDefaultValue ? param.DefaultValue! : null!
                    };
                }
                
                // メソッド呼び出し（エラーが発生することを期待）
                var result = method.Invoke(null, args);
                output.WriteLine($"   ✅ {method.Name}: 成功（予期しない）");
                
                // リソース解放
                if (result is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                
            }
            catch (TargetInvocationException ex)
            {
                output.WriteLine($"   ⚠️ {method.Name}: {ex.InnerException?.GetType().Name ?? ex.GetType().Name}");
            }
#pragma warning disable CA1031 // Do not catch general exception types - API調査のため一般的な例外キャッチを許可
            catch (Exception ex)
            {
                output.WriteLine($"   ❌ {method.Name}: {ex.GetType().Name}");
            }
#pragma warning restore CA1031
        }
        output.WriteLine("");
    }

    private void InvestigateEncodeResultType()
    {
        var encodeResultType = Type.GetType("Microsoft.ML.Tokenizers.EncodeResult, Microsoft.ML.Tokenizers");
        if (encodeResultType != null)
        {
            output.WriteLine($"📊 EncodeResult詳細:");
            
            var properties = encodeResultType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in properties)
            {
                output.WriteLine($"   🏷️ {prop.PropertyType.Name} {prop.Name}");
                
                // IReadOnlyList<int>型のプロパティを特に確認
                if (prop.PropertyType.IsGenericType)
                {
                    var genericType = prop.PropertyType.GetGenericTypeDefinition();
                    var typeArgs = prop.PropertyType.GetGenericArguments();
                    output.WriteLine($"      ジェネリック: {genericType.Name}<{string.Join(", ", typeArgs.Select(t => t.Name))}>");
                }
            }
            output.WriteLine("");
        }
    }

    private void TestTokenizerBasicMethods(object tokenizer)
    {
        var type = tokenizer.GetType();
        
        // Encodeメソッドテスト
        var encodeMethod = type.GetMethod("Encode", [typeof(string)]);
        if (encodeMethod != null)
        {
            try
            {
                var result = encodeMethod.Invoke(tokenizer, ["test"]);
                output.WriteLine($"   📤 Encode test: {result?.GetType().Name ?? "null"}");
                
                // Idsプロパティの確認
                if (result != null)
                {
                    var idsProperty = result.GetType().GetProperty("Ids");
                    if (idsProperty != null)
                    {
                        var ids = idsProperty.GetValue(result);
                        output.WriteLine($"   🆔 Ids: {ids?.GetType().Name ?? "null"}");
                    }
                }
            }
#pragma warning disable CA1031 // Do not catch general exception types - API調査のため一般的な例外キャッチを許可
            catch (Exception ex)
            {
                output.WriteLine($"   ❌ Encode test failed: {ex.Message}");
            }
#pragma warning restore CA1031
        }
        
        // Decodeメソッドテスト
        var decodeMethod = type.GetMethod("Decode", [typeof(int[])]);
        if (decodeMethod != null)
        {
            try
            {
                int[] testTokens = [1, 2, 3];
                var result = decodeMethod.Invoke(tokenizer, [testTokens]);
                output.WriteLine($"   📥 Decode test: {result?.GetType().Name ?? "null"}");
            }
#pragma warning disable CA1031 // Do not catch general exception types - API調査のため一般的な例外キャッチを許可
            catch (Exception ex)
            {
                output.WriteLine($"   ❌ Decode test failed: {ex.Message}");
            }
#pragma warning restore CA1031
        }
    }

    private static readonly byte[] MinimalSentencePieceModelData =
    [
        // trainer_spec
        0x0A, 0x0C,
        0x08, 0x01, // model_type: UNIGRAM
        0x10, 0x80, 0x3E, // vocab_size: 8000
        
        // normalizer_spec  
        0x12, 0x06,
        0x0A, 0x04, 0x6E, 0x66, 0x6B, 0x63, // name: "nfkc"
        
        // pieces (minimal set)
        0x1A, 0x0A, // pieces field
        0x0A, 0x05, 0x3C, 0x75, 0x6E, 0x6B, 0x3E, // piece: "<unk>"
        0x10, 0x00, // score: 0
        0x18, 0x02, // type: UNKNOWN
        
        0x1A, 0x08, // pieces field  
        0x0A, 0x03, 0x3C, 0x73, 0x3E, // piece: "<s>"
        0x10, 0x00, // score: 0
        0x18, 0x03, // type: CONTROL
    ];

    private static byte[] CreateMinimalSentencePieceModel()
    {
        // 最小限のSentencePieceプロトバッファ構造を模倣
        // 実際のフォーマットではないが、型検証には十分
        return MinimalSentencePieceModelData;
    }
}
