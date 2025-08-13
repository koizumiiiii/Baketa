namespace Baketa.Core.Abstractions.GPU;

/// <summary>
/// TDRエラーの種類
/// </summary>
public enum TdrErrorType
{
    /// <summary>
    /// タイムアウト
    /// </summary>
    Timeout,
    
    /// <summary>
    /// GPU応答停止
    /// </summary>
    Hang,
    
    /// <summary>
    /// メモリ不足
    /// </summary>
    OutOfMemory,
    
    /// <summary>
    /// ドライバエラー
    /// </summary>
    DriverError,
    
    /// <summary>
    /// 不明なエラー
    /// </summary>
    Unknown
}

/// <summary>
/// TDR (Timeout Detection and Recovery) 管理インターフェース
/// DirectX/OpenGL GPU タイムアウト検出と自動回復システム
/// Issue #143 Week 2 Phase 3: TDR対策システム
/// </summary>
public interface ITdrRecoveryManager
{
    /// <summary>
    /// TDR状態を監視し、発生時に自動回復を実行
    /// </summary>
    /// <param name="cancellationToken">キャンセレーション トークン</param>
    /// <returns>TDR監視タスク</returns>
    Task StartTdrMonitoringAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// TDR発生時の回復処理を実行
    /// </summary>
    /// <param name="tdrContext">TDR発生コンテキスト</param>
    /// <param name="cancellationToken">キャンセレーション トークン</param>
    /// <returns>回復結果</returns>
    Task<TdrRecoveryResult> RecoverFromTdrAsync(TdrContext tdrContext, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 現在のTDR状態を取得
    /// </summary>
    /// <param name="pnpDeviceId">GPU PNPデバイスID</param>
    /// <param name="cancellationToken">キャンセレーション トークン</param>
    /// <returns>TDR状態情報</returns>
    Task<TdrStatus> GetTdrStatusAsync(string pnpDeviceId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// TDR予防のためのプリエンプティブ処理を実行
    /// </summary>
    /// <param name="sessionInfo">ONNX Session情報</param>
    /// <param name="cancellationToken">キャンセレーション トークン</param>
    /// <returns>予防処理結果</returns>
    Task<TdrPreventionResult> PreventTdrAsync(OnnxSessionInfo sessionInfo, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// TDR履歴を取得
    /// </summary>
    /// <param name="hours">過去何時間の履歴を取得するか</param>
    /// <returns>TDR履歴</returns>
    IReadOnlyList<TdrHistoryEntry> GetTdrHistory(int hours = 24);
}

/// <summary>
/// TDR発生コンテキスト
/// </summary>
public class TdrContext
{
    /// <summary>
    /// TDR発生GPU PNPデバイスID
    /// </summary>
    public string PnpDeviceId { get; init; } = string.Empty;
    
    /// <summary>
    /// TDR発生時刻
    /// </summary>
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    
    /// <summary>
    /// TDRエラーの種類
    /// </summary>
    public TdrErrorType ErrorType { get; init; }
    
    /// <summary>
    /// TDR発生時のGPU使用率
    /// </summary>
    public double GpuUtilization { get; init; }
    
    /// <summary>
    /// TDR発生時のメモリ使用率
    /// </summary>
    public double MemoryUtilization { get; init; }
    
    /// <summary>
    /// 実行中だったONNX Session情報
    /// </summary>
    public OnnxSessionInfo? ActiveSessionInfo { get; init; }
    
    /// <summary>
    /// TDR発生原因（推定）
    /// </summary>
    public TdrCause EstimatedCause { get; init; }
    
    /// <summary>
    /// 追加コンテキスト情報
    /// </summary>
    public Dictionary<string, object> AdditionalContext { get; init; } = new();
}

/// <summary>
/// TDR回復結果
/// </summary>
public class TdrRecoveryResult
{
    /// <summary>
    /// 回復が成功したかどうか
    /// </summary>
    public bool IsSuccessful { get; init; }
    
    /// <summary>
    /// 回復にかかった時間
    /// </summary>
    public TimeSpan RecoveryDuration { get; init; }
    
    /// <summary>
    /// 回復戦略
    /// </summary>
    public TdrRecoveryStrategy UsedStrategy { get; init; }
    
    /// <summary>
    /// 回復後の推奨アクション
    /// </summary>
    public List<string> RecommendedActions { get; init; } = [];
    
    /// <summary>
    /// 回復過程のメッセージ
    /// </summary>
    public string RecoveryMessage { get; init; } = string.Empty;
}

/// <summary>
/// TDR状態情報
/// </summary>
public class TdrStatus
{
    /// <summary>
    /// 現在TDR状態かどうか
    /// </summary>
    public bool IsInTdrState { get; init; }
    
    /// <summary>
    /// TDR回数（直近24時間）
    /// </summary>
    public int TdrCountLast24Hours { get; init; }
    
    /// <summary>
    /// 最近のTDR回数
    /// </summary>
    public int RecentTdrCount { get; init; }
    
    /// <summary>
    /// システムが健全かどうか
    /// </summary>
    public bool IsHealthy { get; init; }
    
    /// <summary>
    /// 最後のTDR発生時刻
    /// </summary>
    public DateTime? LastTdrTime { get; init; }
    
    /// <summary>
    /// 最後のTDR発生時刻（別名）
    /// </summary>
    public DateTime? LastTdrOccurredAt { get; init; }
    
    /// <summary>
    /// TDRリスク評価
    /// </summary>
    public TdrRiskLevel RiskLevel { get; init; }
    
    /// <summary>
    /// リスク評価詳細
    /// </summary>
    public string RiskAssessment { get; init; } = string.Empty;
}

/// <summary>
/// TDR予防処理結果
/// </summary>
public class TdrPreventionResult
{
    /// <summary>
    /// 予防処理が実行されたかどうか
    /// </summary>
    public bool PreventionExecuted { get; init; }
    
    /// <summary>
    /// 実行された予防戦略
    /// </summary>
    public List<TdrPreventionStrategy> ExecutedStrategies { get; init; } = [];
    
    /// <summary>
    /// 予防効果の推定
    /// </summary>
    public double EstimatedEffectiveness { get; init; }
    
    /// <summary>
    /// 予防処理のメッセージ
    /// </summary>
    public string PreventionMessage { get; init; } = string.Empty;
}

/// <summary>
/// TDR履歴エントリ
/// </summary>
public class TdrHistoryEntry
{
    /// <summary>
    /// TDR発生時刻
    /// </summary>
    public DateTime OccurredAt { get; init; }
    
    /// <summary>
    /// 対象GPU PNPデバイスID
    /// </summary>
    public string PnpDeviceId { get; init; } = string.Empty;
    
    /// <summary>
    /// TDR原因
    /// </summary>
    public TdrCause Cause { get; init; }
    
    /// <summary>
    /// 回復戦略
    /// </summary>
    public TdrRecoveryStrategy RecoveryStrategy { get; init; }
    
    /// <summary>
    /// 回復成功フラグ
    /// </summary>
    public bool RecoverySuccessful { get; init; }
    
    /// <summary>
    /// 回復時間
    /// </summary>
    public TimeSpan RecoveryDuration { get; init; }
}


/// <summary>
/// TDR発生原因
/// </summary>
public enum TdrCause
{
    /// <summary>
    /// 不明
    /// </summary>
    Unknown,
    
    /// <summary>
    /// GPU過負荷
    /// </summary>
    GpuOverload,
    
    /// <summary>
    /// メモリ不足
    /// </summary>
    InsufficientMemory,
    
    /// <summary>
    /// ドライバー問題
    /// </summary>
    DriverIssue,
    
    /// <summary>
    /// 長時間実行
    /// </summary>
    LongRunningTask,
    
    /// <summary>
    /// 並行処理競合
    /// </summary>
    ConcurrencyConflict,
    
    /// <summary>
    /// ハードウェア問題
    /// </summary>
    HardwareIssue
}

/// <summary>
/// TDR回復戦略
/// </summary>
public enum TdrRecoveryStrategy
{
    /// <summary>
    /// 回復なし
    /// </summary>
    None,
    
    /// <summary>
    /// Session再作成
    /// </summary>
    RecreateSession,
    
    /// <summary>
    /// GPU切り替え
    /// </summary>
    SwitchGpu,
    
    /// <summary>
    /// CPU フォールバック
    /// </summary>
    FallbackToCpu,
    
    /// <summary>
    /// ドライバー リセット
    /// </summary>
    ResetDriver,
    
    /// <summary>
    /// 処理分散
    /// </summary>
    DistributeWorkload,
    
    /// <summary>
    /// 完全再起動
    /// </summary>
    FullRestart
}

/// <summary>
/// TDRリスクレベル
/// </summary>
public enum TdrRiskLevel
{
    /// <summary>
    /// 低リスク
    /// </summary>
    Low,
    
    /// <summary>
    /// 中リスク
    /// </summary>
    Medium,
    
    /// <summary>
    /// 高リスク
    /// </summary>
    High,
    
    /// <summary>
    /// クリティカル
    /// </summary>
    Critical
}

/// <summary>
/// TDR予防戦略
/// </summary>
public enum TdrPreventionStrategy
{
    /// <summary>
    /// バッチサイズ削減
    /// </summary>
    ReduceBatchSize,
    
    /// <summary>
    /// タイムアウト延長
    /// </summary>
    ExtendTimeout,
    
    /// <summary>
    /// メモリ使用量制限
    /// </summary>
    LimitMemoryUsage,
    
    /// <summary>
    /// 並行処理制限
    /// </summary>
    LimitConcurrency,
    
    /// <summary>
    /// 優先度調整
    /// </summary>
    AdjustPriority,
    
    /// <summary>
    /// プリエンプション有効化
    /// </summary>
    EnablePreemption
}