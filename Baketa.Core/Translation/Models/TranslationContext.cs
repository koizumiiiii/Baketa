using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Baketa.Core.Translation.Models
{
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
        private readonly List<string> _tags = new();
        
        /// <summary>
        /// コンテキストタグ
        /// </summary>
        public IReadOnlyList<string> Tags => _tags;
        
        /// <summary>
        /// コンテキスト優先度（0～100）
        /// </summary>
        public int Priority { get; set; } = 50;
        
        /// <summary>
        /// 追加コンテキスト情報
        /// </summary>
        public Dictionary<string, object?> AdditionalContext { get; } = [];

        /// <summary>
        /// デフォルトコンストラクタ
        /// </summary>
        public TranslationContext()
        {
            // デフォルトコンストラクタは必要ですが、実際には初期化が不要です
        }

        /// <summary>
        /// ゲームプロファイルIDとシーンIDを指定して初期化
        /// </summary>
        /// <param name="gameProfileId">ゲームプロファイルID</param>
        /// <param name="sceneId">シーンID</param>
        public TranslationContext(string gameProfileId, string? sceneId = null)
        {
            GameProfileId = gameProfileId;
            SceneId = sceneId;
        }

        /// <summary>
        /// クローンを作成
        /// </summary>
        /// <returns>このコンテキストのクローン</returns>
        public TranslationContext Clone()
        {
            var clone = new TranslationContext
            {
                GameProfileId = GameProfileId,
                SceneId = SceneId,
                DialogueId = DialogueId,
                ScreenRegion = ScreenRegion?.Clone(),
                Priority = Priority
            };

            // タグのコピー
            foreach (var tag in Tags)
            {
                clone._tags.Add(tag);
            }

            // 追加コンテキストのコピー
            foreach (var kv in AdditionalContext)
            {
                clone.AdditionalContext[kv.Key] = kv.Value;
            }

            return clone;
        }

        /// <summary>
        /// コンテキストのハッシュ文字列を生成
        /// </summary>
        /// <returns>コンテキストハッシュ</returns>
        public string GetHashString()
        {
            var sb = new StringBuilder();
            
            // 基本プロパティ
            sb.Append("G:").Append(GameProfileId ?? "null").Append(';');
            sb.Append("S:").Append(SceneId ?? "null").Append(';');
            sb.Append("D:").Append(DialogueId ?? "null").Append(';');
            
            // 領域
            if (ScreenRegion.HasValue)
            {
                sb.Append("R:")
                  .Append(ScreenRegion.Value.X).Append(',')
                  .Append(ScreenRegion.Value.Y).Append(',')
                  .Append(ScreenRegion.Value.Width).Append(',')
                  .Append(ScreenRegion.Value.Height).Append(';');
            }
            
            // タグ
            if (Tags.Count > 0)
            {
                sb.Append("T:");
                sb.Append(string.Join(",", Tags.OrderBy(t => t)));
                sb.Append(';');
            }
            
            // 優先度
            sb.Append("P:").Append(Priority).Append(';');
            
            // 追加コンテキスト
            if (AdditionalContext.Count > 0)
            {
                sb.Append("A:");
                var sortedKeys = AdditionalContext.Keys.OrderBy(k => k).ToList();
                foreach (var key in sortedKeys)
                {
                    var value = AdditionalContext[key]?.ToString() ?? "null";
                    sb.Append(key).Append('=').Append(value).Append(',');
                }
                sb.Remove(sb.Length - 1, 1); // 最後のカンマを削除
                sb.Append(';');
            }

            // SHA256ハッシュを計算（効率的なメソッドを使用）
            byte[] textBytes = Encoding.UTF8.GetBytes(sb.ToString());
            byte[] hashBytes = SHA256.HashData(textBytes);
            
            return Convert.ToBase64String(hashBytes)
                .Replace("+", "-", StringComparison.Ordinal)
                .Replace("/", "_", StringComparison.Ordinal)
                .Replace("=", "", StringComparison.Ordinal);
        }

        /// <summary>
        /// 等価比較をオーバーライド
        /// </summary>
        public override bool Equals(object? obj)
        {
            if (obj is not TranslationContext other)
            {
                return false;
            }

            return string.Equals(GetHashString(), other.GetHashString(), StringComparison.Ordinal);
        }

        /// <summary>
        /// ハッシュコードを生成
        /// </summary>
        public override int GetHashCode()
        {
            // ホワイトリスト適用：正確なハッシュ値を取得するためのカスタムロジック
            string hashString = GetHashString();
            return StringComparer.Ordinal.GetHashCode(hashString);
        }
    }

    /// <summary>
    /// 矩形領域を表す構造体
    /// </summary>
    public readonly struct Rectangle : IEquatable<Rectangle>
    {
        /// <summary>
        /// X座標
        /// </summary>
        public readonly int X { get; init; }
        
        /// <summary>
        /// Y座標
        /// </summary>
        public readonly int Y { get; init; }
        
        /// <summary>
        /// 幅
        /// </summary>
        public readonly int Width { get; init; }
        
        /// <summary>
        /// 高さ
        /// </summary>
        public readonly int Height { get; init; }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="x">X座標</param>
        /// <param name="y">Y座標</param>
        /// <param name="width">幅</param>
        /// <param name="height">高さ</param>
        public Rectangle(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        /// <summary>
        /// クローンを作成
        /// </summary>
        /// <returns>このRectangleのクローン</returns>
        public Rectangle Clone()
        {
            return new Rectangle(X, Y, Width, Height);
        }

        /// <summary>
        /// 文字列表現を返す
        /// </summary>
        public override string ToString()
        {
            return $"X={X}, Y={Y}, Width={Width}, Height={Height}";
        }

        /// <summary>
        /// 等価比較
        /// </summary>
        public bool Equals(Rectangle other)
        {
            return X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
        }

        /// <summary>
        /// 等価比較をオーバーライド
        /// </summary>
        public override bool Equals(object? obj)
        {
            return obj is Rectangle rectangle && Equals(rectangle);
        }

        /// <summary>
        /// ハッシュコードを生成
        /// </summary>
        public override int GetHashCode()
        {
            return HashCode.Combine(X, Y, Width, Height);
        }

        /// <summary>
        /// 等価演算子
        /// </summary>
        public static bool operator ==(Rectangle left, Rectangle right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// 非等価演算子
        /// </summary>
        public static bool operator !=(Rectangle left, Rectangle right)
        {
            return !left.Equals(right);
        }
    }
}
