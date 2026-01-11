using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Baketa.Core.Abstractions.License;
using Baketa.Core.License.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.License;

/// <summary>
/// ボーナストークンサービス実装
/// Issue #280+#281: プロモーション等で付与されたボーナストークンを管理
/// </summary>
/// <remarks>
/// <para>
/// - Relay Server経由でボーナストークン状態を取得・同期
/// - ローカルでの消費を記録し、オンライン時にCRDT G-Counterで同期
/// - 有効期限が近い順に消費（FIFOライク）
/// </para>
/// </remarks>
public sealed class BonusTokenService : IBonusTokenService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BonusTokenService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly object _lockObject = new();
    private bool _disposed;

    /// <summary>
    /// ローカルに保持するボーナストークン一覧
    /// </summary>
    private List<BonusToken> _bonusTokens = [];

    /// <summary>
    /// ローカルでの消費記録（同期前）
    /// Key: BonusToken.Id, Value: UsedTokens（ローカル最新値）
    /// </summary>
    private readonly Dictionary<Guid, long> _pendingConsumption = [];

    /// <inheritdoc/>
    public event EventHandler<BonusTokensChangedEventArgs>? BonusTokensChanged;

    /// <inheritdoc/>
    public bool HasPendingSync
    {
        get
        {
            lock (_lockObject)
            {
                return _pendingConsumption.Count > 0;
            }
        }
    }

    public BonusTokenService(
        HttpClient httpClient,
        ILogger<BonusTokenService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        _logger.LogDebug("BonusTokenService initialized");
    }

    /// <inheritdoc/>
    public IReadOnlyList<BonusToken> GetBonusTokens()
    {
        lock (_lockObject)
        {
            // 有効期限順にソートして返す
            return [.. _bonusTokens.OrderBy(b => b.ExpiresAt)];
        }
    }

    /// <inheritdoc/>
    public long GetTotalRemainingTokens()
    {
        lock (_lockObject)
        {
            return _bonusTokens
                .Where(b => b.IsUsable)
                .Sum(b => b.RemainingTokens);
        }
    }

    /// <inheritdoc/>
    public long ConsumeTokens(long amount)
    {
        if (amount <= 0)
            return 0;

        lock (_lockObject)
        {
            long totalConsumed = 0;
            var remainingToConsume = amount;

            // 有効期限が近い順に消費
            var usableBonuses = _bonusTokens
                .Where(b => b.IsUsable)
                .OrderBy(b => b.ExpiresAt)
                .ToList();

            foreach (var bonus in usableBonuses)
            {
                if (remainingToConsume <= 0)
                    break;

                var canConsume = Math.Min(bonus.RemainingTokens, remainingToConsume);
                if (canConsume <= 0)
                    continue;

                // ローカル消費を記録
                var newUsedTokens = bonus.UsedTokens + canConsume;

                // _pendingConsumptionを更新
                if (_pendingConsumption.TryGetValue(bonus.Id, out var existingUsed))
                {
                    _pendingConsumption[bonus.Id] = Math.Max(existingUsed, newUsedTokens);
                }
                else
                {
                    _pendingConsumption[bonus.Id] = newUsedTokens;
                }

                // ローカルのBonusTokenを更新
                var index = _bonusTokens.FindIndex(b => b.Id == bonus.Id);
                if (index >= 0)
                {
                    _bonusTokens[index] = bonus with { UsedTokens = newUsedTokens };
                }

                totalConsumed += canConsume;
                remainingToConsume -= canConsume;

                _logger.LogDebug(
                    "Consumed {Amount} tokens from bonus {BonusId} (Remaining: {Remaining})",
                    canConsume, bonus.Id, bonus.RemainingTokens - canConsume);
            }

            if (totalConsumed > 0)
            {
                RaiseBonusTokensChanged("Tokens consumed locally");
            }

            return totalConsumed;
        }
    }

    /// <inheritdoc/>
    public long GetConsumeableAmount(long amount)
    {
        if (amount <= 0)
            return 0;

        lock (_lockObject)
        {
            var totalAvailable = _bonusTokens
                .Where(b => b.IsUsable)
                .Sum(b => b.RemainingTokens);

            return Math.Min(amount, totalAvailable);
        }
    }

    /// <inheritdoc/>
    public async Task<BonusSyncResult> FetchFromServerAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            _logger.LogWarning("[Issue #280] FetchFromServerAsync: accessToken is null or empty");
            return new BonusSyncResult
            {
                Success = false,
                ErrorMessage = "Authentication required"
            };
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/bonus-tokens/status");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning("[Issue #280] FetchFromServerAsync: Rate limited");
                return new BonusSyncResult { Success = false, ErrorMessage = "Rate limited" };
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning("[Issue #280] FetchFromServerAsync: Unauthorized");
                return new BonusSyncResult { Success = false, ErrorMessage = "Authentication failed" };
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[Issue #280] FetchFromServerAsync: Server error {StatusCode}", response.StatusCode);
                return new BonusSyncResult { Success = false, ErrorMessage = $"Server error: {response.StatusCode}" };
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<BonusStatusResponse>(content, _jsonOptions);

            if (result == null)
            {
                _logger.LogWarning("[Issue #280] FetchFromServerAsync: Invalid response");
                return new BonusSyncResult { Success = false, ErrorMessage = "Invalid response" };
            }

            // ローカル状態を更新
            lock (_lockObject)
            {
                // [Gemini Review] ISO 8601形式の日付を常にUTCとして解釈
                _bonusTokens = result.Bonuses?
                    .Select(b => new BonusToken
                    {
                        Id = Guid.Parse(b.Id),
                        SourceType = b.SourceType ?? "unknown",
                        GrantedTokens = b.GrantedTokens,
                        UsedTokens = b.UsedTokens,
                        ExpiresAt = DateTime.Parse(b.ExpiresAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal)
                    })
                    .ToList() ?? [];
            }

            RaiseBonusTokensChanged("Fetched from server");

            _logger.LogInformation(
                "[Issue #280] FetchFromServerAsync: Fetched {Count} bonuses (Total: {Total})",
                result.Bonuses?.Count ?? 0, result.TotalRemaining);

            return new BonusSyncResult
            {
                Success = true,
                Bonuses = GetBonusTokens(),
                TotalRemaining = result.TotalRemaining
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[Issue #280] FetchFromServerAsync: Timeout");
            return new BonusSyncResult { Success = false, ErrorMessage = "Timeout" };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "[Issue #280] FetchFromServerAsync: Network error");
            return new BonusSyncResult { Success = false, ErrorMessage = "Network error" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Issue #280] FetchFromServerAsync: Unexpected error");
            return new BonusSyncResult { Success = false, ErrorMessage = "Unexpected error" };
        }
    }

    /// <inheritdoc/>
    public async Task<BonusSyncResult> SyncToServerAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            _logger.LogWarning("[Issue #281] SyncToServerAsync: accessToken is null or empty");
            return new BonusSyncResult
            {
                Success = false,
                ErrorMessage = "Authentication required"
            };
        }

        List<BonusSyncItem> syncItems;
        lock (_lockObject)
        {
            if (_pendingConsumption.Count == 0)
            {
                _logger.LogDebug("[Issue #281] SyncToServerAsync: No pending consumption");
                return new BonusSyncResult
                {
                    Success = true,
                    Bonuses = GetBonusTokens(),
                    TotalRemaining = GetTotalRemainingTokens()
                };
            }

            syncItems = _pendingConsumption
                .Select(kv => new BonusSyncItem
                {
                    Id = kv.Key.ToString(),
                    UsedTokens = kv.Value
                })
                .ToList();
        }

        try
        {
            var requestBody = new BonusSyncRequest { Bonuses = syncItems };

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/bonus-tokens/sync");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = JsonContent.Create(requestBody, options: _jsonOptions);

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                _logger.LogWarning("[Issue #281] SyncToServerAsync: Rate limited");
                return new BonusSyncResult { Success = false, ErrorMessage = "Rate limited" };
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning("[Issue #281] SyncToServerAsync: Unauthorized");
                return new BonusSyncResult { Success = false, ErrorMessage = "Authentication failed" };
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[Issue #281] SyncToServerAsync: Server error {StatusCode}", response.StatusCode);
                return new BonusSyncResult { Success = false, ErrorMessage = $"Server error: {response.StatusCode}" };
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<BonusSyncResponse>(content, _jsonOptions);

            if (result == null)
            {
                _logger.LogWarning("[Issue #281] SyncToServerAsync: Invalid response");
                return new BonusSyncResult { Success = false, ErrorMessage = "Invalid response" };
            }

            // 同期成功したアイテムのpending消費を削除
            lock (_lockObject)
            {
                if (result.Synced != null)
                {
                    foreach (var synced in result.Synced)
                    {
                        if (Guid.TryParse(synced.Id, out var id))
                        {
                            _pendingConsumption.Remove(id);

                            // ローカルのBonusTokenをサーバー値で更新
                            var index = _bonusTokens.FindIndex(b => b.Id == id);
                            if (index >= 0)
                            {
                                var existing = _bonusTokens[index];
                                _bonusTokens[index] = existing with { UsedTokens = synced.UsedTokens };
                            }
                        }
                    }
                }
            }

            RaiseBonusTokensChanged("Synced to server");

            _logger.LogInformation(
                "[Issue #281] SyncToServerAsync: Synced {Count} bonuses",
                result.SyncedCount);

            return new BonusSyncResult
            {
                Success = true,
                Bonuses = GetBonusTokens(),
                TotalRemaining = GetTotalRemainingTokens()
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[Issue #281] SyncToServerAsync: Timeout");
            return new BonusSyncResult { Success = false, ErrorMessage = "Timeout" };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "[Issue #281] SyncToServerAsync: Network error");
            return new BonusSyncResult { Success = false, ErrorMessage = "Network error" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Issue #281] SyncToServerAsync: Unexpected error");
            return new BonusSyncResult { Success = false, ErrorMessage = "Unexpected error" };
        }
    }

    private void RaiseBonusTokensChanged(string reason)
    {
        BonusTokensChanged?.Invoke(this, new BonusTokensChangedEventArgs
        {
            Bonuses = GetBonusTokens(),
            TotalRemaining = GetTotalRemainingTokens(),
            Reason = reason
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _logger.LogDebug("BonusTokenService disposed");
    }

    #region Request/Response Models

    private sealed class BonusStatusResponse
    {
        [JsonPropertyName("bonuses")]
        public List<BonusTokenDto>? Bonuses { get; set; }

        [JsonPropertyName("total_remaining")]
        public long TotalRemaining { get; set; }

        [JsonPropertyName("active_count")]
        public int ActiveCount { get; set; }
    }

    private sealed class BonusTokenDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("source_type")]
        public string? SourceType { get; set; }

        [JsonPropertyName("granted_tokens")]
        public long GrantedTokens { get; set; }

        [JsonPropertyName("used_tokens")]
        public long UsedTokens { get; set; }

        [JsonPropertyName("remaining_tokens")]
        public long RemainingTokens { get; set; }

        [JsonPropertyName("expires_at")]
        public string ExpiresAt { get; set; } = string.Empty;

        [JsonPropertyName("is_expired")]
        public bool IsExpired { get; set; }
    }

    private sealed class BonusSyncRequest
    {
        [JsonPropertyName("bonuses")]
        public List<BonusSyncItem>? Bonuses { get; set; }
    }

    private sealed class BonusSyncItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("used_tokens")]
        public long UsedTokens { get; set; }
    }

    private sealed class BonusSyncResponse
    {
        [JsonPropertyName("synced")]
        public List<SyncedBonusItem>? Synced { get; set; }

        [JsonPropertyName("synced_count")]
        public int SyncedCount { get; set; }
    }

    private sealed class SyncedBonusItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("used_tokens")]
        public long UsedTokens { get; set; }

        [JsonPropertyName("remaining_tokens")]
        public long RemainingTokens { get; set; }
    }

    #endregion
}
