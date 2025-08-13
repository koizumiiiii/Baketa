namespace Baketa.Core.Settings;

/// <summary>
/// 詳細設定クラス
/// 高度な機能とシステム最適化の設定を管理
/// </summary>
public sealed class AdvancedSettings
{
    /// <summary>
    /// 高度な設定の有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Advanced", "高度な設定有効", 
        Description = "高度な設定とオプションを有効にします", 
        WarningMessage = "高度な設定を変更すると予期しない動作が発生する可能性があります")]
    public bool EnableAdvancedFeatures { get; set; }
    
    /// <summary>
    /// メモリ管理の最適化
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Advanced", "メモリ最適化", 
        Description = "メモリ使用量を最適化します")]
    public bool OptimizeMemoryUsage { get; set; } = true;
    
    /// <summary>
    /// ガベージコレクション調整
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Advanced", "GC最適化", 
        Description = "ガベージコレクションを最適化してパフォーマンスを向上させます")]
    public bool OptimizeGarbageCollection { get; set; } = true;
    
    /// <summary>
    /// CPU親和性の設定
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Advanced", "CPU親和性", 
        Description = "特定のCPUコアにプロセスを割り当てます（0=自動）", 
        MinValue = 0, 
        MaxValue = 64)]
    public int CpuAffinityMask { get; set; }
    
    /// <summary>
    /// プロセス優先度
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Advanced", "プロセス優先度", 
        Description = "アプリケーションのプロセス優先度", 
        ValidValues = [ProcessPriority.Low, ProcessPriority.BelowNormal, ProcessPriority.Normal, ProcessPriority.AboveNormal, ProcessPriority.High])]
    public ProcessPriority ProcessPriority { get; set; } = ProcessPriority.Normal;
    
    /// <summary>
    /// ワーカースレッド数
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Advanced", "ワーカースレッド数", 
        Description = "バックグラウンド処理に使用するスレッド数（0=自動）", 
        MinValue = 0, 
        MaxValue = 32)]
    public int WorkerThreadCount { get; set; }
    
    /// <summary>
    /// I/Oスレッド数
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Advanced", "I/Oスレッド数", 
        Description = "ファイル入出力に使用するスレッド数（0=自動）", 
        MinValue = 0, 
        MaxValue = 16)]
    public int IoThreadCount { get; set; }
    
    /// <summary>
    /// バッファリング戦略
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Advanced", "バッファリング戦略", 
        Description = "メモリバッファリングの戦略", 
        ValidValues = [BufferingStrategy.Conservative, BufferingStrategy.Balanced, BufferingStrategy.Aggressive])]
    public BufferingStrategy BufferingStrategy { get; set; } = BufferingStrategy.Balanced;
    
    /// <summary>
    /// キューサイズ制限
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Advanced", "キューサイズ制限", 
        Description = "内部処理キューの最大サイズ", 
        MinValue = 10, 
        MaxValue = 10000)]
    public int MaxQueueSize { get; set; } = 1000;
    
    /// <summary>
    /// ネットワークタイムアウト
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Advanced", "ネットワークタイムアウト", 
        Description = "ネットワーク通信のタイムアウト時間", 
        Unit = "秒", 
        MinValue = 5, 
        MaxValue = 300)]
    public int NetworkTimeoutSeconds { get; set; } = 30;
    
    /// <summary>
    /// HTTP接続プール最大サイズ
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Advanced", "HTTP接続プール", 
        Description = "HTTP接続プールの最大サイズ", 
        MinValue = 1, 
        MaxValue = 100)]
    public int MaxHttpConnections { get; set; } = 10;
    
    /// <summary>
    /// リトライ戦略の設定
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Advanced", "リトライ戦略", 
        Description = "失敗時の再試行戦略", 
        ValidValues = [RetryStrategy.None, RetryStrategy.Linear, RetryStrategy.Exponential, RetryStrategy.Custom])]
    public RetryStrategy RetryStrategy { get; set; } = RetryStrategy.Exponential;
    
    /// <summary>
    /// 最大リトライ回数
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Advanced", "最大リトライ回数", 
        Description = "失敗時の最大再試行回数", 
        MinValue = 0, 
        MaxValue = 10)]
    public int MaxRetryCount { get; set; } = 3;
    
    /// <summary>
    /// リトライ間隔（ミリ秒）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Advanced", "リトライ間隔", 
        Description = "再試行間の待機時間", 
        Unit = "ms", 
        MinValue = 100, 
        MaxValue = 30000)]
    public int RetryDelayMs { get; set; } = 1000;
    
    /// <summary>
    /// 統計情報収集の有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Advanced", "統計情報収集", 
        Description = "パフォーマンス統計情報を収集します")]
    public bool EnableStatisticsCollection { get; set; } = true;
    
    /// <summary>
    /// 統計データの保持期間（日）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Advanced", "統計保持期間", 
        Description = "統計データを保持する期間", 
        Unit = "日", 
        MinValue = 1, 
        MaxValue = 365)]
    public int StatisticsRetentionDays { get; set; } = 30;
    
    /// <summary>
    /// プロファイリングの有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Advanced", "プロファイリング", 
        Description = "詳細なパフォーマンスプロファイリングを有効にします")]
    public bool EnableProfiling { get; set; }
    
    /// <summary>
    /// 異常検出の有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Advanced", "異常検出", 
        Description = "システムの異常状態を自動検出します")]
    public bool EnableAnomalyDetection { get; set; } = true;
    
    /// <summary>
    /// 自動修復機能
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Advanced", "自動修復", 
        Description = "検出された問題を自動的に修復します")]
    public bool EnableAutoRecovery { get; set; } = true;
    
    /// <summary>
    /// 実験的機能の有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "Advanced", "実験的機能", 
        Description = "実験的な機能を有効にします（開発者向け）", 
        WarningMessage = "実験的機能は不安定である可能性があります")]
    public bool EnableExperimentalFeatures { get; set; }
    
    /// <summary>
    /// 内部API露出
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "Advanced", "内部API露出", 
        Description = "内部APIへのアクセスを許可します（開発者向け）")]
    public bool ExposeInternalApis { get; set; }
    
    /// <summary>
    /// デバッグブレークポイント
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "Advanced", "デバッグブレーク", 
        Description = "特定の条件でデバッガーブレークを発生させます（開発者向け）")]
    public bool EnableDebugBreaks { get; set; }
    
    /// <summary>
    /// メモリダンプ生成
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "Advanced", "メモリダンプ", 
        Description = "クラッシュ時にメモリダンプを生成します（開発者向け）")]
    public bool GenerateMemoryDumps { get; set; }
    
    /// <summary>
    /// カスタム設定ファイルパス
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "Advanced", "カスタム設定パス", 
        Description = "カスタム設定ファイルのパス（開発者向け）")]
    public string CustomConfigPath { get; set; } = string.Empty;
}

/// <summary>
/// プロセス優先度レベル
/// </summary>
public enum ProcessPriority
{
    /// <summary>
    /// 低優先度
    /// </summary>
    Low,
    
    /// <summary>
    /// 標準以下
    /// </summary>
    BelowNormal,
    
    /// <summary>
    /// 標準
    /// </summary>
    Normal,
    
    /// <summary>
    /// 標準以上
    /// </summary>
    AboveNormal,
    
    /// <summary>
    /// 高優先度
    /// </summary>
    High
}

/// <summary>
/// バッファリング戦略
/// </summary>
public enum BufferingStrategy
{
    /// <summary>
    /// 保守的（メモリ使用量優先）
    /// </summary>
    Conservative,
    
    /// <summary>
    /// バランス（メモリと速度のバランス）
    /// </summary>
    Balanced,
    
    /// <summary>
    /// 積極的（速度優先）
    /// </summary>
    Aggressive
}

/// <summary>
/// リトライ戦略
/// </summary>
public enum RetryStrategy
{
    /// <summary>
    /// リトライしない
    /// </summary>
    None,
    
    /// <summary>
    /// 一定間隔
    /// </summary>
    Linear,
    
    /// <summary>
    /// 指数バックオフ
    /// </summary>
    Exponential,
    
    /// <summary>
    /// カスタム
    /// </summary>
    Custom
}
