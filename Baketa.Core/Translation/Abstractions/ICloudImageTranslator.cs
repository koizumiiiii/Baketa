namespace Baketa.Core.Translation.Abstractions;

/// <summary>
/// Cloud AIによる画像翻訳エンジンのインターフェース
/// 画像を直接入力として翻訳を実行
/// </summary>
public interface ICloudImageTranslator : IAsyncDisposable
{
    /// <summary>
    /// エンジンの識別子（"primary", "secondary"）
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// 表示名
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// エンジンが利用可能かチェック
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 画像からテキストを翻訳
    /// </summary>
    Task<ImageTranslationResponse> TranslateImageAsync(
        ImageTranslationRequest request,
        CancellationToken cancellationToken = default);
}
