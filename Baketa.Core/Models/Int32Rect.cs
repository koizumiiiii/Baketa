namespace Baketa.Core.Models;

/// <summary>
/// プラットフォーム非依存の32ビット整数座標の四角形を表します。
/// Phase 2: 座標ベースオーバーレイ表示用
/// </summary>
/// <remarks>
/// System.Drawing.Rectangleに依存せず、クリーンアーキテクチャの依存関係ルールを維持するために定義。
/// UI層で使用するAvalonia.Rectへの変換はUI層で行う。
/// </remarks>
public readonly record struct Int32Rect(int X, int Y, int Width, int Height)
{
    /// <summary>
    /// 幅または高さがゼロ以下の場合にtrueを返します
    /// </summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>
    /// 中心点のX座標を取得します
    /// </summary>
    public int CenterX => X + Width / 2;

    /// <summary>
    /// 中心点のY座標を取得します
    /// </summary>
    public int CenterY => Y + Height / 2;

    /// <summary>
    /// 右端のX座標を取得します
    /// </summary>
    public int Right => X + Width;

    /// <summary>
    /// 下端のY座標を取得します
    /// </summary>
    public int Bottom => Y + Height;

    /// <summary>
    /// デバッグ用文字列表現
    /// </summary>
    public override string ToString() => $"({X}, {Y}, {Width}x{Height})";
}
