using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Translation;

namespace Baketa.Core.Events.Translation;

/// <summary>
/// 集約チャンク翻訳失敗イベント
/// 個別翻訳が失敗した場合に発行され、全画面一括翻訳フォールバックを起動
/// </summary>
/// <remarks>
/// <para>
/// このイベントは、AggregatedChunksReadyEventHandlerで個別翻訳処理中に例外が発生した場合に発行されます。
/// イベントを購読するCoordinateBasedTranslationServiceは、フォールバックとして全画面一括翻訳を実行します。
/// </para>
/// <para>
/// 設計原則: フェイルセーフ - 個別翻訳失敗時も、ユーザーには何らかの翻訳結果を提供
/// </para>
/// </remarks>
public sealed class AggregatedChunksFailedEvent : IEvent
{
    /// <summary>
    /// イベント固有のID
    /// </summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>
    /// イベント発生タイムスタンプ
    /// </summary>
    public DateTime Timestamp { get; } = DateTime.UtcNow;

    /// <summary>
    /// 翻訳セッションID（トレース用）
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// 翻訳に失敗したチャンクのリスト
    /// </summary>
    public required List<TextChunk> FailedChunks { get; init; }

    /// <summary>
    /// ソース言語コード（例: "ja"）
    /// </summary>
    public required string SourceLanguage { get; init; }

    /// <summary>
    /// ターゲット言語コード（例: "en"）
    /// </summary>
    public required string TargetLanguage { get; init; }

    /// <summary>
    /// エラーメッセージ
    /// </summary>
    public required string ErrorMessage { get; init; }

    /// <summary>
    /// エラー例外（デバッグ用）
    /// </summary>
    public Exception? ErrorException { get; init; }

    /// <summary>
    /// イベント名
    /// </summary>
    public string Name => "AggregatedChunksFailed";

    /// <summary>
    /// イベントカテゴリ
    /// </summary>
    public string Category => "Translation";
}
