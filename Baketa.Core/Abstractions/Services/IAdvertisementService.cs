using System;
using System.Threading;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Services;

/// <summary>
/// Advertisement service interface for managing ad display logic
/// </summary>
public interface IAdvertisementService
{
    /// <summary>
    /// Gets whether ads should be shown based on user plan and authentication status
    /// </summary>
    bool ShouldShowAd { get; }

    /// <summary>
    /// Gets the current advertisement HTML content
    /// </summary>
    string AdHtmlContent { get; }

    /// <summary>
    /// Event fired when ad display state changes
    /// </summary>
    event EventHandler<AdDisplayChangedEventArgs>? AdDisplayChanged;

    /// <summary>
    /// Load advertisement content asynchronously
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async operation</returns>
    Task LoadAdAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Hide advertisement
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async operation</returns>
    Task HideAdAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Issue #240: Initialize and show advertisement window if needed.
    /// This method should be called during app startup to let the service
    /// manage the AdWindow lifecycle based on user plan.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the async operation</returns>
    Task InitializeAdWindowAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Event args for advertisement display state changes
/// </summary>
public sealed class AdDisplayChangedEventArgs : EventArgs
{
    /// <summary>
    /// Gets whether ads should be displayed
    /// </summary>
    public required bool ShouldShowAd { get; init; }

    /// <summary>
    /// Gets the reason for the change
    /// </summary>
    public required string Reason { get; init; }
}
