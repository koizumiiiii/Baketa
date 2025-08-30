using Baketa.Core.Abstractions.Auth;
using Baketa.Core.Abstractions.DI;
using Baketa.Core.DI;
using Baketa.Core.DI.Attributes;
using Baketa.Core.DI.Modules;
using Baketa.Core.Settings;
using Baketa.Infrastructure.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Supabase;

namespace Baketa.Infrastructure.DI.Modules;

/// <summary>
/// Authentication module for registering authentication and user management services
/// Provides comprehensive authentication infrastructure using Supabase
/// </summary>
[ModulePriority(ModulePriority.Infrastructure)]
public sealed class AuthModule : ServiceModuleBase
{
    /// <summary>
    /// Register authentication services using modern C# 12 patterns
    /// </summary>
    /// <param name="services">Service collection</param>
    public override void RegisterServices(IServiceCollection services)
    {
        var environment = BaketaEnvironment.Production; // TODO: Get from configuration

        // Core authentication services
        RegisterAuthenticationServices(services);
        
        // User management services
        RegisterUserServices(services);
        
        // Authentication configuration
        RegisterAuthConfiguration(services);
        
        // Background services
        RegisterBackgroundServices(services);
        
        // Environment-specific services
        RegisterEnvironmentSpecificServices(services, environment);
    }

    /// <summary>
    /// Register core authentication services
    /// </summary>
    /// <param name="services">Service collection</param>
    private static void RegisterAuthenticationServices(IServiceCollection services)
    {
        // Supabase client registration
        services.AddSingleton<Supabase.Client>(provider =>
        {
            var authSettings = provider.GetRequiredService<IOptions<AuthSettings>>().Value;
            var options = new Supabase.SupabaseOptions
            {
                AutoRefreshToken = authSettings.AutoRefreshSession,
                AutoConnectRealtime = false // Set to false for auth-only usage
            };
            return new Supabase.Client(authSettings.SupabaseUrl, authSettings.SupabaseAnonKey, options);
        });

        // Primary authentication service
        services.AddSingleton<SupabaseAuthService>();
        services.AddSingleton<IAuthService>(provider => provider.GetRequiredService<SupabaseAuthService>());

        // Authentication event handlers (future extension)
        // services.AddSingleton<IAuthEventHandler, DefaultAuthEventHandler>();

        // Authentication cache services (future extension)
        // services.AddSingleton<IAuthTokenCache, MemoryAuthTokenCache>();
    }

    /// <summary>
    /// Register user management services
    /// </summary>
    /// <param name="services">Service collection</param>
    private static void RegisterUserServices(IServiceCollection services)
    {
        // User profile management
        services.AddSingleton<SupabaseUserService>();
        services.AddSingleton<IUserService>(provider => provider.GetRequiredService<SupabaseUserService>());

        // User analytics and statistics (future extension)
        // services.AddSingleton<IUserAnalyticsService, UserAnalyticsService>();

        // User preferences management (future extension)
        // services.AddSingleton<IUserPreferencesService, UserPreferencesService>();
    }

    /// <summary>
    /// Register authentication configuration services
    /// </summary>
    /// <param name="services">Service collection</param>
    private static void RegisterAuthConfiguration(IServiceCollection services)
    {
        // Authentication settings validation
        services.AddSingleton<IValidateOptions<AuthSettings>, AuthSettingsValidator>();

        // Authentication settings accessor (future extension)
        // services.AddSingleton<IAuthSettingsProvider, AuthSettingsProvider>();

        // OAuth configuration (future extension)
        // services.AddSingleton<IOAuthConfigurationProvider, OAuthConfigurationProvider>();
    }

    /// <summary>
    /// Register background services for authentication
    /// </summary>
    /// <param name="services">Service collection</param>
    private static void RegisterBackgroundServices(IServiceCollection services)
    {
        // Authentication initialization service
        services.AddSingleton<AuthInitializationService>();
        // TEMPORARY: Disable AuthInitializationService for Phase 1 testing
        // Phase 1 Goal: Focus on translation pipeline functionality
        // Will re-enable after Phase 4 completion for premium membership features
        // services.AddHostedService<AuthInitializationService>();

        // Session refresh service (future extension)
        // services.AddSingleton<SessionRefreshService>();
        // services.AddHostedService<SessionRefreshService>();

        // Authentication analytics service (future extension)
        // services.AddSingleton<AuthAnalyticsService>();
        // services.AddHostedService<AuthAnalyticsService>();
    }

    /// <summary>
    /// Register environment-specific authentication services
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="environment">Application environment</param>
    private static void RegisterEnvironmentSpecificServices(IServiceCollection services, BaketaEnvironment environment)
    {
        // Environment-specific authentication behavior
        switch (environment)
        {
            case BaketaEnvironment.Development:
                RegisterDevelopmentServices(services);
                break;
                
            case BaketaEnvironment.Test:
                RegisterTestServices(services);
                break;
                
            case BaketaEnvironment.Production:
                RegisterProductionServices(services);
                break;
                
            default:
                throw new ArgumentOutOfRangeException(nameof(environment), environment, "Unsupported environment");
        }
    }

    /// <summary>
    /// Register development-specific authentication services
    /// </summary>
    /// <param name="_">Service collection</param>
    private static void RegisterDevelopmentServices(IServiceCollection _)
    {
        // Development auth debugging tools
        // services.AddSingleton<IAuthDebugService, AuthDebugService>();
        
        // Mock services for offline development
        // services.AddSingleton<IMockAuthProvider, MockAuthProvider>();
        
        // Development auth logging
        // services.AddSingleton<IAuthLogger, VerboseAuthLogger>();
    }

    /// <summary>
    /// Register test-specific authentication services
    /// </summary>
    /// <param name="_">Service collection</param>
    private static void RegisterTestServices(IServiceCollection _)
    {
        // Test authentication providers
        // services.AddSingleton<ITestAuthProvider, InMemoryTestAuthProvider>();
        
        // Test user factories
        // services.AddSingleton<ITestUserFactory, TestUserFactory>();
        
        // Authentication state reset services
        // services.AddSingleton<IAuthStateResetService, AuthStateResetService>();
    }

    /// <summary>
    /// Register production-specific authentication services
    /// </summary>
    /// <param name="_">Service collection</param>
    private static void RegisterProductionServices(IServiceCollection _)
    {
        // Production monitoring
        // services.AddSingleton<IAuthMonitoringService, AuthMonitoringService>();
        
        // Security audit logging
        // services.AddSingleton<IAuthAuditLogger, AuthAuditLogger>();
        
        // Production rate limiting
        // services.AddSingleton<IAuthRateLimiter, AuthRateLimiter>();
    }

    /// <summary>
    /// Get dependent modules for this authentication module
    /// </summary>
    /// <returns>Collection of dependent module types</returns>
    public override IEnumerable<Type> GetDependentModules()
    {
        yield return typeof(CoreModule);
        // Add InfrastructureModule dependency once it includes required services
        // yield return typeof(InfrastructureModule);
    }
}

/// <summary>
/// Authentication settings validator for dependency injection
/// </summary>
public sealed class AuthSettingsValidator : IValidateOptions<AuthSettings>
{
    /// <summary>
    /// Validate authentication settings
    /// </summary>
    /// <param name="name">Settings name</param>
    /// <param name="options">Authentication settings</param>
    /// <returns>Validation result</returns>
    public ValidateOptionsResult Validate(string? name, AuthSettings options)
    {
        var validationResult = options.ValidateSettings();
        
        if (!validationResult.IsValid)
        {
            var errors = validationResult.GetErrorMessages();
            return ValidateOptionsResult.Fail($"Authentication settings validation failed: {errors}");
        }
        
        if (validationResult.HasWarnings)
        {
            var _ = validationResult.GetWarningMessages();
            // Log warnings but don't fail validation
            // TODO: Add logging when available
            // _logger.LogWarning("Authentication settings warnings: {Warnings}", warnings);
        }
        
        return ValidateOptionsResult.Success;
    }
}