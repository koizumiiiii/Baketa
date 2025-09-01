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
    /// オーバーレイ表示用の最適位置を計算（改良版）
    /// テキスト領域の座標を正確に反映し、画面外に出ない位置を計算
    /// </summary>
    public Point CalculateOptimalOverlayPosition(Size overlaySize, Rectangle screenBounds)
    {
        // 優先順位付きポジショニング戦略
        
        // 1. テキスト領域の直下（5px余白）
        var positions = new[]
        {
            new { CombinedBounds.X, Y = CombinedBounds.Bottom + 5, Priority = 1 },
            // 2. テキスト領域の直上（5px余白）
            new { CombinedBounds.X, Y = CombinedBounds.Y - overlaySize.Height - 5, Priority = 2 },
            // 3. テキスト領域の右側（5px余白）
            new { X = CombinedBounds.Right + 5, CombinedBounds.Y, Priority = 3 },
            // 4. テキスト領域の左側（5px余白）
            new { X = CombinedBounds.X - overlaySize.Width - 5, CombinedBounds.Y, Priority = 4 },
            // 5. テキスト領域の右下角
            new { X = CombinedBounds.Right - overlaySize.Width, Y = CombinedBounds.Bottom + 5, Priority = 5 },
            // 6. テキスト領域の左下角
            new { CombinedBounds.X, Y = CombinedBounds.Bottom + 5, Priority = 6 }
        };

        // 各候補位置を優先順位順で試行
        foreach (var pos in positions.OrderBy(p => p.Priority))
        {
            var candidateRect = new Rectangle(pos.X, pos.Y, overlaySize.Width, overlaySize.Height);
            
            // 画面境界内チェック
            if (screenBounds.Contains(candidateRect))
            {
                return new Point(pos.X, pos.Y);
            }
        }

        // すべての候補が画面外の場合は座標をクランプして強制表示
        var clampedX = Math.Max(screenBounds.Left, 
                       Math.Min(screenBounds.Right - overlaySize.Width, CombinedBounds.X));
        var clampedY = Math.Max(screenBounds.Top, 
                       Math.Min(screenBounds.Bottom - overlaySize.Height, CombinedBounds.Bottom + 5));
                       
        return new Point(clampedX, clampedY);
    }

    /// <summary>
    /// 既存オーバーレイとの衝突を回避した最適なオーバーレイ位置を計算
    /// 複数オーバーレイ表示時の重なり防止機能
    /// </summary>
    /// <param name="overlaySize">オーバーレイのサイズ</param>
    /// <param name="screenBounds">画面境界</param>
    /// <param name="existingOverlayBounds">既存オーバーレイの位置情報リスト</param>
    /// <returns>衝突を回避した最適な表示位置</returns>
    public Point CalculateOptimalOverlayPositionWithCollisionAvoidance(Size overlaySize, Rectangle screenBounds, IReadOnlyList<Rectangle> existingOverlayBounds)
    {
        // 優先順位付きポジショニング戦略（衝突回避版）
        var positions = new[]
        {
            // 1. テキスト領域の直上（5px余白）
            new { CombinedBounds.X, Y = CombinedBounds.Y - overlaySize.Height - 5, Priority = 1 },
            // 2. テキスト領域の直下（5px余白）  
            new { CombinedBounds.X, Y = CombinedBounds.Bottom + 5, Priority = 2 },
            // 3. テキスト領域の右側（5px余白）
            new { X = CombinedBounds.Right + 5, CombinedBounds.Y, Priority = 3 },
            // 4. テキスト領域の左側（5px余白）
            new { X = CombinedBounds.X - overlaySize.Width - 5, CombinedBounds.Y, Priority = 4 },
            // 5. テキスト領域の右上角
            new { X = CombinedBounds.Right + 5, Y = CombinedBounds.Y - overlaySize.Height - 5, Priority = 5 },
            // 6. テキスト領域の左上角
            new { X = CombinedBounds.X - overlaySize.Width - 5, Y = CombinedBounds.Y - overlaySize.Height - 5, Priority = 6 },
            // 7. テキスト領域の右下角
            new { X = CombinedBounds.Right + 5, Y = CombinedBounds.Bottom + 5, Priority = 7 },
            // 8. テキスト領域の左下角
            new { X = CombinedBounds.X - overlaySize.Width - 5, Y = CombinedBounds.Bottom + 5, Priority = 8 }
        };

        // 各候補位置を優先順位順で試行（衝突チェック付き）
        foreach (var pos in positions.OrderBy(p => p.Priority))
        {
            var candidateRect = new Rectangle(pos.X, pos.Y, overlaySize.Width, overlaySize.Height);
            
            // 画面境界内チェック
            if (!screenBounds.Contains(candidateRect))
                continue;

            // 既存オーバーレイとの衝突チェック
            bool hasCollision = false;
            foreach (var existingBounds in existingOverlayBounds)
            {
                if (candidateRect.IntersectsWith(existingBounds))
                {
                    hasCollision = true;
                    break;
                }
            }

            if (!hasCollision)
            {
                return new Point(pos.X, pos.Y);
            }
        }

        // 衝突回避できない場合は動的オフセット調整
        var basePosition = CalculateOptimalOverlayPosition(overlaySize, screenBounds);
        return FindNonCollidingPosition(basePosition, overlaySize, screenBounds, existingOverlayBounds);
    }

    /// <summary>
    /// 動的オフセット調整による衝突回避位置の検索
    /// 全ての優先位置で衝突が発生した場合の最終的な回避戦略
    /// </summary>
    private Point FindNonCollidingPosition(Point basePosition, Size overlaySize, Rectangle screenBounds, IReadOnlyList<Rectangle> existingOverlayBounds)
    {
        const int stepSize = 10; // 調整ステップサイズ
        const int maxSteps = 20;  // 最大調整回数
        
        // X軸方向の調整を試行
        for (int step = 1; step <= maxSteps; step++)
        {
            var offsetX = step * stepSize;
            
            // 右方向へのオフセット
            var rightPosition = new Point(basePosition.X + offsetX, basePosition.Y);
            var rightRect = new Rectangle(rightPosition.X, rightPosition.Y, overlaySize.Width, overlaySize.Height);
            if (screenBounds.Contains(rightRect) && !HasCollisionWithExistingOverlays(rightRect, existingOverlayBounds))
            {
                return rightPosition;
            }
            
            // 左方向へのオフセット
            var leftPosition = new Point(basePosition.X - offsetX, basePosition.Y);
            var leftRect = new Rectangle(leftPosition.X, leftPosition.Y, overlaySize.Width, overlaySize.Height);
            if (screenBounds.Contains(leftRect) && !HasCollisionWithExistingOverlays(leftRect, existingOverlayBounds))
            {
                return leftPosition;
            }
        }

        // Y軸方向の調整を試行
        for (int step = 1; step <= maxSteps; step++)
        {
            var offsetY = step * stepSize;
            
            // 下方向へのオフセット
            var downPosition = new Point(basePosition.X, basePosition.Y + offsetY);
            var downRect = new Rectangle(downPosition.X, downPosition.Y, overlaySize.Width, overlaySize.Height);
            if (screenBounds.Contains(downRect) && !HasCollisionWithExistingOverlays(downRect, existingOverlayBounds))
            {
                return downPosition;
            }
            
            // 上方向へのオフセット
            var upPosition = new Point(basePosition.X, basePosition.Y - offsetY);
            var upRect = new Rectangle(upPosition.X, upPosition.Y, overlaySize.Width, overlaySize.Height);
            if (screenBounds.Contains(upRect) && !HasCollisionWithExistingOverlays(upRect, existingOverlayBounds))
            {
                return upPosition;
            }
        }

        // 全ての調整が失敗した場合は元の位置を返す
        return basePosition;
    }

    /// <summary>
    /// 指定された領域が既存オーバーレイと衝突するかチェック
    /// </summary>
    private static bool HasCollisionWithExistingOverlays(Rectangle candidateRect, IReadOnlyList<Rectangle> existingOverlayBounds)
    {
        foreach (var existingBounds in existingOverlayBounds)
        {
            if (candidateRect.IntersectsWith(existingBounds))
            {
                return true;
            }
        }
        return false;
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
    /// オーバーレイ表示用の位置を取得
    /// 翻訳結果をテキスト上方に表示するため、最適化されたポジショニング戦略を使用
    /// 座標ずれ修正: テキスト領域上方への最適配置
    /// </summary>
    public Point GetOverlayPosition()
    {
        // 翻訳結果をテキスト上方に表示するため、最適化されたポジショニング戦略を使用
        var defaultBounds = GetDefaultScreenBounds();
        var overlaySize = GetOverlaySize();
        return CalculateOptimalOverlayPosition(overlaySize, defaultBounds);
    }
    
    /// <summary>
    /// 指定された画面境界内での最適なオーバーレイ位置を計算
    /// 特定の画面境界を考慮した最適な位置に翻訳テキストを表示するために使用
    /// 元テキストを避けた配置が必要な場合に使用
    /// </summary>
    /// <param name="screenBounds">対象画面の境界情報</param>
    /// <returns>最適化された表示位置</returns>
    public Point GetOverlayPosition(Rectangle screenBounds)
    {
        var overlaySize = GetOverlaySize();
        return CalculateOptimalOverlayPosition(overlaySize, screenBounds);
    }
    
    /// <summary>
    /// デフォルト画面境界を取得
    /// 画面サイズが不明な場合の安全なデフォルト値を提供
    /// </summary>
    /// <returns>デフォルト画面境界 (Full HD 1920x1080)</returns>
    private static Rectangle GetDefaultScreenBounds()
    {
        // TODO: 将来的には設定ファイルからデフォルト画面サイズを取得する実装を検討
        return new Rectangle(0, 0, 1920, 1080);
    }
    
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
        $"InPlace Display - ChunkId: {ChunkId} | Position: ({GetOverlayPosition().X},{GetOverlayPosition().Y}) | " +
        $"Size: ({GetOverlaySize().Width},{GetOverlaySize().Height}) | FontSize: {CalculateOptimalFontSize()} | " +
        $"CanShow: {CanShowInPlace()} | TranslatedText: '{TranslatedText}'";
}
