using Baketa.Core.Abstractions.GPU;
using Microsoft.ML.OnnxRuntime;

namespace Baketa.Infrastructure.OCR.GPU;

/// <summary>
/// ONNX Runtime セッション作成の抽象化
/// テスト時のMock化とDI対応
/// </summary>
public interface IOnnxSessionProvider
{
    /// <summary>
    /// 指定されたモデルパスとGPU環境に基づいてONNXセッションを作成
    /// </summary>
    /// <param name="modelPath">ONNXモデルファイルパス</param>
    /// <param name="gpuInfo">GPU環境情報</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>作成されたInferenceSession</returns>
    Task<InferenceSession> CreateSessionAsync(string modelPath, GpuEnvironmentInfo gpuInfo, CancellationToken cancellationToken = default);

    /// <summary>
    /// セッションオプションの作成
    /// </summary>
    /// <param name="gpuInfo">GPU環境情報</param>
    /// <returns>最適化されたSessionOptions</returns>
    SessionOptions CreateOptimalSessionOptions(GpuEnvironmentInfo gpuInfo);

    /// <summary>
    /// DirectML専用セッションオプション作成（TDRフォールバック用）
    /// </summary>
    /// <returns>DirectML専用SessionOptions</returns>
    SessionOptions CreateDirectMLOnlySessionOptions();
}
