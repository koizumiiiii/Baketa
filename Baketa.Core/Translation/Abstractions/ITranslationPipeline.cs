using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;

namespace Baketa.Core.Translation.Abstractions;

    /// <summary>
    /// 翻訳パイプラインのインターフェース
    /// 翻訳リクエストの処理フローを定義します
    /// </summary>
    public interface ITranslationPipeline
    {
        /// <summary>
        /// 翻訳パイプラインを実行します
        /// </summary>
        /// <param name="request">翻訳リクエスト</param>
        /// <param name="preferredEngine">優先エンジン名（オプション）</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>翻訳レスポンス</returns>
        Task<TranslationResponse> ExecuteAsync(
            TranslationRequest request,
            string? preferredEngine = null,
            CancellationToken cancellationToken = default);
            
        /// <summary>
        /// 複数の翻訳リクエストをバッチ処理します
        /// </summary>
        /// <param name="requests">翻訳リクエストのリスト</param>
        /// <param name="preferredEngine">優先エンジン名（オプション）</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>翻訳レスポンスのリスト</returns>
        Task<IReadOnlyList<TranslationResponse>> ExecuteBatchAsync(
            IReadOnlyList<TranslationRequest> requests,
            string? preferredEngine = null,
            CancellationToken cancellationToken = default);
    }
