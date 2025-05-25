namespace XiansAi.Server.Providers;

/// <summary>
/// Interface for cache providers to handle their own service registration
/// </summary>
public interface ICacheProviderRegistration
{
    /// <summary>
    /// Gets the name of this provider
    /// </summary>
    static abstract string ProviderName { get; }

    /// <summary>
    /// Determines if this provider can be registered with the given configuration
    /// </summary>
    /// <param name="configuration">Application configuration</param>
    /// <returns>True if the provider can be registered, false otherwise</returns>
    static abstract bool CanRegister(IConfiguration configuration);

    /// <summary>
    /// Registers the services required by this provider
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Application configuration</param>
    static abstract void RegisterServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>
    /// Gets the priority of this provider (lower numbers = higher priority)
    /// </summary>
    static abstract int Priority { get; }
} 