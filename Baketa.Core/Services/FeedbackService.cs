using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Baketa.Core.Abstractions.Feedback;
using Baketa.Core.Abstractions.Privacy;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Core.Services;

/// <summary>
/// GitHub Issues APIベースのフィードバック収集サービス実装
/// プライバシー設定に準拠した安全なデータ収集
/// </summary>
public sealed class FeedbackService : IFeedbackService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<FeedbackSettings> _optionsMonitor;
    private readonly IPrivacyConsentService _privacyConsentService;
    private readonly ILogger<FeedbackService> _logger;
    
    private readonly SemaphoreSlim _submissionSemaphore = new(1, 1);
    private readonly Dictionary<string, DateTime> _lastSubmissionTimes = [];
    
    private FeedbackSettings _currentSettings;
    private bool _disposed;

    public FeedbackService(
        HttpClient httpClient,
        IOptionsMonitor<FeedbackSettings> optionsMonitor,
        IPrivacyConsentService privacyConsentService,
        ILogger<FeedbackService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _privacyConsentService = privacyConsentService ?? throw new ArgumentNullException(nameof(privacyConsentService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _currentSettings = _optionsMonitor.CurrentValue;
        
        // HttpClient設定
        ConfigureHttpClient();
        
        // 設定変更の監視
        _optionsMonitor.OnChange((settings, _) => OnSettingsChanged(settings));
        
        _logger.LogInformation("FeedbackService初期化完了");
    }

    #region IFeedbackService実装

    public bool CanSubmitFeedback => 
        _currentSettings.EnableFeedback && 
        _privacyConsentService.HasConsentFor(DataCollectionType.Feedback);

    public async Task<FeedbackSubmissionResult> SubmitBugReportAsync(BugReport report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (!_currentSettings.EnableBugReports)
        {
            _logger.LogDebug("バグ報告機能が無効化されています");
            return FeedbackSubmissionResult.Disabled;
        }

        if (!_privacyConsentService.HasConsentFor(DataCollectionType.Feedback))
        {
            _logger.LogDebug("フィードバック送信のプライバシー同意が得られていません");
            return FeedbackSubmissionResult.PrivacyBlocked;
        }

        if (!IsSubmissionAllowed("bug_report"))
        {
            _logger.LogDebug("バグ報告の送信間隔制限により送信をスキップします");
            return FeedbackSubmissionResult.RateLimited;
        }

        await _submissionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger.LogInformation("バグ報告を送信します: {Title}", report.Title);

            var issueData = await CreateBugReportIssueAsync(report, cancellationToken).ConfigureAwait(false);
            var result = await SubmitIssueToGitHubAsync(issueData, cancellationToken).ConfigureAwait(false);
            
            if (result.IsSuccess)
            {
                RecordSubmissionTime("bug_report");
                FireFeedbackSubmitted(FeedbackSubmissionResult.Success, result.IssueUrl, null, "BugReport");
            }
            else
            {
                FireFeedbackSubmitted(result.Result, null, result.Error, "BugReport");
            }

            return result.Result;
        }
        finally
        {
            _submissionSemaphore.Release();
        }
    }

    public async Task<FeedbackSubmissionResult> SubmitFeatureRequestAsync(FeatureRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_currentSettings.EnableFeatureRequests)
        {
            _logger.LogDebug("機能要望機能が無効化されています");
            return FeedbackSubmissionResult.Disabled;
        }

        if (!_privacyConsentService.HasConsentFor(DataCollectionType.Feedback))
        {
            _logger.LogDebug("フィードバック送信のプライバシー同意が得られていません");
            return FeedbackSubmissionResult.PrivacyBlocked;
        }

        if (!IsSubmissionAllowed("feature_request"))
        {
            _logger.LogDebug("機能要望の送信間隔制限により送信をスキップします");
            return FeedbackSubmissionResult.RateLimited;
        }

        await _submissionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger.LogInformation("機能要望を送信します: {Title}", request.Title);

            var issueData = CreateFeatureRequestIssue(request);
            var result = await SubmitIssueToGitHubAsync(issueData, cancellationToken).ConfigureAwait(false);
            
            if (result.IsSuccess)
            {
                RecordSubmissionTime("feature_request");
                FireFeedbackSubmitted(FeedbackSubmissionResult.Success, result.IssueUrl, null, "FeatureRequest");
            }
            else
            {
                FireFeedbackSubmitted(result.Result, null, result.Error, "FeatureRequest");
            }

            return result.Result;
        }
        finally
        {
            _submissionSemaphore.Release();
        }
    }

    public async Task<FeedbackSubmissionResult> SubmitGeneralFeedbackAsync(GeneralFeedback feedback, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feedback);

        if (!_currentSettings.EnableGeneralFeedback)
        {
            _logger.LogDebug("一般フィードバック機能が無効化されています");
            return FeedbackSubmissionResult.Disabled;
        }

        if (!_privacyConsentService.HasConsentFor(DataCollectionType.Feedback))
        {
            _logger.LogDebug("フィードバック送信のプライバシー同意が得られていません");
            return FeedbackSubmissionResult.PrivacyBlocked;
        }

        if (!IsSubmissionAllowed("general_feedback"))
        {
            _logger.LogDebug("一般フィードバックの送信間隔制限により送信をスキップします");
            return FeedbackSubmissionResult.RateLimited;
        }

        await _submissionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger.LogInformation("一般フィードバックを送信します: {Title}", feedback.Title);

            var issueData = await CreateGeneralFeedbackIssueAsync(feedback, cancellationToken).ConfigureAwait(false);
            var result = await SubmitIssueToGitHubAsync(issueData, cancellationToken).ConfigureAwait(false);
            
            if (result.IsSuccess)
            {
                RecordSubmissionTime("general_feedback");
                FireFeedbackSubmitted(FeedbackSubmissionResult.Success, result.IssueUrl, null, "GeneralFeedback");
            }
            else
            {
                FireFeedbackSubmitted(result.Result, null, result.Error, "GeneralFeedback");
            }

            return result.Result;
        }
        finally
        {
            _submissionSemaphore.Release();
        }
    }

    public FeedbackSettings GetSettings() => _currentSettings;

    public Task UpdateSettingsAsync(FeedbackSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.IsValid())
        {
            throw new ArgumentException("無効なフィードバック設定です", nameof(settings));
        }

        _currentSettings = settings;
        ConfigureHttpClient();
        
        _logger.LogInformation("フィードバック設定が更新されました");
        
        return Task.CompletedTask;
    }

    public Task<SystemInfo?> CollectSystemInfoAsync()
    {
        if (!_currentSettings.CollectSystemInfo)
        {
            return Task.FromResult<SystemInfo?>(null);
        }

        if (!_privacyConsentService.HasConsentFor(DataCollectionType.SystemInformation))
        {
            _logger.LogDebug("システム情報収集のプライバシー同意が得られていません");
            return Task.FromResult<SystemInfo?>(null);
        }

        try
        {
            var appVersion = GetApplicationVersion();
            var osInfo = Environment.OSVersion.ToString();
            var runtimeVersion = Environment.Version.ToString();
            var architecture = RuntimeInformation.OSArchitecture.ToString();

            var additionalInfo = new Dictionary<string, string>
            {
                ["MachineName"] = Environment.MachineName,
                ["ProcessorCount"] = Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture),
                ["WorkingSet"] = Environment.WorkingSet.ToString(CultureInfo.InvariantCulture),
                ["CLRVersion"] = Environment.Version.ToString(),
                ["Is64BitOperatingSystem"] = Environment.Is64BitOperatingSystem.ToString(),
                ["Is64BitProcess"] = Environment.Is64BitProcess.ToString()
            };

            var systemInfo = new SystemInfo
            {
                AppVersion = appVersion,
                OperatingSystem = osInfo,
                RuntimeVersion = runtimeVersion,
                Architecture = architecture,
                Language = CultureInfo.CurrentUICulture.Name,
                AdditionalInfo = additionalInfo
            };

            return Task.FromResult<SystemInfo?>(systemInfo);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "システム情報へのアクセスが拒否されました");
            return Task.FromResult<SystemInfo?>(null);
        }
        catch (PlatformNotSupportedException ex)
        {
            _logger.LogWarning(ex, "現在のプラットフォームではシステム情報の取得がサポートされていません");
            return Task.FromResult<SystemInfo?>(null);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
        {
            _logger.LogError(ex, "システム情報の収集で予期しないエラーが発生しました");
            return Task.FromResult<SystemInfo?>(null);
        }
    }

    public event EventHandler<FeedbackSubmittedEventArgs>? FeedbackSubmitted;

    #endregion

    #region プライベートメソッド

    private void ConfigureHttpClient()
    {
        _httpClient.Timeout = TimeSpan.FromSeconds(_currentSettings.TimeoutSeconds);
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", _currentSettings.UserAgent);
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");

        if (!string.IsNullOrEmpty(_currentSettings.GitHubToken))
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"token {_currentSettings.GitHubToken}");
        }
    }

    private async Task<GitHubIssueData> CreateBugReportIssueAsync(BugReport report, CancellationToken _)
    {
        var bodyParts = new List<string>
        {
            "## バグ報告",
            "",
            "### 説明",
            report.Description,
            ""
        };

        if (!string.IsNullOrEmpty(report.StepsToReproduce))
        {
            bodyParts.AddRange(
            [
                "### 再現手順",
                report.StepsToReproduce,
                ""
            ]);
        }

        if (!string.IsNullOrEmpty(report.ExpectedBehavior))
        {
            bodyParts.AddRange(
            [
                "### 期待される動作",
                report.ExpectedBehavior,
                ""
            ]);
        }

        if (!string.IsNullOrEmpty(report.ActualBehavior))
        {
            bodyParts.AddRange(
            [
                "### 実際の動作",
                report.ActualBehavior,
                ""
            ]);
        }

        if (!string.IsNullOrEmpty(report.ErrorDetails))
        {
            bodyParts.AddRange(
            [
                "### エラー詳細",
                "```",
                report.ErrorDetails,
                "```",
                ""
            ]);
        }

        // システム情報の追加
        var systemInfo = report.SystemInfo ?? await CollectSystemInfoAsync().ConfigureAwait(false);
        if (systemInfo != null)
        {
            bodyParts.AddRange(
            [
                "### システム情報",
                $"- **アプリバージョン**: {systemInfo.AppVersion}",
                $"- **OS**: {systemInfo.OperatingSystem}",
                $"- **Runtime**: {systemInfo.RuntimeVersion}",
                $"- **アーキテクチャ**: {systemInfo.Architecture}",
                ""
            ]);
        }

        if (!string.IsNullOrEmpty(report.ContactInfo))
        {
            bodyParts.AddRange(
            [
                "### 連絡先",
                report.ContactInfo,
                ""
            ]);
        }

        List<string> labels = ["bug"];
        labels.Add(report.Severity switch
        {
            BugSeverity.Critical => "priority: critical",
            BugSeverity.High => "priority: high",
            BugSeverity.Medium => "priority: medium",
            BugSeverity.Low => "priority: low",
            _ => "priority: medium"
        });

        return new GitHubIssueData
        {
            Title = $"[Bug] {report.Title}",
            Body = string.Join(Environment.NewLine, bodyParts),
            Labels = labels
        };
    }

    private GitHubIssueData CreateFeatureRequestIssue(FeatureRequest request)
    {
        var bodyParts = new List<string>
        {
            "## 機能要望",
            "",
            "### 説明",
            request.Description,
            ""
        };

        if (!string.IsNullOrEmpty(request.UseCase))
        {
            bodyParts.AddRange(
            [
                "### 使用ケース",
                request.UseCase,
                ""
            ]);
        }

        if (!string.IsNullOrEmpty(request.References))
        {
            bodyParts.AddRange(
            [
                "### 参考例",
                request.References,
                ""
            ]);
        }

        if (!string.IsNullOrEmpty(request.ContactInfo))
        {
            bodyParts.AddRange(
            [
                "### 連絡先",
                request.ContactInfo,
                ""
            ]);
        }

        List<string> labels = ["enhancement"];
        labels.Add(request.Priority switch
        {
            FeaturePriority.Critical => "priority: critical",
            FeaturePriority.High => "priority: high",
            FeaturePriority.Medium => "priority: medium",
            FeaturePriority.Low => "priority: low",
            _ => "priority: medium"
        });

        return new GitHubIssueData
        {
            Title = $"[Feature] {request.Title}",
            Body = string.Join(Environment.NewLine, bodyParts),
            Labels = labels
        };
    }

    private async Task<GitHubIssueData> CreateGeneralFeedbackIssueAsync(GeneralFeedback feedback, CancellationToken _)
    {
        var bodyParts = new List<string>
        {
            "## フィードバック",
            "",
            "### 内容",
            feedback.Content,
            ""
        };

        // システム情報の追加（必要に応じて）
        var systemInfo = feedback.SystemInfo ?? await CollectSystemInfoAsync().ConfigureAwait(false);
        if (systemInfo != null && feedback.Type == FeedbackType.Performance)
        {
            bodyParts.AddRange(
            [
                "### システム情報",
                $"- **アプリバージョン**: {systemInfo.AppVersion}",
                $"- **OS**: {systemInfo.OperatingSystem}",
                $"- **Runtime**: {systemInfo.RuntimeVersion}",
                ""
            ]);
        }

        if (!string.IsNullOrEmpty(feedback.ContactInfo))
        {
            bodyParts.AddRange(
            [
                "### 連絡先",
                feedback.ContactInfo,
                ""
            ]);
        }

        List<string> labels = ["feedback"];
        labels.Add(feedback.Type switch
        {
            FeedbackType.Usability => "usability",
            FeedbackType.Performance => "performance",
            FeedbackType.Documentation => "documentation",
            FeedbackType.Other => "other",
            _ => "general"
        });

        return new GitHubIssueData
        {
            Title = $"[Feedback] {feedback.Title}",
            Body = string.Join(Environment.NewLine, bodyParts),
            Labels = labels
        };
    }

    private async Task<SubmissionResult> SubmitIssueToGitHubAsync(GitHubIssueData issueData, CancellationToken cancellationToken)
    {
        var url = _currentSettings.GetIssuesApiUrl();
        var retryCount = 0;

        while (retryCount <= _currentSettings.RetryCount)
        {
            try
            {
                _logger.LogDebug("GitHub Issues APIを呼び出します: {Url} (試行 {Retry}/{MaxRetry})", 
                    url, retryCount + 1, _currentSettings.RetryCount + 1);

                var response = await _httpClient.PostAsJsonAsync(url, issueData, cancellationToken).ConfigureAwait(false);
                
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    _logger.LogWarning("GitHub API制限に達しました。しばらく待機します");
                    await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);
                    return new SubmissionResult(FeedbackSubmissionResult.RateLimited, null, null);
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    _logger.LogWarning("GitHub API認証エラーが発生しました");
                    return new SubmissionResult(FeedbackSubmissionResult.AuthenticationError, null, null);
                }

                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var issueResponse = JsonSerializer.Deserialize<GitHubIssueResponse>(responseContent);
                
                var issueUrl = issueResponse?.HtmlUrl != null ? new Uri(issueResponse.HtmlUrl) : null;
                
                _logger.LogInformation("GitHub Issueが正常に作成されました: {IssueUrl}", issueUrl);
                
                return new SubmissionResult(FeedbackSubmissionResult.Success, issueUrl, null);
            }
            catch (Exception ex) when (retryCount < _currentSettings.RetryCount)
            {
                retryCount++;
                var delay = TimeSpan.FromSeconds((double)_currentSettings.RetryDelaySeconds * retryCount);
                
                _logger.LogWarning(ex, "GitHub API呼び出しが失敗しました。{Delay}秒後にリトライします（{Retry}/{MaxRetry}）", 
                    delay.TotalSeconds, retryCount, _currentSettings.RetryCount);
                
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "GitHub API呼び出しでネットワークエラーが発生しました");
                return new SubmissionResult(FeedbackSubmissionResult.NetworkError, null, ex);
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "GitHub API呼び出しがタイムアウトまたはキャンセルされました");
                return new SubmissionResult(FeedbackSubmissionResult.NetworkError, null, ex);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "GitHub APIレスポンスのJSON解析エラーが発生しました");
                return new SubmissionResult(FeedbackSubmissionResult.ValidationError, null, ex);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogError(ex, "GitHub API呼び出しで予期しないエラーが発生しました");
                return new SubmissionResult(FeedbackSubmissionResult.NetworkError, null, ex);
            }
        }

        return new SubmissionResult(FeedbackSubmissionResult.NetworkError, null, 
            new HttpRequestException($"GitHub API呼び出しが{_currentSettings.RetryCount + 1}回失敗しました"));
    }

    private bool IsSubmissionAllowed(string submissionType)
    {
        if (!_lastSubmissionTimes.TryGetValue(submissionType, out var lastTime))
        {
            return true;
        }

        var elapsed = DateTime.UtcNow - lastTime;
        var minInterval = TimeSpan.FromMinutes(_currentSettings.MinSubmissionIntervalMinutes);
        
        return elapsed >= minInterval;
    }

    private void RecordSubmissionTime(string submissionType)
    {
        _lastSubmissionTimes[submissionType] = DateTime.UtcNow;
    }

    private static string GetApplicationVersion()
    {
        return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
    }

    private void OnSettingsChanged(FeedbackSettings newSettings)
    {
        _currentSettings = newSettings;
        ConfigureHttpClient();
        _logger.LogInformation("フィードバック設定が外部から変更されました");
    }

    private void FireFeedbackSubmitted(FeedbackSubmissionResult result, Uri? issueUrl, Exception? error, string feedbackType)
    {
        FeedbackSubmitted?.Invoke(this, new FeedbackSubmittedEventArgs
        {
            Result = result,
            IssueUrl = issueUrl,
            Error = error,
            Timestamp = DateTime.UtcNow,
            FeedbackType = feedbackType
        });
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed)
            return;

        _submissionSemaphore.Dispose();
        
        _disposed = true;
        _logger.LogInformation("FeedbackService disposed");
    }

    #endregion
}

// GitHub API用のJSONモデル
internal sealed record GitHubIssueData
{
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("body")]
    public required string Body { get; init; }

    [JsonPropertyName("labels")]
    public IReadOnlyList<string>? Labels { get; init; }
}

internal sealed record GitHubIssueResponse
{
    [JsonPropertyName("number")]
    public int Number { get; init; }

    [JsonPropertyName("html_url")]
    public required string HtmlUrl { get; init; }

    [JsonPropertyName("state")]
    public required string State { get; init; }
}

// 送信結果のヘルパークラス
internal sealed record SubmissionResult(
    FeedbackSubmissionResult Result,
    Uri? IssueUrl,
    Exception? Error)
{
    public bool IsSuccess => Result == FeedbackSubmissionResult.Success;
}