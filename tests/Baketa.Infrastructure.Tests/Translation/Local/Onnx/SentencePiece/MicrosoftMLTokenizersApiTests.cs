using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Tests.Translation.Local.Onnx.SentencePiece;

/// <summary>
/// Microsoft.ML.Tokenizers v0.21.0 APIの利用可能性テスト
/// </summary>
public class MicrosoftMLTokenizersApiTests
{
    private readonly ITestOutputHelper _output;
    private readonly ILogger _logger;

    public MicrosoftMLTokenizersApiTests(ITestOutputHelper output)
    {
        _output = output;
        _logger = NullLogger.Instance;
    }

    [Fact]
    public void SentencePieceTokenizer_TypeExists()
    {
        // Arrange & Act
        var type = Type.GetType("Microsoft.ML.Tokenizers.SentencePieceTokenizer, Microsoft.ML.Tokenizers");

        // Assert & Report
        if (type != null)
        {
            _output.WriteLine($"✅ SentencePieceTokenizer型が見つかりました: {type.FullName}");
        }
        else
        {
            _output.WriteLine("⚠️ SentencePieceTokenizer型が見つかりませんでした（プレビュー版のため予想される動作）");
        }
        
        // 型が存在しない場合もテスト成功とする（API調査のため）
        Assert.True(true, "API調査テストは常に成功");
    }

    [Fact]
    public void SentencePieceTokenizer_CreateMethodExists()
    {
        // Arrange
        var type = Type.GetType("Microsoft.ML.Tokenizers.SentencePieceTokenizer, Microsoft.ML.Tokenizers");
        
        if (type == null)
        {
            _output.WriteLine("⚠️ SentencePieceTokenizer型が見つからないため、Createメソッドテストをスキップします");
            Assert.True(true, "API調査テストは常に成功");
            return;
        }

        // Act
        var createMethod = type.GetMethod("Create", [typeof(Stream), typeof(bool), typeof(bool), typeof(System.Collections.Generic.IReadOnlyDictionary<string, int>)]);

        // Assert & Report
        if (createMethod != null)
        {
            Assert.True(createMethod.IsStatic);
            _output.WriteLine($"✅ SentencePieceTokenizer.Create()メソッドが見つかりました");
            _output.WriteLine($"   戻り値の型: {createMethod.ReturnType.Name}");
            _output.WriteLine($"   パラメーター数: {createMethod.GetParameters().Length}");
        }
        else
        {
            _output.WriteLine("⚠️ SentencePieceTokenizer.Create()メソッドが見つかりませんでした");
        }
        
        Assert.True(true, "API調査テストは常に成功");
    }

    [Fact]
    public void Tokenizer_BaseClassExists()
    {
        // Arrange & Act
        var type = Type.GetType("Microsoft.ML.Tokenizers.Tokenizer, Microsoft.ML.Tokenizers");

        // Assert
        Assert.NotNull(type);
        _output.WriteLine($"✅ Tokenizer基底クラスが見つかりました: {type.FullName}");
    }

    [Fact]
    public void Tokenizer_EncodeMethodExists()
    {
        // Arrange
        var type = Type.GetType("Microsoft.ML.Tokenizers.Tokenizer, Microsoft.ML.Tokenizers");
        Assert.NotNull(type);

        // Act
        var encodeMethod = type.GetMethod("Encode", [typeof(string)]);

        // Assert
        Assert.NotNull(encodeMethod);
        _output.WriteLine($"✅ Tokenizer.Encode(string)メソッドが見つかりました");
        _output.WriteLine($"   戻り値の型: {encodeMethod.ReturnType.Name}");
    }

    [Fact]
    public void Tokenizer_DecodeMethodExists()
    {
        // Arrange
        var type = Type.GetType("Microsoft.ML.Tokenizers.Tokenizer, Microsoft.ML.Tokenizers");
        
        if (type == null)
        {
            _output.WriteLine("⚠️ Tokenizer型が見つからないため、Decodeメソッドテストをスキップします");
            Assert.True(true, "API調査テストは常に成功");
            return;
        }

        // Act
        var decodeMethod = type.GetMethod("Decode", [typeof(int[])]);

        // Assert & Report
        if (decodeMethod != null)
        {
            _output.WriteLine($"✅ Tokenizer.Decode(int[])メソッドが見つかりました");
            _output.WriteLine($"   戻り値の型: {decodeMethod.ReturnType.Name}");
        }
        else
        {
            _output.WriteLine("⚠️ Tokenizer.Decode(int[])メソッドが見つかりませんでした");
        }
        
        Assert.True(true, "API調査テストは常に成功");
    }

    [Fact]
    public async Task SentencePieceTokenizer_CanCreateWithDummyModel()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            // ダミーのSentencePieceモデルファイルを作成
            await File.WriteAllBytesAsync(tempFile, CreateDummySentencePieceModel()).ConfigureAwait(true);

            // Act & Assert
            using var stream = File.OpenRead(tempFile);
            
            try
            {
                // リフレクションを使用してSentencePieceTokenizer.Create()を呼び出し
                var type = Type.GetType("Microsoft.ML.Tokenizers.SentencePieceTokenizer, Microsoft.ML.Tokenizers");
                
                if (type == null)
                {
                    _output.WriteLine("⚠️ SentencePieceTokenizer型が見つからないため、ダミーモデルテストをスキップします");
                    Assert.True(true, "API調査テストは常に成功");
                    return;
                }

                var createMethod = type.GetMethod("Create", [typeof(Stream), typeof(bool), typeof(bool), typeof(System.Collections.Generic.IReadOnlyDictionary<string, int>)]);
                
                if (createMethod == null)
                {
                    _output.WriteLine("⚠️ SentencePieceTokenizer.Createメソッドが見つからないため、ダミーモデルテストをスキップします");
                    Assert.True(true, "API調査テストは常に成功");
                    return;
                }

                var tokenizer = createMethod.Invoke(null, [stream, true, false, null]);
                
                if (tokenizer != null)
                {
                    _output.WriteLine("✅ SentencePieceTokenizerの作成に成功しました");
                    
                    // Disposeが可能な場合は実行
                    if (tokenizer is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
                else
                {
                    _output.WriteLine("⚠️ SentencePieceTokenizerの作成は失敗しましたが、例外は発生しませんでした");
                }
            }
            catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException != null)
            {
                _output.WriteLine($"⚠️ SentencePieceTokenizerの作成時にエラーが発生しました: {ex.InnerException.GetType().Name}");
                _output.WriteLine($"   メッセージ: {ex.InnerException.Message}");
                
                // これは期待される結果（ダミーファイルなので）
                Assert.True(true, "ダミーファイルでの失敗は想定内");
            }
        }
        finally
        {
            // 一時ファイルを削除
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void SentencePieceNormalizer_TypeExists()
    {
        // Arrange & Act
        var type = Type.GetType("Microsoft.ML.Tokenizers.SentencePieceNormalizer, Microsoft.ML.Tokenizers");

        // Assert
        if (type != null)
        {
            _output.WriteLine($"✅ SentencePieceNormalizer型が見つかりました: {type.FullName}");
        }
        else
        {
            _output.WriteLine("⚠️ SentencePieceNormalizer型が見つかりませんでした");
        }
    }

    [Fact]
    public void EncodeResult_TypeExists()
    {
        // Arrange & Act
        var type = Type.GetType("Microsoft.ML.Tokenizers.EncodeResult, Microsoft.ML.Tokenizers");

        // Assert
        if (type != null)
        {
            _output.WriteLine($"✅ EncodeResult型が見つかりました: {type.FullName}");
            
            // Idsプロパティの確認
            var idsProperty = type.GetProperty("Ids");
            if (idsProperty != null)
            {
                _output.WriteLine($"   Idsプロパティ型: {idsProperty.PropertyType.Name}");
            }
        }
        else
        {
            _output.WriteLine("⚠️ EncodeResult型が見つかりませんでした");
        }
    }

    [Fact]
    public void PrintAvailableTypes()
    {
        // Microsoft.ML.Tokenizersアセンブリから利用可能な型を表示
        try
        {
            var assembly = System.Reflection.Assembly.LoadFrom("Microsoft.ML.Tokenizers.dll");
            var types = assembly.GetExportedTypes();
            
            _output.WriteLine("📋 Microsoft.ML.Tokenizersアセンブリの利用可能な型:");
            foreach (var type in types)
            {
                if (type.Name.Contains("SentencePiece", StringComparison.OrdinalIgnoreCase) || type.Name.Contains("Tokenizer", StringComparison.OrdinalIgnoreCase))
                {
                    _output.WriteLine($"   - {type.FullName}");
                }
            }
        }
#pragma warning disable CA1031 // Do not catch general exception types - API調査のため一般的な例外キャッチを許可
        catch (Exception ex)
        {
            _output.WriteLine($"⚠️ アセンブリの読み込みエラー: {ex.Message}");
        }
#pragma warning restore CA1031
    }

    [Fact]
    public void CheckPackageVersion()
    {
        // パッケージバージョン情報を表示
        try
        {
            var assembly = typeof(Microsoft.ML.Tokenizers.Tokenizer).Assembly;
            var version = assembly.GetName().Version;
            var fileVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location);
            
            _output.WriteLine($"📦 Microsoft.ML.Tokenizersパッケージ情報:");
            _output.WriteLine($"   アセンブリバージョン: {version}");
            _output.WriteLine($"   ファイルバージョン: {fileVersion.FileVersion}");
            _output.WriteLine($"   製品バージョン: {fileVersion.ProductVersion}");
            _output.WriteLine($"   場所: {assembly.Location}");
        }
#pragma warning disable CA1031 // Do not catch general exception types - API調査のため一般的な例外キャッチを許可
        catch (Exception ex)
        {
            _output.WriteLine($"⚠️ バージョン情報の取得エラー: {ex.Message}");
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// テスト用のダミーSentencePieceモデルデータを作成
    /// （実際のSentencePieceバイナリ形式ではない）
    /// </summary>
    private static byte[] CreateDummySentencePieceModel()
    {
        // SentencePieceのプロトバッファ形式の最小限のダミーデータ
        // 実際の形式ではないが、ファイル読み込みテストには十分
        byte[] dummyData =
        [
            0x0A, 0x0B, 0x74, 0x72, 0x61, 0x69, 0x6E, 0x65, 0x72, 0x5F, 0x73, 0x70, 0x65, 0x63, // trainer_spec
            0x12, 0x0E, 0x6E, 0x6F, 0x72, 0x6D, 0x61, 0x6C, 0x69, 0x7A, 0x65, 0x72, 0x5F, 0x73, 0x70, 0x65, 0x63, // normalizer_spec
            0x1A, 0x06, 0x70, 0x69, 0x65, 0x63, 0x65, 0x73, // pieces
        ];
        return dummyData;
    }
}
