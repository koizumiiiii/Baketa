namespace Baketa.Core.Abstractions.GPU;

/// <summary>
/// ONNX Runtime セッションファクトリーの抽象化
/// DI Container完全対応とMulti-GPU環境での最適化
/// Issue #143 Week 2: DI統合とGPU分散処理対応
/// 
/// Clean Architecture原則に従い、Core層では具象型への依存を避け、
/// インターフェースベースで抽象化を行います。
/// </summary>
public interface IOnnxSessionFactory
{
    /// <summary>
    /// テキスト検出用ONNXセッションを作成
    /// GPU環境に最適化されたセッション設定で初期化
    /// </summary>
    /// <param name="gpuInfo">GPU環境情報</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>検出用セッション（ラップされたオブジェクト）</returns>
    Task<IOnnxSession> CreateDetectionSessionAsync(GpuEnvironmentInfo gpuInfo, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// テキスト認識用ONNXセッションを作成
    /// GPU環境に最適化されたセッション設定で初期化
    /// </summary>
    /// <param name="gpuInfo">GPU環境情報</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>認識用セッション（ラップされたオブジェクト）</returns>
    Task<IOnnxSession> CreateRecognitionSessionAsync(GpuEnvironmentInfo gpuInfo, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 言語識別用ONNXセッションを作成
    /// CPU実行に最適化されたセッション設定
    /// </summary>
    /// <param name="gpuInfo">GPU環境情報（フォールバック判定用）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>言語識別用セッション（ラップされたオブジェクト）</returns>
    Task<IOnnxSession> CreateLanguageIdentificationSessionAsync(GpuEnvironmentInfo gpuInfo, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 指定されたGPUデバイスIDに特化したセッションを作成
    /// Multi-GPU環境での負荷分散対応
    /// </summary>
    /// <param name="modelPath">モデルファイルパス</param>
    /// <param name="gpuDeviceId">GPU Device ID</param>
    /// <param name="executionProviders">優先実行プロバイダー</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>指定GPU用セッション（ラップされたオブジェクト）</returns>
    Task<IOnnxSession> CreateSessionForGpuAsync(string modelPath, int gpuDeviceId, 
        ExecutionProvider[] executionProviders, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// セッション作成時のパフォーマンス統計情報を取得
    /// </summary>
    /// <returns>セッション作成統計</returns>
    OnnxSessionCreationStats GetCreationStats();
    
    /// <summary>
    /// ファクトリーリソースのクリーンアップ
    /// </summary>
    Task DisposeAsync();
}

/// <summary>
/// ONNX セッションの抽象化
/// Clean Architecture原則に従いCore層での具象型依存を回避
/// </summary>
public interface IOnnxSession : IDisposable
{
    /// <summary>
    /// セッション名（識別用）
    /// </summary>
    string SessionName { get; }
    
    /// <summary>
    /// セッションが初期化済みかどうか
    /// </summary>
    bool IsInitialized { get; }
    
    /// <summary>
    /// セッション作成時刻
    /// </summary>
    DateTime CreatedAt { get; }
    
    /// <summary>
    /// 使用中のGPU環境情報
    /// </summary>
    GpuEnvironmentInfo GpuEnvironment { get; }
    
    /// <summary>
    /// セッションの詳細情報を取得
    /// 実行時のメタデータやパフォーマンス情報
    /// </summary>
    /// <returns>セッション詳細情報</returns>
    OnnxSessionInfo GetSessionInfo();
}

/// <summary>
/// ONNX セッション詳細情報
/// </summary>
public class OnnxSessionInfo
{
    /// <summary>
    /// モデルファイルパス
    /// </summary>
    public string ModelPath { get; init; } = string.Empty;
    
    /// <summary>
    /// 入力テンソル名一覧
    /// </summary>
    public List<string> InputNames { get; init; } = [];
    
    /// <summary>
    /// 出力テンソル名一覧
    /// </summary>
    public List<string> OutputNames { get; init; } = [];
    
    /// <summary>
    /// 使用プロバイダー名
    /// </summary>
    public List<string> UsedProviders { get; init; } = [];
    
    /// <summary>
    /// セッション初期化時間（ミリ秒）
    /// </summary>
    public double InitializationTimeMs { get; init; }
    
    /// <summary>
    /// 推定メモリ使用量（MB）
    /// </summary>
    public long EstimatedMemoryUsageMB { get; init; }
}

/// <summary>
/// ONNXセッション作成統計情報
/// </summary>
public class OnnxSessionCreationStats
{
    /// <summary>
    /// 作成されたセッション総数
    /// </summary>
    public int TotalSessionsCreated { get; init; }
    
    /// <summary>
    /// 平均作成時間（ミリ秒）
    /// </summary>
    public double AverageCreationTimeMs { get; init; }
    
    /// <summary>
    /// GPU加速セッション数
    /// </summary>
    public int GpuAcceleratedSessions { get; init; }
    
    /// <summary>
    /// CPUフォールバック数
    /// </summary>
    public int CpuFallbackSessions { get; init; }
    
    /// <summary>
    /// TDR発生によるDirectMLフォールバック数
    /// </summary>
    public int TdrFallbackCount { get; init; }
    
    /// <summary>
    /// 最後のセッション作成時刻
    /// </summary>
    public DateTime LastSessionCreatedAt { get; init; }
}