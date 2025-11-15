using Baketa.Core.Abstractions.GPU;
using Microsoft.ML.OnnxRuntime;

namespace Baketa.Infrastructure.OCR.GPU.Providers;

/// <summary>
/// Execution Provider Factory 拡張メソッド
/// Core層のインターフェースからInfrastructure層のSessionOptions作成
/// Phase 4: Clean Architecture準拠の実装分離
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

            // ONNX Runtime制限対応: 一部プロバイダーは直接追加できない
            if (CanAppendDirectly(factory.Type))
            {
                var providerName = GetOnnxProviderName(factory.Type);
                sessionOptions.AppendExecutionProvider(providerName, providerOptions);
            }
            else
            {
                // OpenVINO等の場合は基本設定のみ適用
                ApplyProviderSpecificSettings(sessionOptions, factory.Type, providerOptions);
            }

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
    /// ExecutionProviderタイプからONNX Runtime用プロバイダー名に変換
    /// </summary>
    private static string GetOnnxProviderName(ExecutionProvider providerType)
    {
        return providerType switch
        {
            ExecutionProvider.CPU => "CPUExecutionProvider",
            ExecutionProvider.CUDA => "CUDAExecutionProvider",
            ExecutionProvider.DirectML => "DmlExecutionProvider",
            ExecutionProvider.TensorRT => "TensorrtExecutionProvider",
            ExecutionProvider.OpenVINO => "OpenVINOExecutionProvider",
            ExecutionProvider.OpenCL => "OpenCLExecutionProvider",
            _ => throw new ArgumentException($"Unsupported provider type: {providerType}")
        };
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
    /// ONNX Runtime AppendExecutionProviderで直接追加可能かどうか判定
    /// 制限: OpenVINO等は実行時環境に依存するため直接追加できない場合がある
    /// </summary>
    private static bool CanAppendDirectly(ExecutionProvider providerType)
    {
        return providerType switch
        {
            ExecutionProvider.CPU => true,
            ExecutionProvider.CUDA => true,
            ExecutionProvider.DirectML => true,
            ExecutionProvider.TensorRT => true,
            ExecutionProvider.OpenVINO => false, // 実行環境依存
            ExecutionProvider.OpenCL => false,   // 実行環境依存
            _ => false
        };
    }

    /// <summary>
    /// プロバイダー固有設定を適用（直接追加できない場合）
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
