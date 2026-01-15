using Baketa.Core.Translation.Models;

namespace Baketa.Core.Translation.Abstractions;

/// <summary>
/// Cloud AI翻訳のトークン消費を追跡するインターフェース
/// </summary>
public interface ITokenConsumptionTracker
{
    /// <summary>
    /// トークン使用量を記録する
    /// </summary>
    /// <param name="tokensUsed">使用したトークン数</param>
    /// <param name="providerId">プロバイダーID（"primary", "secondary"等）</param>
    /// <param name="usageType">使用タイプ（入力/出力）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task RecordUsageAsync(
        int tokensUsed,
        string providerId,
        TokenUsageType usageType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 月間使用量情報を取得する
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>月間トークン使用情報</returns>
    Task<TokenUsageInfo> GetMonthlyUsageAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 残りトークン数を取得する
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>残りトークン数（-1は無制限）</returns>
    Task<long> GetRemainingTokensAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 月間上限に達しているかチェックする
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>上限に達している場合true</returns>
    Task<bool> IsLimitExceededAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 使用率（パーセンテージ）を取得する
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>使用率（0.0-1.0）</returns>
    Task<double> GetUsagePercentageAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 画像のトークン数を推定する
    /// </summary>
    /// <param name="width">画像幅（ピクセル）</param>
    /// <param name="height">画像高さ（ピクセル）</param>
    /// <param name="providerId">プロバイダーID</param>
    /// <returns>推定トークン数</returns>
    int EstimateImageTokens(int width, int height, string providerId);

    /// <summary>
    /// 使用量アラート閾値に達しているかチェックする
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>アラートレベル（None, Warning80, Warning90, Exceeded）</returns>
    Task<UsageAlertLevel> CheckAlertLevelAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 使用量データをリセットする（月初め自動実行用）
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task ResetMonthlyUsageAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// [Issue #296] サーバーサイドの月間使用状況でローカルを同期する
    /// </summary>
    /// <param name="serverUsage">サーバーから取得した月間使用状況</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <remarks>
    /// サーバーの値が正（authoritative）として、ローカルのトークン使用量を上書きします。
    /// </remarks>
    Task SyncFromServerAsync(ServerMonthlyUsage serverUsage, CancellationToken cancellationToken = default);
}

/// <summary>
/// トークン使用タイプ
/// </summary>
public enum TokenUsageType
{
    /// <summary>入力トークン（プロンプト+画像）</summary>
    Input,

    /// <summary>出力トークン（翻訳結果）</summary>
    Output,

    /// <summary>合計（APIレスポンスから取得）</summary>
    Total
}

/// <summary>
/// 使用量アラートレベル
/// </summary>
public enum UsageAlertLevel
{
    /// <summary>アラートなし</summary>
    None,

    /// <summary>80%到達警告</summary>
    Warning80,

    /// <summary>90%到達警告</summary>
    Warning90,

    /// <summary>100%到達（上限超過）</summary>
    Exceeded
}
