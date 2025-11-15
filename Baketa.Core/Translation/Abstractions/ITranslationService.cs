using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;

// 名前空間エイリアスの定義
using TransModels = Baketa.Core.Translation.Models;

namespace Baketa.Core.Translation.Abstractions;

/// <summary>
/// 翻訳サービスのインターフェース
/// </summary>
public interface ITranslationService
{
    /// <summary>
    /// テキストを翻訳します
    /// </summary>
    /// <param name="sourceText">翻訳元テキスト</param>
    /// <param name="sourceLanguage">元言語</param>
    /// <param name="targetLanguage">対象言語</param>
    /// <param name="context">翻訳コンテキスト（オプション）</param>
    /// <param name="preferredEngine">優先エンジン名（オプション）</param>
    /// <param name="options">翻訳オプション（オプション）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>翻訳レスポンス</returns>
    Task<TranslationResponse> TranslateAsync(
        string sourceText,
        Language sourceLanguage,
        Language targetLanguage,
        TranslationContext? context = null,
        string? preferredEngine = null,
        Dictionary<string, object?>? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 翻訳リクエストを処理します
    /// </summary>
    /// <param name="request">翻訳リクエスト</param>
    /// <param name="preferredEngine">優先エンジン名（オプション）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>翻訳レスポンス</returns>
    Task<TranslationResponse> TranslateAsync(
        TranslationRequest request,
        string? preferredEngine = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 複数のテキストを一括翻訳します
    /// </summary>
    /// <param name="requests">翻訳リクエストのリスト</param>
    /// <param name="preferredEngine">優先エンジン名（オプション）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>翻訳レスポンスのリスト</returns>
    Task<IReadOnlyList<TranslationResponse>> TranslateBatchAsync(
        IReadOnlyList<TranslationRequest> requests,
        string? preferredEngine = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 利用可能な翻訳エンジンの一覧を取得します
    /// </summary>
    /// <returns>翻訳エンジン名のリスト</returns>
    Task<IReadOnlyList<string>> GetAvailableEnginesAsync();

    /// <summary>
    /// 指定した言語ペアをサポートする翻訳エンジンを取得します
    /// </summary>
    /// <param name="sourceLang">元言語</param>
    /// <param name="targetLang">対象言語</param>
    /// <returns>サポートする翻訳エンジン名のリスト</returns>
    Task<IReadOnlyList<string>> GetSupportedEnginesForLanguagePairAsync(
        Language sourceLang,
        Language targetLang);

    /// <summary>
    /// テキストの言語を自動検出します
    /// </summary>
    /// <param name="text">検出対象テキスト</param>
    /// <param name="preferredEngine">優先エンジン名（オプション）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>検出された言語と信頼度</returns>
    Task<TransModels.LanguageDetectionResult> DetectLanguageAsync(
        string text,
        string? preferredEngine = null,
        CancellationToken cancellationToken = default);
}
