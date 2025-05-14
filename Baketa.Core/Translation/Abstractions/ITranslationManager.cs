using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;

namespace Baketa.Core.Translation.Abstractions
{
    /// <summary>
    /// 翻訳結果管理インターフェース
    /// </summary>
    public interface ITranslationManager
    {
        /// <summary>
        /// 翻訳結果を保存します
        /// </summary>
        /// <param name="translationResponse">翻訳レスポンス</param>
        /// <param name="context">翻訳コンテキスト（オプション）</param>
        /// <returns>保存された翻訳レコード</returns>
        Task<TranslationRecord> SaveTranslationAsync(
            TranslationResponse translationResponse, 
            TranslationContext? context = null);
            
        /// <summary>
        /// キャッシュから翻訳結果を取得します
        /// </summary>
        /// <param name="sourceText">元テキスト</param>
        /// <param name="sourceLang">元言語</param>
        /// <param name="targetLang">対象言語</param>
        /// <param name="context">翻訳コンテキスト（オプション）</param>
        /// <returns>キャッシュにある場合は翻訳レコード、なければnull</returns>
        Task<TranslationRecord?> GetTranslationAsync(
            string sourceText, 
            Language sourceLang, 
            Language targetLang, 
            TranslationContext? context = null);
            
        /// <summary>
        /// 複数のテキストのキャッシュ状態を一括確認します
        /// </summary>
        /// <param name="sourceTexts">元テキストのコレクション</param>
        /// <param name="sourceLang">元言語</param>
        /// <param name="targetLang">対象言語</param>
        /// <param name="context">翻訳コンテキスト（オプション）</param>
        /// <returns>キャッシュ状態マップ（key=sourceText, value=翻訳レコードまたはnull）</returns>
        Task<IReadOnlyDictionary<string, TranslationRecord?>> GetTranslationStatusAsync(
            IReadOnlyCollection<string> sourceTexts, 
            Language sourceLang, 
            Language targetLang, 
            TranslationContext? context = null);
            
        /// <summary>
        /// 翻訳結果を更新します
        /// </summary>
        /// <param name="recordId">レコードID</param>
        /// <param name="newTranslatedText">新しい翻訳テキスト</param>
        /// <returns>更新が成功すればtrue</returns>
        Task<bool> UpdateTranslationAsync(Guid recordId, string newTranslatedText);
        
        /// <summary>
        /// 翻訳結果を削除します
        /// </summary>
        /// <param name="recordId">レコードID</param>
        /// <returns>削除が成功すればtrue</returns>
        Task<bool> DeleteTranslationAsync(Guid recordId);
        
        /// <summary>
        /// 翻訳履歴を検索します
        /// </summary>
        /// <param name="query">検索クエリ</param>
        /// <returns>検索結果のコレクション</returns>
        Task<IReadOnlyList<TranslationRecord>> SearchTranslationsAsync(TranslationSearchQuery query);
        
        /// <summary>
        /// 翻訳統計を取得します
        /// </summary>
        /// <param name="options">統計オプション</param>
        /// <returns>翻訳統計データ</returns>
        Task<TranslationStatistics> GetStatisticsAsync(StatisticsOptions options);
        
        /// <summary>
        /// キャッシュをクリアします
        /// </summary>
        /// <param name="options">クリアオプション</param>
        /// <returns>クリアされたレコード数</returns>
        Task<int> ClearCacheAsync(CacheClearOptions options);
        
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
