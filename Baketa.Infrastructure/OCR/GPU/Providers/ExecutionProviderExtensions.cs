using Baketa.Core.Abstractions.GPU;
using Microsoft.ML.OnnxRuntime;

namespace Baketa.Infrastructure.OCR.GPU.Providers;

/// <summary>
/// Execution Provider Factory 拡張メソッド
/// Core層のインターフェースからInfrastructure層のSessionOptions作成
/// Phase 4: Clean Architecture準拠の実装分離
/// Issue #181: ONNX Runtime プロバイダー固有メソッド対応
/// </summary>
public static class ExecutionProviderExtensions
{
    /// <summary>
    /// IExecutionProviderFactoryからSessionOptionsを作成
    /// Infrastructure層でONNX Runtime依存を解決
    /// </summary>
    /// <param name="factory">プロバイダーファクトリー</param>
    /// <param name="environment">GPU環境情報</param>
    /// <returns>最適化されたSessionOptions</returns>
    public static SessionOptions CreateSessionOptions(this IExecutionProviderFactory factory, GpuEnvironmentInfo environment)
    {
        var sessionOptions = new SessionOptions();

        try
        {
            // プロバイダー設定を取得
            var providerOptions = factory.GetProviderOptions(environment);

            // Issue #181: ONNX Runtime プロバイダー固有メソッドを使用
            // AppendExecutionProvider() は QNN, SNPE, XNNPACK, AZURE のみ対応
            // DirectML, CUDA, CPU は専用メソッドを使用する必要がある
            AppendExecutionProviderByType(sessionOptions, factory.Type, providerOptions, environment);

            // 共通最適化設定
            ApplyCommonOptimizations(sessionOptions, factory.Type, environment);

            return sessionOptions;
        }
        catch (Exception ex)
        {
            sessionOptions.Dispose();
            throw new InvalidOperationException($"Failed to create SessionOptions for {factory.Type}", ex);
        }
    }

    /// <summary>
    /// プロバイダータイプに応じた専用メソッドで実行プロバイダーを追加
    /// Issue #181: ONNX Runtime 1.x 互換性対応
    /// </summary>
    private static void AppendExecutionProviderByType(
        SessionOptions sessionOptions,
        ExecutionProvider providerType,
        Dictionary<string, string> providerOptions,
        GpuEnvironmentInfo environment)
    {
        switch (providerType)
        {
            case ExecutionProvider.DirectML:
                // DirectML専用メソッド（デバイスID指定）
                var dmlDeviceId = 0;
                if (providerOptions.TryGetValue("device_id", out var dmlDeviceIdStr) &&
                    int.TryParse(dmlDeviceIdStr, out var parsedDmlDeviceId))
                {
                    dmlDeviceId = parsedDmlDeviceId;
                }
                sessionOptions.AppendExecutionProvider_DML(dmlDeviceId);
                Console.WriteLine($"✅ [Issue #181] DirectML プロバイダー追加 (DeviceId: {dmlDeviceId})");
                break;

            case ExecutionProvider.CUDA:
                // CUDA専用メソッド（デバイスID指定）
                var cudaDeviceId = 0;
                if (providerOptions.TryGetValue("device_id", out var cudaDeviceIdStr) &&
                    int.TryParse(cudaDeviceIdStr, out var parsedCudaDeviceId))
                {
                    cudaDeviceId = parsedCudaDeviceId;
                }
                sessionOptions.AppendExecutionProvider_CUDA(cudaDeviceId);
                Console.WriteLine($"✅ [Issue #181] CUDA プロバイダー追加 (DeviceId: {cudaDeviceId})");
                break;

            case ExecutionProvider.TensorRT:
                // TensorRT専用メソッド（デバイスID指定）
                var trtDeviceId = 0;
                if (providerOptions.TryGetValue("device_id", out var trtDeviceIdStr) &&
                    int.TryParse(trtDeviceIdStr, out var parsedTrtDeviceId))
                {
                    trtDeviceId = parsedTrtDeviceId;
                }
                sessionOptions.AppendExecutionProvider_Tensorrt(trtDeviceId);
                Console.WriteLine($"✅ [Issue #181] TensorRT プロバイダー追加 (DeviceId: {trtDeviceId})");
                break;

            case ExecutionProvider.CPU:
                // CPUはデフォルトで含まれるため追加不要
                // ただしスレッド数等の設定は ApplyCommonOptimizations で適用
                Console.WriteLine("✅ [Issue #181] CPU プロバイダー使用（デフォルト）");
                break;

            case ExecutionProvider.OpenVINO:
            case ExecutionProvider.OpenCL:
                // これらは実行環境依存のため基本設定のみ適用
                ApplyProviderSpecificSettings(sessionOptions, providerType, providerOptions);
                Console.WriteLine($"⚠️ [Issue #181] {providerType} プロバイダー: 基本設定のみ適用");
                break;

            default:
                throw new NotSupportedException($"Unsupported execution provider: {providerType}");
        }
    }

    /// <summary>
    /// プロバイダータイプ別共通最適化設定
    /// </summary>
    private static void ApplyCommonOptimizations(SessionOptions sessionOptions, ExecutionProvider providerType, GpuEnvironmentInfo environment)
    {
        // GPU系プロバイダー共通設定
        if (IsGpuProvider(providerType))
        {
            sessionOptions.EnableCpuMemArena = false; // GPU使用時はCPUアリーナ無効
            sessionOptions.ExecutionMode = ExecutionMode.ORT_PARALLEL;
        }
        else
        {
            // CPU系プロバイダー設定
            sessionOptions.EnableCpuMemArena = true;
            sessionOptions.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
            sessionOptions.IntraOpNumThreads = Environment.ProcessorCount;
        }

        // 共通設定
        sessionOptions.EnableMemoryPattern = true;
        sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

        // 高性能GPU向け追加設定
        if (environment.IsHighPerformanceGpu && IsGpuProvider(providerType))
        {
            sessionOptions.EnableProfiling = false; // プロファイリング無効で性能優先
        }
    }

    /// <summary>
    /// プロバイダー固有設定を適用（OpenVINO/OpenCL用）
    /// </summary>
    private static void ApplyProviderSpecificSettings(SessionOptions sessionOptions, ExecutionProvider providerType, Dictionary<string, string> providerOptions)
    {
        // OpenVINO固有設定
        if (providerType == ExecutionProvider.OpenVINO)
        {
            // スレッド数設定
            if (providerOptions.TryGetValue("num_of_threads", out var numThreads) && int.TryParse(numThreads, out var threads))
            {
                sessionOptions.IntraOpNumThreads = threads;
            }

            // CPU最適化設定
            if (providerOptions.ContainsKey("enable_cpu_mem_arena"))
            {
                sessionOptions.EnableCpuMemArena = true;
            }
        }
    }

    /// <summary>
    /// GPU系プロバイダーかどうかを判定
    /// </summary>
    private static bool IsGpuProvider(ExecutionProvider providerType)
    {
        return providerType switch
        {
            ExecutionProvider.CUDA => true,
            ExecutionProvider.DirectML => true,
            ExecutionProvider.TensorRT => true,
            ExecutionProvider.OpenCL => true,
            _ => false
        };
    }
}
