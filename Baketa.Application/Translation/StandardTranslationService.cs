using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Abstractions;
using Baketa.Core.Translation.Models;
using Microsoft.Extensions.Logging;
using TransModels = Baketa.Core.Translation.Models;

namespace Baketa.Application.Translation;

/// <summary>
/// 標準翻訳サービス
/// 翻訳パイプラインを使用してITranslationServiceを実装します
/// </summary>
/// <remarks>
/// 標準翻訳サービスのコンストラクタ
/// </remarks>
/// <param name="logger">ロガー</param>
/// <param name="pipeline">翻訳パイプライン</param>
/// <param name="engineDiscovery">翻訳エンジン検出サービス</param>
public sealed class StandardTranslationService(
        ILogger<StandardTranslationService> logger,
        ITranslationPipeline pipeline,
        ITranslationEngineDiscovery engineDiscovery) : ITranslationService
{
    private readonly ILogger<StandardTranslationService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly ITranslationPipeline _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    private readonly ITranslationEngineDiscovery _engineDiscovery = engineDiscovery ?? throw new ArgumentNullException(nameof(engineDiscovery));

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
    public async Task<TranslationResponse> TranslateAsync(
            string sourceText,
            Language sourceLanguage,
            Language targetLanguage,
            TranslationContext? context = null,
            string? preferredEngine = null,
            Dictionary<string, object?>? options = null,
            CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            throw new ArgumentException("翻訳元テキストが無効です。", nameof(sourceText));
        }

        var request = new TranslationRequest
        {
            SourceText = sourceText,
            SourceLanguage = sourceLanguage,
            TargetLanguage = targetLanguage,
            Context = context
        };

        // オプションが指定されていれば追加
        if (options != null)
        {
            // Dictionaryのキー/値ペアをコレクション初期化子を使用して追加
            foreach (var (key, value) in options)
            {
                request.Options[key] = value;
            }
        }

        return await _pipeline.ExecuteAsync(request, preferredEngine, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 翻訳リクエストを処理します
    /// </summary>
    /// <param name="request">翻訳リクエスト</param>
    /// <param name="preferredEngine">優先エンジン名（オプション）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>翻訳レスポンス</returns>
    public async Task<TranslationResponse> TranslateAsync(
        TranslationRequest request,
        string? preferredEngine = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request, nameof(request));

        return await _pipeline.ExecuteAsync(request, preferredEngine, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 複数のテキストを一括翻訳します
    /// </summary>
    /// <param name="requests">翻訳リクエストのリスト</param>
    /// <param name="preferredEngine">優先エンジン名（オプション）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>翻訳レスポンスのリスト</returns>
    public async Task<IReadOnlyList<TranslationResponse>> TranslateBatchAsync(
        IReadOnlyList<TranslationRequest> requests,
        string? preferredEngine = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests, nameof(requests));

        if (requests.Count == 0)
        {
            return Enumerable.Empty<TranslationResponse>().ToArray();
        }

        return await _pipeline.ExecuteBatchAsync(requests, preferredEngine, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 利用可能な翻訳エンジンの一覧を取得します
    /// </summary>
    /// <returns>翻訳エンジン名のリスト</returns>
    public async Task<IReadOnlyList<string>> GetAvailableEnginesAsync()
    {
        return await _engineDiscovery.GetAvailableEngineNamesAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 指定した言語ペアをサポートする翻訳エンジンを取得します
    /// </summary>
    /// <param name="sourceLang">元言語</param>
    /// <param name="targetLang">対象言語</param>
    /// <returns>サポートする翻訳エンジン名のリスト</returns>
    public async Task<IReadOnlyList<string>> GetSupportedEnginesForLanguagePairAsync(
        Language sourceLang,
        Language targetLang)
    {
        ArgumentNullException.ThrowIfNull(sourceLang, nameof(sourceLang));
        ArgumentNullException.ThrowIfNull(targetLang, nameof(targetLang));

        return await _engineDiscovery.GetSupportedEnginesForLanguagePairAsync(sourceLang, targetLang).ConfigureAwait(false);
    }

    /// <summary>
    /// テキストの言語を自動検出します
    /// </summary>
    /// <param name="text">検出対象テキスト</param>
    /// <param name="preferredEngine">優先エンジン名（オプション）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>検出された言語と信頼度</returns>
    public async Task<TransModels.LanguageDetectionResult> DetectLanguageAsync(
        string text,
        string? preferredEngine = null,
        CancellationToken cancellationToken = default)
    {
        // 非同期のawaitがないという警告を解消するために少なくとも一つのawaitを追加
        await Task.CompletedTask.ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("検出対象テキストが無効です。", nameof(text));
        }

        // 言語検出機能は現在実装中で、APIが変更されています。
        // 一時的に固定値を返すように修正
        _logger.LogWarning("言語検出機能は現在実装中です。一時的に固定値を返します。");

        // 一時的な言語検出結果を返す
        return new TransModels.LanguageDetectionResult
        {
            DetectedLanguage = new TransModels.Language { Code = "auto", DisplayName = "自動検出" },
            Confidence = 0.5f,
            EngineName = "DummyDetector"
        };
    }
}
