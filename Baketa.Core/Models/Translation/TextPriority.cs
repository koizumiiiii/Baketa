using System.Drawing;

namespace Baketa.Core.Models.Translation;

/// <summary>
/// テキスト領域の優先度計算クラス
/// 画面中央からの距離に基づいて翻訳優先度を決定する
/// </summary>
public sealed record TextPriority
{
    /// <summary>
    /// 元のテキスト
    /// </summary>
    public string OriginalText { get; init; } = string.Empty;

    /// <summary>
    /// テキスト領域の境界ボックス（元座標）
    /// </summary>
    public Rectangle BoundingBox { get; init; }

    /// <summary>
    /// 正規化された座標（0.0-1.0の相対座標）
    /// </summary>
    public required NormalizedCoordinate NormalizedPosition { get; init; }

    /// <summary>
    /// 画面中央からの二乗ユークリッド距離（優先度スコア）
    /// 値が小さいほど優先度が高い
    /// </summary>
    public double DistanceFromCenterSquared { get; init; }

    /// <summary>
    /// テキスト領域とスクリーン情報からTextPriorityを作成
    /// </summary>
    /// <param name="originalText">元のテキスト</param>
    /// <param name="boundingBox">テキスト領域</param>
    /// <param name="screenWidth">画面幅</param>
    /// <param name="screenHeight">画面高さ</param>
    /// <returns>優先度付きテキスト情報</returns>
    public static TextPriority Create(string originalText, Rectangle boundingBox, int screenWidth, int screenHeight)
    {
        var normalized = NormalizeCoordinates(boundingBox, screenWidth, screenHeight);
        var distance = CalculateDistanceFromCenterSquared(normalized);

        return new TextPriority
        {
            OriginalText = originalText,
            BoundingBox = boundingBox,
            NormalizedPosition = normalized,
            DistanceFromCenterSquared = distance
        };
    }

    /// <summary>
    /// 座標を0.0-1.0の相対座標に正規化
    /// </summary>
    private static NormalizedCoordinate NormalizeCoordinates(Rectangle boundingBox, int screenWidth, int screenHeight)
    {
        if (screenWidth <= 0 || screenHeight <= 0)
            return new NormalizedCoordinate(0.5, 0.5); // デフォルト中央

        // バウンディングボックスの中央点を計算
        var centerX = boundingBox.X + boundingBox.Width / 2.0;
        var centerY = boundingBox.Y + boundingBox.Height / 2.0;

        // 0.0-1.0に正規化
        var normalizedX = Math.Max(0.0, Math.Min(1.0, centerX / screenWidth));
        var normalizedY = Math.Max(0.0, Math.Min(1.0, centerY / screenHeight));

        return new NormalizedCoordinate(normalizedX, normalizedY);
    }

    /// <summary>
    /// 画面中央（0.5, 0.5）からの二乗ユークリッド距離を計算
    /// 平方根を計算せず二乗距離のままで比較効率を向上
    /// </summary>
    private static double CalculateDistanceFromCenterSquared(NormalizedCoordinate position)
    {
        const double screenCenterX = 0.5;
        const double screenCenterY = 0.5;

        var dx = position.X - screenCenterX;
        var dy = position.Y - screenCenterY;

        return dx * dx + dy * dy;
    }
}

/// <summary>
/// 正規化された座標（0.0-1.0の相対座標）
/// </summary>
public sealed record NormalizedCoordinate(double X, double Y)
{
    /// <summary>
    /// X座標（0.0-1.0）
    /// </summary>
    public double X { get; init; } = Math.Max(0.0, Math.Min(1.0, X));

    /// <summary>
    /// Y座標（0.0-1.0）
    /// </summary>
    public double Y { get; init; } = Math.Max(0.0, Math.Min(1.0, Y));
}
