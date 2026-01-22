using System;
using Baketa.Core.Models.Primitives;

namespace Baketa.Core.Models.Roi;

/// <summary>
/// ROI領域の信頼度レベル
/// </summary>
public enum RoiConfidenceLevel
{
    /// <summary>
    /// 低信頼度（学習サンプル数が少ない）
    /// </summary>
    Low = 0,

    /// <summary>
    /// 中信頼度（ある程度の学習データがある）
    /// </summary>
    Medium = 1,

    /// <summary>
    /// 高信頼度（十分な学習データがある）
    /// </summary>
    High = 2
}

/// <summary>
/// ROI領域の種類
/// </summary>
public enum RoiRegionType
{
    /// <summary>
    /// 通常のテキスト領域
    /// </summary>
    Text = 0,

    /// <summary>
    /// ダイアログボックス領域
    /// </summary>
    DialogBox = 1,

    /// <summary>
    /// メニュー領域
    /// </summary>
    Menu = 2,

    /// <summary>
    /// HUD（ヘッドアップディスプレイ）領域
    /// </summary>
    Hud = 3,

    /// <summary>
    /// 字幕領域
    /// </summary>
    Subtitle = 4,

    /// <summary>
    /// 除外領域（翻訳不要）
    /// </summary>
    Exclusion = 5
}

/// <summary>
/// ROI（Region of Interest）領域を表すモデル
/// </summary>
/// <remarks>
/// テキストが頻繁に出現する領域を表し、学習によって自動検出・最適化されます。
/// </remarks>
public sealed record RoiRegion
{
    /// <summary>
    /// 浮動小数点比較用のイプシロン値
    /// </summary>
    public const float EpsilonForComparison = 1e-6f;

    /// <summary>
    /// 領域の一意識別子
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// 領域の境界矩形（正規化座標: 0.0-1.0）
    /// </summary>
    /// <remarks>
    /// 画面サイズに依存しない相対座標で保存。
    /// 実際のピクセル座標への変換は使用時に行う。
    /// </remarks>
    public required NormalizedRect NormalizedBounds { get; init; }

    /// <summary>
    /// 領域の種類
    /// </summary>
    public RoiRegionType RegionType { get; init; } = RoiRegionType.Text;

    /// <summary>
    /// 信頼度レベル
    /// </summary>
    public RoiConfidenceLevel ConfidenceLevel { get; init; } = RoiConfidenceLevel.Low;

    /// <summary>
    /// 信頼度スコア（0.0-1.0）
    /// </summary>
    public float ConfidenceScore { get; init; }

    /// <summary>
    /// この領域でテキストが検出された回数
    /// </summary>
    public int DetectionCount { get; init; }

    /// <summary>
    /// 最後に検出された時刻
    /// </summary>
    public DateTime LastDetectedAt { get; init; }

    /// <summary>
    /// 領域が作成された時刻
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// この領域に適用する変化検知閾値（オプション）
    /// </summary>
    /// <remarks>
    /// nullの場合はデフォルト閾値を使用。
    /// テキスト領域の特性に基づいて動的に調整される。
    /// </remarks>
    public float? CustomThreshold { get; init; }

    /// <summary>
    /// 領域のヒートマップ値（0.0-1.0）
    /// </summary>
    /// <remarks>
    /// この領域でのテキスト出現頻度を表す。
    /// 1.0に近いほど頻繁にテキストが出現する。
    /// </remarks>
    public float HeatmapValue { get; init; }

    /// <summary>
    /// 設定値の妥当性を検証
    /// </summary>
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(Id)
            && NormalizedBounds.IsValid()
            && ConfidenceScore is >= 0.0f and <= 1.0f
            && HeatmapValue is >= 0.0f and <= 1.0f
            && DetectionCount >= 0
            && (CustomThreshold is null or (>= 0.0f and <= 1.0f));
    }

    /// <summary>
    /// 絶対座標の矩形に変換
    /// </summary>
    /// <param name="screenWidth">画面幅</param>
    /// <param name="screenHeight">画面高さ</param>
    /// <returns>ピクセル座標の矩形</returns>
    public Rect ToAbsoluteRect(int screenWidth, int screenHeight)
    {
        return NormalizedBounds.ToAbsoluteRect(screenWidth, screenHeight);
    }

    /// <summary>
    /// 検出回数をインクリメントした新しいインスタンスを作成
    /// </summary>
    public RoiRegion WithDetection()
    {
        return this with
        {
            DetectionCount = DetectionCount + 1,
            LastDetectedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 信頼度を更新した新しいインスタンスを作成
    /// </summary>
    /// <param name="score">新しい信頼度スコア</param>
    /// <param name="level">新しい信頼度レベル</param>
    public RoiRegion WithConfidence(float score, RoiConfidenceLevel level)
    {
        return this with
        {
            ConfidenceScore = Math.Clamp(score, 0.0f, 1.0f),
            ConfidenceLevel = level
        };
    }

    /// <summary>
    /// 2つの浮動小数点値が等しいかをイプシロン比較
    /// </summary>
    public static bool ApproximatelyEquals(float a, float b)
    {
        return Math.Abs(a - b) < EpsilonForComparison;
    }
}

/// <summary>
/// 正規化された矩形（0.0-1.0の相対座標）
/// </summary>
public readonly record struct NormalizedRect
{
    /// <summary>
    /// 浮動小数点比較用のイプシロン値
    /// </summary>
    public const float EpsilonForComparison = 1e-6f;

    /// <summary>
    /// 左端X座標（0.0-1.0）
    /// </summary>
    public float X { get; init; }

    /// <summary>
    /// 上端Y座標（0.0-1.0）
    /// </summary>
    public float Y { get; init; }

    /// <summary>
    /// 幅（0.0-1.0）
    /// </summary>
    public float Width { get; init; }

    /// <summary>
    /// 高さ（0.0-1.0）
    /// </summary>
    public float Height { get; init; }

    /// <summary>
    /// 正規化矩形を初期化
    /// </summary>
    public NormalizedRect(float x, float y, float width, float height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>
    /// 右端X座標
    /// </summary>
    public float Right => X + Width;

    /// <summary>
    /// 下端Y座標
    /// </summary>
    public float Bottom => Y + Height;

    /// <summary>
    /// 中心X座標
    /// </summary>
    public float CenterX => X + Width / 2;

    /// <summary>
    /// 中心Y座標
    /// </summary>
    public float CenterY => Y + Height / 2;

    /// <summary>
    /// 面積
    /// </summary>
    public float Area => Width * Height;

    /// <summary>
    /// 空の矩形かどうか
    /// </summary>
    public bool IsEmpty => Width <= EpsilonForComparison || Height <= EpsilonForComparison;

    /// <summary>
    /// 設定値の妥当性を検証
    /// </summary>
    public bool IsValid()
    {
        return X is >= 0.0f and <= 1.0f
            && Y is >= 0.0f and <= 1.0f
            && Width is > 0.0f and <= 1.0f
            && Height is > 0.0f and <= 1.0f
            && (X + Width) <= 1.0f + EpsilonForComparison
            && (Y + Height) <= 1.0f + EpsilonForComparison;
    }

    /// <summary>
    /// 絶対座標の矩形に変換
    /// </summary>
    /// <param name="screenWidth">画面幅</param>
    /// <param name="screenHeight">画面高さ</param>
    /// <returns>ピクセル座標の矩形</returns>
    public Rect ToAbsoluteRect(int screenWidth, int screenHeight)
    {
        return new Rect(
            (int)(X * screenWidth),
            (int)(Y * screenHeight),
            (int)(Width * screenWidth),
            (int)(Height * screenHeight)
        );
    }

    /// <summary>
    /// 絶対座標の矩形から作成
    /// </summary>
    /// <param name="rect">ピクセル座標の矩形</param>
    /// <param name="screenWidth">画面幅</param>
    /// <param name="screenHeight">画面高さ</param>
    /// <returns>正規化された矩形</returns>
    public static NormalizedRect FromAbsoluteRect(Rect rect, int screenWidth, int screenHeight)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(screenWidth, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(screenHeight, 0);

        return new NormalizedRect(
            (float)rect.X / screenWidth,
            (float)rect.Y / screenHeight,
            (float)rect.Width / screenWidth,
            (float)rect.Height / screenHeight
        );
    }

    /// <summary>
    /// 指定した矩形と交差するかを判定
    /// </summary>
    public bool Intersects(NormalizedRect other)
    {
        return other.X < Right && other.Right > X && other.Y < Bottom && other.Bottom > Y;
    }

    /// <summary>
    /// 指定した矩形を完全に含むかを判定
    /// </summary>
    public bool Contains(NormalizedRect other)
    {
        return other.X >= X && other.Right <= Right && other.Y >= Y && other.Bottom <= Bottom;
    }

    /// <summary>
    /// IoU（Intersection over Union）を計算
    /// </summary>
    public float CalculateIoU(NormalizedRect other)
    {
        if (!Intersects(other))
        {
            return 0.0f;
        }

        var intersectionX = Math.Max(X, other.X);
        var intersectionY = Math.Max(Y, other.Y);
        var intersectionRight = Math.Min(Right, other.Right);
        var intersectionBottom = Math.Min(Bottom, other.Bottom);

        var intersectionArea = (intersectionRight - intersectionX) * (intersectionBottom - intersectionY);
        var unionArea = Area + other.Area - intersectionArea;

        return unionArea > EpsilonForComparison ? intersectionArea / unionArea : 0.0f;
    }

    /// <summary>
    /// 空の矩形
    /// </summary>
    public static NormalizedRect Empty => default;

    /// <summary>
    /// 文字列表現を取得
    /// </summary>
    public override string ToString() => $"{{X={X:F4},Y={Y:F4},W={Width:F4},H={Height:F4}}}";
}
