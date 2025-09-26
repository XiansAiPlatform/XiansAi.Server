using Shared.Auth;
using Shared.Utils.GenAi;

namespace Shared.Providers;

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
    /// Gets the name of the LLM provider
    /// </summary>
    /// <returns>The LLM provider</returns>
    public string GetLlmProvider()
    {
        return _config.Provider ?? string.Empty;
    }

    /// <summary>
    /// Gets the model for the Anthropic provider
    /// </summary>
    /// <returns>The model</returns>
    public string GetModel()
    {
        return _config.Model;
    }

    /// <summary>
    /// Gets the additional details of the LLM provider
    /// </summary>
    /// <returns>Additional configuration details</returns>
    public Dictionary<string, string> GetAdditionalConfig()
    {
        if (_config.AdditionalConfig == null || _config.AdditionalConfig.Count == 0)
        {
            _logger.LogWarning("Additional configuration is missing or empty for LLM provider");
            return new Dictionary<string, string>();
        }

        return _config.AdditionalConfig;
    }

    /// <summary>
    /// Gets Base URL of the Model
    /// </summary>
    /// <returns>Base URL</returns>
    public string GetBaseUrl()
    {
        return _config.BaseUrl ?? string.Empty;
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