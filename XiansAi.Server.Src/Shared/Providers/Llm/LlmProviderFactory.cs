using Shared.Auth;
using Shared.Utils.GenAi;

namespace XiansAi.Server.Providers;


/// <summary>
/// Implementation of LLM provider factory
/// </summary>
public class LlmProviderFactory 
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

} 