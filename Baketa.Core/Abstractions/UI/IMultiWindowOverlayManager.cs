using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Translation;

namespace Baketa.Core.Abstractions.UI;

/// <summary>
/// 複数ウィンドウオーバーレイ管理の抽象インターフェース
/// Phase 2: 座標ベース翻訳表示のための基盤
/// ユーザー要求: 「テキストの塊ごとに」「複数のウィンドウで」「対象のテキストの座標位置付近に表示」
/// </summary>
public interface IMultiWindowOverlayManager
{
    /// <summary>
    /// テキストチャンクのリストを複数のオーバーレイウィンドウで表示
    /// </summary>
    /// <param name="chunks">表示するテキストチャンクのリスト</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    Task DisplayTranslationResultsAsync(
        IReadOnlyList<TextChunk> chunks, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 特定のチャンクIDのオーバーレイを更新
    /// </summary>
    /// <param name="chunkId">更新対象のチャンクID</param>
    /// <param name="chunk">更新後のテキストチャンク</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    Task UpdateOverlayAsync(
        int chunkId, 
        TextChunk chunk, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// すべてのオーバーレイウィンドウを非表示にする
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    Task HideAllOverlaysAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 特定のチャンクIDのオーバーレイを非表示にする
    /// </summary>
    /// <param name="chunkId">非表示にするチャンクID</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    Task HideOverlayAsync(int chunkId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// オーバーレイの表示設定を更新
    /// </summary>
    /// <param name="options">オーバーレイ表示オプション</param>
    Task ConfigureOverlayOptionsAsync(OverlayDisplayOptions options);
    
    /// <summary>
    /// アクティブなオーバーレイウィンドウの数を取得
    /// </summary>
    int GetActiveOverlayCount();
    
    /// <summary>
    /// 特定の領域と重複するオーバーレイを検出
    /// 新しいオーバーレイの配置計算用
    /// </summary>
    /// <param name="region">チェック対象の領域</param>
    /// <returns>重複するオーバーレイのチャンクIDリスト</returns>
    IReadOnlyList<int> GetOverlappingOverlays(Rectangle region);
    
    /// <summary>
    /// オーバーレイマネージャーのリソースをクリーンアップ
    /// </summary>
    Task CleanupAsync();
}

/// <summary>
/// オーバーレイ表示のオプション設定
/// </summary>
public sealed class OverlayDisplayOptions
{
    /// <summary>オーバーレイの透明度（0.0-1.0）</summary>
    public double Opacity { get; init; } = 0.9;
    
    /// <summary>背景色</summary>
    public string BackgroundColor { get; init; } = "#000000";
    
    /// <summary>テキスト色</summary>
    public string TextColor { get; init; } = "#FFFFFF";
    
    /// <summary>フォントサイズ</summary>
    public int FontSize { get; init; } = 14;
    
    /// <summary>フォントファミリー</summary>
    public string FontFamily { get; init; } = "Segoe UI";
    
    /// <summary>パディング（ピクセル）</summary>
    public int Padding { get; init; } = 8;
    
    /// <summary>角の丸み（ピクセル）</summary>
    public int CornerRadius { get; init; } = 4;
    
    /// <summary>最大表示時間（ミリ秒、0で無制限）</summary>
    public int MaxDisplayTimeMs { get; init; }
    
    /// <summary>フェードイン時間（ミリ秒）</summary>
    public int FadeInTimeMs { get; init; } = 200;
    
    /// <summary>フェードアウト時間（ミリ秒）</summary>
    public int FadeOutTimeMs { get; init; } = 200;
    
    /// <summary>オーバーレイ間の最小距離（ピクセル）</summary>
    public int MinOverlayDistance { get; init; } = 10;
    
    /// <summary>画面境界からの最小距離（ピクセル）</summary>
    public int MinScreenMargin { get; init; } = 20;
    
    /// <summary>最大同時表示オーバーレイ数</summary>
    public int MaxConcurrentOverlays { get; init; } = 10;
    
    /// <summary>クリック通過を有効化（オーバーレイを透過してクリック）</summary>
    public bool EnableClickThrough { get; init; } = true;
    
    /// <summary>オーバーレイの最大幅（ピクセル）</summary>
    public int MaxWidth { get; init; } = 400;
    
    /// <summary>オーバーレイの最大高さ（ピクセル）</summary>
    public int MaxHeight { get; init; } = 200;
    
    /// <summary>常に最前面に表示</summary>
    public bool AlwaysOnTop { get; init; } = true;
    
    /// <summary>テキストの自動折り返し</summary>
    public bool WordWrap { get; init; } = true;
    
    /// <summary>オーバーレイの境界線の色</summary>
    public string BorderColor { get; init; } = "#666666";
    
    /// <summary>境界線の太さ（ピクセル）</summary>
    public int BorderThickness { get; init; } = 1;
    
    /// <summary>影の有効化</summary>
    public bool EnableShadow { get; init; } = true;
    
    /// <summary>影の色</summary>
    public string ShadowColor { get; init; } = "#000000";
    
    /// <summary>影のオフセット（ピクセル）</summary>
    public Point ShadowOffset { get; init; } = new(2, 2);
    
    /// <summary>影のぼかし半径（ピクセル）</summary>
    public int ShadowBlurRadius { get; init; } = 4;
}

/// <summary>
/// オーバーレイウィンドウの状態
/// </summary>
public enum OverlayState
{
    /// <summary>非表示</summary>
    Hidden,
    
    /// <summary>フェードイン中</summary>
    FadingIn,
    
    /// <summary>表示中</summary>
    Visible,
    
    /// <summary>フェードアウト中</summary>
    FadingOut,
    
    /// <summary>エラー状態</summary>
    Error
}

/// <summary>
/// オーバーレイウィンドウの情報
/// </summary>
public sealed class OverlayWindowInfo
{
    /// <summary>チャンクID</summary>
    public required int ChunkId { get; init; }
    
    /// <summary>ウィンドウの位置</summary>
    public required Point Position { get; init; }
    
    /// <summary>ウィンドウのサイズ</summary>
    public required Size Size { get; init; }
    
    /// <summary>表示状態</summary>
    public required OverlayState State { get; init; }
    
    /// <summary>作成日時</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    
    /// <summary>最後の更新日時</summary>
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>ソースウィンドウハンドル</summary>
    public IntPtr SourceWindowHandle { get; init; }
}