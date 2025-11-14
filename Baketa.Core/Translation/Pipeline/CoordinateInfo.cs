using System.Drawing;

namespace Baketa.Core.Translation.Pipeline;

/// <summary>
/// UI表示用座標情報
/// TextChunkの座標データを統合翻訳パイプライン用に最適化
/// </summary>
/// <param name="bounds">テキスト領域の矩形座標</param>
/// <param name="windowHandle">ソースウィンドウハンドル</param>
public record CoordinateInfo(
    Rectangle Bounds,
    IntPtr WindowHandle
)
{
    /// <summary>X座標</summary>
    public int X => Bounds.X;

    /// <summary>Y座標</summary>
    public int Y => Bounds.Y;

    /// <summary>幅</summary>
    public int Width => Bounds.Width;

    /// <summary>高さ</summary>
    public int Height => Bounds.Height;

    /// <summary>
    /// 中心点を取得
    /// </summary>
    public Point CenterPoint => new(Bounds.X + Bounds.Width / 2, Bounds.Y + Bounds.Height / 2);

    /// <summary>
    /// 座標が有効かどうか（ゼロサイズでないかつ正の座標）
    /// </summary>
    public bool IsValid => Bounds.Width > 0 && Bounds.Height > 0 && Bounds.X >= 0 && Bounds.Y >= 0;

    /// <summary>
    /// 画面境界内かどうか判定（プラットフォーム非依存）
    /// </summary>
    /// <param name="screenBounds">画面境界（必須）</param>
    /// <returns>境界内の場合true</returns>
    public bool IsWithinScreenBounds(Rectangle screenBounds)
    {
        return Bounds.X < screenBounds.Width && Bounds.Y < screenBounds.Height &&
               Bounds.Right <= screenBounds.Right && Bounds.Bottom <= screenBounds.Bottom;
    }

    /// <summary>
    /// TextChunkからCoordinateInfoを作成
    /// </summary>
    /// <param name="textChunk">変換元TextChunk</param>
    /// <returns>CoordinateInfo</returns>
    public static CoordinateInfo FromTextChunk(Baketa.Core.Abstractions.Translation.TextChunk textChunk)
    {
        ArgumentNullException.ThrowIfNull(textChunk);
        return new CoordinateInfo(textChunk.CombinedBounds, textChunk.SourceWindowHandle);
    }
}
