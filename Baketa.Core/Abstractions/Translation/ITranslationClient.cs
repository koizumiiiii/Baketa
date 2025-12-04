using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;

namespace Baketa.Core.Abstractions.Translation;

/// <summary>
/// Python翻訳サーバーとの通信クライアント抽象化
/// Phase 3.3: gRPC通信アーキテクチャ
///
/// 責務:
/// - 低レベルな通信プロトコル管理（gRPC）
/// - GrpcTranslationEngineAdapter から利用される
/// - Strategy パターンによる通信方式の切り替え
/// </summary>
public interface ITranslationClient
{
    /// <summary>
    /// 通信モード名（診断・ログ用）
    /// </summary>
    /// <example>"Grpc", "TCP"</example>
    string CommunicationMode { get; }

    /// <summary>
    /// テキストを翻訳します（通信レイヤー）
    /// </summary>
    /// <param name="request">翻訳リクエスト</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>翻訳レスポンス</returns>
    /// <exception cref="TranslationException">通信エラーまたは翻訳エラー</exception>
    Task<TranslationResponse> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 複数のテキストを一括翻訳します（バッチ翻訳）
    /// Issue #182: gRPCネイティブバッチ翻訳対応
    /// </summary>
    /// <param name="requests">翻訳リクエストのリスト</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>翻訳レスポンスのリスト（入力と同じ順序を保証）</returns>
    /// <exception cref="TranslationException">通信エラーまたは翻訳エラー</exception>
    Task<IReadOnlyList<TranslationResponse>> TranslateBatchAsync(
        IReadOnlyList<TranslationRequest> requests,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// クライアントの準備状態を確認します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>準備完了の場合 true</returns>
    Task<bool> IsReadyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// クライアントが正常状態かヘルスチェック
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>正常状態の場合 true</returns>
    Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default);
}
