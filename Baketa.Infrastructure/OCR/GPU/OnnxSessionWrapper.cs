using Microsoft.ML.OnnxRuntime;
using Baketa.Core.Abstractions.GPU;

namespace Baketa.Infrastructure.OCR.GPU;

/// <summary>
/// ONNX Runtime InferenceSession のラッパー実装
/// Clean Architecture 原則に従い、Infrastructure層で具象型を抽象化
/// Issue #143 Week 2: DI統合とClean Architecture準拠
/// </summary>
internal sealed class OnnxSessionWrapper(
    InferenceSession session,
    GpuEnvironmentInfo gpuEnvironment,
    string sessionName,
    string modelPath) : IOnnxSession
{
    private readonly InferenceSession _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly GpuEnvironmentInfo _gpuEnvironment = gpuEnvironment ?? throw new ArgumentNullException(nameof(gpuEnvironment));
    private readonly string _sessionName = sessionName ?? throw new ArgumentNullException(nameof(sessionName));
    private readonly DateTime _createdAt = DateTime.UtcNow;
    private readonly OnnxSessionInfo _sessionInfo = BuildSessionInfo(session, modelPath);
    private bool _disposed = false;

    public string SessionName => _sessionName;

    public bool IsInitialized => !_disposed && _session != null;

    public DateTime CreatedAt => _createdAt;

    public GpuEnvironmentInfo GpuEnvironment => _gpuEnvironment;

    /// <summary>
    /// 内部のInferenceSessionを取得
    /// Infrastructure層内でのみ使用
    /// </summary>
    internal InferenceSession InternalSession => _session;

    public OnnxSessionInfo GetSessionInfo()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(OnnxSessionWrapper));
        }
        
        return _sessionInfo;
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        try
        {
            _session?.Dispose();
        }
        catch
        {
            // セッション解放時の例外は無視
        }
        
        _disposed = true;
    }

    private static OnnxSessionInfo BuildSessionInfo(InferenceSession session, string modelPath)
    {
        try
        {
            var inputNames = session.InputMetadata?.Keys.ToList() ?? new List<string>();
            var outputNames = session.OutputMetadata?.Keys.ToList() ?? new List<string>();
            
            // プロバイダー情報は実行時に取得困難なため、推定値を使用
            var usedProviders = new List<string> { "Unknown" };
            
            return new OnnxSessionInfo
            {
                ModelPath = modelPath,
                InputNames = inputNames,
                OutputNames = outputNames,
                UsedProviders = usedProviders,
                InitializationTimeMs = 0, // 初期化時間は呼び出し元で設定
                EstimatedMemoryUsageMB = EstimateMemoryUsage(session)
            };
        }
        catch (Exception)
        {
            // メタデータ取得に失敗した場合は最小限の情報を返す
            return new OnnxSessionInfo
            {
                ModelPath = modelPath,
                InputNames = new List<string>(),
                OutputNames = new List<string>(),
                UsedProviders = new List<string> { "Unknown" },
                InitializationTimeMs = 0,
                EstimatedMemoryUsageMB = 0
            };
        }
    }

    private static long EstimateMemoryUsage(InferenceSession session)
    {
        try
        {
            // 簡易的なメモリ使用量推定
            // 実際の実装では、モデルサイズやテンソルサイズから計算
            var inputCount = session.InputMetadata?.Count ?? 0;
            var outputCount = session.OutputMetadata?.Count ?? 0;
            
            // 基本的な推定値（実際の用途に応じて調整）
            return Math.Max((inputCount + outputCount) * 10, 100); // 最低100MB
        }
        catch
        {
            return 100; // デフォルト値
        }
    }
}
