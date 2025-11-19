using System;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Auth;
using Baketa.Core.Abstractions.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Services;

/// <summary>
/// Advertisement service implementation for managing Google AdSense display
/// </summary>
public sealed class AdvertisementService : IAdvertisementService, IDisposable
{
    private readonly IAuthService _authService;
    private readonly IUserPlanService _userPlanService;
    private readonly ILogger<AdvertisementService> _logger;
    private readonly IConfiguration _configuration;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private bool _disposed;

    /// <inheritdoc/>
    public bool ShouldShowAd { get; private set; }

    /// <inheritdoc/>
    public string AdHtmlContent { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public event EventHandler<AdDisplayChangedEventArgs>? AdDisplayChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="AdvertisementService"/> class
    /// </summary>
    /// <param name="authService">Authentication service</param>
    /// <param name="userPlanService">User plan service</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="configuration">Configuration instance</param>
    public AdvertisementService(
        IAuthService authService,
        IUserPlanService userPlanService,
        ILogger<AdvertisementService> logger,
        IConfiguration configuration)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _userPlanService = userPlanService ?? throw new ArgumentNullException(nameof(userPlanService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

        // Subscribe to authentication status changes
        _authService.AuthStatusChanged += OnAuthStatusChanged;

        // Subscribe to user plan changes
        _userPlanService.PlanChanged += OnPlanChanged;

        // Note: Initial ad display state will be determined lazily on first LoadAdAsync() call
        // or when authentication/plan events fire
        _logger.LogInformation("AdvertisementService initialized.");
    }

    /// <inheritdoc/>
    public async Task LoadAdAsync(CancellationToken cancellationToken = default)
    {
        await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Update display state before loading
            await UpdateAdDisplayStateAsync().ConfigureAwait(false);

            if (!ShouldShowAd)
            {
                AdHtmlContent = string.Empty;
                _logger.LogDebug("広告表示不要のため、HTMLコンテンツをクリア");
                return;
            }

            try
            {
                // Get AdSense configuration from appsettings.json
                var adSenseClientId = _configuration["Advertisement:AdSenseClientId"];
                var adSenseSlotId = _configuration["Advertisement:AdSenseSlotId"];

                if (string.IsNullOrEmpty(adSenseClientId))
                {
                    _logger.LogWarning("AdSense Client IDが設定されていません");
                    AdHtmlContent = string.Empty;
                    return;
                }

                AdHtmlContent = GenerateAdSenseHtml(adSenseClientId, adSenseSlotId);
                _logger.LogInformation("AdSense広告HTMLを生成しました");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "広告HTML生成中にエラーが発生しました");
                AdHtmlContent = string.Empty; // On error, show blank area
            }
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task HideAdAsync(CancellationToken cancellationToken = default)
    {
        ShouldShowAd = false;
        AdHtmlContent = string.Empty;

        _logger.LogInformation("広告を非表示にしました");

        AdDisplayChanged?.Invoke(this, new AdDisplayChangedEventArgs
        {
            ShouldShowAd = false,
            Reason = "User request"
        });

        await Task.CompletedTask;
    }

    /// <summary>
    /// Handle authentication status changes
    /// </summary>
    /// <param name="sender">Event sender</param>
    /// <param name="e">Event arguments</param>
    private async void OnAuthStatusChanged(object? sender, AuthStatusChangedEventArgs e)
    {
        _logger.LogDebug("認証状態変更を検出: IsLoggedIn={IsLoggedIn}", e.IsLoggedIn);
        await UpdateAdDisplayStateAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Handle user plan changes
    /// </summary>
    /// <param name="sender">Event sender</param>
    /// <param name="e">Event arguments</param>
    private async void OnPlanChanged(object? sender, UserPlanChangedEventArgs e)
    {
        _logger.LogInformation("プラン変更を検出: {OldPlan} → {NewPlan}", e.OldPlan, e.NewPlan);
        await UpdateAdDisplayStateAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Update advertisement display state based on authentication and user plan
    /// </summary>
    private async Task UpdateAdDisplayStateAsync()
    {
        try
        {
            // Check if user is authenticated
            var session = await _authService.GetCurrentSessionAsync().ConfigureAwait(false);
            var isAuthenticated = session != null;

            // Check if user has premium plan
            var isPremium = isAuthenticated && _userPlanService.CurrentPlan == UserPlanType.Premium;

            // Show ads for free plan users and unauthenticated users
            var shouldShow = !isPremium;
            var reason = isPremium ? "Premium plan" : "Free plan or not logged in";

            if (ShouldShowAd != shouldShow)
            {
                var oldState = ShouldShowAd;
                ShouldShowAd = shouldShow;

                _logger.LogInformation(
                    "広告表示状態変更: {OldState} → {NewState} (理由: {Reason})",
                    oldState, shouldShow, reason);

                AdDisplayChanged?.Invoke(this, new AdDisplayChangedEventArgs
                {
                    ShouldShowAd = shouldShow,
                    Reason = reason
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "広告表示状態の更新中にエラーが発生しました");
        }
    }

    /// <summary>
    /// Generate Google AdSense HTML with CSP security headers
    /// </summary>
    /// <param name="clientId">AdSense client ID</param>
    /// <param name="slotId">AdSense slot ID (optional)</param>
    /// <returns>AdSense HTML content</returns>
    private static string GenerateAdSenseHtml(string clientId, string? slotId)
    {
        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <meta http-equiv=""Content-Security-Policy"" content=""default-src 'self'; script-src 'unsafe-inline' https://pagead2.googlesyndication.com; frame-src https://googleads.g.doubleclick.net; img-src * data:; style-src 'unsafe-inline';"">
    <style>
        body {{
            margin: 0;
            padding: 0;
            background-color: #2C2C2C;
            display: flex;
            justify-content: center;
            align-items: center;
            height: 100px;
            overflow: hidden;
        }}
    </style>
</head>
<body>
    <!-- AdSense広告ユニット -->
    <script async src=""https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js?client={clientId}""
         crossorigin=""anonymous""></script>
    <ins class=""adsbygoogle""
         style=""display:block""
         data-ad-client=""{clientId}""
         data-ad-slot=""{slotId ?? "1234567890"}""
         data-ad-format=""horizontal""
         data-full-width-responsive=""true""></ins>
    <script>
         (adsbygoogle = window.adsbygoogle || []).push({{}});
    </script>
</body>
</html>
";
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _authService.AuthStatusChanged -= OnAuthStatusChanged;
        _userPlanService.PlanChanged -= OnPlanChanged;
        _loadLock.Dispose();

        _disposed = true;
        _logger.LogDebug("AdvertisementService disposed");
    }
}
