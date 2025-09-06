using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.UI.Overlay;

/// <summary>
/// オーバーレイ描画インターフェース
/// UI層の具体的な描画実装を抽象化
/// Clean Architecture: Core層 - 抽象化定義（UI層実装）
/// </summary>
public interface IOverlayRenderer
{
    /// <summary>
    /// オーバーレイを描画
    /// 新規作成または既存の更新を自動判定
    /// </summary>
    /// <param name="info">描画するオーバーレイ情報</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>描画が成功した場合はtrue</returns>
    Task<bool> RenderOverlayAsync(OverlayInfo info, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// オーバーレイの表示内容を更新
    /// テキスト・位置・スタイル変更に対応
    /// </summary>
    /// <param name="overlayId">更新対象オーバーレイID</param>
    /// <param name="updateInfo">更新情報</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>更新が成功した場合はtrue</returns>
    Task<bool> UpdateOverlayAsync(string overlayId, OverlayRenderUpdate updateInfo, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// オーバーレイの可視性を制御
    /// パフォーマンス最適化: 削除・再作成ではなく可視性のみ変更
    /// </summary>
    /// <param name="overlayId">対象オーバーレイID</param>
    /// <param name="visible">表示する場合はtrue</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>可視性変更が成功した場合はtrue</returns>
    Task<bool> SetVisibilityAsync(string overlayId, bool visible, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 全オーバーレイの可視性を一括制御
    /// ホットキー切り替え・一時非表示機能用
    /// </summary>
    /// <param name="visible">表示する場合はtrue</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>変更されたオーバーレイ数</returns>
    Task<int> SetAllVisibilityAsync(bool visible, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// オーバーレイを削除
    /// UI要素の完全削除とリソース解放
    /// </summary>
    /// <param name="overlayId">削除対象オーバーレイID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>削除が成功した場合はtrue</returns>
    Task<bool> RemoveOverlayAsync(string overlayId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 指定領域内のオーバーレイを一括削除
    /// 画面変化時の自動クリーンアップ用
    /// </summary>
    /// <param name="area">削除対象領域</param>
    /// <param name="excludeIds">削除から除外するID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>削除されたオーバーレイ数</returns>
    Task<int> RemoveOverlaysInAreaAsync(Rectangle area, IEnumerable<string>? excludeIds = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// すべてのオーバーレイを削除
    /// 完全リセット・アプリケーション終了時用
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    Task RemoveAllOverlaysAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// オーバーレイの現在位置を取得
    /// 衝突検出・位置調整用
    /// </summary>
    /// <param name="overlayId">対象オーバーレイID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>現在の表示領域、存在しない場合はnull</returns>
    Task<Rectangle?> GetOverlayBoundsAsync(string overlayId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// レンダラーの初期化
    /// UI フレームワークの準備・設定読み込み
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    Task InitializeAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 現在レンダリングされているオーバーレイ数
    /// パフォーマンス監視用
    /// </summary>
    int RenderedCount { get; }
    
    /// <summary>
    /// レンダラーがサポートする機能フラグ
    /// プラットフォーム・フレームワーク依存機能の判定用
    /// </summary>
    RendererCapabilities Capabilities { get; }
}

/// <summary>
/// オーバーレイレンダー更新情報
/// </summary>
public record OverlayRenderUpdate
{
    /// <summary>
    /// 更新するテキスト（nullの場合は変更なし）
    /// </summary>
    public string? Text { get; init; }
    
    /// <summary>
    /// 更新する表示領域（nullの場合は変更なし）
    /// </summary>
    public Rectangle? DisplayArea { get; init; }
    
    /// <summary>
    /// 更新するスタイル情報（nullの場合は変更なし）
    /// </summary>
    public OverlayRenderStyle? Style { get; init; }
    
    /// <summary>
    /// Z-index変更（nullの場合は変更なし）
    /// </summary>
    public int? ZIndex { get; init; }
    
    /// <summary>
    /// 更新時のアニメーション設定
    /// </summary>
    public OverlayAnimation? Animation { get; init; }
}

/// <summary>
/// オーバーレイ描画スタイル
/// UI層の具体的なスタイル実装を抽象化
/// </summary>
public record OverlayRenderStyle
{
    /// <summary>
    /// 背景色（ARGB形式）
    /// </summary>
    public uint? BackgroundColor { get; init; }
    
    /// <summary>
    /// 前景色・テキスト色（ARGB形式）
    /// </summary>
    public uint? ForegroundColor { get; init; }
    
    /// <summary>
    /// フォントサイズ
    /// </summary>
    public double? FontSize { get; init; }
    
    /// <summary>
    /// フォントファミリー
    /// </summary>
    public string? FontFamily { get; init; }
    
    /// <summary>
    /// フォント太さ
    /// </summary>
    public FontWeight? FontWeight { get; init; }
    
    /// <summary>
    /// 不透明度（0.0-1.0）
    /// </summary>
    public double? Opacity { get; init; }
    
    /// <summary>
    /// 枠線色（ARGB形式）
    /// </summary>
    public uint? BorderColor { get; init; }
    
    /// <summary>
    /// 枠線の太さ
    /// </summary>
    public double? BorderThickness { get; init; }
    
    /// <summary>
    /// 角丸半径
    /// </summary>
    public double? CornerRadius { get; init; }
}

/// <summary>
/// フォント太さ定義
/// </summary>
public enum FontWeight
{
    Thin = 100,
    Light = 300,
    Normal = 400,
    Medium = 500,
    Bold = 700,
    ExtraBold = 800,
    Black = 900
}

/// <summary>
/// オーバーレイアニメーション設定
/// </summary>
public record OverlayAnimation
{
    /// <summary>
    /// アニメーション種類
    /// </summary>
    public AnimationType Type { get; init; } = AnimationType.None;
    
    /// <summary>
    /// アニメーション継続時間
    /// </summary>
    public TimeSpan Duration { get; init; } = TimeSpan.FromMilliseconds(300);
    
    /// <summary>
    /// イージング関数
    /// </summary>
    public AnimationEasing Easing { get; init; } = AnimationEasing.EaseOut;
}

/// <summary>
/// アニメーション種類
/// </summary>
public enum AnimationType
{
    None,
    FadeIn,
    FadeOut,
    SlideIn,
    SlideOut,
    ScaleIn,
    ScaleOut
}

/// <summary>
/// アニメーションイージング
/// </summary>
public enum AnimationEasing
{
    Linear,
    EaseIn,
    EaseOut,
    EaseInOut
}

/// <summary>
/// レンダラー機能フラグ
/// プラットフォーム・フレームワーク依存機能の判定用
/// </summary>
[Flags]
public enum RendererCapabilities
{
    None = 0,
    HardwareAcceleration = 1 << 0,
    Transparency = 1 << 1,
    Animation = 1 << 2,
    MultiMonitor = 1 << 3,
    HighDpi = 1 << 4,
    TouchSupport = 1 << 5
}

/// <summary>
/// レンダリング統計情報
/// パフォーマンス監視・デバッグ用
/// </summary>
public record RenderingStatistics
{
    /// <summary>
    /// 描画されたオーバーレイ総数
    /// </summary>
    public long TotalRendered { get; init; }
    
    /// <summary>
    /// 削除されたオーバーレイ総数
    /// </summary>
    public long TotalRemoved { get; init; }
    
    /// <summary>
    /// 平均描画時間（ミリ秒）
    /// </summary>
    public double AverageRenderTime { get; init; }
    
    /// <summary>
    /// 現在のフレームレート（FPS）
    /// </summary>
    public double CurrentFps { get; init; }
    
    /// <summary>
    /// GPU使用率（0.0-1.0）
    /// </summary>
    public double GpuUsage { get; init; }
}