namespace Baketa.Core.Events.EventTypes;

/// <summary>
/// 最初の翻訳結果受信イベント（ローディング終了用）
/// 翻訳セッション開始後、最初の有効な翻訳結果が表示された時に発行される
/// </summary>
public class FirstTranslationResultReceivedEvent : EventBase
{
    /// <inheritdoc />
    public override string Name => "FirstTranslationResultReceived";

    /// <inheritdoc />
    public override string Category => "Translation";
}
