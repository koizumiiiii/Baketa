using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Baketa.Core.Abstractions.OCR.Results;
using Baketa.Core.UI.Monitors;

namespace Baketa.Core.Abstractions.Translation;

/// <summary>
/// テキスト塊管理クラス
/// UltraThink Phase 10.3: Clean Architecture準拠のデータモデル
/// UI位置調整責務をIOverlayPositioningServiceに分離
/// </summary>

public sealed class TextChunk
{
    /// <summary>テキストチャンクの一意ID</summary>
    public required int ChunkId { get; init; }
    
    /// <summary>チャンクを構成するテキスト結果のリスト</summary>
    public required IReadOnlyList<PositionedTextResult> TextResults { get; init; } = [];

    /// <summary>
    /// チャンク全体のバウンディングボックス（画像絶対座標）
    /// 🔥 [FIX6_COORDINATE_SYSTEM] 座標系統一: 画像絶対座標を格納
    /// - ROI相対座標ではなく、キャプチャ画像全体での絶対座標
    /// - TextChunk作成時に CaptureRegion.Offset を加算して正規化済み
    /// - オーバーレイ表示、キャッシング、Multi-ROI対応で一貫した座標系を保証
    /// </summary>
    public required Rectangle CombinedBounds { get; init; }

    /// <summary>チャンク内のテキストを結合した文字列</summary>
    public required string CombinedText { get; init; } = string.Empty;

    /// <summary>翻訳結果テキスト</summary>
    public string TranslatedText { get; set; } = string.Empty;

    /// <summary>ソースウィンドウのハンドル</summary>
    public required IntPtr SourceWindowHandle { get; init; }

    /// <summary>検出された言語コード</summary>
    public string? DetectedLanguage { get; init; }

    /// <summary>
    /// ROI画像のキャプチャ領域情報（オフセット座標）
    /// 🆕 [FIX6_CONTEXT_INFO] ROI相対座標 → 画像絶対座標の変換コンテキスト
    /// - null = フルスクリーンキャプチャ、または座標変換不要
    /// - 値あり = ROIキャプチャ、CombinedBoundsは既に画像絶対座標に正規化済み
    /// - 用途: 座標系変換、Multi-ROI対応、デバッグ情報、座標検証
    /// - Gemini推奨: TextChunkが座標変換に必要な全情報を保持すべき（DDD原則）
    /// </summary>
    public Rectangle? CaptureRegion { get; init; }
    
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
    /// オーバーレイ表示用の最適位置を計算（改良版）
    /// テキスト領域の座標を正確に反映し、画面外に出ない位置を計算
    /// </summary>
    
    /// <summary>
    /// ログ出力用の文字列表現
    /// ユーザー要求: 座標位置もログで確認できるように
    /// 🆕 [FIX6_DEBUG] CaptureRegion情報も出力（座標系検証用）
    /// </summary>
    public string ToLogString()
    {
        var captureInfo = CaptureRegion.HasValue
            ? $"CaptureRegion: ({CaptureRegion.Value.X},{CaptureRegion.Value.Y},{CaptureRegion.Value.Width},{CaptureRegion.Value.Height})"
            : "CaptureRegion: FullScreen";

        return $"ChunkId: {ChunkId} | Text: '{CombinedText}' | Translated: '{TranslatedText}' | " +
               $"Bounds: ({CombinedBounds.X},{CombinedBounds.Y},{CombinedBounds.Width},{CombinedBounds.Height}) | " +
               $"{captureInfo} | " +
               $"Confidence: {AverageConfidence:F3} | TextCount: {TextResults.Count} | Language: {DetectedLanguage ?? "unknown"}";
    }
    
    /// <summary>
    /// チャンクの詳細情報をログ出力用に取得
    /// 開発・デバッグ用
    /// </summary>
    public string ToDetailedLogString()
    {
        var results = string.Join("; ", TextResults.Select(r => r.ToLogString()));
        return $"{ToLogString()} | Details: [{results}]";
    }
    
    /// <summary>
    /// AR表示用の正確な位置を取得
    /// 元テキストと同じ位置に翻訳テキストを重ね表示するために使用
    /// </summary>
    public Point GetARPosition() => new(CombinedBounds.X, CombinedBounds.Y);
    
    /// <summary>
    /// AR表示用のサイズを取得
    /// 元テキストと同じサイズで翻訳テキストを表示するために使用
    /// </summary>
    public Size GetARSize() => new(CombinedBounds.Width, CombinedBounds.Height);
    
    /// <summary>
    /// AR表示用の最適フォントサイズを計算
    /// OCR領域の高さに基づいて自動的にフォントサイズを決定
    /// </summary>
    public int CalculateARFontSize()
    {
        // OCR領域の高さの45%をベースフォントサイズとして計算（さらに保守的に）
        var baseFontSize = (int)(CombinedBounds.Height * 0.45);
        
        // 翻訳テキストの長さを考慮して調整
        if (!string.IsNullOrEmpty(TranslatedText))
        {
            // テキストが領域幅に収まるように調整
            // 日本語文字の幅をより精密に計算
            var estimatedCharWidth = baseFontSize * 0.6; // 日本語は約半角～全角幅
            var requiredWidth = TranslatedText.Length * estimatedCharWidth;
            
            if (requiredWidth > CombinedBounds.Width)
            {
                // テキストが領域幅を超える場合は縮小
                var scaleFactor = CombinedBounds.Width / requiredWidth;
                baseFontSize = (int)(baseFontSize * scaleFactor * 0.8); // 80%でさらに余裕を持たせる
            }
        }
        
        // 最小8px、最大32pxの範囲に制限（さらに小さく）
        return Math.Max(8, Math.Min(32, baseFontSize));
    }
    
    /// <summary>
    /// AR表示が可能かどうかを判定
    /// 有効な座標情報と翻訳テキストが存在するかチェック
    /// </summary>
    public bool CanShowAR()
    {
        return CombinedBounds.Width > 0 && 
               CombinedBounds.Height > 0 && 
               !string.IsNullOrEmpty(TranslatedText) &&
               CombinedBounds.Width >= 10 &&  // 最小表示幅
               CombinedBounds.Height >= 8;    // 最小表示高さ
    }
    
    /// <summary>
    /// AR表示用のログ情報を取得
    /// デバッグ・トラブルシューティング用
    /// </summary>
    public string ToARLogString() => 
        $"AR Display - ChunkId: {ChunkId} | Position: ({GetARPosition().X},{GetARPosition().Y}) | " +
        $"Size: ({GetARSize().Width},{GetARSize().Height}) | FontSize: {CalculateARFontSize()} | " +
        $"CanShow: {CanShowAR()} | TranslatedText: '{TranslatedText}'";

    // === InPlace版メソッド（AR技術を使わないため、より適切な名称） ===
    
    /// <summary>
    /// インプレース表示が可能かどうかを判定
    /// 有効な座標情報と翻訳テキストが存在するかチェック
    /// </summary>
    public bool CanShowInPlace()
    {
        return CombinedBounds.Width > 0 && 
               CombinedBounds.Height > 0 && 
               !string.IsNullOrEmpty(TranslatedText) &&
               CombinedBounds.Width >= 10 &&  // 最小表示幅
               CombinedBounds.Height >= 8;    // 最小表示高さ
    }
    
    /// <summary>
    /// オーバーレイ表示用の基本位置を取得
    /// UltraThink Phase 10.3: 位置調整ロジックはIOverlayPositioningServiceに分離
    /// 注意: 実際の位置計算は消費側でIOverlayPositioningServiceを使用する
    /// 修正: 30ピクセル上オフセットを削除し、元テキストと正確に同じ位置に表示
    /// </summary>
    /// <returns>テキスト領域の基本位置（左上座標）</returns>
    public Point GetBasicOverlayPosition() => new(CombinedBounds.X, CombinedBounds.Y);
    
    /// <summary>
    /// テキスト領域の中心座標を取得
    /// 位置調整サービスでの位置計算に使用される
    /// </summary>
    /// <returns>テキスト領域の中心座標</returns>
    public Point GetTextCenterPoint() => GetCenterPoint();
    
    /// <summary>
    /// テキスト領域の境界を取得
    /// 位置調整サービスでの衝突検知に使用される
    /// </summary>
    /// <returns>テキスト領域の境界</returns>
    public Rectangle GetTextBounds() => CombinedBounds;
    
    /// <summary>
    /// オーバーレイ表示用のサイズを取得
    /// 元テキストと同じサイズで翻訳テキストを表示するために使用
    /// </summary>
    public Size GetOverlaySize() => new(CombinedBounds.Width, CombinedBounds.Height);
    
    /// <summary>
    /// オーバーレイ表示用の最適フォントサイズを計算
    /// OCR領域の高さに基づいて自動的にフォントサイズを決定
    /// </summary>
    public int CalculateOptimalFontSize()
    {
        // OCR領域の高さの45%をベースフォントサイズとして計算（さらに保守的に）
        var baseFontSize = (int)(CombinedBounds.Height * 0.45);
        
        // 翻訳テキストの長さを考慮して調整
        if (!string.IsNullOrEmpty(TranslatedText))
        {
            // テキストが領域幅に収まるように調整
            // 日本語文字の幅をより精密に計算
            var estimatedCharWidth = baseFontSize * 0.6; // 日本語は約半角～全角幅
            var availableWidth = CombinedBounds.Width * 0.9; // 余白を考慮
            var maxCharsPerLine = (int)(availableWidth / estimatedCharWidth);
            
            if (maxCharsPerLine > 0 && TranslatedText.Length > maxCharsPerLine)
            {
                // テキストが長い場合はフォントサイズを縮小
                var lines = Math.Ceiling((double)TranslatedText.Length / maxCharsPerLine);
                var heightPerLine = CombinedBounds.Height / lines;
                baseFontSize = (int)(heightPerLine * 0.4); // より小さく調整
            }
        }
        
        // フォントサイズの範囲制限
        return Math.Max(8, Math.Min(32, baseFontSize));
    }
    
    /// <summary>
    /// インプレース表示用のログ情報を取得
    /// デバッグ・トラブルシューティング用
    /// </summary>
    public string ToInPlaceLogString() => 
        $"InPlace Display - ChunkId: {ChunkId} | Position: ({GetBasicOverlayPosition().X},{GetBasicOverlayPosition().Y}) | " +
        $"Size: ({GetOverlaySize().Width},{GetOverlaySize().Height}) | FontSize: {CalculateOptimalFontSize()} | " +
        $"CanShow: {CanShowInPlace()} | TranslatedText: '{TranslatedText}'";
}
