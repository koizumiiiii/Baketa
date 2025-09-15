using System.Drawing;
using MemoryRectangle = Baketa.Core.Abstractions.Memory.Rectangle;

namespace Baketa.Core.Extensions;

/// <summary>
/// Rectangle型変換のための拡張メソッド
/// System.Drawing.Rectangle ↔ Baketa.Core.Abstractions.Memory.Rectangle
/// </summary>
public static class RectangleExtensions
{
    /// <summary>
    /// Drawing.RectangleをMemory.Rectangleに変換
    /// </summary>
    public static MemoryRectangle ToMemoryRectangle(this Rectangle rect)
    {
        return new MemoryRectangle(rect.X, rect.Y, rect.Width, rect.Height);
    }

    /// <summary>
    /// Memory.RectangleをDrawing.Rectangleに変換
    /// </summary>
    public static Rectangle ToDrawingRectangle(this MemoryRectangle rect)
    {
        return new Rectangle(rect.X, rect.Y, rect.Width, rect.Height);
    }

    /// <summary>
    /// Memory.RectangleをDrawing.RectangleFに変換
    /// </summary>
    public static RectangleF ToDrawingRectangleF(this MemoryRectangle rect)
    {
        return new RectangleF(rect.X, rect.Y, rect.Width, rect.Height);
    }

    /// <summary>
    /// Drawing.RectangleからMemory.Rectangleリストに変換
    /// </summary>
    public static List<MemoryRectangle> ToMemoryRectangleList(this IEnumerable<Rectangle> rects)
    {
        return rects.Select(r => r.ToMemoryRectangle()).ToList();
    }

    /// <summary>
    /// Memory.RectangleからDrawing.Rectangleリストに変換
    /// </summary>
    public static List<Rectangle> ToDrawingRectangleList(this IEnumerable<MemoryRectangle> rects)
    {
        return rects.Select(r => r.ToDrawingRectangle()).ToList();
    }
}