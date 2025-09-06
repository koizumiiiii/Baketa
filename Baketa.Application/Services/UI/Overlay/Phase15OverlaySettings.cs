using System;

namespace Baketa.Application.Services.UI.Overlay;

/// <summary>
/// Phase 15 新オーバーレイシステム設定
/// 段階的移行とフィーチャーフラグ管理
/// </summary>
public class Phase15OverlaySettings
{
    /// <summary>
    /// Phase 15 新システムの有効化フラグ
    /// </summary>
    public bool EnableNewOverlaySystem { get; set; } = false;

    /// <summary>
    /// 新システム使用時の旧システム並行実行フラグ
    /// デバッグ・比較用
    /// </summary>
    public bool EnableLegacySystemFallback { get; set; } = true;

    /// <summary>
    /// 新システム初期化のタイムアウト時間
    /// </summary>
    public TimeSpan InitializationTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 詳細ログの有効化
    /// パフォーマンス監視・デバッグ用
    /// </summary>
    public bool EnableDetailedLogging { get; set; } = true;

    /// <summary>
    /// 新システムエラー時のフォールバック有効化
    /// </summary>
    public bool EnableErrorFallback { get; set; } = true;

    /// <summary>
    /// パフォーマンス統計の収集有効化
    /// </summary>
    public bool EnablePerformanceStatistics { get; set; } = false;

    /// <summary>
    /// 重複検出設定
    /// </summary>
    public CollisionDetectionConfiguration CollisionDetection { get; set; } = new();
}

/// <summary>
/// 重複検出設定の詳細
/// </summary>
public class CollisionDetectionConfiguration
{
    /// <summary>
    /// 重複防止ウィンドウ（秒）
    /// Phase 13互換デフォルト値
    /// </summary>
    public double DuplicationPreventionWindowSeconds { get; set; } = 2.0;

    /// <summary>
    /// 自動クリーンアップ閾値
    /// </summary>
    public int AutoCleanupThreshold { get; set; } = 100;

    /// <summary>
    /// エントリ最大生存時間（分）
    /// </summary>
    public double MaxEntryLifetimeMinutes { get; set; } = 5.0;

    /// <summary>
    /// 位置衝突検出の有効化
    /// </summary>
    public bool EnablePositionCollisionDetection { get; set; } = true;

    /// <summary>
    /// 位置重複判定の閾値（0.0-1.0）
    /// </summary>
    public double PositionOverlapThreshold { get; set; } = 0.7;
}