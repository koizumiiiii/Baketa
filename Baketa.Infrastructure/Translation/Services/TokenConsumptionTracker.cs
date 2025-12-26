using Baketa.Core.License.Models;
using Baketa.Core.Translation.Abstractions;
using Baketa.Core.Translation.Models;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Services;

/// <summary>
/// Cloud AI翻訳のトークン消費追跡実装
/// </summary>
public sealed class TokenConsumptionTracker : ITokenConsumptionTracker
{
    private readonly IEngineAccessController _accessController;
    private readonly ITokenUsageRepository _repository;
    private readonly ILogger<TokenConsumptionTracker> _logger;

    // 画像トークン推定係数（プロバイダー別）
    private static readonly Dictionary<string, double> ImageTokenCoefficients = new()
    {
        ["primary"] = 750.0,    // Gemini: tokens ≈ (width × height) / 750
        ["secondary"] = 435.2,  // OpenAI: tokens ≈ (width × height) / 512 × 85 ≒ / 6.02
        ["default"] = 750.0
    };

    public TokenConsumptionTracker(
        IEngineAccessController accessController,
        ITokenUsageRepository repository,
        ILogger<TokenConsumptionTracker> logger)
    {
        _accessController = accessController ?? throw new ArgumentNullException(nameof(accessController));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task RecordUsageAsync(
        int tokensUsed,
        string providerId,
        TokenUsageType usageType,
        CancellationToken cancellationToken = default)
    {
        if (tokensUsed <= 0)
        {
            _logger.LogDebug("トークン使用量が0以下のため記録をスキップ: {Tokens}", tokensUsed);
            return;
        }

        ArgumentException.ThrowIfNullOrEmpty(providerId);

        var record = new TokenUsageRecord
        {
            TokensUsed = tokensUsed,
            ProviderId = providerId,
            UsageType = usageType.ToString(),
            YearMonth = GetCurrentYearMonth(),
            Timestamp = DateTime.UtcNow
        };

        await _repository.SaveRecordAsync(record, cancellationToken).ConfigureAwait(false);

        _logger.LogDebug(
            "トークン使用量を記録: Provider={Provider}, Tokens={Tokens}, Type={Type}",
            providerId, tokensUsed, usageType);
    }

    /// <inheritdoc/>
    public async Task<TokenUsageInfo> GetMonthlyUsageAsync(CancellationToken cancellationToken = default)
    {
        var yearMonth = GetCurrentYearMonth();
        var summary = await _repository.GetMonthlySummaryAsync(yearMonth, cancellationToken)
            .ConfigureAwait(false);

        var plan = await _accessController.GetCurrentPlanAsync(cancellationToken).ConfigureAwait(false);
        var limit = _accessController.GetMonthlyTokenLimit(plan);

        var now = DateTime.UtcNow;
        var periodStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var resetDate = periodStart.AddMonths(1);

        if (summary == null)
        {
            return new TokenUsageInfo
            {
                TotalTokensUsed = 0,
                MonthlyLimit = limit,
                PeriodStartDate = periodStart,
                ResetDate = resetDate,
                InputTokensUsed = 0,
                OutputTokensUsed = 0,
                LastUpdated = now,
                UsageByProvider = new Dictionary<string, long>()
            };
        }

        return new TokenUsageInfo
        {
            TotalTokensUsed = summary.TotalTokens,
            MonthlyLimit = limit,
            PeriodStartDate = periodStart,
            ResetDate = resetDate,
            InputTokensUsed = summary.InputTokens,
            OutputTokensUsed = summary.OutputTokens,
            LastUpdated = summary.LastUpdated,
            UsageByProvider = summary.ByProvider
        };
    }

    /// <inheritdoc/>
    public async Task<long> GetRemainingTokensAsync(CancellationToken cancellationToken = default)
    {
        var usage = await GetMonthlyUsageAsync(cancellationToken).ConfigureAwait(false);
        return usage.RemainingTokens;
    }

    /// <inheritdoc/>
    public async Task<bool> IsLimitExceededAsync(CancellationToken cancellationToken = default)
    {
        var usage = await GetMonthlyUsageAsync(cancellationToken).ConfigureAwait(false);
        return usage.IsLimitExceeded;
    }

    /// <inheritdoc/>
    public async Task<double> GetUsagePercentageAsync(CancellationToken cancellationToken = default)
    {
        var usage = await GetMonthlyUsageAsync(cancellationToken).ConfigureAwait(false);
        return usage.UsagePercentage;
    }

    /// <inheritdoc/>
    public int EstimateImageTokens(int width, int height, string providerId)
    {
        if (width <= 0 || height <= 0)
            return 0;

        var coefficient = ImageTokenCoefficients.TryGetValue(providerId, out var c)
            ? c
            : ImageTokenCoefficients["default"];

        var pixels = (long)width * height;
        var estimatedTokens = (int)Math.Ceiling(pixels / coefficient);

        _logger.LogDebug(
            "画像トークン推定: {Width}x{Height} = {Pixels}px → {Tokens}tokens (Provider={Provider})",
            width, height, pixels, estimatedTokens, providerId);

        return estimatedTokens;
    }

    /// <inheritdoc/>
    public async Task<UsageAlertLevel> CheckAlertLevelAsync(CancellationToken cancellationToken = default)
    {
        var percentage = await GetUsagePercentageAsync(cancellationToken).ConfigureAwait(false);

        return percentage switch
        {
            >= 1.0 => UsageAlertLevel.Exceeded,
            >= 0.9 => UsageAlertLevel.Warning90,
            >= 0.8 => UsageAlertLevel.Warning80,
            _ => UsageAlertLevel.None
        };
    }

    /// <inheritdoc/>
    public async Task ResetMonthlyUsageAsync(CancellationToken cancellationToken = default)
    {
        var yearMonth = GetCurrentYearMonth();
        await _repository.ClearMonthAsync(yearMonth, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("月間トークン使用量をリセット: {YearMonth}", yearMonth);
    }

    private static string GetCurrentYearMonth()
    {
        var now = DateTime.UtcNow;
        return $"{now.Year:D4}-{now.Month:D2}";
    }
}

/// <summary>
/// トークン使用量の永続化リポジトリインターフェース
/// </summary>
public interface ITokenUsageRepository
{
    /// <summary>
    /// 使用量記録を保存する
    /// </summary>
    Task SaveRecordAsync(TokenUsageRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// 月間サマリーを取得する
    /// </summary>
    Task<MonthlyUsageSummary?> GetMonthlySummaryAsync(string yearMonth, CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定月のデータをクリアする
    /// </summary>
    Task ClearMonthAsync(string yearMonth, CancellationToken cancellationToken = default);
}
