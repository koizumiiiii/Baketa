using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;

namespace Baketa.Core.Translation.Abstractions
{
    /// <summary>
    /// 翻訳キャッシュインターフェース
    /// </summary>
    public interface ITranslationCache
    {
        /// <summary>
        /// キャッシュからエントリを取得します
        /// </summary>
        /// <param name="key">キャッシュキー</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>キャッシュエントリ（存在しない場合はnull）</returns>
        Task<TranslationCacheEntry?> GetAsync(string key, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// 複数のキャッシュエントリを取得します
        /// </summary>
        /// <param name="keys">キャッシュキーのコレクション</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>キャッシュエントリの辞書</returns>
        Task<IDictionary<string, TranslationCacheEntry>> GetManyAsync(
            IEnumerable<string> keys,
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// キャッシュにエントリを保存します
        /// </summary>
        /// <param name="key">キャッシュキー</param>
        /// <param name="entry">キャッシュエントリ</param>
        /// <param name="expiration">有効期限（null=デフォルト）</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>保存に成功した場合はtrue</returns>
        Task<bool> SetAsync(
            string key,
            TranslationCacheEntry entry,
            TimeSpan? expiration = null,
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// 複数のキャッシュエントリを保存します
        /// </summary>
        /// <param name="entries">キャッシュエントリの辞書</param>
        /// <param name="expiration">有効期限（null=デフォルト）</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>保存に成功した場合はtrue</returns>
        Task<bool> SetManyAsync(
            IDictionary<string, TranslationCacheEntry> entries,
            TimeSpan? expiration = null,
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// キャッシュからエントリを削除します
        /// </summary>
        /// <param name="key">キャッシュキー</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>削除に成功した場合はtrue</returns>
        Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// キャッシュをクリアします
        /// </summary>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>クリアに成功した場合はtrue</returns>
        Task<bool> ClearAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// キャッシュの統計情報を取得します
        /// </summary>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>キャッシュ統計情報</returns>
        Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
    }
    
    /// <summary>
    /// 永続化キャッシュインターフェース
    /// </summary>
    public interface ITranslationPersistentCache : ITranslationCache
    {
        // 永続化キャッシュ特有のメソッドがあれば追加
    }
}