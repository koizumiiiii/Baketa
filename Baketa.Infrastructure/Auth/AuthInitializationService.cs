using Baketa.Core.Abstractions.Auth;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Auth;

/// <summary>
/// Background service for authentication initialization and session restoration
/// Runs on application startup to restore user sessions and initialize auth state
/// </summary>
/// <remarks>
/// Initialize authentication initialization service
/// </remarks>
/// <param name="authService">Authentication service</param>
/// <param name="logger">Logger instance</param>
public sealed class AuthInitializationService(IAuthService authService, ILogger<AuthInitializationService> logger) : IHostedService, IDisposable
{
    private readonly IAuthService _authService = authService ?? throw new ArgumentNullException(nameof(authService));
    private readonly ILogger<AuthInitializationService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly SemaphoreSlim _initSemaphore = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Start the authentication initialization service
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the start operation</returns>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        await _initSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger.LogInformation("Starting authentication initialization service...");

            // Attempt to restore user session from local storage
            await _authService.RestoreSessionAsync(cancellationToken).ConfigureAwait(false);

            // Subscribe to auth status changes for logging
            _authService.AuthStatusChanged += OnAuthStatusChanged;

            _logger.LogInformation("Authentication initialization service started successfully");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Authentication initialization cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting authentication initialization service");
            // Don't throw - allow the application to continue even if auth init fails
        }
        finally
        {
            _initSemaphore.Release();
        }
    }

    /// <summary>
    /// Stop the authentication initialization service
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task representing the stop operation</returns>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        await _initSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger.LogInformation("Stopping authentication initialization service...");

            // Unsubscribe from auth status changes
            _authService.AuthStatusChanged -= OnAuthStatusChanged;

            // Perform any cleanup if needed
            // Note: We don't sign out the user here - that's handled by the auth service

            _logger.LogInformation("Authentication initialization service stopped");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Authentication stop cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping authentication initialization service");
        }
        finally
        {
            _initSemaphore.Release();
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// Handle authentication status changes for logging and monitoring
    /// </summary>
    /// <param name="sender">Event sender</param>
    /// <param name="e">Auth status change event arguments</param>
    private void OnAuthStatusChanged(object? sender, AuthStatusChangedEventArgs e)
    {
        try
        {
            var statusMessage = e.IsLoggedIn switch
            {
                true when !e.WasLoggedIn => "User logged in",
                false when e.WasLoggedIn => "User logged out",
                true when e.WasLoggedIn => "User session updated",
                _ => "Authentication status unchanged"
            };

            var userInfo = e.User != null ? $" (User: {e.User.Email}, ID: {e.User.Id})" : "";

            _logger.LogInformation("{StatusMessage}{UserInfo} at {Timestamp}",
                statusMessage, userInfo, e.ChangedAt);

            // Additional monitoring could be added here:
            // - Send analytics events
            // - Update application state
            // - Trigger UI updates via event aggregator
        }
        catch (Exception ex)
        {
            // Ensure event handlers don't throw exceptions
            _logger.LogError(ex, "Error handling authentication status change event");
        }
    }

    /// <summary>
    /// Helper method to check if object is disposed
    /// </summary>
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>
    /// Dispose of resources using modern disposal pattern
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        // Unsubscribe from events to prevent memory leaks
        _authService.AuthStatusChanged -= OnAuthStatusChanged;

        _initSemaphore.Dispose();
        _disposed = true;

        _logger.LogInformation("AuthInitializationService disposed");
    }
}
