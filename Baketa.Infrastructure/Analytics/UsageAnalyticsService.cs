using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Baketa.Core.Abstractions.Privacy;
using Baketa.Core.Abstractions.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Analytics;

/// <summary>
/// [Issue #269] 使用統計収集サービス実装
///
/// 機能:
/// - イベントをメモリバッファに蓄積
/// - 適応型バッチ送信（50件 or 5分間隔）
/// - プライバシー同意チェック
/// - DEBUGビルドでは収集しない（設定で上書き可）
/// </summary>
public sealed class UsageAnalyticsService : IUsageAnalyticsService, IAsyncDisposable, IDisposable
{
    private const int MaxBatchSize = 50;
    private const int MaxBufferSize = MaxBatchSize * 2;  // 最大バッファサイズ
    private const int MaxBatchIntervalSeconds = 300;  // 5分
    private const int MinBatchIntervalSeconds = 60;   // 最低1分

    // [Gemini Review] JsonSerializerOptionsをstaticキャッシュ
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly ILogger<UsageAnalyticsService> _logger;
    private readonly IPrivacyConsentService _privacyConsentService;
    private readonly HttpClient _httpClient;
    private readonly string _analyticsEndpoint;
    private readonly string _analyticsApiKey;
    private readonly string _appVersion;
    private readonly bool _enableInDebug;

    private readonly List<UsageEventDto> _buffer = [];
    private readonly object _bufferLock = new();
    private readonly System.Threading.Timer _flushTimer;
    private DateTime _lastFlushTime = DateTime.UtcNow;
    private volatile bool _disposed;  // [Gemini Review] volatileでスレッドセーフ

    public Guid SessionId { get; } = Guid.NewGuid();

    public bool IsEnabled
    {
        get
        {
#if DEBUG
            if (!_enableInDebug)
            {
                return false;
            }
#endif
            return _privacyConsentService.HasUsageStatisticsConsent;
        }
    }

    public UsageAnalyticsService(
        ILogger<UsageAnalyticsService> logger,
        IPrivacyConsentService privacyConsentService,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _privacyConsentService = privacyConsentService ?? throw new ArgumentNullException(nameof(privacyConsentService));

        ArgumentNullException.ThrowIfNull(httpClientFactory);
        _httpClient = httpClientFactory.CreateClient("Analytics");

        // 設定読み込み
        _analyticsEndpoint = configuration["Analytics:Endpoint"]
            ?? "https://baketa-relay.suke009.workers.dev/api/analytics/events";
        _analyticsApiKey = configuration["Analytics:ApiKey"] ?? string.Empty;
        _enableInDebug = configuration.GetValue("Analytics:EnableInDebug", false);

        // アプリバージョン取得
        _appVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";

        // 定期フラッシュタイマー（5分間隔）
        _flushTimer = new System.Threading.Timer(
            OnFlushTimerElapsed,
            null,
            TimeSpan.FromSeconds(MaxBatchIntervalSeconds),
            TimeSpan.FromSeconds(MaxBatchIntervalSeconds));

        _logger.LogInformation(
            "[Issue #269] UsageAnalyticsService initialized: SessionId={SessionId}, Enabled={Enabled}",
            SessionId.ToString()[..8],
            IsEnabled);
    }

    public void TrackEvent(string eventType, Dictionary<string, object>? eventData = null)
    {
        if (_disposed || !IsEnabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(eventType))
        {
            _logger.LogWarning("[Issue #269] TrackEvent called with empty eventType");
            return;
        }

        var evt = new UsageEventDto
        {
            SessionId = SessionId.ToString(),
            EventType = eventType,
            EventData = eventData,
            SchemaVersion = 1,
            AppVersion = _appVersion,
            OccurredAt = DateTime.UtcNow.ToString("O")
        };

        bool shouldFlush;
        lock (_bufferLock)
        {
            _buffer.Add(evt);
            shouldFlush = _buffer.Count >= MaxBatchSize;
        }

        _logger.LogDebug("[Issue #269] Event tracked: {EventType}, BufferCount={Count}", eventType, _buffer.Count);

        // バッファが満杯なら即座にフラッシュ
        if (shouldFlush)
        {
            _ = FlushAsync();
        }
    }

    public async Task<bool> FlushAsync(CancellationToken cancellationToken = default)
    {
        // [Gemini Review] disposed チェックを先頭に追加
        if (_disposed)
        {
            return true;
        }

        if (!IsEnabled)
        {
            return true;
        }

        List<UsageEventDto> eventsToSend;
        lock (_bufferLock)
        {
            if (_buffer.Count == 0)
            {
                return true;
            }

            eventsToSend = [.. _buffer];
            _buffer.Clear();
        }

        _lastFlushTime = DateTime.UtcNow;

        try
        {
            if (string.IsNullOrEmpty(_analyticsApiKey))
            {
                _logger.LogWarning("[Issue #269] Analytics API key not configured, skipping flush");
                return false;
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, _analyticsEndpoint);
            request.Headers.Add("X-Analytics-Key", _analyticsApiKey);
            // [Gemini Review] キャッシュされたJsonOptionsを使用
            request.Content = JsonContent.Create(eventsToSend, options: JsonOptions);

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "[Issue #269] Analytics events sent: Count={Count}",
                    eventsToSend.Count);
                return true;
            }

            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogWarning(
                "[Issue #269] Failed to send analytics events: Status={Status}, Body={Body}",
                response.StatusCode,
                errorBody);

            // 送信失敗時はバッファに戻す（次回リトライ）
            ReturnEventsToBuffer(eventsToSend);

            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "[Issue #269] Network error sending analytics events");

            // ネットワークエラー時はバッファに戻す
            ReturnEventsToBuffer(eventsToSend);

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Issue #269] Unexpected error sending analytics events");
            return false;
        }
    }

    /// <summary>
    /// [Gemini Review] 送信失敗時にイベントをバッファに戻す
    /// パフォーマンス改善: AddRange + RemoveRange を使用
    /// </summary>
    private void ReturnEventsToBuffer(List<UsageEventDto> events)
    {
        lock (_bufferLock)
        {
            // [Gemini Review] AddRangeで末尾に追加（先頭挿入より効率的）
            // サーバー側でタイムスタンプ順にソートされる前提
            _buffer.AddRange(events);

            // バッファサイズ制限（古いイベントを破棄）
            if (_buffer.Count > MaxBufferSize)
            {
                int itemsToRemove = _buffer.Count - MaxBufferSize;
                // [Gemini Review] RemoveRangeで効率的に削除 + ログ出力
                _logger.LogWarning(
                    "[Issue #269] Analytics buffer is full. Dropping {Count} oldest events.",
                    itemsToRemove);
                _buffer.RemoveRange(0, itemsToRemove);
            }
        }
    }

    private void OnFlushTimerElapsed(object? state)
    {
        if (_disposed) return;

        var elapsed = DateTime.UtcNow - _lastFlushTime;
        if (elapsed.TotalSeconds >= MinBatchIntervalSeconds)
        {
            _ = FlushAsync();
        }
    }

    /// <summary>
    /// [Gemini Review] IAsyncDisposable実装 - デッドロック回避
    /// 破棄ロジックを集約し、タイムアウト付きでフラッシュ
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // [Gemini Review] タイマーを非同期で安全に停止
        await _flushTimer.DisposeAsync().ConfigureAwait(false);

        // 終了時に残りのイベントを送信（タイムアウト付き）
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await FlushAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[Issue #269] Flush on dispose timed out");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Issue #269] Failed to flush events on dispose");
        }
    }

    /// <summary>
    /// [Gemini Review] 同期Dispose - DisposeAsyncに処理を委譲
    /// 注意: UIスレッドから呼び出すとデッドロックのリスクあり
    /// </summary>
    public void Dispose()
    {
        // DisposeAsyncを同期的に呼び出す形に統一
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

/// <summary>
/// 使用統計イベントDTO
/// </summary>
internal sealed class UsageEventDto
{
    public required string SessionId { get; init; }
    public string? UserId { get; init; }
    public required string EventType { get; init; }
    public Dictionary<string, object>? EventData { get; init; }
    public required int SchemaVersion { get; init; }
    public required string AppVersion { get; init; }
    public required string OccurredAt { get; init; }
}
