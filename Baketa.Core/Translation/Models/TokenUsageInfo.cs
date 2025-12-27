namespace Baketa.Core.Translation.Models;

/// <summary>
/// 月間トークン使用情報
/// </summary>
public sealed class TokenUsageInfo
{
    /// <summary>
    /// 今月の合計使用トークン数
    /// </summary>
    public long TotalTokensUsed { get; init; }

    /// <summary>
    /// 月間上限トークン数（-1は無制限）
    /// </summary>
    public long MonthlyLimit { get; init; }

    /// <summary>
    /// 使用量リセット日（次月1日 00:00 UTC）
    /// </summary>
    public DateTime ResetDate { get; init; }

    /// <summary>
    /// 集計期間の開始日（当月1日 00:00 UTC）
    /// </summary>
    public DateTime PeriodStartDate { get; init; }

    /// <summary>
    /// プロバイダー別使用量
    /// </summary>
    public IReadOnlyDictionary<string, long> UsageByProvider { get; init; }
        = new Dictionary<string, long>();

    /// <summary>
    /// 入力トークン使用量
    /// </summary>
    public long InputTokensUsed { get; init; }

    /// <summary>
    /// 出力トークン使用量
    /// </summary>
    public long OutputTokensUsed { get; init; }

    /// <summary>
    /// 最終更新日時
    /// </summary>
    public DateTime LastUpdated { get; init; }

    /// <summary>
    /// 使用率（0.0-1.0）を計算する
    /// </summary>
    public double UsagePercentage =>
        MonthlyLimit <= 0 ? 0.0 : Math.Min(1.0, (double)TotalTokensUsed / MonthlyLimit);

    /// <summary>
    /// 残りトークン数を計算する
    /// </summary>
    public long RemainingTokens =>
        MonthlyLimit <= 0 ? -1 : Math.Max(0, MonthlyLimit - TotalTokensUsed);

    /// <summary>
    /// 上限に達しているかチェックする
    /// </summary>
    public bool IsLimitExceeded =>
        MonthlyLimit > 0 && TotalTokensUsed >= MonthlyLimit;

    /// <summary>
    /// 空の使用情報を作成する
    /// </summary>
    public static TokenUsageInfo CreateEmpty(long monthlyLimit)
    {
        var now = DateTime.UtcNow;
        var periodStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var resetDate = periodStart.AddMonths(1);

        return new TokenUsageInfo
        {
            TotalTokensUsed = 0,
            MonthlyLimit = monthlyLimit,
            PeriodStartDate = periodStart,
            ResetDate = resetDate,
            InputTokensUsed = 0,
            OutputTokensUsed = 0,
            LastUpdated = now,
            UsageByProvider = new Dictionary<string, long>()
        };
    }
}

/// <summary>
/// トークン使用量記録（永続化用）
/// </summary>
public sealed class TokenUsageRecord
{
    /// <summary>
    /// 記録ID
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 記録日時（UTC）
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 使用トークン数
    /// </summary>
    public int TokensUsed { get; init; }

    /// <summary>
    /// プロバイダーID
    /// </summary>
    public string ProviderId { get; init; } = string.Empty;

    /// <summary>
    /// 使用タイプ
    /// </summary>
    public string UsageType { get; init; } = string.Empty;

    /// <summary>
    /// 年月（集計キー: "2025-01"形式）
    /// </summary>
    public string YearMonth { get; init; } = string.Empty;
}

/// <summary>
/// 月間使用量サマリー（永続化用）
/// </summary>
public sealed class MonthlyUsageSummary
{
    /// <summary>
    /// 年月（"2025-01"形式）
    /// </summary>
    public string YearMonth { get; set; } = string.Empty;

    /// <summary>
    /// 合計トークン使用量
    /// </summary>
    public long TotalTokens { get; set; }

    /// <summary>
    /// 入力トークン使用量
    /// </summary>
    public long InputTokens { get; set; }

    /// <summary>
    /// 出力トークン使用量
    /// </summary>
    public long OutputTokens { get; set; }

    /// <summary>
    /// プロバイダー別使用量
    /// </summary>
    public Dictionary<string, long> ByProvider { get; set; } = [];

    /// <summary>
    /// 最終更新日時
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
