namespace Baketa.Core.Abstractions.Translation;

/// <summary>
/// 翻訳戦略インターフェース
/// Issue #147 Phase 3.2: ハイブリッド統合
/// </summary>
public interface ITranslationStrategy
{
    /// <summary>
    /// この戦略が指定されたリクエストを処理できるかを判定
    /// </summary>
    bool CanHandle(TranslationStrategyContext context);
    
    /// <summary>
    /// 戦略の優先度（高い値が優先される）
    /// </summary>
    int Priority { get; }
    
    /// <summary>
    /// 翻訳を実行
    /// </summary>
    Task<TranslationResult> ExecuteAsync(
        string text, 
        string? sourceLanguage, 
        string? targetLanguage,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// バッチ翻訳を実行
    /// </summary>
    Task<IReadOnlyList<TranslationResult>> ExecuteBatchAsync(
        IReadOnlyList<string> texts,
        string? sourceLanguage,
        string? targetLanguage,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 翻訳戦略選択用コンテキスト
/// </summary>
public record TranslationStrategyContext(
    int TextCount,
    int TotalCharacterCount,
    bool IsBatchRequest,
    double? AverageTextLength = null,
    Dictionary<string, object>? Metadata = null);

/// <summary>
/// 翻訳結果
/// </summary>
public record TranslationResult(
    string OriginalText,
    string TranslatedText,
    bool Success,
    string? ErrorMessage = null,
    TimeSpan? ProcessingTime = null,
    Dictionary<string, object>? Metadata = null);