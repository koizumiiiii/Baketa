using System;
using System.Collections.Generic;
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
        /// キャッシュからテキストを取得します
        /// </summary>
        /// <param name="key">キャッシュキー</param>
        /// <returns>キャッシュされた翻訳結果、存在しない場合はnull</returns>
        Task<TranslationCacheEntry?> GetAsync(string key);
        
        /// <summary>
        /// 複数のキーに対応するキャッシュを一括取得します
        /// </summary>
        /// <param name="keys">キャッシュキーのリスト</param>
        /// <returns>キーと翻訳結果のディクショナリ</returns>
        Task<IDictionary<string, TranslationCacheEntry>> GetManyAsync(IEnumerable<string> keys);
        
        /// <summary>
        /// テキストをキャッシュに保存します
        /// </summary>
        /// <param name="key">キャッシュキー</param>
        /// <param name="entry">キャッシュエントリ</param>
        /// <param name="expiration">有効期限（null=無期限）</param>
        /// <returns>保存が成功したかどうか</returns>
        Task<bool> SetAsync(string key, TranslationCacheEntry entry, TimeSpan? expiration = null);
        
        /// <summary>
        /// 複数のエントリを一括保存します
        /// </summary>
        /// <param name="entries">キーとエントリのディクショナリ</param>
        /// <param name="expiration">有効期限（null=無期限）</param>
        /// <returns>保存が成功したかどうか</returns>
        Task<bool> SetManyAsync(IDictionary<string, TranslationCacheEntry> entries, TimeSpan? expiration = null);
        
        /// <summary>
        /// キャッシュからアイテムを削除します
        /// </summary>
        /// <param name="key">キャッシュキー</param>
        /// <returns>削除が成功したかどうか</returns>
        Task<bool> RemoveAsync(string key);
        
        /// <summary>
        /// 複数のキーに対応するキャッシュを一括削除します
        /// </summary>
        /// <param name="keys">キャッシュキーのリスト</param>
        /// <returns>削除が成功したかどうか</returns>
        Task<bool> RemoveManyAsync(IEnumerable<string> keys);
        
        /// <summary>
        /// キャッシュをクリアします
        /// </summary>
        /// <returns>クリアが成功したかどうか</returns>
        Task<bool> ClearAsync();
        
        /// <summary>
        /// キャッシュのキー一覧を取得します
        /// </summary>
        /// <param name="pattern">検索パターン</param>
        /// <param name="maxCount">最大取得数</param>
        /// <returns>キャッシュキーのリスト</returns>
        Task<IEnumerable<string>> GetKeysAsync(string? pattern = null, int maxCount = 1000);
        
        /// <summary>
        /// キャッシュの統計情報を取得します
        /// </summary>
        /// <returns>キャッシュ統計情報</returns>
        Task<CacheStatistics> GetStatisticsAsync();
        
        /// <summary>
        /// キャッシュを最適化します
        /// </summary>
        /// <returns>最適化が成功したかどうか</returns>
        Task<bool> OptimizeAsync();
        
        /// <summary>
        /// キャッシュをエクスポートします
        /// </summary>
        /// <param name="filePath">エクスポート先ファイルパス</param>
        /// <returns>エクスポートが成功したかどうか</returns>
        Task<bool> ExportAsync(string filePath);
        
        /// <summary>
        /// キャッシュをインポートします
        /// </summary>
        /// <param name="filePath">インポート元ファイルパス</param>
        /// <param name="mergeStrategy">マージ戦略</param>
        /// <returns>インポートが成功したかどうか</returns>
        Task<bool> ImportAsync(string filePath, CacheMergeStrategy mergeStrategy = CacheMergeStrategy.ReplaceExisting);
    }
}
