using Baketa.Core.UI.Geometry;

namespace Baketa.UI.Extensions;

/// <summary>
/// Avalonia UI型とCore幾何学型間の変換拡張メソッド
/// </summary>
public static class AvaloniaGeometryExtensions
{
    /// <summary>
    /// CorePointをAvalonia.Pointに変換します
    /// </summary>
    /// <param name="point">変換元のCorePoint</param>
    /// <returns>Avalonia.Point</returns>
    public static Avalonia.Point ToAvaloniaPoint(this CorePoint point) => 
        new(point.X, point.Y);
    
    /// <summary>
    /// CoreSizeをAvalonia.Sizeに変換します
    /// </summary>
    /// <param name="size">変換元のCoreSize</param>
    /// <returns>Avalonia.Size</returns>
    public static Avalonia.Size ToAvaloniaSize(this CoreSize size) => 
        new(size.Width, size.Height);
    
    /// <summary>
    /// CoreRectをAvalonia.Rectに変換します
    /// </summary>
    /// <param name="rect">変換元のCoreRect</param>
    /// <returns>Avalonia.Rect</returns>
    public static Avalonia.Rect ToAvaloniaRect(this CoreRect rect) => 
        new(rect.X, rect.Y, rect.Width, rect.Height);
    
    /// <summary>
    /// CoreVectorをAvalonia.Vectorに変換します
    /// </summary>
    /// <param name="vector">変換元のCoreVector</param>
    /// <returns>Avalonia.Vector</returns>
    public static Avalonia.Vector ToAvaloniaVector(this CoreVector vector) => 
        new(vector.X, vector.Y);
    
    /// <summary>
    /// Avalonia.PointをCorePointに変換します
    /// </summary>
    /// <param name="point">変換元のAvalonia.Point</param>
    /// <returns>CorePoint</returns>
    public static CorePoint ToCorePoint(this Avalonia.Point point) => 
        new(point.X, point.Y);
    
    /// <summary>
    /// Avalonia.SizeをCoreSizeに変換します
    /// </summary>
    /// <param name="size">変換元のAvalonia.Size</param>
    /// <returns>CoreSize</returns>
    public static CoreSize ToCoreSize(this Avalonia.Size size) => 
        new(size.Width, size.Height);
    
    /// <summary>
    /// Avalonia.RectをCoreRectに変換します
    /// </summary>
    /// <param name="rect">変換元のAvalonia.Rect</param>
    /// <returns>CoreRect</returns>
    public static CoreRect ToCoreRect(this Avalonia.Rect rect) => 
        new(rect.X, rect.Y, rect.Width, rect.Height);
    
    /// <summary>
    /// Avalonia.VectorをCoreVectorに変換します
    /// </summary>
    /// <param name="vector">変換元のAvalonia.Vector</param>
    /// <returns>CoreVector</returns>
    public static CoreVector ToCoreVector(this Avalonia.Vector vector) => 
        new(vector.X, vector.Y);
}
