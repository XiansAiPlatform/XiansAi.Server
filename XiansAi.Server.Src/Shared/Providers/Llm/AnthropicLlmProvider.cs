using Shared.Auth;
using Shared.Utils.GenAi;

namespace XiansAi.Server.Providers;

/// <summary>
/// Anthropic implementation of the LLM provider (placeholder implementation)
/// </summary>
public class AnthropicLlmProvider : ILlmProvider
{
    private readonly ILogger<AnthropicLlmProvider> _logger;
    private readonly LlmConfig _config;
    private readonly ITenantContext _tenantContext;

    /// <summary>
    /// Creates a new instance of the AnthropicLlmProvider
    /// </summary>
    /// <param name="config">LLM configuration</param>
    /// <param name="logger">Logger for the provider</param>
    /// <param name="tenantContext">Tenant context</param>
    public AnthropicLlmProvider(
        LlmConfig config,
        ILogger<AnthropicLlmProvider> logger,
        ITenantContext tenantContext)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    /// <summary>
    /// Gets the API key for the Anthropic provider
    /// </summary>
    /// <returns>The API key</returns>
    public string GetApiKey()
    {
        var apiKey = _config.ApiKey;
        if (string.IsNullOrEmpty(apiKey))
            throw new Exception("Anthropic ApiKey is not set");

        return apiKey;
    }

    /// <summary>
    /// Gets a chat completion from the Anthropic provider
    /// </summary>
    /// <param name="messages">The chat messages</param>
    /// <param name="model">The model to use</param>
    /// <returns>The completion response</returns>
    public Task<string> GetChatCompletionAsync(List<ChatMessage> messages, string model = "claude-3-sonnet-20240229")
    {
        throw new NotImplementedException();
    }
} 