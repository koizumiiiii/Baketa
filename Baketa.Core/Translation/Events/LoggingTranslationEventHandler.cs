using System;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Translation.Common;
using Baketa.Core.Translation.Events;
using Microsoft.Extensions.Logging;

namespace Baketa.Core.Translation.Events;

/// <summary>
/// 翻訳イベントをログに記録するハンドラー
/// </summary>
/// <remarks>
/// コンストラクタ
/// </remarks>
/// <param name="logger">ロガー</param>
public class LoggingTranslationEventHandler(ILogger<LoggingTranslationEventHandler> logger) :
        IEventProcessor<TranslationStartedEvent>,
        IEventProcessor<TranslationCompletedEvent>,
        IEventProcessor<TranslationErrorEvent>,
        ITranslationEventHandler<TranslationStartedEvent>,
        ITranslationEventHandler<TranslationCompletedEvent>,
        ITranslationEventHandler<TranslationErrorEvent>
{
    private readonly ILogger<LoggingTranslationEventHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// ハンドラーの優先度
    /// </summary>
    public int Priority => 0;

    /// <summary>
    /// 同期実行かどうか
    /// </summary>
    public bool SynchronousExecution => false;

    /// <summary>
    /// 翻訳開始イベントを処理します
    /// </summary>
    /// <param name="eventData">イベント</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>完了タスク</returns>
    public Task HandleAsync(TranslationStartedEvent eventData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        _logger.LogInformation(
            "翻訳開始: ソーステキスト「{SourceText}」、言語: {SourceLanguage}→{TargetLanguage}、エンジン: {EngineName}",
            TruncateText(eventData.SourceText, 30),
            eventData.SourceLanguage,
            eventData.TargetLanguage,
            eventData.RequestId);


        return Task.CompletedTask;
    }

    /// <summary>
    /// 翻訳完了イベントを処理します
    /// </summary>
    /// <param name="eventData">イベント</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>完了タスク</returns>
    public Task HandleAsync(TranslationCompletedEvent eventData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        _logger.LogInformation(
            "翻訳完了: 「{SourceText}」→「{TranslatedText}」、言語: {SourceLanguage}→{TargetLanguage}、エンジン: {EngineName}、処理時間: {ProcessingTimeMs}ms",
            TruncateText(eventData.SourceText, 30),
            TruncateText(eventData.TranslatedText ?? "N/A", 30),
            eventData.SourceLanguage,
            eventData.TargetLanguage,
            eventData.TranslationEngine,
            eventData.ProcessingTimeMs);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 翻訳エラーイベントを処理します
    /// </summary>
    /// <param name="eventData">イベント</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>完了タスク</returns>
    public Task HandleAsync(TranslationErrorEvent eventData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        _logger.LogError(
            "翻訳エラー: ソーステキスト「{SourceText}」、言語: {SourceLanguage}→{TargetLanguage}、エンジン: {EngineName}、エラー: {ErrorMessage}",
            TruncateText(eventData.SourceText, 30),
            eventData.SourceLanguage,
            eventData.TargetLanguage,
            eventData.TranslationEngine ?? "Unknown",
            eventData.ErrorMessage);

        return Task.CompletedTask;
    }

    /// <summary>
    /// テキストを指定された長さに切り詰めます
    /// </summary>
    /// <param name="text">テキスト</param>
    /// <param name="maxLength">最大長</param>
    /// <returns>切り詰められたテキスト</returns>
    private static string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text;
        }

        return text[..maxLength] + "...";
    }
}
