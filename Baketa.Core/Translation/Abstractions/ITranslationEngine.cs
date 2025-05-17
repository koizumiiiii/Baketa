using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;

// 名前空間エイリアスの定義

using TransModels = Baketa.Core.Translation.Models;

namespace Baketa.Core.Translation.Abstractions
{
    /// <summary>
    /// 翻訳エンジンの基本機能を定義するインターフェース
    /// </summary>
    public interface ITranslationEngine : IDisposable
    {
        /// <summary>
        /// 翻訳エンジンの名称
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// 翻訳エンジンの説明
        /// </summary>
        string Description { get; }
        
        /// <summary>
        /// ネットワーク接続が必要かどうか
        /// </summary>
        bool RequiresNetwork { get; }
        
        /// <summary>
        /// テキストを翻訳します
        /// </summary>
        /// <param name="request">翻訳リクエスト</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>翻訳レスポンス</returns>
        Task<TranslationResponse> TranslateAsync(
            TranslationRequest request, 
            CancellationToken cancellationToken = default);
            
        /// <summary>
        /// 複数のテキストを一括翻訳します
        /// </summary>
        /// <param name="requests">翻訳リクエストのリスト</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>翻訳レスポンスのリスト</returns>
        Task<IReadOnlyList<TranslationResponse>> TranslateBatchAsync(
            IReadOnlyList<TranslationRequest> requests, 
            CancellationToken cancellationToken = default);
            
        /// <summary>
        /// サポートしている言語ペアを取得します
        /// </summary>
        /// <returns>サポートされている言語ペアのコレクション</returns>
        Task<IReadOnlyCollection<LanguagePair>> GetSupportedLanguagePairsAsync();
        
        /// <summary>
        /// 指定した言語ペアがサポートされているか確認します
        /// </summary>
        /// <param name="languagePair">言語ペア</param>
        /// <returns>サポートされている場合はtrue</returns>
        Task<bool> SupportsLanguagePairAsync(LanguagePair languagePair);
        
        /// <summary>
        /// 翻訳エンジンが準備完了しているか確認します
        /// </summary>
        /// <returns>準備完了している場合はtrue</returns>
        Task<bool> IsReadyAsync();
        
        /// <summary>
        /// 翻訳エンジンを初期化します
        /// </summary>
        /// <returns>初期化に成功した場合はtrue</returns>
        Task<bool> InitializeAsync();
        
        /// <summary>
        /// テキストの言語を自動検出します
        /// </summary>
        /// <param name="text">検出対象テキスト</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>検出された言語と信頼度</returns>
        Task<TransModels.LanguageDetectionResult> DetectLanguageAsync(
            string text,
            CancellationToken cancellationToken = default);
    }
}
