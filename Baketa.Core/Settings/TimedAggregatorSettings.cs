using System.ComponentModel.DataAnnotations;

namespace Baketa.Core.Settings;

/// <summary>
/// TimedChunkAggregator時間軸統合システムの設定クラス
/// 戦略書設計: translation-quality-improvement-strategy.md 完全準拠
/// UltraThink Phase: Feature Flag制御による段階的有効化
/// </summary>
public sealed class TimedAggregatorSettings
{
    /// <summary>
    /// TimedChunkAggregator機能の有効/無効制御
    /// Feature Flag: 段階的ロールアウト制御
    /// </summary>
    public bool IsFeatureEnabled { get; set; } = false;
    
    /// <summary>
    /// チャンク集約のバッファ遅延時間（ミリ秒）
    /// 戦略書仕様: 150ms基準値、OCR間隔との調整
    /// </summary>
    [Range(50, 2000)]
    public int BufferDelayMs { get; set; } = 150;
    
    /// <summary>
    /// メモリ保護のための最大チャンク数
    /// この数を超えると強制的に処理を実行
    /// </summary>
    [Range(10, 1000)]
    public int MaxChunkCount { get; set; } = 100;
    
    /// <summary>
    /// 強制フラッシュ時間（ミリ秒）
    /// 無限タイマーリセットを防ぐ安全装置
    /// </summary>
    [Range(1000, 30000)]
    public int ForceFlushMs { get; set; } = 5000;
    
    /// <summary>
    /// パフォーマンス統計ログの有効化
    /// 本番環境では無効化推奨
    /// </summary>
    public bool EnablePerformanceLogging { get; set; } = true;
    
    /// <summary>
    /// 統計情報出力間隔（集約イベント数）
    /// この回数毎にパフォーマンス統計を出力
    /// </summary>
    [Range(1, 1000)]
    public int PerformanceLogInterval { get; set; } = 10;
    
    /// <summary>
    /// 開発環境用デフォルト設定プロファイル
    /// 戦略書フィードバック反映: 即座利用可能な開発設定
    /// </summary>
    public static TimedAggregatorSettings Development => new()
    {
        IsFeatureEnabled = true,
        BufferDelayMs = 150,
        MaxChunkCount = 50,
        ForceFlushMs = 3000,
        EnablePerformanceLogging = true,
        PerformanceLogInterval = 5
    };
    
    /// <summary>
    /// 本番環境用デフォルト設定プロファイル
    /// 戦略書設計: 安全性重視、ログ最小化
    /// </summary>
    public static TimedAggregatorSettings Production => new()
    {
        IsFeatureEnabled = false, // 段階的有効化のため初期値false
        BufferDelayMs = 150,
        MaxChunkCount = 100,
        ForceFlushMs = 5000,
        EnablePerformanceLogging = false,
        PerformanceLogInterval = 50
    };
}