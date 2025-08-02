using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.ML.OnnxRuntime;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Tests.Translation.Local.Onnx.SentencePiece.Native;

/// <summary>
/// ONNX-Communityモデルの入力仕様分析テスト
/// </summary>
public class OnnxModelSpecAnalysisTests(ITestOutputHelper output)
{
    [Fact]
    public void AnalyzeOnnxCommunityModels_ShouldDisplayInputOutputSpecs()
    {
        // Arrange
        var projectRoot = GetProjectRootDirectory();
        var decoderPath = Path.Combine(projectRoot, "Models", "ONNX", "onnx-community-decoder_model.onnx");
        var encoderPath = Path.Combine(projectRoot, "Models", "ONNX", "onnx-community-encoder_model.onnx");
        var originalPath = Path.Combine(projectRoot, "Models", "ONNX", "helsinki-opus-mt-ja-en.onnx");

        output.WriteLine("🔍 ONNX Model Specification Analysis");
        output.WriteLine("");

        // Decoder Model Analysis
        if (File.Exists(decoderPath))
        {
            output.WriteLine($"📦 Decoder Model: {Path.GetFileName(decoderPath)} ({new FileInfo(decoderPath).Length / (1024 * 1024)} MB)");
            
            try
            {
                using var decoderSession = new InferenceSession(decoderPath);
                
                output.WriteLine("  Inputs:");
                foreach (var input in decoderSession.InputMetadata)
                {
                    var shape = string.Join(", ", input.Value.Dimensions.Select(d => d.ToString()));
                    output.WriteLine($"    - {input.Key}: {input.Value.ElementType} [{shape}]");
                }
                
                output.WriteLine("  Outputs:");
                foreach (var output_meta in decoderSession.OutputMetadata)
                {
                    var shape = string.Join(", ", output_meta.Value.Dimensions.Select(d => d.ToString()));
                    output.WriteLine($"    - {output_meta.Key}: {output_meta.Value.ElementType} [{shape}]");
                }
                output.WriteLine("");
            }
            catch (Exception ex)
            {
                output.WriteLine($"  ❌ Error loading decoder: {ex.Message}");
                output.WriteLine("");
            }
        }

        // Encoder Model Analysis
        if (File.Exists(encoderPath))
        {
            output.WriteLine($"📦 Encoder Model: {Path.GetFileName(encoderPath)} ({new FileInfo(encoderPath).Length / (1024 * 1024)} MB)");
            
            try
            {
                using var encoderSession = new InferenceSession(encoderPath);
                
                output.WriteLine("  Inputs:");
                foreach (var input in encoderSession.InputMetadata)
                {
                    var shape = string.Join(", ", input.Value.Dimensions.Select(d => d.ToString()));
                    output.WriteLine($"    - {input.Key}: {input.Value.ElementType} [{shape}]");
                }
                
                output.WriteLine("  Outputs:");
                foreach (var output_meta in encoderSession.OutputMetadata)
                {
                    var shape = string.Join(", ", output_meta.Value.Dimensions.Select(d => d.ToString()));
                    output.WriteLine($"    - {output_meta.Key}: {output_meta.Value.ElementType} [{shape}]");
                }
                output.WriteLine("");
            }
            catch (Exception ex)
            {
                output.WriteLine($"  ❌ Error loading encoder: {ex.Message}");
                output.WriteLine("");
            }
        }

        // Original Model Analysis (for comparison)
        if (File.Exists(originalPath))
        {
            output.WriteLine($"📦 Original Model: {Path.GetFileName(originalPath)} ({new FileInfo(originalPath).Length / (1024 * 1024)} MB)");
            
            try
            {
                using var originalSession = new InferenceSession(originalPath);
                
                output.WriteLine("  Inputs:");
                foreach (var input in originalSession.InputMetadata)
                {
                    var shape = string.Join(", ", input.Value.Dimensions.Select(d => d.ToString()));
                    output.WriteLine($"    - {input.Key}: {input.Value.ElementType} [{shape}]");
                }
                
                output.WriteLine("  Outputs:");
                foreach (var output_meta in originalSession.OutputMetadata)
                {
                    var shape = string.Join(", ", output_meta.Value.Dimensions.Select(d => d.ToString()));
                    output.WriteLine($"    - {output_meta.Key}: {output_meta.Value.ElementType} [{shape}]");
                }
                output.WriteLine("");
            }
            catch (Exception ex)
            {
                output.WriteLine($"  ❌ Error loading original: {ex.Message}");
                output.WriteLine("");
            }
        }

        // 問題分析と推奨事項
        output.WriteLine("🔧 Analysis & Recommendations:");
        output.WriteLine("  1. ONNX-Community models use separate encoder/decoder architecture");
        output.WriteLine("  2. Decoder model does not expect 'attention_mask' input");
        output.WriteLine("  3. Need to implement proper encoder-decoder pipeline");
        output.WriteLine("  4. Current AlphaOpusMtTranslationEngine assumes single model with attention_mask");
        output.WriteLine("");

        // 少なくとも1つのモデルファイルが存在することを確認
        (File.Exists(decoderPath) || File.Exists(encoderPath) || File.Exists(originalPath)).Should().BeTrue();
    }
    
    private static string GetProjectRootDirectory()
    {
        var currentDir = Directory.GetCurrentDirectory();
        while (currentDir != null && !File.Exists(Path.Combine(currentDir, "Baketa.sln")))
        {
            currentDir = Directory.GetParent(currentDir)?.FullName;
        }
        return currentDir ?? throw new DirectoryNotFoundException("Project root not found");
    }
}