using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Baketa.Core.Abstractions.GPU;

namespace Baketa.Infrastructure.OCR.GPU;

/// <summary>
/// デフォルトONNXセッションプロバイダー実装
/// 実際のONNX Runtimeを使用したセッション作成
/// </summary>
public sealed class DefaultOnnxSessionProvider(ILogger<DefaultOnnxSessionProvider> logger) : IOnnxSessionProvider
{
    private readonly ILogger<DefaultOnnxSessionProvider> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<InferenceSession> CreateSessionAsync(string modelPath, GpuEnvironmentInfo gpuInfo, CancellationToken cancellationToken = default)
    {
        await Task.Yield(); // 非同期化のため
        
        _logger.LogDebug("ONNXセッション作成: {ModelPath}", modelPath);
        
        var sessionOptions = CreateOptimalSessionOptions(gpuInfo);
        return new InferenceSession(modelPath, sessionOptions);
    }

    public SessionOptions CreateOptimalSessionOptions(GpuEnvironmentInfo gpuInfo)
    {
        var sessionOptions = new SessionOptions();
        
        // GPU環境に応じた最適プロバイダー選択
        foreach (var provider in gpuInfo.RecommendedProviders)
        {
            try
            {
                switch (provider)
                {
                    case ExecutionProvider.TensorRT:
                        _logger.LogDebug("TensorRT Execution Provider追加");
                        sessionOptions.AppendExecutionProvider_Tensorrt(0);
                        break;
                        
                    case ExecutionProvider.CUDA:
                        _logger.LogDebug("CUDA Execution Provider追加");
                        sessionOptions.AppendExecutionProvider_CUDA(0);
                        break;
                        
                    case ExecutionProvider.DirectML:
                        _logger.LogDebug("DirectML Execution Provider追加");
                        sessionOptions.AppendExecutionProvider_DML(0);
                        break;
                        
                    case ExecutionProvider.OpenVINO:
                        _logger.LogDebug("OpenVINO Execution Provider追加");
                        sessionOptions.AppendExecutionProvider_OpenVINO("GPU");
                        break;
                        
                    case ExecutionProvider.CPU:
                        _logger.LogDebug("CPU Execution Provider使用（フォールバック）");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{Provider} Execution Provider追加失敗", provider);
            }
        }
        
        // 共通最適化設定
        sessionOptions.InterOpNumThreads = 1;
        sessionOptions.IntraOpNumThreads = Environment.ProcessorCount;
        sessionOptions.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
        sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
        
        return sessionOptions;
    }

    public SessionOptions CreateDirectMLOnlySessionOptions()
    {
        var sessionOptions = new SessionOptions();
        
        try
        {
            _logger.LogInformation("TDRフォールバック: DirectML専用セッション作成中");
            
            sessionOptions.AppendExecutionProvider_DML(0);
            
            sessionOptions.EnableMemoryPattern = false;
            sessionOptions.EnableCpuMemArena = false;
            sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_EXTENDED;
            sessionOptions.InterOpNumThreads = 1;
            sessionOptions.IntraOpNumThreads = Environment.ProcessorCount;
            sessionOptions.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
            
            _logger.LogDebug("DirectML専用セッションオプション作成完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DirectML専用セッションオプション作成失敗");
            throw;
        }
        
        return sessionOptions;
    }
}
