using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;

namespace Baketa.Core.Abstractions.Translation
{
    /// <summary>
    /// 翻訳サービスの機能を定義するインターフェース
    /// アプリケーション層でこのサービスを使用して、適切なエンジンを選択し翻訳を実行します
    /// </summary>
    public interface ITranslationService
    {
        /// <summary>
        /// 利用可能な翻訳エンジンを取得します
        /// </summary>
        /// <returns>利用可能な翻訳エンジンのコレクション</returns>
        IReadOnlyList<ITranslationEngine> GetAvailableEngines();

        /// <summary>
        /// 現在アクティブな翻訳エンジンを取得します
        /// </summary>
        ITranslationEngine ActiveEngine { get; }

        /// <summary>
        /// 指定された名前のエンジンをアクティブにします
        /// このメソッドはユーザー設定変更や外部イベントによって呼び出されます
        /// </summary>
        /// <param name="engineName">アクティブにするエンジン名</param>
        /// <returns>成功すればtrue</returns>
        Task<bool> SetActiveEngineAsync(string engineName);

        /// <summary>
        /// テキストを翻訳します
        /// 内部的に現在のアクティブエンジンを使用します
        /// </summary>
        /// <param name="text">翻訳元テキスト</param>
        /// <param name="sourceLang">元言語</param>
        /// <param name="targetLang">対象言語</param>
        /// <param name="context">翻訳コンテキスト（オプション）</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>翻訳結果</returns>
        Task<TranslationResponse> TranslateAsync(
            string text,
            Language sourceLang,
            Language targetLang,
            string? context = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 複数のテキストをバッチ翻訳します
        /// 内部的に現在のアクティブエンジンを使用します
        /// </summary>
        /// <param name="texts">翻訳元テキストのコレクション</param>
        /// <param name="sourceLang">元言語</param>
        /// <param name="targetLang">対象言語</param>
        /// <param name="context">翻訳コンテキスト（オプション）</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>翻訳結果のコレクション</returns>
        Task<IReadOnlyList<TranslationResponse>> TranslateBatchAsync(
            IReadOnlyList<string> texts,
            Language sourceLang,
            Language targetLang,
            string? context = null,
            CancellationToken cancellationToken = default);
    }
}
