using System;

namespace Baketa.Core.Abstractions.Roi;

/// <summary>
/// [Issue #293] 翻訳Gatekeeperサービスインターフェース
/// </summary>
/// <remarks>
/// Application層で使用する翻訳Gate機能のインターフェース。
/// IRoiGatekeeperをラップし、テキストソース別の状態管理を提供します。
/// </remarks>
public interface ITranslationGatekeeperService
{
    /// <summary>
    /// Gatekeeperが有効かどうか
    /// </summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// 翻訳を実行すべきかどうかを判定
    /// </summary>
    /// <param name="sourceId">テキストソースID（ウィンドウハンドルやROI IDなど）</param>
    /// <param name="currentText">現在のテキスト</param>
    /// <param name="regionInfo">オプショナルな領域情報</param>
    /// <returns>Gatekeeper判定結果</returns>
    GatekeeperDecision ShouldTranslate(
        string sourceId,
        string currentText,
        GatekeeperRegionInfo? regionInfo = null);

    /// <summary>
    /// 指定されたソースIDの前回テキストをクリア
    /// </summary>
    /// <param name="sourceId">テキストソースID</param>
    void ClearPreviousText(string sourceId);

    /// <summary>
    /// すべてのソースの前回テキストをクリア
    /// </summary>
    void ClearAllPreviousText();

    /// <summary>
    /// 翻訳結果を報告（統計記録用）
    /// </summary>
    /// <param name="decision">Gatekeeper判定結果</param>
    /// <param name="wasSuccessful">翻訳成功かどうか</param>
    /// <param name="tokensUsed">使用トークン数</param>
    void ReportTranslationResult(GatekeeperDecision decision, bool wasSuccessful, int tokensUsed);

    /// <summary>
    /// Gatekeeper統計を取得
    /// </summary>
    /// <returns>統計情報</returns>
    GatekeeperStatistics GetStatistics();

    /// <summary>
    /// Gatekeeper統計をリセット
    /// </summary>
    void ResetStatistics();
}
