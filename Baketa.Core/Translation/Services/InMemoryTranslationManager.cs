using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Translation.Models;
using Baketa.Core.Translation.Abstractions;
using Microsoft.Extensions.Logging;

namespace Baketa.Core.Translation.Services
{
    /// <summary>
    /// インメモリ翻訳マネージャー
    /// </summary>
    public class InMemoryTranslationManager : ITranslationManager
    {
        private readonly ITranslationRepository _repository;
        private readonly ILogger<InMemoryTranslationManager> _logger;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="repository">翻訳リポジトリ</param>
        /// <param name="logger">ロガー</param>
        public InMemoryTranslationManager(
            ITranslationRepository repository,
            ILogger<InMemoryTranslationManager> logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 翻訳レコードを保存します
        /// </summary>
        /// <param name="record">翻訳レコード</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>保存に成功した場合はtrue</returns>
        public async Task<bool> SaveRecordAsync(TranslationRecord record, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(record);
            
            try
            {
                await _repository.SaveRecordAsync(record, cancellationToken).ConfigureAwait(false);
                _logger.LogDebug("翻訳レコード {Id} を保存しました", record.Id);
                return true;
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(ex, "翻訳レコード {Id} の保存がキャンセルされました", record.Id);
                return false;
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "翻訳レコード {Id} の保存操作がキャンセルされました", record.Id);
                return false;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "翻訳レコード {Id} の保存中に無効な操作が行われました", record.Id);
                return false;
            }
            catch (TimeoutException ex)
            {
                _logger.LogError(ex, "翻訳レコード {Id} の保存がタイムアウトしました", record.Id);
                return false;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogError(ex, "翻訳レコード {Id} の保存中に予期しないエラーが発生しました", record.Id);
                return false;
            }
        }

        /// <summary>
        /// 翻訳レコードを取得します
        /// </summary>
        /// <param name="id">レコードID</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>翻訳レコード（存在しない場合はnull）</returns>
        public async Task<TranslationRecord?> GetRecordAsync(Guid id, CancellationToken cancellationToken = default)
        {
            try
            {
                var record = await _repository.GetRecordAsync(id, cancellationToken).ConfigureAwait(false);
                if (record != null)
                {
                    _logger.LogDebug("翻訳レコード {Id} を取得しました", id);
                }
                else
                {
                    _logger.LogDebug("翻訳レコード {Id} は存在しません", id);
                }
                return record;
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(ex, "翻訳レコード {Id} の取得がキャンセルされました", id);
                return null;
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "翻訳レコード {Id} の取得操作がキャンセルされました", id);
                return null;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "翻訳レコード {Id} の取得中に無効な操作が行われました", id);
                return null;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogError(ex, "翻訳レコード {Id} の取得中に予期しないエラーが発生しました", id);
                return null;
            }
        }

        /// <summary>
        /// 一致する翻訳レコードを検索します
        /// </summary>
        /// <param name="sourceText">元テキスト</param>
        /// <param name="sourceLanguage">元言語</param>
        /// <param name="targetLanguage">対象言語</param>
        /// <param name="context">翻訳コンテキスト（オプション）</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>一致する翻訳レコード（存在しない場合はnull）</returns>
        public async Task<TranslationRecord?> FindMatchingRecordAsync(
            string sourceText,
            Language sourceLanguage,
            Language targetLanguage,
            TranslationContext? context = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(sourceText);
            ArgumentNullException.ThrowIfNull(sourceLanguage);
            ArgumentNullException.ThrowIfNull(targetLanguage);
            
            try
            {
                var record = await _repository.FindMatchingRecordAsync(
                    sourceText, sourceLanguage, targetLanguage, context, cancellationToken).ConfigureAwait(false);
                
                if (record != null)
                {
                    _logger.LogDebug("テキスト「{Text}」の翻訳レコードを見つけました", sourceText);
                    
                    // 使用回数と最終使用日時を更新
                    record.UsageCount++;
                    record.LastUsedAt = DateTime.UtcNow;
                    await _repository.UpdateRecordAsync(record, cancellationToken).ConfigureAwait(false);
                }
                
                return record;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "テキスト「{Text}」の翻訳レコード検索中にエラーが発生しました", sourceText);
                return null;
            }
        }

        /// <summary>
        /// 翻訳レコードを検索します
        /// </summary>
        /// <param name="query">検索クエリ</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>検索結果</returns>
        public async Task<IReadOnlyList<TranslationRecord>> SearchRecordsAsync(
            TranslationSearchQuery query,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(query);
            
            try
            {
                var records = await _repository.SearchRecordsAsync(query, cancellationToken).ConfigureAwait(false);
                _logger.LogDebug("{Count} 件の翻訳レコードが見つかりました", records.Count);
                return records;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "翻訳レコードの検索中にエラーが発生しました");
                return Array.Empty<TranslationRecord>();
            }
        }

        /// <summary>
        /// 翻訳レコードを削除します
        /// </summary>
        /// <param name="id">レコードID</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>削除に成功した場合はtrue</returns>
        public async Task<bool> DeleteRecordAsync(Guid id, CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await _repository.DeleteRecordAsync(id, cancellationToken).ConfigureAwait(false);
                if (result)
                {
                    _logger.LogDebug("翻訳レコード {Id} を削除しました", id);
                }
                else
                {
                    _logger.LogDebug("翻訳レコード {Id} は削除できませんでした", id);
                }
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "翻訳レコード {Id} の削除中にエラーが発生しました", id);
                return false;
            }
        }

        /// <summary>
        /// 翻訳統計を取得します
        /// </summary>
        /// <param name="options">統計オプション</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>翻訳統計</returns>
        public async Task<TranslationStatistics> GetStatisticsAsync(
            StatisticsOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            options ??= new StatisticsOptions();
            
            try
            {
                var statistics = await _repository.GetStatisticsAsync(options, cancellationToken).ConfigureAwait(false);
                _logger.LogDebug("翻訳統計を取得しました（トータル: {Total}件）", statistics.TotalRecords);
                return statistics;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "翻訳統計の取得中にエラーが発生しました");
                return new TranslationStatistics();
            }
        }

        /// <summary>
        /// キャッシュをクリアします
        /// </summary>
        /// <param name="options">クリアオプション</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>クリアに成功した場合はtrue</returns>
        public async Task<bool> ClearCacheAsync(
            CacheClearOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            options ??= new CacheClearOptions { ClearAll = true };
            
            try
            {
                var result = await _repository.ClearCacheAsync(options, cancellationToken).ConfigureAwait(false);
                _logger.LogDebug("翻訳キャッシュをクリアしました（オプション: {ClearOption}）", options.ClearAll ? "すべて" : "一部");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "翻訳キャッシュのクリア中にエラーが発生しました");
                return false;
            }
        }
    }
}