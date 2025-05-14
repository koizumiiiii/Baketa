using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;

namespace Baketa.Core.Translation.Abstractions
{
    /// <summary>
    /// 翻訳結果リポジトリインターフェース
    /// </summary>
    public interface ITranslationRepository
    {
        /// <summary>
        /// 翻訳レコードを保存します
        /// </summary>
        /// <param name="record">翻訳レコード</param>
        /// <returns>保存が成功すればtrue</returns>
        Task<bool> SaveRecordAsync(TranslationRecord record);
        
        /// <summary>
        /// 翻訳レコードを取得します
        /// </summary>
        /// <param name="id">レコードID</param>
        /// <returns>レコードが存在すればそのレコード、なければnull</returns>
        Task<TranslationRecord?> GetRecordAsync(Guid id);
        
        /// <summary>
        /// 翻訳レコードを検索します
        /// </summary>
        /// <param name="sourceText">元テキスト</param>
        /// <param name="sourceLang">元言語</param>
        /// <param name="targetLang">対象言語</param>
        /// <param name="context">翻訳コンテキスト（オプション）</param>
        /// <returns>検索結果のコレクション</returns>
        Task<IReadOnlyList<TranslationRecord>> FindRecordsAsync(
            string sourceText, 
            Language sourceLang, 
            Language targetLang, 
            TranslationContext? context = null);
            
        /// <summary>
        /// 翻訳レコードを更新します
        /// </summary>
        /// <param name="record">更新するレコード</param>
        /// <returns>更新が成功すればtrue</returns>
        Task<bool> UpdateRecordAsync(TranslationRecord record);
        
        /// <summary>
        /// 翻訳レコードを削除します
        /// </summary>
        /// <param name="id">レコードID</param>
        /// <returns>削除が成功すればtrue</returns>
        Task<bool> DeleteRecordAsync(Guid id);
        
        /// <summary>
        /// 条件に一致するレコードを検索します
        /// </summary>
        /// <param name="query">検索クエリ</param>
        /// <returns>検索結果のコレクション</returns>
        Task<IReadOnlyList<TranslationRecord>> SearchRecordsAsync(TranslationSearchQuery query);
        
        /// <summary>
        /// 統計情報を取得します
        /// </summary>
        /// <param name="options">統計オプション</param>
        /// <returns>統計データ</returns>
        Task<TranslationStatistics> GetStatisticsAsync(StatisticsOptions options);
        
        /// <summary>
        /// 条件に一致するレコードを削除します
        /// </summary>
        /// <param name="options">削除オプション</param>
        /// <returns>削除されたレコード数</returns>
        Task<int> DeleteRecordsAsync(CacheClearOptions options);
        
        /// <summary>
        /// データベースをエクスポートします
        /// </summary>
        /// <param name="filePath">エクスポート先ファイルパス</param>
        /// <returns>エクスポートが成功すればtrue</returns>
        Task<bool> ExportDatabaseAsync(string filePath);
        
        /// <summary>
        /// データベースをインポートします
        /// </summary>
        /// <param name="filePath">インポート元ファイルパス</param>
        /// <param name="mergeStrategy">マージ戦略</param>
        /// <returns>インポートが成功すればtrue</returns>
        Task<bool> ImportDatabaseAsync(string filePath, MergeStrategy mergeStrategy);
    }
}
