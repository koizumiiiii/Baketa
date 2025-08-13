using Microsoft.ML.OnnxRuntime;
using Moq;
using Baketa.Core.Abstractions.GPU;
using Baketa.Infrastructure.OCR.GPU;

namespace Baketa.Infrastructure.Tests.OCR.GPU;

/// <summary>
/// テスト用ONNXセッションプロバイダーMock
/// 実際のONNXモデルファイルを使用せずテストを可能にする
/// </summary>
/// <summary>
/// テスト用ONNXセッションプロバイダーMock
/// 実際のONNXモデルファイルを使用せずテストを可能にする
/// </summary>
public class MockOnnxSessionProvider : IOnnxSessionProvider
{
    public Task<InferenceSession> CreateSessionAsync(string modelPath, GpuEnvironmentInfo gpuInfo, CancellationToken cancellationToken = default)
    {
        // テスト用に例外を投げて、実際のテスト対象が初期化失敗を適切にハンドリングできるかテストする
        throw new InvalidOperationException("Test environment: ONNX Runtime is not available for unit tests");
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
}