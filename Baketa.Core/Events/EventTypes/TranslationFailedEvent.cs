using System;

namespace Baketa.Core.Events.EventTypes;

/// <summary>
/// 翻訳失敗イベント
/// </summary>
/// <remarks>
/// コンストラクタ
/// </remarks>
/// <param name="sourceText">元のテキスト</param>
/// <param name="sourceLanguage">元言語コード</param>
/// <param name="targetLanguage">翻訳先言語コード</param>
/// <param name="engineName">使用された翻訳エンジン名</param>
/// <param name="exception">発生した例外</param>
/// <param name="errorMessage">エラーメッセージ</param>
public class TranslationFailedEvent(
        string? sourceText,
        string? sourceLanguage,
        string? targetLanguage,
        string? engineName,
        Exception? exception,
        string? errorMessage = null) : EventBase
    {
    /// <summary>
    /// 元のテキスト
    /// </summary>
    public string SourceText { get; } = sourceText ?? string.Empty;

    /// <summary>
    /// 元言語コード
    /// </summary>
    public string SourceLanguage { get; } = sourceLanguage ?? "auto";

    /// <summary>
    /// 翻訳先言語コード
    /// </summary>
    public string TargetLanguage { get; } = targetLanguage ?? string.Empty;

    /// <summary>
    /// 使用された翻訳エンジン名
    /// </summary>
    public string EngineName { get; } = engineName ?? "Default";

    /// <summary>
    /// 発生した例外
    /// </summary>
    public Exception? Exception { get; } = exception;

    /// <summary>
    /// エラーメッセージ
    /// </summary>
    public string ErrorMessage { get; } = errorMessage ?? exception?.Message ?? "不明なエラー";

    /// <inheritdoc />
    public override string Name => "TranslationFailed";
        
        /// <inheritdoc />
        public override string Category => "Translation";
    }
