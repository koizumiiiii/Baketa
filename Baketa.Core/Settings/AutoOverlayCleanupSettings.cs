using System.ComponentModel.DataAnnotations;

namespace Baketa.Core.Settings;

/// <summary>
/// オーバーレイ自動削除システムの設定クラス
/// UltraThink Phase 1 + Gemini Review: 設定値外部化による柔軟性向上
/// </summary>
public sealed class AutoOverlayCleanupSettings
{
    /// <summary>
    /// Circuit Breaker - 最小信頼度閾値 (0.0-1.0)
    /// この値以下の信頼度のイベントは削除要求が却下される
    /// </summary>
    [Range(0.0, 1.0)]
    public float MinConfidenceScore { get; set; } = 0.7f;
    
    /// <summary>
    /// Circuit Breaker - 毎秒最大削除数 (1-100)
    /// レート制限により誤検知によるオーバーレイ大量削除を防止
    /// </summary>
    [Range(1, 100)]
    public int MaxCleanupPerSecond { get; set; } = 10;
    
    /// <summary>
    /// 画像変化検知でのテキスト消失判定閾値 (0.0-1.0)
    /// この値以下の変化率の場合にテキスト消失とみなしてイベントを発行
    /// </summary>
    [Range(0.0, 1.0)]
    public float TextDisappearanceChangeThreshold { get; set; } = 0.05f;
    
    /// <summary>
    /// 統計情報をログ出力する間隔（処理回数）
    /// この回数毎に統計情報がログに出力される（0で無効化）
    /// </summary>
    [Range(0, 10000)]
    public int StatisticsLogInterval { get; set; } = 100;
    
    /// <summary>
    /// 初期化タイムアウト時間（ミリ秒）
    /// InitializeAsync処理のタイムアウト時間
    /// </summary>
    [Range(1000, 60000)]
    public int InitializationTimeoutMs { get; set; } = 10000;
}