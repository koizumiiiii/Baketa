using System;

namespace Baketa.Core.Abstractions.Platform.Windows.Adapters;

    /// <summary>
    /// Windows固有実装とコア抽象化レイヤー間のアダプター基本インターフェース
    /// </summary>
    public interface IWindowsAdapter
    {
        /// <summary>
        /// アダプターがサポートする機能名
        /// </summary>
        string FeatureName { get; }
        
        /// <summary>
        /// 特定の型変換をサポートするかどうか
        /// </summary>
        /// <typeparam name="TSource">ソース型</typeparam>
        /// <typeparam name="TTarget">ターゲット型</typeparam>
        /// <returns>サポートする場合はtrue</returns>
        bool SupportsConversion<TSource, TTarget>();
        
        /// <summary>
        /// 変換を試行
        /// </summary>
        /// <typeparam name="TSource">ソース型</typeparam>
        /// <typeparam name="TTarget">ターゲット型</typeparam>
        /// <param name="source">ソースオブジェクト</param>
        /// <param name="target">変換結果（出力）</param>
        /// <returns>変換成功時はtrue</returns>
        bool TryConvert<TSource, TTarget>(TSource source, out TTarget target) where TSource : class where TTarget : class;
    }
