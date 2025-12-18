using Baketa.Core.Events;

namespace Baketa.Core.License.Events;

/// <summary>
/// トークン使用量警告レベル
/// </summary>
public enum TokenWarningLevel
{
    /// <summary>通知（50%使用）</summary>
    Notice,

    /// <summary>警告（75%使用）</summary>
    Warning,

    /// <summary>危険（90%使用）</summary>
    Critical,

    /// <summary>超過（100%使用）</summary>
    Exceeded
}

/// <summary>
/// トークン使用量警告イベント
/// </summary>
public sealed class TokenUsageWarningEvent : EventBase
{
    /// <summary>
    /// トークン使用量警告イベントを作成
    /// </summary>
    /// <param name="currentUsage">現在の使用量</param>
    /// <param name="monthlyLimit">月間上限</param>
    /// <param name="warningLevel">警告レベル</param>
    public TokenUsageWarningEvent(
        long currentUsage,
        long monthlyLimit,
        TokenWarningLevel warningLevel)
    {
        CurrentUsage = currentUsage;
        MonthlyLimit = monthlyLimit;
        WarningLevel = warningLevel;
        UsagePercentage = monthlyLimit > 0 ? (double)currentUsage / monthlyLimit * 100 : 0;
    }

    /// <inheritdoc />
    public override string Name => "TokenUsageWarning";

    /// <inheritdoc />
    public override string Category => "License";

    /// <summary>
    /// 現在の使用量
    /// </summary>
    public long CurrentUsage { get; }

    /// <summary>
    /// 月間上限
    /// </summary>
    public long MonthlyLimit { get; }

    /// <summary>
    /// 警告レベル
    /// </summary>
    public TokenWarningLevel WarningLevel { get; }

    /// <summary>
    /// 使用率（%）
    /// </summary>
    public double UsagePercentage { get; }

    /// <summary>
    /// 残りトークン数
    /// </summary>
    public long RemainingTokens => Math.Max(0, MonthlyLimit - CurrentUsage);

    /// <summary>
    /// 警告メッセージを取得
    /// </summary>
    public string GetWarningMessage() => WarningLevel switch
    {
        TokenWarningLevel.Notice => $"クラウドAI翻訳のトークンを50%使用しました（残り: {RemainingTokens:N0}）",
        TokenWarningLevel.Warning => $"クラウドAI翻訳のトークンを75%使用しました（残り: {RemainingTokens:N0}）",
        TokenWarningLevel.Critical => $"クラウドAI翻訳のトークンを90%使用しました（残り: {RemainingTokens:N0}）",
        TokenWarningLevel.Exceeded => "今月のクラウドAI翻訳上限に達しました。ローカル翻訳に切り替わります。",
        _ => "トークン使用量に関する警告"
    };
}

/// <summary>
/// トークン使用量警告イベント引数（EventHandler用）
/// </summary>
public sealed record TokenUsageWarningEventArgs(
    long CurrentUsage,
    long MonthlyLimit,
    TokenWarningLevel Level);
