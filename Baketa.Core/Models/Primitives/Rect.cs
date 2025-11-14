using System;

namespace Baketa.Core.Models.Primitives;

/// <summary>
/// プラットフォーム非依存の矩形構造体
/// System.Drawing.Rectangleの代替として使用
/// </summary>
public readonly record struct Rect
{
    /// <summary>
    /// X座標
    /// </summary>
    public int X { get; }

    /// <summary>
    /// Y座標
    /// </summary>
    public int Y { get; }

    /// <summary>
    /// 幅
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// 高さ
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// 矩形構造体を初期化
    /// </summary>
    /// <param name="x">X座標</param>
    /// <param name="y">Y座標</param>
    /// <param name="width">幅</param>
    /// <param name="height">高さ</param>
    public Rect(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>
    /// 左端のX座標
    /// </summary>
    public int Left => X;

    /// <summary>
    /// 上端のY座標
    /// </summary>
    public int Top => Y;

    /// <summary>
    /// 右端のX座標
    /// </summary>
    public int Right => X + Width;

    /// <summary>
    /// 下端のY座標
    /// </summary>
    public int Bottom => Y + Height;

    /// <summary>
    /// 中心点
    /// </summary>
    public Point Center => new(X + Width / 2, Y + Height / 2);

    /// <summary>
    /// 面積
    /// </summary>
    public long Area => (long)Width * Height;

    /// <summary>
    /// 空の矩形かどうか
    /// </summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>
    /// 指定した点が矩形内にあるかを判定
    /// </summary>
    /// <param name="point">判定する点</param>
    /// <returns>矩形内にある場合true</returns>
    public bool Contains(Point point) =>
        point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;

    /// <summary>
    /// 指定した矩形が完全に含まれるかを判定
    /// </summary>
    /// <param name="rect">判定する矩形</param>
    /// <returns>完全に含まれる場合true</returns>
    public bool Contains(Rect rect) =>
        rect.Left >= Left && rect.Right <= Right && rect.Top >= Top && rect.Bottom <= Bottom;

    /// <summary>
    /// 指定した矩形と交差するかを判定
    /// </summary>
    /// <param name="rect">判定する矩形</param>
    /// <returns>交差する場合true</returns>
    public bool Intersects(Rect rect) =>
        rect.Left < Right && rect.Right > Left && rect.Top < Bottom && rect.Bottom > Top;

    /// <summary>
    /// 指定した矩形との交差領域を取得
    /// </summary>
    /// <param name="rect">交差判定する矩形</param>
    /// <returns>交差領域（交差しない場合は空の矩形）</returns>
    public Rect Intersect(Rect rect)
    {
        var left = Math.Max(Left, rect.Left);
        var top = Math.Max(Top, rect.Top);
        var right = Math.Min(Right, rect.Right);
        var bottom = Math.Min(Bottom, rect.Bottom);

        if (left >= right || top >= bottom)
            return default; // 空の矩形

        return new Rect(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// 指定した矩形を包含する最小の矩形を取得
    /// </summary>
    /// <param name="rect">包含する矩形</param>
    /// <returns>結合された矩形</returns>
    public Rect Union(Rect rect)
    {
        if (IsEmpty) return rect;
        if (rect.IsEmpty) return this;

        var left = Math.Min(Left, rect.Left);
        var top = Math.Min(Top, rect.Top);
        var right = Math.Max(Right, rect.Right);
        var bottom = Math.Max(Bottom, rect.Bottom);

        return new Rect(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// 指定したオフセットだけ移動した矩形を取得
    /// </summary>
    /// <param name="dx">X方向のオフセット</param>
    /// <param name="dy">Y方向のオフセット</param>
    /// <returns>移動後の矩形</returns>
    public Rect Offset(int dx, int dy) => new(X + dx, Y + dy, Width, Height);

    /// <summary>
    /// 指定した値だけ膨らませた矩形を取得
    /// </summary>
    /// <param name="dx">X方向の膨張値</param>
    /// <param name="dy">Y方向の膨張値</param>
    /// <returns>膨張後の矩形</returns>
    public Rect Inflate(int dx, int dy) => new(X - dx, Y - dy, Width + 2 * dx, Height + 2 * dy);

    /// <summary>
    /// 文字列表現を取得
    /// </summary>
    public override string ToString() => $"{{X={X},Y={Y},Width={Width},Height={Height}}}";

    /// <summary>
    /// 空の矩形
    /// </summary>
    public static Rect Empty => default;
}

/// <summary>
/// プラットフォーム非依存の点構造体
/// </summary>
public readonly record struct Point(int X, int Y)
{
    /// <summary>
    /// 原点
    /// </summary>
    public static Point Empty => default;

    /// <summary>
    /// 指定したオフセットだけ移動した点を取得
    /// </summary>
    /// <param name="dx">X方向のオフセット</param>
    /// <param name="dy">Y方向のオフセット</param>
    /// <returns>移動後の点</returns>
    public Point Offset(int dx, int dy) => new(X + dx, Y + dy);

    /// <summary>
    /// 文字列表現を取得
    /// </summary>
    public override string ToString() => $"{{X={X},Y={Y}}}";
}
