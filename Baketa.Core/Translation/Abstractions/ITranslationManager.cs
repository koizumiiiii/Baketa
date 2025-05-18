using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;

namespace Baketa.Core.Translation.Abstractions;

    /// <summary>
    /// 翻訳結果管理インターフェース
    /// </summary>
    public interface ITranslationManager
    {
        /// <summary>
        /// 翻訳レコードを保存します
        /// </summary>
        /// <param name="record">翻訳レコード</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>保存に成功した場合はtrue</returns>
        Task<bool> SaveRecordAsync(TranslationRecord record, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// 翻訳レコードを取得します
        /// </summary>
        /// <param name="id">レコードID</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>翻訳レコード（存在しない場合はnull）</returns>
        Task<TranslationRecord?> GetRecordAsync(Guid id, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// 一致する翻訳レコードを検索します
        /// </summary>
        /// <param name="sourceText">元テキスト</param>
        /// <param name="sourceLanguage">元言語</param>
        /// <param name="targetLanguage">対象言語</param>
        /// <param name="context">翻訳コンテキスト（オプション）</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>一致する翻訳レコード（存在しない場合はnull）</returns>
        Task<TranslationRecord?> FindMatchingRecordAsync(
            string sourceText,
            Language sourceLanguage,
            Language targetLanguage,
            TranslationContext? context = null,
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// 翻訳レコードを検索します
        /// </summary>
        /// <param name="query">検索クエリ</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>検索結果</returns>
        Task<IReadOnlyList<TranslationRecord>> SearchRecordsAsync(
            TranslationSearchQuery query,
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// 翻訳レコードを削除します
        /// </summary>
        /// <param name="id">レコードID</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>削除に成功した場合はtrue</returns>
        Task<bool> DeleteRecordAsync(Guid id, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// 翻訳統計を取得します
        /// </summary>
        /// <param name="options">統計オプション</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>翻訳統計</returns>
        Task<TranslationStatistics> GetStatisticsAsync(
            StatisticsOptions? options = null,
            CancellationToken cancellationToken = default);
        
        /// <summary>
        /// キャッシュをクリアします
        /// </summary>
        /// <param name="options">クリアオプション</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>クリアに成功した場合はtrue</returns>
        Task<bool> ClearCacheAsync(
            CacheClearOptions? options = null,
            CancellationToken cancellationToken = default);
    }
