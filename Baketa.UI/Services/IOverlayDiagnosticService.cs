using System;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Translation;

namespace Baketa.UI.Services;

/// <summary>
/// オーバーレイ診断イベント発行を担当するサービス
/// Phase 4.1: InPlaceTranslationOverlayManagerから診断ロジックを抽出
/// </summary>
public interface IOverlayDiagnosticService
{
    /// <summary>
    /// オーバーレイ表示開始時の診断イベントを発行します
    /// </summary>
    /// <param name="textChunk">テキストチャンク</param>
    /// <param name="sessionId">セッションID</param>
    /// <param name="isInitialized">初期化状態</param>
    /// <param name="isDisposed">破棄状態</param>
    Task PublishOverlayStartedAsync(
        TextChunk textChunk,
        string sessionId,
        bool isInitialized,
        bool isDisposed);

    /// <summary>
    /// オーバーレイ表示成功時の診断イベントを発行します
    /// </summary>
    /// <param name="textChunk">テキストチャンク</param>
    /// <param name="sessionId">セッションID</param>
    /// <param name="processingTimeMs">処理時間（ミリ秒）</param>
    /// <param name="activeOverlaysCount">アクティブオーバーレイ数</param>
    /// <param name="isUpdate">更新処理かどうか</param>
    Task PublishOverlaySuccessAsync(
        TextChunk textChunk,
        string sessionId,
        long processingTimeMs,
        int activeOverlaysCount,
        bool isUpdate);

    /// <summary>
    /// オーバーレイ表示失敗時の診断イベントを発行します
    /// </summary>
    /// <param name="textChunk">テキストチャンク</param>
    /// <param name="sessionId">セッションID</param>
    /// <param name="processingTimeMs">処理時間（ミリ秒）</param>
    /// <param name="exception">発生した例外</param>
    /// <param name="isInitialized">初期化状態</param>
    /// <param name="isDisposed">破棄状態</param>
    /// <param name="activeOverlaysCount">アクティブオーバーレイ数</param>
    Task PublishOverlayFailedAsync(
        TextChunk textChunk,
        string sessionId,
        long processingTimeMs,
        Exception exception,
        bool isInitialized,
        bool isDisposed,
        int activeOverlaysCount);
}
