using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Baketa.Core.Translation.Models;

/// <summary>
/// 翻訳コンテキストを表すクラス
/// </summary>
public class TranslationContext
{
    /// <summary>
    /// ゲームプロファイルID
    /// </summary>
    public string? GameProfileId { get; set; }
    
    /// <summary>
    /// シーン識別子
    /// </summary>
    public string? SceneId { get; set; }
    
    /// <summary>
    /// 会話ID
    /// </summary>
    public string? DialogueId { get; set; }
    
    /// <summary>
    /// 画面領域
    /// </summary>
    public Rectangle? ScreenRegion { get; set; }
    
    /// <summary>
    /// コンテキストタグ
    /// </summary>
    public readonly Collection<string> Tags = [];
    
    /// <summary>
    /// コンテキスト優先度（0～100）
    /// </summary>
    public int Priority { get; set; } = 50;
    
    /// <summary>
    /// 追加コンテキスト情報
    /// </summary>
    public readonly Dictionary<string, object?> AdditionalContext = [];
    
    /// <summary>
    /// テキストのジャンル（小説、マニュアル、会話など）
    /// </summary>
    public string? Genre { get; set; }
    
    /// <summary>
    /// テキストのドメイン（ゲーム、技術、医学など）
    /// </summary>
    public string? Domain { get; set; }
    
    /// <summary>
    /// 文字列表現を取得します
    /// </summary>
    /// <returns>文字列表現</returns>
    public override string ToString()
    {
        return $"Context[Game:{GameProfileId}, Scene:{SceneId}, Tags:{string.Join(",", Tags)}]";
    }
    
    /// <summary>
    /// この翻訳コンテキストのコピーを作成します
    /// </summary>
    /// <returns>翻訳コンテキストのコピー</returns>
    public TranslationContext Clone()
    {
        var clone = new TranslationContext
        {
            GameProfileId = this.GameProfileId,
            SceneId = this.SceneId,
            DialogueId = this.DialogueId,
            ScreenRegion = this.ScreenRegion,
            Priority = this.Priority,
            Genre = this.Genre,
            Domain = this.Domain
        };
        
        // コレクションの要素をコピー
        foreach (var tag in this.Tags)
        {
            clone.Tags.Add(tag);
        }
        
        // 追加コンテキストをコピー
        foreach (var item in this.AdditionalContext)
        {
            clone.AdditionalContext[item.Key] = item.Value;
        }
        
        return clone;
    }
}

/// <summary>
/// 矩形領域を表す構造体
/// </summary>
/// <remarks>
/// コンストラクタ
/// </remarks>
/// <param name="x">X座標</param>
/// <param name="y">Y座標</param>
/// <param name="width">幅</param>
/// <param name="height">高さ</param>
public struct Rectangle(int x, int y, int width, int height) : IEquatable<Rectangle>
{
    /// <summary>
    /// X座標
    /// </summary>
    public int X { get; set; } = x;

    /// <summary>
    /// Y座標
    /// </summary>
    public int Y { get; set; } = y;

    /// <summary>
    /// 幅
    /// </summary>
    public int Width { get; set; } = width;

    /// <summary>
    /// 高さ
    /// </summary>
    public int Height { get; set; } = height;

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
    /// 文字列表現を取得します
    /// </summary>
    /// <returns>文字列表現</returns>
    public override string ToString()
    {
        return $"({X},{Y},{Width},{Height})";
    }
    
    /// <summary>
    /// 型安全な等価性比較を行います
    /// </summary>
    /// <param name="other">比較対象</param>
    /// <returns>等しい場合はtrue</returns>
    public bool Equals(Rectangle other)
    {
        return X == other.X && 
               Y == other.Y && 
               Width == other.Width && 
               Height == other.Height;
    }

    /// <summary>
    /// 等価性比較
    /// </summary>
    /// <param name="obj">比較対象</param>
    /// <returns>等しい場合はtrue</returns>
    public override bool Equals(object? obj)
    {
        return obj is Rectangle other && Equals(other);
    }
    
    /// <summary>
    /// ハッシュコードを取得します
    /// </summary>
    /// <returns>ハッシュコード</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(X, Y, Width, Height);
    }
    
    /// <summary>
    /// 等価性比較演算子
    /// </summary>
    /// <param name="left">左辺</param>
    /// <param name="right">右辺</param>
    /// <returns>等しい場合はtrue</returns>
    public static bool operator ==(Rectangle left, Rectangle right) => left.Equals(right);

    /// <summary>
    /// 非等価性比較演算子
    /// </summary>
    /// <param name="left">左辺</param>
    /// <param name="right">右辺</param>
    /// <returns>等しくない場合はtrue</returns>
    public static bool operator !=(Rectangle left, Rectangle right) => !(left == right);
}
