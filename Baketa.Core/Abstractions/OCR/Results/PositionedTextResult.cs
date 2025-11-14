using System;
using System.Drawing;

namespace Baketa.Core.Abstractions.OCR.Results;

/// <summary>
/// 座標情報付きOCRテキスト結果
/// Phase 2: 座標ベース翻訳表示のためのデータ構造
/// </summary>
public sealed record PositionedTextResult
{
    /// <summary>認識されたテキスト</summary>
    public required string Text { get; init; }

    /// <summary>テキストのバウンディングボックス（画面座標）</summary>
    public required Rectangle BoundingBox { get; init; }

    /// <summary>認識信頼度 (0.0-1.0)</summary>
    public required float Confidence { get; init; }

    /// <summary>テキストチャンクID</summary>
    public required int ChunkId { get; init; }

    /// <summary>OCR処理時間</summary>
    public TimeSpan ProcessingTime { get; init; }

    /// <summary>検出された言語コード (ja, en等)</summary>
    public string? DetectedLanguage { get; init; }

    /// <summary>テキストの方向 (水平、縦書き等)</summary>
    public TextOrientation Orientation { get; init; } = TextOrientation.Horizontal;

    /// <summary>
    /// 座標ログ用の文字列表現
    /// ユーザー要求: 認識したテキストとともに座標位置もログで確認
    /// </summary>
    /// <returns>ログ出力用のフォーマット済み文字列</returns>
    public string ToLogString() =>
        $"Text: '{Text}' | Bounds: ({BoundingBox.X},{BoundingBox.Y},{BoundingBox.Width},{BoundingBox.Height}) | Confidence: {Confidence:F3} | ChunkId: {ChunkId} | Language: {DetectedLanguage ?? "unknown"}";

    /// <summary>
    /// テキスト中心座標を取得
    /// UI表示位置計算用
    /// </summary>
    public Point GetCenterPoint() => new(
        BoundingBox.X + BoundingBox.Width / 2,
        BoundingBox.Y + BoundingBox.Height / 2);

    /// <summary>
    /// 別のテキスト結果との距離を計算
    /// テキストチャンクグループ化用
    /// </summary>
    public double DistanceTo(PositionedTextResult other)
    {
        var thisCenter = GetCenterPoint();
        var otherCenter = other.GetCenterPoint();

        var dx = thisCenter.X - otherCenter.X;
        var dy = thisCenter.Y - otherCenter.Y;

        return Math.Sqrt(dx * dx + dy * dy);
    }
}

/// <summary>
/// テキストの方向
/// </summary>
public enum TextOrientation
{
    /// <summary>水平（左から右）</summary>
    Horizontal,

    /// <summary>縦書き（上から下）</summary>
    Vertical,

    /// <summary>回転されたテキスト</summary>
    Rotated
}
