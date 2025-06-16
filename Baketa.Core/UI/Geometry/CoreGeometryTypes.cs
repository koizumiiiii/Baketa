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
/// Coreレイヤー用の2D座標構造体
/// </summary>
public readonly struct CorePoint(double x, double y) : IEquatable<CorePoint>
{
    public double X { get; } = x;
    public double Y { get; } = y;
    
    public static CorePoint Zero => new(0, 0);
    
    public static CorePoint operator +(CorePoint left, CoreVector right) => 
        new(left.X + right.X, left.Y + right.Y);
    
    public static CorePoint operator -(CorePoint left, CoreVector right) => 
        new(left.X - right.X, left.Y - right.Y);
    
    public static CoreVector operator -(CorePoint left, CorePoint right) => 
        new(left.X - right.X, left.Y - right.Y);
    
    /// <summary>
    /// 加算演算子の代替メソッド (CA2225)
    /// </summary>
    public static CorePoint Add(CorePoint left, CoreVector right) => left + right;
    
    /// <summary>
    /// 減算演算子の代替メソッド (CA2225)
    /// </summary>
    public static CorePoint Subtract(CorePoint left, CoreVector right) => left - right;
    
    /// <summary>
    /// 減算演算子の代替メソッド (CA2225)
    /// </summary>
    public static CoreVector Subtract(CorePoint left, CorePoint right) => left - right;
    
    public static bool operator ==(CorePoint left, CorePoint right) => 
        left.X == right.X && left.Y == right.Y;
    
    public static bool operator !=(CorePoint left, CorePoint right) => !(left == right);
    
    public bool Equals(CorePoint other) => this == other;
    
    public override bool Equals(object? obj) => obj is CorePoint point && Equals(point);
    
    public override int GetHashCode() => HashCode.Combine(X, Y);
    
    public override string ToString() => $"({X}, {Y})";
    
    /// <summary>
    /// Point型への変換
    /// </summary>
    public Point ToPoint() => new(X, Y);
    
    /// <summary>
    /// Point型からの変換
    /// </summary>
    public static implicit operator CorePoint(Point point) => new(point.X, point.Y);
    
    /// <summary>
    /// Point型への変換
    /// </summary>
    public static implicit operator Point(CorePoint point) => new(point.X, point.Y);
    
    /// <summary>
    /// 暗黙的変換演算子の代替メソッド (CA2225)
    /// </summary>
    public static CorePoint ToCorePoint(Point point) => point;
    
    /// <summary>
    /// 暗黙的変換演算子の代替メソッド (CA2225)
    /// </summary>
    public static Point FromPoint(CorePoint point) => point;
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
/// Coreレイヤー用の2Dサイズ構造体
/// </summary>
public readonly struct CoreSize(double width, double height) : IEquatable<CoreSize>
{
    public double Width { get; } = width >= 0 ? width : throw new ArgumentOutOfRangeException(nameof(width));
    public double Height { get; } = height >= 0 ? height : throw new ArgumentOutOfRangeException(nameof(height));
    
    public static CoreSize Empty => new(0, 0);
    
    /// <summary>
    /// 空かどうか
    /// </summary>
    public bool IsEmpty => Width == 0 || Height == 0;
    
    /// <summary>
    /// 面積
    /// </summary>
    public double Area => Width * Height;
    
    public static bool operator ==(CoreSize left, CoreSize right) => 
        left.Width == right.Width && left.Height == right.Height;
    
    public static bool operator !=(CoreSize left, CoreSize right) => !(left == right);
    
    public bool Equals(CoreSize other) => this == other;
    
    public override bool Equals(object? obj) => obj is CoreSize size && Equals(size);
    
    public override int GetHashCode() => HashCode.Combine(Width, Height);
    
    public override string ToString() => $"{Width}x{Height}";
    
    /// <summary>
    /// Size型への変換
    /// </summary>
    public Size ToSize() => new(Width, Height);
    
    /// <summary>
    /// Size型からの変換
    /// </summary>
    public static implicit operator CoreSize(Size size) => new(size.Width, size.Height);
    
    /// <summary>
    /// Size型への変換
    /// </summary>
    public static implicit operator Size(CoreSize size) => new(size.Width, size.Height);
    
    /// <summary>
    /// 暗黙的変換演算子の代替メソッド (CA2225)
    /// </summary>
    public static CoreSize ToCoreSize(Size size) => size;
    
    /// <summary>
    /// 暗黙的変換演算子の代替メソッド (CA2225)
    /// </summary>
    public static Size FromSize(CoreSize size) => size;
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
    
    /// <summary>
    /// 矩形が空（幅または高さが0以下）かどうかを判定
    /// </summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;
    
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
    
    /// <summary>
    /// 2つの矩形の交差部分を計算
    /// </summary>
    /// <param name="other">交差計算する矩形</param>
    /// <returns>交差部分の矩形。交差しない場合はEmpty</returns>
    public Rect Intersect(Rect other)
    {
        if (!IntersectsWith(other))
            return Empty;
            
        var left = Math.Max(Left, other.Left);
        var top = Math.Max(Top, other.Top);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        
        return new Rect(left, top, right - left, bottom - top);
    }
    
    /// <summary>
    /// 2つの矩形の結合（union）を計算
    /// </summary>
    /// <param name="other">結合する矩形</param>
    /// <returns>結合された矩形</returns>
    public Rect Union(Rect other)
    {
        if (IsEmpty) return other;
        if (other.IsEmpty) return this;
        
        var left = Math.Min(Left, other.Left);
        var top = Math.Min(Top, other.Top);
        var right = Math.Max(Right, other.Right);
        var bottom = Math.Max(Bottom, other.Bottom);
        
        return new Rect(left, top, right - left, bottom - top);
    }
    
    /// <summary>
    /// 矩形を指定した距離だけ膨張または収縮
    /// </summary>
    /// <param name="deltaX">水平方向の変化量</param>
    /// <param name="deltaY">垂直方向の変化量</param>
    /// <returns>変更された矩形</returns>
    public Rect Inflate(double deltaX, double deltaY) =>
        new(X - deltaX, Y - deltaY, Width + 2 * deltaX, Height + 2 * deltaY);
    
    /// <summary>
    /// 矩形を指定した距離だけ膨張または収縮
    /// </summary>
    /// <param name="delta">変化量</param>
    /// <returns>変更された矩形</returns>
    public Rect Inflate(double delta) => Inflate(delta, delta);
    
    /// <summary>
    /// 矩形を指定した位置だけ移動
    /// </summary>
    /// <param name="offsetX">水平方向の移動量</param>
    /// <param name="offsetY">垂直方向の移動量</param>
    /// <returns>移動された矩形</returns>
    public Rect Offset(double offsetX, double offsetY) =>
        new(X + offsetX, Y + offsetY, Width, Height);
    
    /// <summary>
    /// 矩形を指定したベクトルだけ移動
    /// </summary>
    /// <param name="offset">移動ベクトル</param>
    /// <returns>移動された矩形</returns>
    public Rect Offset(Point offset) => Offset(offset.X, offset.Y);
    
    public static bool operator ==(Rect left, Rect right) => 
        left.X == right.X && left.Y == right.Y && left.Width == right.Width && left.Height == right.Height;
    
    public static bool operator !=(Rect left, Rect right) => !(left == right);
    
    public bool Equals(Rect other) => this == other;
    
    public override bool Equals(object? obj) => obj is Rect rect && Equals(rect);
    
    public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);
    
    public override string ToString() => $"({X}, {Y}, {Width}, {Height})";
}

/// <summary>
/// Coreレイヤー用の2D矩形構造体
/// </summary>
public readonly struct CoreRect(double x, double y, double width, double height) : IEquatable<CoreRect>
{
    public double X { get; } = x;
    public double Y { get; } = y;
    public double Width { get; } = width >= 0 ? width : throw new ArgumentOutOfRangeException(nameof(width));
    public double Height { get; } = height >= 0 ? height : throw new ArgumentOutOfRangeException(nameof(height));
    
    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;
    
    public CorePoint TopLeft => new(X, Y);
    public CorePoint TopRight => new(Right, Y);
    public CorePoint BottomLeft => new(X, Bottom);
    public CorePoint BottomRight => new(Right, Bottom);
    
    public CorePoint Center => new(X + Width / 2, Y + Height / 2);
    public CoreSize Size => new(Width, Height);
    
    public static CoreRect Empty => new(0, 0, 0, 0);
    
    /// <summary>
    /// 矩形が空（幅または高さが0以下）かどうかを判定
    /// </summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;
    
    /// <summary>
    /// 面積
    /// </summary>
    public double Area => Width * Height;
    
    public CoreRect(CorePoint position, CoreSize size) : this(position.X, position.Y, size.Width, size.Height) { }
    
    /// <summary>
    /// 指定された点が矩形内に含まれるかどうかを判定
    /// </summary>
    public bool Contains(CorePoint point) => 
        point.X >= Left && point.X <= Right && point.Y >= Top && point.Y <= Bottom;
    
    /// <summary>
    /// 指定された矩形がこの矩形と交差するかどうかを判定
    /// </summary>
    public bool IntersectsWith(CoreRect other) =>
        !(other.Left > Right || other.Right < Left || other.Top > Bottom || other.Bottom < Top);
    
    /// <summary>
    /// 2つの矩形の交差部分を計算
    /// </summary>
    /// <param name="other">交差計算する矩形</param>
    /// <returns>交差部分の矩形。交差しない場合はEmpty</returns>
    public CoreRect Intersect(CoreRect other)
    {
        if (!IntersectsWith(other))
            return Empty;
            
        var left = Math.Max(Left, other.Left);
        var top = Math.Max(Top, other.Top);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        
        return new CoreRect(left, top, right - left, bottom - top);
    }
    
    /// <summary>
    /// 2つの矩形の結合（union）を計算
    /// </summary>
    /// <param name="other">結合する矩形</param>
    /// <returns>結合された矩形</returns>
    public CoreRect Union(CoreRect other)
    {
        if (IsEmpty) return other;
        if (other.IsEmpty) return this;
        
        var left = Math.Min(Left, other.Left);
        var top = Math.Min(Top, other.Top);
        var right = Math.Max(Right, other.Right);
        var bottom = Math.Max(Bottom, other.Bottom);
        
        return new CoreRect(left, top, right - left, bottom - top);
    }
    
    /// <summary>
    /// 矩形を指定した距離だけ膨張または収縮
    /// </summary>
    /// <param name="deltaX">水平方向の変化量</param>
    /// <param name="deltaY">垂直方向の変化量</param>
    /// <returns>変更された矩形</returns>
    public CoreRect Inflate(double deltaX, double deltaY) =>
        new(X - deltaX, Y - deltaY, Width + 2 * deltaX, Height + 2 * deltaY);
    
    /// <summary>
    /// 矩形を指定した距離だけ膨張または収縮
    /// </summary>
    /// <param name="delta">変化量</param>
    /// <returns>変更された矩形</returns>
    public CoreRect Inflate(double delta) => Inflate(delta, delta);
    
    /// <summary>
    /// 矩形を指定した位置だけ移動
    /// </summary>
    /// <param name="offsetX">水平方向の移動量</param>
    /// <param name="offsetY">垂直方向の移動量</param>
    /// <returns>移動された矩形</returns>
    public CoreRect Offset(double offsetX, double offsetY) =>
        new(X + offsetX, Y + offsetY, Width, Height);
    
    /// <summary>
    /// 矩形を指定したベクトルだけ移動
    /// </summary>
    /// <param name="offset">移動ベクトル</param>
    /// <returns>移動された矩形</returns>
    public CoreRect Offset(CoreVector offset) => Offset(offset.X, offset.Y);
    
    public static bool operator ==(CoreRect left, CoreRect right) => 
        left.X == right.X && left.Y == right.Y && left.Width == right.Width && left.Height == right.Height;
    
    public static bool operator !=(CoreRect left, CoreRect right) => !(left == right);
    
    public bool Equals(CoreRect other) => this == other;
    
    public override bool Equals(object? obj) => obj is CoreRect rect && Equals(rect);
    
    public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);
    
    public override string ToString() => $"({X}, {Y}, {Width}, {Height})";
    
    /// <summary>
    /// Rect型への変換
    /// </summary>
    public Rect ToRect() => new(X, Y, Width, Height);
    
    /// <summary>
    /// Rect型からの変換
    /// </summary>
    public static implicit operator CoreRect(Rect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
    
    /// <summary>
    /// Rect型への変換
    /// </summary>
    public static implicit operator Rect(CoreRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
    
    /// <summary>
    /// 暗黙的変換演算子の代替メソッド (CA2225)
    /// </summary>
    public static CoreRect ToCoreRect(Rect rect) => rect;
    
    /// <summary>
    /// 暗黙的変換演算子の代替メソッド (CA2225)
    /// </summary>
    public static Rect FromRect(CoreRect rect) => rect;
}

/// <summary>
/// 2Dベクトルを表すCoreレイヤー用構造体
/// </summary>
public readonly struct CoreVector(double x, double y) : IEquatable<CoreVector>
{
    public double X { get; } = x;
    public double Y { get; } = y;
    
    public static CoreVector Zero => new(0, 0);
    
    /// <summary>
    /// ベクトルの長さ
    /// </summary>
    public double Length => Math.Sqrt(X * X + Y * Y);
    
    /// <summary>
    /// ベクトルの長さの2乗
    /// </summary>
    public double LengthSquared => X * X + Y * Y;
    
    /// <summary>
    /// 正規化されたベクトル
    /// </summary>
    public CoreVector Normalized
    {
        get
        {
            var length = Length;
            return length > 0 ? new CoreVector(X / length, Y / length) : Zero;
        }
    }
    
    public static CoreVector operator +(CoreVector left, CoreVector right) => 
        new(left.X + right.X, left.Y + right.Y);
    
    public static CoreVector operator -(CoreVector left, CoreVector right) => 
        new(left.X - right.X, left.Y - right.Y);
    
    public static CoreVector operator *(CoreVector vector, double scalar) => 
        new(vector.X * scalar, vector.Y * scalar);
    
    public static CoreVector operator *(double scalar, CoreVector vector) => 
        new(vector.X * scalar, vector.Y * scalar);
    
    public static CoreVector operator /(CoreVector vector, double scalar) => 
        new(vector.X / scalar, vector.Y / scalar);
    
    public static CoreVector operator -(CoreVector vector) => 
        new(-vector.X, -vector.Y);
    
    /// <summary>
    /// 加算演算子の代替メソッド (CA2225)
    /// </summary>
    public static CoreVector Add(CoreVector left, CoreVector right) => left + right;
    
    /// <summary>
    /// 減算演算子の代替メソッド (CA2225)
    /// </summary>
    public static CoreVector Subtract(CoreVector left, CoreVector right) => left - right;
    
    /// <summary>
    /// 乗算演算子の代替メソッド (CA2225)
    /// </summary>
    public static CoreVector Multiply(CoreVector vector, double scalar) => vector * scalar;
    
    /// <summary>
    /// 乗算演算子の代替メソッド (CA2225)
    /// </summary>
    public static CoreVector Multiply(double scalar, CoreVector vector) => scalar * vector;
    
    /// <summary>
    /// 除算演算子の代替メソッド (CA2225)
    /// </summary>
    public static CoreVector Divide(CoreVector vector, double scalar) => vector / scalar;
    
    /// <summary>
    /// 単項否定演算子の代替メソッド (CA2225)
    /// </summary>
    public static CoreVector Negate(CoreVector vector) => -vector;
    
    public static bool operator ==(CoreVector left, CoreVector right) => 
        left.X == right.X && left.Y == right.Y;
    
    public static bool operator !=(CoreVector left, CoreVector right) => !(left == right);
    
    /// <summary>
    /// 内積を計算
    /// </summary>
    public double Dot(CoreVector other) => X * other.X + Y * other.Y;
    
    /// <summary>
    /// 2D外積（Z成分のみ）を計算
    /// </summary>
    public double Cross(CoreVector other) => X * other.Y - Y * other.X;
    
    public bool Equals(CoreVector other) => this == other;
    
    public override bool Equals(object? obj) => obj is CoreVector vector && Equals(vector);
    
    public override int GetHashCode() => HashCode.Combine(X, Y);
    
    public override string ToString() => $"<{X}, {Y}>";
}

/// <summary>
/// Core幾何学型用の拡張メソッド
/// </summary>
public static class CoreGeometryExtensions
{
    /// <summary>
    /// Rect型の面積を計算します
    /// </summary>
    /// <param name="rect">矩形</param>
    /// <returns>面積</returns>
    public static double Area(this Rect rect) => rect.Width * rect.Height;
    
    /// <summary>
    /// Point型をCorePoint型に変換します
    /// </summary>
    /// <param name="point">変換元の点</param>
    /// <returns>CorePoint型の点</returns>
    public static CorePoint ToCorePoint(this Point point) => new(point.X, point.Y);
    
    /// <summary>
    /// Size型をCoreSize型に変換します
    /// </summary>
    /// <param name="size">変換元のサイズ</param>
    /// <returns>CoreSize型のサイズ</returns>
    public static CoreSize ToCoreSize(this Size size) => new(size.Width, size.Height);
    
    /// <summary>
    /// Rect型をCoreRect型に変換します
    /// </summary>
    /// <param name="rect">変換元の矩形</param>
    /// <returns>CoreRect型の矩形</returns>
    public static CoreRect ToCoreRect(this Rect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
}

/// <summary>
/// 厚み（上下左右のマージン）を表すCoreレイヤー用構造体
/// </summary>
public readonly struct CoreThickness(double left, double top, double right, double bottom) : IEquatable<CoreThickness>
{
    public double Left { get; } = left >= 0 ? left : throw new ArgumentOutOfRangeException(nameof(left));
    public double Top { get; } = top >= 0 ? top : throw new ArgumentOutOfRangeException(nameof(top));
    public double Right { get; } = right >= 0 ? right : throw new ArgumentOutOfRangeException(nameof(right));
    public double Bottom { get; } = bottom >= 0 ? bottom : throw new ArgumentOutOfRangeException(nameof(bottom));
    
    /// <summary>
    /// 全方向に同じ厚みを持つコンストラクタ
    /// </summary>
    public CoreThickness(double uniformThickness) : this(uniformThickness, uniformThickness, uniformThickness, uniformThickness) { }
    
    /// <summary>
    /// 水平と垂直で異なる厚みを持つコンストラクタ
    /// </summary>
    public CoreThickness(double horizontal, double vertical) : this(horizontal, vertical, horizontal, vertical) { }
    
    public static CoreThickness Zero => new(0, 0, 0, 0);
    
    /// <summary>
    /// 水平方向の合計厚み
    /// </summary>
    public double Horizontal => Left + Right;
    
    /// <summary>
    /// 垂直方向の合計厚み
    /// </summary>
    public double Vertical => Top + Bottom;
    
    /// <summary>
    /// 空かどうか
    /// </summary>
    public bool IsEmpty => Left == 0 && Top == 0 && Right == 0 && Bottom == 0;
    
    /// <summary>
    /// 矩形に厚みを適用（内側に縮小）
    /// </summary>
    public CoreRect Deflate(CoreRect rect) => 
        new(rect.X + Left, rect.Y + Top, 
            Math.Max(0, rect.Width - Horizontal), 
            Math.Max(0, rect.Height - Vertical));
    
    /// <summary>
    /// 矩形に厚みを適用（外側に拡大）
    /// </summary>
    public CoreRect Inflate(CoreRect rect) => 
        new(rect.X - Left, rect.Y - Top, 
            rect.Width + Horizontal, 
            rect.Height + Vertical);
    
    public static bool operator ==(CoreThickness left, CoreThickness right) => 
        left.Left == right.Left && left.Top == right.Top && 
        left.Right == right.Right && left.Bottom == right.Bottom;
    
    public static bool operator !=(CoreThickness left, CoreThickness right) => !(left == right);
    
    public bool Equals(CoreThickness other) => this == other;
    
    public override bool Equals(object? obj) => obj is CoreThickness thickness && Equals(thickness);
    
    public override int GetHashCode() => HashCode.Combine(Left, Top, Right, Bottom);
    
    public override string ToString() => $"({Left}, {Top}, {Right}, {Bottom})";
}