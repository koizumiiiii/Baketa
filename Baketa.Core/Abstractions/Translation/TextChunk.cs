using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Baketa.Core.Abstractions.OCR.Results;

namespace Baketa.Core.Abstractions.Translation;

/// <summary>
/// テキスト塊管理クラス
/// Phase 2: 複数ウィンドウでの座標ベース翻訳表示のためのデータ構造
/// </summary>
public sealed class TextChunk
{
    /// <summary>テキストチャンクの一意ID</summary>
    public required int ChunkId { get; init; }
    
    /// <summary>チャンクを構成するテキスト結果のリスト</summary>
    public required IReadOnlyList<PositionedTextResult> TextResults { get; init; } = [];
    
    /// <summary>チャンク全体のバウンディングボックス（画面座標）</summary>
    public required Rectangle CombinedBounds { get; init; }
    
    /// <summary>チャンク内のテキストを結合した文字列</summary>
    public required string CombinedText { get; init; } = string.Empty;
    
    /// <summary>翻訳結果テキスト</summary>
    public string TranslatedText { get; set; } = string.Empty;
    
    /// <summary>ソースウィンドウのハンドル</summary>
    public required IntPtr SourceWindowHandle { get; init; }
    
    /// <summary>検出された言語コード</summary>
    public string? DetectedLanguage { get; init; }
    
    /// <summary>チャンクの信頼度（構成テキストの平均信頼度）</summary>
    public float AverageConfidence => TextResults.Count > 0 
        ? TextResults.Average(t => t.Confidence) 
        : 0f;
    
    /// <summary>チャンク作成日時</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    
    /// <summary>
    /// チャンク中心座標を取得
    /// オーバーレイ表示位置計算用
    /// </summary>
    public Point GetCenterPoint() => new(
        CombinedBounds.X + CombinedBounds.Width / 2,
        CombinedBounds.Y + CombinedBounds.Height / 2);
    
    /// <summary>
    /// 別のテキストチャンクとの距離を計算
    /// 近接チャンクの統合判定用
    /// </summary>
    public double DistanceTo(TextChunk other)
    {
        var thisCenter = GetCenterPoint();
        var otherCenter = other.GetCenterPoint();
        
        var dx = thisCenter.X - otherCenter.X;
        var dy = thisCenter.Y - otherCenter.Y;
        
        return Math.Sqrt(dx * dx + dy * dy);
    }
    
    /// <summary>
    /// チャンクが指定領域と重複するかチェック
    /// 表示領域の衝突検出用
    /// </summary>
    public bool OverlapsWith(Rectangle region)
    {
        return CombinedBounds.IntersectsWith(region);
    }
    
    /// <summary>
    /// オーバーレイ表示用の最適位置を計算
    /// テキスト領域の近くで画面外に出ない位置を計算
    /// </summary>
    public Point CalculateOptimalOverlayPosition(Size overlaySize, Rectangle screenBounds)
    {
        // デフォルト: テキスト領域の下側に表示
        var x = CombinedBounds.X;
        var y = CombinedBounds.Y + CombinedBounds.Height + 5;
        
        // 画面右端チェック
        if (x + overlaySize.Width > screenBounds.Right)
        {
            x = screenBounds.Right - overlaySize.Width;
        }
        
        // 画面下端チェック - 下に表示できない場合は上に表示
        if (y + overlaySize.Height > screenBounds.Bottom)
        {
            y = CombinedBounds.Y - overlaySize.Height - 5;
            
            // 上にも表示できない場合は右側に表示
            if (y < screenBounds.Top)
            {
                x = CombinedBounds.X + CombinedBounds.Width + 5;
                y = CombinedBounds.Y;
                
                // 右側にも表示できない場合は重複表示
                if (x + overlaySize.Width > screenBounds.Right)
                {
                    x = CombinedBounds.X;
                    y = CombinedBounds.Y;
                }
            }
        }
        
        // 画面左端チェック
        if (x < screenBounds.Left)
        {
            x = screenBounds.Left;
        }
        
        // 画面上端チェック
        if (y < screenBounds.Top)
        {
            y = screenBounds.Top;
        }
        
        return new Point(x, y);
    }
    
    /// <summary>
    /// ログ出力用の文字列表現
    /// ユーザー要求: 座標位置もログで確認できるように
    /// </summary>
    public string ToLogString() => 
        $"ChunkId: {ChunkId} | Text: '{CombinedText}' | Translated: '{TranslatedText}' | " +
        $"Bounds: ({CombinedBounds.X},{CombinedBounds.Y},{CombinedBounds.Width},{CombinedBounds.Height}) | " +
        $"Confidence: {AverageConfidence:F3} | TextCount: {TextResults.Count} | Language: {DetectedLanguage ?? "unknown"}";
    
    /// <summary>
    /// チャンクの詳細情報をログ出力用に取得
    /// 開発・デバッグ用
    /// </summary>
    public string ToDetailedLogString()
    {
        var results = string.Join("; ", TextResults.Select(r => r.ToLogString()));
        return $"{ToLogString()} | Details: [{results}]";
    }
}