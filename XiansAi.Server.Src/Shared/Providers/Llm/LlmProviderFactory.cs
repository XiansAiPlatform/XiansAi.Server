using Shared.Auth;
using Shared.Utils.GenAi;

namespace XiansAi.Server.Providers;

/// <summary>
/// Factory for creating LLM providers based on configuration
/// </summary>
public interface ILlmProviderFactory
{
    /// <summary>
    /// Creates an LLM provider based on the current configuration
    /// </summary>
    /// <returns>The appropriate LLM provider implementation</returns>
    ILlmProvider CreateLlmProvider();
}

/// <summary>
/// Implementation of LLM provider factory
/// </summary>
public class LlmProviderFactory : ILlmProviderFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LlmProviderFactory> _logger;

    public LlmProviderFactory(
        IServiceProvider serviceProvider,
        ILogger<LlmProviderFactory> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Registers the LLM provider based on configuration
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Application configuration</param>
    public static void RegisterProvider(IServiceCollection services, IConfiguration configuration)
    {
        var llmConfig = configuration.GetSection("Llm").Get<LlmConfig>();
        if (llmConfig == null || string.IsNullOrWhiteSpace(llmConfig.Provider))
        {
            throw new InvalidOperationException("Llm configuration is missing or Provider is not specified");
        }

        // Register the config
        services.AddSingleton(llmConfig);

        // Register the appropriate provider based on configuration
        switch (llmConfig.Provider.ToLowerInvariant())
        {
            case "openai":
                services.AddScoped<ILlmProvider, OpenAILlmProvider>();
                break;
            case "anthropic":
                services.AddScoped<ILlmProvider, AnthropicLlmProvider>();
                break;
            default:
                throw new InvalidOperationException($"Unsupported LLM provider: {llmConfig.Provider}");
        }
    }

    /// <summary>
    /// Creates an LLM provider based on the configured provider type
    /// </summary>
    /// <returns>The appropriate LLM provider implementation</returns>
    public ILlmProvider CreateLlmProvider()
    {
        return _serviceProvider.GetRequiredService<ILlmProvider>();
    }
} 