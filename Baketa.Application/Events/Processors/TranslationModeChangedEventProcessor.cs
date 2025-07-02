using System.Threading.Tasks;
using Baketa.Application.Events;
using Baketa.Core.Abstractions.Events;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.Events.Processors;

/// <summary>
/// 翻訳モード変更イベントを処理するプロセッサー
/// </summary>
public sealed class TranslationModeChangedEventProcessor : IEventProcessor<TranslationModeChangedEvent>
{
    private readonly ILogger<TranslationModeChangedEventProcessor> _logger;

    /// <summary>
    /// プロセッサーを初期化します
    /// </summary>
    /// <param name="logger">ロガー</param>
    public TranslationModeChangedEventProcessor(ILogger<TranslationModeChangedEventProcessor> logger)
    {
        _logger = logger ?? throw new System.ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public int Priority => 100; // 標準優先度

    /// <inheritdoc />
    public bool SynchronousExecution => false; // 非同期実行

    /// <inheritdoc />
    public async Task HandleAsync(TranslationModeChangedEvent eventData)
    {
        // CA1062: 引数のnullチェックを実施
        ArgumentNullException.ThrowIfNull(eventData);
        
        _logger.LogInformation(
            "翻訳モードが変更されました: {PreviousMode} → {NewMode} (イベントID: {EventId})",
            eventData.PreviousMode, 
            eventData.NewMode, 
            eventData.Id);

        // ここで翻訳モード変更に関連する処理を実装
        // 例：
        // - 設定の永続化
        // - 他のサービスへの通知
        // - UI状態の更新

        // 現在は基本的なログ記録のみ実装
        await Task.Delay(1).ConfigureAwait(false); // 非同期処理のサンプル

        _logger.LogDebug(
            "翻訳モード変更イベントの処理が完了しました (イベントID: {EventId})", 
            eventData.Id);
    }
}
