using System;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;

namespace Baketa.Core.Translation.Abstractions;

/// <summary>
/// クラウドAI翻訳エンジンのインターフェース
/// </summary>
public interface ICloudTranslationEngine : ITranslationEngine
{
    /// <summary>
    /// APIのベースURL
    /// </summary>
    Uri ApiBaseUrl { get; }

    /// <summary>
    /// APIキーが設定されているかどうか
    /// </summary>
    bool HasApiKey { get; }

    /// <summary>
    /// クラウドプロバイダーの種類
    /// </summary>
    CloudProviderType ProviderType { get; }

    /// <summary>
    /// 高度な翻訳機能の実行
    /// </summary>
    Task<AdvancedTranslationResponse> TranslateAdvancedAsync(
        AdvancedTranslationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// APIのステータスを確認します
    /// </summary>
    Task<ApiStatusInfo> CheckApiStatusAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// クラウドプロバイダーの種類
/// </summary>
public enum CloudProviderType
{
    /// <summary>
    /// Google Gemini
    /// </summary>
    Gemini,

    /// <summary>
    /// その他のクラウドプロバイダー
    /// </summary>
    Other
}
