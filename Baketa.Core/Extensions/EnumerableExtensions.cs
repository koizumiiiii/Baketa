using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Baketa.Core.Extensions;

    /// <summary>
    /// IEnumerable拡張メソッドを提供します
    /// </summary>
    public static class EnumerableExtensions
    {
        /// <summary>
        /// 複数回の列挙を防ぐためのToListラッパー
        /// </summary>
        /// <typeparam name="T">要素の型</typeparam>
        /// <param name="source">元のコレクション</param>
        /// <returns>具体化された読み取り専用コレクション</returns>
        public static ReadOnlyCollection<T> AsList<T>(this IEnumerable<T> source)
        {
            ArgumentNullException.ThrowIfNull(source, nameof(source));

            // すでにReadOnlyCollectionの場合は変換せずにそのまま返す
            if (source is ReadOnlyCollection<T> readOnlyCollection)
            {
                return readOnlyCollection;
            }

            // Listの場合は読み取り専用コレクションに変換
            if (source is List<T> list)
            {
                return new ReadOnlyCollection<T>(list);
            }

            // それ以外は新しいリストを作成して読み取り専用にラップ
            return new ReadOnlyCollection<T>([.. source]);
        }

        /// <summary>
        /// 複数回の列挙を防ぐためのToArrayラッパー
        /// </summary>
        /// <typeparam name="T">要素の型</typeparam>
        /// <param name="source">元のコレクション</param>
        /// <returns>具体化された配列</returns>
        public static T[] AsArray<T>(this IEnumerable<T> source)
        {
            ArgumentNullException.ThrowIfNull(source, nameof(source));

            // すでに配列の場合は変換せずにそのまま返す
            if (source is T[] array)
            {
                return array;
            }

            // 具体化されたコレクションの場合は新しい配列を作成
            return [.. source];
        }

        /// <summary>
        /// 安全にAnyを実行します（複数列挙を防止）
        /// </summary>
        /// <typeparam name="T">要素の型</typeparam>
        /// <param name="source">元のコレクション</param>
        /// <returns>コレクションが空でない場合はtrue</returns>
        public static bool SafeAny<T>(this IEnumerable<T> source)
        {
            if (source == null)
            {
                return false;
            }

            // ICollectionの場合はCountプロパティを使用
            if (source is ICollection<T> collection)
            {
                return collection.Count > 0;
            }

            // それ以外はAnyを使用
            return source.Any();
        }

        /// <summary>
        /// 安全にAnyを実行します（複数列挙を防止）
        /// </summary>
        /// <typeparam name="T">要素の型</typeparam>
        /// <param name="source">元のコレクション</param>
        /// <param name="predicate">条件</param>
        /// <returns>条件を満たす要素がある場合はtrue</returns>
        public static bool SafeAny<T>(this IEnumerable<T> source, Func<T, bool> predicate)
        {
            if (source == null || predicate == null)
            {
                return false;
            }

            // 一度具体化して列挙
            var concreteList = source.ToList();
            return concreteList.Any(predicate);
        }
    }
