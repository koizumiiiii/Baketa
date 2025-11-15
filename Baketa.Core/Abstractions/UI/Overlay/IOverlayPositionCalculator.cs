using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.UI.Overlay;

/// <summary>
/// オーバーレイ位置計算インターフェース
/// 既存の OverlayPositioningService を抽象化・拡張
/// Clean Architecture: Core層 - 抽象化定義
/// </summary>
public interface IOverlayPositionCalculator
{
    /// <summary>
    /// 最適な表示位置を計算
    /// 衝突回避・画面外はみ出し防止・マルチモニター対応を含む
    /// </summary>
    /// <param name="request">位置計算要求</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>最適化された表示領域</returns>
    Task<Rectangle> CalculateOptimalPositionAsync(PositionCalculationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 複数オーバーレイの位置を一括最適化
    /// レイアウト全体の調整・重複解消
    /// </summary>
    /// <param name="requests">位置計算要求のリスト</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>最適化された位置のリスト（要求と同じ順序）</returns>
    Task<IEnumerable<Rectangle>> CalculateBatchPositionsAsync(IEnumerable<PositionCalculationRequest> requests, CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定領域での衝突検出
    /// 他のオーバーレイとの重複判定
    /// </summary>
    /// <param name="area">チェック対象領域</param>
    /// <param name="existingOverlays">既存オーバーレイの位置リスト</param>
    /// <param name="excludeIds">衝突判定から除外するID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>衝突が検出された場合はtrue</returns>
    Task<bool> DetectCollisionAsync(Rectangle area, IEnumerable<OverlayPositionInfo> existingOverlays, IEnumerable<string>? excludeIds = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 画面境界に合わせて領域を調整
    /// マルチモニター環境での画面外はみ出し防止
    /// </summary>
    /// <param name="area">調整対象領域</param>
    /// <param name="targetMonitor">対象モニター（nullの場合は自動検出）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>調整された領域</returns>
    Task<Rectangle> AdjustToScreenBoundsAsync(Rectangle area, int? targetMonitor = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定座標が含まれるモニターを取得
    /// マルチモニター環境での最適表示
    /// </summary>
    /// <param name="point">座標</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>モニター情報、見つからない場合はnull</returns>
    Task<MonitorInfo?> GetMonitorFromPointAsync(Point point, CancellationToken cancellationToken = default);

    /// <summary>
    /// 利用可能なすべてのモニター情報を取得
    /// マルチモニター対応・設定UI用
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>モニター情報のリスト</returns>
    Task<IEnumerable<MonitorInfo>> GetAvailableMonitorsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// DPI スケーリングを考慮した座標変換
    /// 高DPI環境での正確な位置計算
    /// </summary>
    /// <param name="logicalArea">論理座標系の領域</param>
    /// <param name="targetMonitor">対象モニター</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>物理座標系に変換された領域</returns>
    Task<Rectangle> ConvertLogicalToPhysicalAsync(Rectangle logicalArea, int? targetMonitor = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 位置計算器の初期化
    /// モニター情報の取得・設定読み込み
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 位置計算要求データ
/// </summary>
public record PositionCalculationRequest
{
    /// <summary>
    /// オーバーレイID
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// 希望する表示領域
    /// </summary>
    public required Rectangle DesiredArea { get; init; }

    /// <summary>
    /// 表示テキスト（サイズ計算用）
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// 優先度（高い値ほど優先的に配置）
    /// </summary>
    public int Priority { get; init; } = 0;

    /// <summary>
    /// 配置戦略
    /// </summary>
    public PositionStrategy Strategy { get; init; } = PositionStrategy.AvoidCollision;

    /// <summary>
    /// 最大移動距離（元位置からの許容移動量）
    /// </summary>
    public int MaxDisplacement { get; init; } = 100;

    /// <summary>
    /// フォント情報（サイズ計算用）
    /// </summary>
    public FontInfo? FontInfo { get; init; }

    /// <summary>
    /// 対象モニター（nullの場合は自動検出）
    /// </summary>
    public int? TargetMonitor { get; init; }

    /// <summary>
    /// スタイル情報（描画サイズ計算用）
    /// </summary>
    public OverlayRenderStyle? Style { get; init; }
}

/// <summary>
/// オーバーレイ位置情報
/// </summary>
public record OverlayPositionInfo
{
    /// <summary>
    /// オーバーレイID
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// 現在の表示領域
    /// </summary>
    public required Rectangle Area { get; init; }

    /// <summary>
    /// 優先度
    /// </summary>
    public int Priority { get; init; } = 0;

    /// <summary>
    /// Z-index（表示レイヤー）
    /// </summary>
    public int ZIndex { get; init; } = 0;

    /// <summary>
    /// 固定位置フラグ（移動不可）
    /// </summary>
    public bool IsFixed { get; init; } = false;

    /// <summary>
    /// 最終更新時刻
    /// </summary>
    public DateTimeOffset LastUpdate { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// モニター情報
/// </summary>
public record MonitorInfo
{
    /// <summary>
    /// モニターID
    /// </summary>
    public required int Id { get; init; }

    /// <summary>
    /// モニター名
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 作業領域（タスクバー等を除いた使用可能領域）
    /// </summary>
    public required Rectangle WorkingArea { get; init; }

    /// <summary>
    /// 全画面領域
    /// </summary>
    public required Rectangle FullArea { get; init; }

    /// <summary>
    /// DPIスケーリング率
    /// </summary>
    public double DpiScale { get; init; } = 1.0;

    /// <summary>
    /// プライマリモニターかどうか
    /// </summary>
    public bool IsPrimary { get; init; } = false;

    /// <summary>
    /// 色深度
    /// </summary>
    public int ColorDepth { get; init; } = 32;

    /// <summary>
    /// リフレッシュレート
    /// </summary>
    public int RefreshRate { get; init; } = 60;
}

/// <summary>
/// 配置戦略
/// </summary>
public enum PositionStrategy
{
    /// <summary>
    /// 元位置を維持（衝突を無視）
    /// </summary>
    KeepOriginal,

    /// <summary>
    /// 衝突回避（自動位置調整）
    /// </summary>
    AvoidCollision,

    /// <summary>
    /// 画面中央寄せ
    /// </summary>
    CenterScreen,

    /// <summary>
    /// 最も空いている領域に配置
    /// </summary>
    FindOpenSpace,

    /// <summary>
    /// グリッド配置（整列）
    /// </summary>
    GridAlign,

    /// <summary>
    /// カスケード配置（段階的ずらし）
    /// </summary>
    Cascade
}

/// <summary>
/// フォント情報
/// サイズ計算用
/// </summary>
public record FontInfo
{
    /// <summary>
    /// フォントファミリー
    /// </summary>
    public string Family { get; init; } = "Segoe UI";

    /// <summary>
    /// フォントサイズ（論理単位）
    /// </summary>
    public double Size { get; init; } = 12.0;

    /// <summary>
    /// フォント太さ
    /// </summary>
    public FontWeight Weight { get; init; } = FontWeight.Normal;

    /// <summary>
    /// 斜体フラグ
    /// </summary>
    public bool IsItalic { get; init; } = false;
}

/// <summary>
/// 位置計算統計情報
/// パフォーマンス監視・デバッグ用
/// </summary>
public record PositionCalculationStatistics
{
    /// <summary>
    /// 処理した位置計算要求数
    /// </summary>
    public long TotalCalculations { get; init; }

    /// <summary>
    /// 衝突回避が必要だった回数
    /// </summary>
    public long CollisionAvoidanceCount { get; init; }

    /// <summary>
    /// 平均計算時間（ミリ秒）
    /// </summary>
    public double AverageCalculationTime { get; init; }

    /// <summary>
    /// 最大計算時間（ミリ秒）
    /// </summary>
    public double MaxCalculationTime { get; init; }

    /// <summary>
    /// 画面外配置修正回数
    /// </summary>
    public long OffScreenCorrectionCount { get; init; }

    /// <summary>
    /// マルチモニター配置回数
    /// </summary>
    public long MultiMonitorPlacementCount { get; init; }
}
