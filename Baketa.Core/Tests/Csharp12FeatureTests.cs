using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace Baketa.Core.Tests
{
    // C# 12 型エイリアスのテスト
    using Point = (int X, int Y);

    /// <summary>
    /// C# 12の新機能サポート状況を確認するためのクラス
    /// </summary>
    /// <remarks>
    /// このクラスはプロジェクト全体でC# 12の新機能が正しくサポートされているかを確認するためのものです。
    /// 実際のテストでは使用せず、コンパイルエラーが発生しないことのみを確認します。
    /// </remarks>
    public static class Csharp12FeatureTests
    {
        // 1. コレクション式の様々な使用方法テスト
        public static void CollectionExpressions()
        {
            // 空のコレクション式 - 型指定が必要
            int[] _ = [];
            List<string> __ = [];
            Dictionary<int, string> ___ = [];
            
            // 要素を持つコレクション式
            int[] numbers = [1, 2, 3, 4, 5];
            List<string> words = ["hello", "world"];
            // 値を表示して使用しない
            Console.WriteLine(words[0]);
            
            // 辞書のコレクション式
            Dictionary<int, string> dict = new()
            {
                { 1, "one" },
                { 2, "two" },
                { 3, "three" }
            };
            // 値を表示して使用しない
            Console.WriteLine(dict[1]);
            
            // スプレッド演算子
            int[] firstHalf = [1, 2, 3];
            int[] secondHalf = [4, 5, 6];
            int[] combined = [..firstHalf, ..secondHalf]; // 使用されないが、型指定が必要
            // 最初の値のみ表示して使用しない
            Console.WriteLine(combined[0]);
            
            // 既存コレクションへの追加
            int[] extended = [..numbers, 6, 7, 8]; // 使用されないが、型指定が必要
            // 最初の値のみ表示して使用しない
            Console.WriteLine(extended[0]);
            
            // 型推論を持つコレクション式
            var inferredArray = new[] { 1, 2, 3 }; // varで型推論を使用
            var inferredDict = new Dictionary<int, string>
            {
                { 1, "one" },
                { 2, "two" }
            };
            // 値を表示して使用しない
            Console.WriteLine(inferredArray[0]);
            Console.WriteLine(inferredDict[1]);
        }
        
        // 2. プライマリコンストラクタのオプション引数
    internal class ConfigOptions(string name, bool enabled = true, int timeout = 30)
        {
            public string Name { get; } = name;
            public bool Enabled { get; } = enabled;
            public int Timeout { get; } = timeout;
        }
        
        // 3. ref readonly パラメーター
        public static bool TryGetValueSafe<TKey, TValue>(
            Dictionary<TKey, TValue> dict, 
            ref readonly TKey key, 
            out TValue value) where TKey : notnull
        {
            ArgumentNullException.ThrowIfNull(dict, nameof(dict));
            
            return dict.TryGetValue(key, out value!);
        }
        
        // 4. インラインアレイ
        [System.Runtime.CompilerServices.InlineArray(10)]
        // readonly構造体にすると、すべてのフィールドもreadonlyである必要があるが、インラインアレイはそれをサポートしない
        internal struct Buffer10<T> : IEquatable<Buffer10<T>> where T : IEquatable<T>
        {
            // インライン配列要素はreadonlyとして宣言できない
            private T _element;
            
            public readonly bool Equals(Buffer10<T> other)
            {
                // シンプルな実装として、最初の要素のみを比較
                if (_element is null)
                    return other._element is null;
                    
                return _element.Equals(other._element);
            }
            
            public readonly override bool Equals(object? obj)
            {
                return obj is Buffer10<T> other && Equals(other);
            }
            
            public readonly override int GetHashCode()
            {
                return _element?.GetHashCode() ?? 0;
            }
            
            public static bool operator ==(Buffer10<T> left, Buffer10<T> right)
            {
                return left.Equals(right);
            }
            
            public static bool operator !=(Buffer10<T> left, Buffer10<T> right)
            {
                return !(left == right);
            }
        }
        
        // 5. interceptors (C# 12プレビュー機能)
        // 注: このプレビュー機能はまだ完全にサポートされていない可能性があるため注意
        // InterceptsLocationはプレビュー機能であり、特別な設定が必要です
        public static void InterceptMethod() 
        { 
            Console.WriteLine(TestResources.InterceptedMessage); 
        }
        
        // 6. alias any type (型エイリアス)
        public static void AliasTypes()
        {
            // ネームスペースレベルで定義した型エイリアスを使用
            Point p = (10, 20);
            // コンポジットフォーマットを使用してチャッチ
            string format = TestResources.PositionMessage;
            var compositeFormat = System.Text.CompositeFormat.Parse(format);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, compositeFormat, p.X, p.Y));
        }
    }
}