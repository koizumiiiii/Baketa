namespace Baketa.Core.Abstractions.GPU;

/// <summary>
/// ONNX Session永続キャッシュインターフェース
/// GPU推論セッションの永続化・復元システム
/// Issue #143 Week 2 Phase 3: 高速起動・高可用性システム
/// </summary>
public interface IPersistentSessionCache
{
    /// <summary>
    /// ONNX Sessionをキャッシュに保存
    /// </summary>
    /// <param name="cacheKey">キャッシュキー</param>
    /// <param name="sessionData">セッションデータ</param>
    /// <param name="metadata">メタデータ</param>
    /// <param name="cancellationToken">キャンセレーション トークン</param>
    /// <returns>保存結果</returns>
    Task<CacheStoreResult> StoreSessionAsync(string cacheKey, SessionCacheData sessionData, SessionMetadata metadata, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// キャッシュからONNX Sessionを復元
    /// </summary>
    /// <param name="cacheKey">キャッシュキー</param>
    /// <param name="cancellationToken">キャンセレーション トークン</param>
    /// <returns>復元されたセッションデータ</returns>
    Task<CacheRetrieveResult> RetrieveSessionAsync(string cacheKey, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// キャッシュエントリが存在するかチェック
    /// </summary>
    /// <param name="cacheKey">キャッシュキー</param>
    /// <param name="cancellationToken">キャンセレーション トークン</param>
    /// <returns>存在確認結果</returns>
    Task<bool> ExistsAsync(string cacheKey, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// キャッシュエントリを削除
    /// </summary>
    /// <param name="cacheKey">キャッシュキー</param>
    /// <param name="cancellationToken">キャンセレーション トークン</param>
    /// <returns>削除結果</returns>
    Task<bool> RemoveAsync(string cacheKey, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 期限切れキャッシュエントリを削除
    /// </summary>
    /// <param name="cancellationToken">キャンセレーション トークン</param>
    /// <returns>削除件数</returns>
    Task<int> CleanupExpiredEntriesAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// キャッシュ統計情報を取得
    /// </summary>
    /// <param name="cancellationToken">キャンセレーション トークン</param>
    /// <returns>統計情報</returns>
    Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// キャッシュ最適化を実行
    /// </summary>
    /// <param name="cancellationToken">キャンセレーション トークン</param>
    /// <returns>最適化結果</returns>
    Task<CacheOptimizationResult> OptimizeCacheAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 利用可能なキャッシュキー一覧を取得
    /// </summary>
    /// <param name="pattern">検索パターン（オプション）</param>
    /// <param name="cancellationToken">キャンセレーション トークン</param>
    /// <returns>キャッシュキー一覧</returns>
    Task<IReadOnlyList<string>> GetAvailableKeysAsync(string? pattern = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// セッションキャッシュデータ
/// </summary>
public class SessionCacheData
{
    /// <summary>
    /// モデルファイルパス
    /// </summary>
    public string ModelPath { get; init; } = string.Empty;
    
    /// <summary>
    /// セッション設定
    /// </summary>
    public SessionConfiguration Configuration { get; init; } = new();
    
    /// <summary>
    /// 実行プロバイダー設定
    /// </summary>
    public ExecutionProviderConfiguration ProviderConfig { get; init; } = new();
    
    /// <summary>
    /// セッション固有オプション
    /// </summary>
    public Dictionary<string, object> SessionOptions { get; init; } = [];
    
    /// <summary>
    /// 初期化データ（シリアライズ可能な形式）
    /// </summary>
    public byte[]? InitializationData { get; init; }
    
    /// <summary>
    /// ウォームアップデータ
    /// </summary>
    public WarmupData? WarmupData { get; init; }
}

/// <summary>
/// セッションメタデータ
/// </summary>
public class SessionMetadata
{
    /// <summary>
    /// 作成日時
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    
    /// <summary>
    /// 最終アクセス日時
    /// </summary>
    public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// 有効期限
    /// </summary>
    public DateTime ExpiresAt { get; init; } = DateTime.UtcNow.AddHours(24);
    
    /// <summary>
    /// 使用回数
    /// </summary>
    public int UsageCount { get; set; }
    
    /// <summary>
    /// GPU環境情報
    /// </summary>
    public GpuEnvironmentInfo GpuInfo { get; init; } = new();
    
    /// <summary>
    /// パフォーマンス統計
    /// </summary>
    public SessionPerformanceStats PerformanceStats { get; init; } = new();
    
    /// <summary>
    /// キャッシュ優先度
    /// </summary>
    public CachePriority Priority { get; init; } = CachePriority.Normal;
    
    /// <summary>
    /// タグ情報
    /// </summary>
    public List<string> Tags { get; init; } = [];
    
    /// <summary>
    /// カスタム属性
    /// </summary>
    public Dictionary<string, object> CustomAttributes { get; init; } = [];
}

/// <summary>
/// セッション設定
/// </summary>
public class SessionConfiguration
{
    /// <summary>
    /// セッションID
    /// </summary>
    public string SessionId { get; init; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// モデル名
    /// </summary>
    public string ModelName { get; init; } = string.Empty;
    
    /// <summary>
    /// モデルバージョン
    /// </summary>
    public string ModelVersion { get; init; } = "1.0";
    
    /// <summary>
    /// 入出力テンソル情報
    /// </summary>
    public TensorConfiguration TensorConfig { get; init; } = new();
    
    /// <summary>
    /// 最大バッチサイズ
    /// </summary>
    public int MaxBatchSize { get; init; } = 1;
    
    /// <summary>
    /// タイムアウト設定
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// 実行プロバイダー設定
/// </summary>
public class ExecutionProviderConfiguration
{
    /// <summary>
    /// プライマリプロバイダー
    /// </summary>
    public ExecutionProvider PrimaryProvider { get; init; } = ExecutionProvider.CPU;
    
    /// <summary>
    /// フォールバックプロバイダー
    /// </summary>
    public List<ExecutionProvider> FallbackProviders { get; init; } = [];
    
    /// <summary>
    /// プロバイダー固有オプション
    /// </summary>
    public Dictionary<ExecutionProvider, Dictionary<string, object>> ProviderOptions { get; init; } = [];
    
    /// <summary>
    /// GPU デバイスID
    /// </summary>
    public int GpuDeviceId { get; init; }
    
    /// <summary>
    /// メモリ制限（MB）
    /// </summary>
    public long? MemoryLimitMB { get; init; }
}

/// <summary>
/// テンソル設定
/// </summary>
public class TensorConfiguration
{
    /// <summary>
    /// 入力テンソル名とサイズ
    /// </summary>
    public Dictionary<string, int[]> InputTensors { get; init; } = [];
    
    /// <summary>
    /// 出力テンソル名とサイズ
    /// </summary>
    public Dictionary<string, int[]> OutputTensors { get; init; } = [];
    
    /// <summary>
    /// テンソル データ型
    /// </summary>
    public Dictionary<string, string> TensorDataTypes { get; init; } = [];
    
    /// <summary>
    /// 動的シェイプ対応
    /// </summary>
    public bool SupportsDynamicShapes { get; init; }
}

/// <summary>
/// ウォームアップデータ
/// </summary>
public class WarmupData
{
    /// <summary>
    /// ウォームアップ入力データ
    /// </summary>
    public Dictionary<string, byte[]> WarmupInputs { get; init; } = [];
    
    /// <summary>
    /// 期待される出力形状
    /// </summary>
    public Dictionary<string, int[]> ExpectedOutputShapes { get; init; } = [];
    
    /// <summary>
    /// ウォームアップ実行回数
    /// </summary>
    public int WarmupIterations { get; init; } = 3;
    
    /// <summary>
    /// ウォームアップタイムアウト
    /// </summary>
    public TimeSpan WarmupTimeout { get; init; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// セッションパフォーマンス統計
/// </summary>
public class SessionPerformanceStats
{
    /// <summary>
    /// 初期化時間
    /// </summary>
    public TimeSpan InitializationTime { get; set; }
    
    /// <summary>
    /// 平均実行時間
    /// </summary>
    public TimeSpan AverageExecutionTime { get; set; }
    
    /// <summary>
    /// 最小実行時間
    /// </summary>
    public TimeSpan MinExecutionTime { get; set; }
    
    /// <summary>
    /// 最大実行時間
    /// </summary>
    public TimeSpan MaxExecutionTime { get; set; }
    
    /// <summary>
    /// 実行回数
    /// </summary>
    public int ExecutionCount { get; set; }
    
    /// <summary>
    /// エラー回数
    /// </summary>
    public int ErrorCount { get; set; }
    
    /// <summary>
    /// メモリ使用量統計
    /// </summary>
    public MemoryUsageStats MemoryUsage { get; set; } = new();
}

/// <summary>
/// メモリ使用量統計
/// </summary>
public class MemoryUsageStats
{
    /// <summary>
    /// 平均メモリ使用量（MB）
    /// </summary>
    public long AverageMemoryUsageMB { get; set; }
    
    /// <summary>
    /// 最大メモリ使用量（MB）
    /// </summary>
    public long PeakMemoryUsageMB { get; set; }
    
    /// <summary>
    /// GPU メモリ使用量（MB）
    /// </summary>
    public long GpuMemoryUsageMB { get; set; }
}

/// <summary>
/// キャッシュ保存結果
/// </summary>
public class CacheStoreResult
{
    /// <summary>
    /// 保存が成功したかどうか
    /// </summary>
    public bool IsSuccessful { get; init; }
    
    /// <summary>
    /// 保存にかかった時間
    /// </summary>
    public TimeSpan StoreDuration { get; init; }
    
    /// <summary>
    /// 保存サイズ（バイト）
    /// </summary>
    public long StoredSize { get; init; }
    
    /// <summary>
    /// エラーメッセージ
    /// </summary>
    public string? ErrorMessage { get; init; }
    
    /// <summary>
    /// 既存エントリを上書きしたかどうか
    /// </summary>
    public bool OverwroteExisting { get; init; }
}

/// <summary>
/// キャッシュ取得結果
/// </summary>
public class CacheRetrieveResult
{
    /// <summary>
    /// 取得が成功したかどうか
    /// </summary>
    public bool IsSuccessful { get; init; }
    
    /// <summary>
    /// セッションデータ
    /// </summary>
    public SessionCacheData? SessionData { get; init; }
    
    /// <summary>
    /// メタデータ
    /// </summary>
    public SessionMetadata? Metadata { get; init; }
    
    /// <summary>
    /// 取得にかかった時間
    /// </summary>
    public TimeSpan RetrieveDuration { get; init; }
    
    /// <summary>
    /// エラーメッセージ
    /// </summary>
    public string? ErrorMessage { get; init; }
    
    /// <summary>
    /// キャッシュヒット率
    /// </summary>
    public double HitRatio { get; init; }
}

/// <summary>
/// キャッシュ統計
/// </summary>
public class CacheStatistics
{
    /// <summary>
    /// 総エントリ数
    /// </summary>
    public int TotalEntries { get; init; }
    
    /// <summary>
    /// 使用サイズ（バイト）
    /// </summary>
    public long UsedSize { get; init; }
    
    /// <summary>
    /// ヒット回数
    /// </summary>
    public int HitCount { get; init; }
    
    /// <summary>
    /// ミス回数
    /// </summary>
    public int MissCount { get; init; }
    
    /// <summary>
    /// ヒット率
    /// </summary>
    public double HitRatio => TotalRequests > 0 ? (double)HitCount / TotalRequests : 0.0;
    
    /// <summary>
    /// 総リクエスト数
    /// </summary>
    public int TotalRequests => HitCount + MissCount;
    
    /// <summary>
    /// 平均エントリサイズ（バイト）
    /// </summary>
    public long AverageEntrySize => TotalEntries > 0 ? UsedSize / TotalEntries : 0;
    
    /// <summary>
    /// 期限切れエントリ数
    /// </summary>
    public int ExpiredEntries { get; init; }
    
    /// <summary>
    /// 最終最適化実行時刻
    /// </summary>
    public DateTime? LastOptimizationTime { get; init; }
}

/// <summary>
/// キャッシュ最適化結果
/// </summary>
public class CacheOptimizationResult
{
    /// <summary>
    /// 最適化が実行されたかどうか
    /// </summary>
    public bool OptimizationExecuted { get; init; }
    
    /// <summary>
    /// 削除されたエントリ数
    /// </summary>
    public int RemovedEntries { get; init; }
    
    /// <summary>
    /// 解放されたサイズ（バイト）
    /// </summary>
    public long FreedSize { get; init; }
    
    /// <summary>
    /// 最適化にかかった時間
    /// </summary>
    public TimeSpan OptimizationDuration { get; init; }
    
    /// <summary>
    /// 最適化実行アクション
    /// </summary>
    public List<string> ExecutedActions { get; init; } = [];
    
    /// <summary>
    /// 最適化後のパフォーマンス改善予測
    /// </summary>
    public double EstimatedPerformanceImprovement { get; init; }
}

/// <summary>
/// キャッシュ優先度
/// </summary>
public enum CachePriority
{
    /// <summary>
    /// 低優先度
    /// </summary>
    Low,
    
    /// <summary>
    /// 通常優先度
    /// </summary>
    Normal,
    
    /// <summary>
    /// 高優先度
    /// </summary>
    High,
    
    /// <summary>
    /// クリティカル優先度
    /// </summary>
    Critical
}