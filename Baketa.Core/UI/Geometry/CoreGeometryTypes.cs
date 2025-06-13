namespace Baketa.Core.UI.Geometry;

/// <summary>
/// 2D座標を表すプラットフォーム非依存の構造体
/// </summary>
public readonly struct Point(double x, double y) : IEquatable<Point>
{
    public double X { get; } = x;
    public double Y { get; } = y;
    
    public static Point Zero => new(0, 0);
    
    public static bool operator ==(Point left, Point right) => 
        left.X == right.X && left.Y == right.Y;
    
    public static bool operator !=(Point left, Point right) => !(left == right);
    
    public bool Equals(Point other) => this == other;
    
    public override bool Equals(object? obj) => obj is Point point && Equals(point);
    
    public override int GetHashCode() => HashCode.Combine(X, Y);
    
    public override string ToString() => $"({X}, {Y})";
}

/// <summary>
/// 2Dサイズを表すプラットフォーム非依存の構造体
/// </summary>
public readonly struct Size(double width, double height) : IEquatable<Size>
{
    public double Width { get; } = width;
    public double Height { get; } = height;
    
    public static Size Empty => new(0, 0);
    
    public static bool operator ==(Size left, Size right) => 
        left.Width == right.Width && left.Height == right.Height;
    
    public static bool operator !=(Size left, Size right) => !(left == right);
    
    public bool Equals(Size other) => this == other;
    
    public override bool Equals(object? obj) => obj is Size size && Equals(size);
    
    public override int GetHashCode() => HashCode.Combine(Width, Height);
    
    public override string ToString() => $"{Width}x{Height}";
}

/// <summary>
/// 2D矩形を表すプラットフォーム非依存の構造体
/// </summary>
public readonly struct Rect(double x, double y, double width, double height) : IEquatable<Rect>
{
    public double X { get; } = x;
    public double Y { get; } = y;
    public double Width { get; } = width;
    public double Height { get; } = height;
    
    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;
    
    public Point TopLeft => new(X, Y);
    public Point TopRight => new(Right, Y);
    public Point BottomLeft => new(X, Bottom);
    public Point BottomRight => new(Right, Bottom);
    
    public Point Center => new(X + Width / 2, Y + Height / 2);
    
    public static Rect Empty => new(0, 0, 0, 0);
    
    public Rect(Point position, Size size) : this(position.X, position.Y, size.Width, size.Height) { }
    
    /// <summary>
    /// 指定された点が矩形内に含まれるかどうかを判定
    /// </summary>
    public bool Contains(Point point) => 
        point.X >= Left && point.X <= Right && point.Y >= Top && point.Y <= Bottom;
    
    /// <summary>
    /// 指定された矩形がこの矩形と交差するかどうかを判定
    /// </summary>
    public bool IntersectsWith(Rect other) =>
        !(other.Left > Right || other.Right < Left || other.Top > Bottom || other.Bottom < Top);
    
    public static bool operator ==(Rect left, Rect right) => 
        left.X == right.X && left.Y == right.Y && left.Width == right.Width && left.Height == right.Height;
    
    public static bool operator !=(Rect left, Rect right) => !(left == right);
    
    public bool Equals(Rect other) => this == other;
    
    public override bool Equals(object? obj) => obj is Rect rect && Equals(rect);
    
    public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);
    
    public override string ToString() => $"({X}, {Y}, {Width}, {Height})";
}