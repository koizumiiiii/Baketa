using Microsoft.ML.OnnxRuntime;
using Moq;
using Baketa.Core.Abstractions.GPU;
using Baketa.Infrastructure.OCR.GPU;

namespace Baketa.Infrastructure.Tests.OCR.GPU;

/// <summary>
/// テスト用ONNXセッションプロバイダーMock
/// 実際のONNXモデルファイルを使用せずテストを可能にする
/// </summary>
public class MockOnnxSessionProvider : IOnnxSessionProvider
{
    public Task<InferenceSession> CreateSessionAsync(string modelPath, GpuEnvironmentInfo gpuInfo, CancellationToken cancellationToken = default)
    {
        // テスト用に実際のONNXモデル不要のMockSessionを返す
        // モデルファイルチェックをスキップし、基本的なセッションを作成
        try
        {
            var sessionOptions = CreateOptimalSessionOptions(gpuInfo);
            
            // 最小限のONNXモデルバイナリを生成
            var minimalOnnxBytes = CreateValidMinimalOnnxModel();
            
            // メモリ上のバイナリからInferenceSessionを作成
            var session = new InferenceSession(minimalOnnxBytes, sessionOptions);
            
            return Task.FromResult(session);
        }
        catch (Exception ex)
        {
            // ONNXモデル作成に失敗した場合は例外をスロー
            // テスト環境ではONNXRuntime自体が利用できない可能性がある
            throw new InvalidOperationException($"テスト用MockONNXセッション作成失敗: {ex.Message}", ex);
        }
    }

    public SessionOptions CreateOptimalSessionOptions(GpuEnvironmentInfo gpuInfo)
    {
        // テスト用の基本SessionOptions
        var sessionOptions = new SessionOptions();
        
        // GPU設定をシミュレート（実際のプロバイダー追加なし）
        sessionOptions.InterOpNumThreads = 1;
        sessionOptions.IntraOpNumThreads = Environment.ProcessorCount;
        sessionOptions.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
        sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
        
        return sessionOptions;
    }

    public SessionOptions CreateDirectMLOnlySessionOptions()
    {
        // DirectMLフォールバック用の基本SessionOptions
        var sessionOptions = new SessionOptions();
        
        sessionOptions.EnableMemoryPattern = false;
        sessionOptions.EnableCpuMemArena = false;
        sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_EXTENDED;
        sessionOptions.InterOpNumThreads = 1;
        sessionOptions.IntraOpNumThreads = Environment.ProcessorCount;
        sessionOptions.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
        
        return sessionOptions;
    }

    /// <summary>
    /// 有効な最小限のONNXモデルバイナリを作成
    /// Identity操作のみを含む軽量モデル
    /// </summary>
    private static byte[] CreateValidMinimalOnnxModel()
    {
        // 有効なONNXモデルファイルのBase64エンコード版
        // これは1つの入力と1つの出力を持つ最小限のIdentityモデル
        var base64Model = @"
CKsNEgZibGFua3MaJwgBMiMKA0FkZBgCIhAKBWlucHV0UgVpbnB1dBIFCgNhZGRiBmlucHV0cg==";
        
        try
        {
            return Convert.FromBase64String(base64Model.Replace("\n", "").Replace("\r", ""));
        }
        catch
        {
            // Base64デコードに失敗した場合、非常に基本的なバイト配列を返す
            return new byte[] { 0x08, 0x01, 0x12, 0x04, 0x74, 0x65, 0x73, 0x74 };
        }
    }
}