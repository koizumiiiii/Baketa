using System;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Tests.Translation;

/// <summary>
/// ONNXモデルの入力・出力仕様を確認するテスト
/// </summary>
public class OnnxModelDebugTest
{
    private readonly ITestOutputHelper _output;

    public OnnxModelDebugTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void DebugOnnxModelInputOutput()
    {
        // モデルファイルパスの取得
        var modelsBaseDir = FindModelsDirectory();
        var onnxModelPath = Path.Combine(modelsBaseDir, "ONNX", "opus-mt-ja-en.onnx");
        
        _output.WriteLine($"ONNXモデルパス: {onnxModelPath}");
        
        if (!File.Exists(onnxModelPath))
        {
            _output.WriteLine("❌ ONNXモデルファイルが見つかりません");
            return;
        }

        try
        {
            var sessionOptions = new SessionOptions();
            using var session = new InferenceSession(onnxModelPath, sessionOptions);
            
            _output.WriteLine("🔍 OPUS-MT ONNX モデル解析");
            _output.WriteLine("=" + new string('=', 50));
            
            // 入力メタデータの確認
            _output.WriteLine("\n📥 モデル入力:");
            var inputMetadata = session.InputMetadata;
            for (int i = 0; i < inputMetadata.Count; i++)
            {
                var kvp = inputMetadata.ElementAt(i);
                var name = kvp.Key;
                var metadata = kvp.Value;
                
                _output.WriteLine($"  {i + 1}. 名前: '{name}'");
                _output.WriteLine($"     型: {metadata.ElementType}");
                _output.WriteLine($"     形状: [{string.Join(", ", metadata.Dimensions)}]");
                _output.WriteLine($"     説明: {metadata.ToString()}");
                _output.WriteLine("");
            }
            
            // 出力メタデータの確認
            _output.WriteLine("📤 モデル出力:");
            var outputMetadata = session.OutputMetadata;
            for (int i = 0; i < outputMetadata.Count; i++)
            {
                var kvp = outputMetadata.ElementAt(i);
                var name = kvp.Key;
                var metadata = kvp.Value;
                
                _output.WriteLine($"  {i + 1}. 名前: '{name}'");
                _output.WriteLine($"     型: {metadata.ElementType}");
                _output.WriteLine($"     形状: [{string.Join(", ", metadata.Dimensions)}]");
                _output.WriteLine($"     説明: {metadata.ToString()}");
                _output.WriteLine("");
            }
            
            _output.WriteLine("=" + new string('=', 50));
            
            // 重要な分析ポイント
            _output.WriteLine("\n🔍 分析結果:");
            
            var hasEncoderHiddenStates = inputMetadata.ContainsKey("encoder_hidden_states");
            var hasEncoderOutputs = inputMetadata.ContainsKey("encoder_outputs");
            var hasLastHiddenState = inputMetadata.ContainsKey("last_hidden_state");
            
            _output.WriteLine($"  ✅ encoder_hidden_states 入力: {hasEncoderHiddenStates}");
            _output.WriteLine($"  ✅ encoder_outputs 入力: {hasEncoderOutputs}");
            _output.WriteLine($"  ✅ last_hidden_state 入力: {hasLastHiddenState}");
            
            if (!hasEncoderHiddenStates && !hasEncoderOutputs && !hasLastHiddenState)
            {
                _output.WriteLine("  ⚠️  エンコーダー出力に関連する入力が見つかりません！");
                _output.WriteLine("  💡 これが「You about you」問題の根本原因の可能性があります");
            }
            
            _output.WriteLine("\n📋 現在の実装で使用している入力名:");
            _output.WriteLine("  - input_ids");
            _output.WriteLine("  - attention_mask");
            _output.WriteLine("  - decoder_input_ids");
            
            _output.WriteLine("\n💡 修正すべき入力名（推測）:");
            foreach (var input in inputMetadata.Keys)
            {
                if (input.Contains("encoder") || input.Contains("hidden") || input.Contains("context"))
                {
                    _output.WriteLine($"  - {input} ← 重要！");
                }
                else
                {
                    _output.WriteLine($"  - {input}");
                }
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"❌ エラー: {ex.Message}");
            _output.WriteLine($"詳細: {ex}");
        }
    }

    private static string FindModelsDirectory()
    {
        var candidatePaths = new[]
        {
            @"E:\dev\Baketa\Models",
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "..", "Models"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "Models"),
        };

        foreach (var path in candidatePaths)
        {
            var normalizedPath = Path.GetFullPath(path);
            if (Directory.Exists(normalizedPath))
            {
                return normalizedPath;
            }
        }

        throw new DirectoryNotFoundException("Modelsディレクトリが見つかりません");
    }
}