using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;

namespace Baketa.Core.Abstractions.Translation;

/// <summary>
/// Python翻訳サーバーとの通信クライアント抽象化
/// UltraPhase 14.25: ハイブリッド通信アーキテクチャ (stdin/stdout vs TCP)
///
/// 責務:
/// - 低レベルな通信プロトコル管理（stdin/stdout, TCP）
/// - OptimizedPythonTranslationEngine から利用される
/// - Strategy パターンによる通信方式の切り替え
/// </summary>
public interface ITranslationClient
{
    /// <summary>
    /// 通信モード名（診断・ログ用）
    /// </summary>
    /// <example>"StdinStdout", "TCP"</example>
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