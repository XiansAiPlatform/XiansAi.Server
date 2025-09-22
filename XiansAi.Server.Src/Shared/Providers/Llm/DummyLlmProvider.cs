using Shared.Auth;
using Shared.Utils.GenAi;

namespace Shared.Providers;

/// <summary>
/// Dummy implementation of the LLM provider that echoes messages back
/// </summary>
public class DummyLlmProvider : ILlmProvider
{
    private readonly ILogger<DummyLlmProvider> _logger;
    private readonly LlmConfig _config;
    private readonly ITenantContext _tenantContext;

    /// <summary>
    /// Creates a new instance of the DummyLlmProvider
    /// </summary>
    /// <param name="config">LLM configuration</param>
    /// <param name="logger">Logger for the provider</param>
    /// <param name="tenantContext">Tenant context</param>
    public DummyLlmProvider(
        LlmConfig config,
        ILogger<DummyLlmProvider> logger,
        ITenantContext tenantContext)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    /// <summary>
    /// Gets the API key for the Dummy provider
    /// </summary>
    /// <returns>The API key</returns>
    public string GetApiKey()
    {
        return _config.ApiKey ?? "dummy-api-key";
    }

    /// <summary>
    /// Gets the name of the LLM provider
    /// </summary>
    /// <returns>The LLM provider</returns>
    public string? GetLlmProvider()
    {
        return _config.Provider ?? "dummy";
    }

    /// <summary>
    /// Gets the model for the Dummy provider
    /// </summary>
    /// <returns>The model</returns>
    public string GetModel()
    {
        return _config.Model ?? "dummy-echo-model";
    }

    /// <summary>
    /// Gets the additional details of the LLM provider
    /// </summary>
    /// <returns>Additional configuration details</returns>
    public Dictionary<string, string>? GetAdditionalConfig()
    {
        return _config.AdditionalConfig ?? new Dictionary<string, string>
        {
            { "Type", "Echo" },
            { "Description", "Dummy provider that echoes messages" }
        };
    }

    /// <summary>
    /// Gets Base URL of the Model
    /// </summary>
    /// <returns>Base URL</returns>
    public string? GetBaseUrl()
    {
        return _config.BaseUrl ?? "https://dummy-llm-provider.local";
    }

    /// <summary>
    /// Gets a chat completion from the Dummy provider by echoing the user's message
    /// </summary>
    /// <param name="messages">The chat messages</param>
    /// <param name="model">The model to use (ignored for dummy provider)</param>
    /// <returns>The echoed completion response</returns>
    public Task<string> GetChatCompletionAsync(List<ChatMessage> messages, string model = "dummy-echo-model")
    {
        _logger.LogInformation("DummyLlmProvider: Processing {MessageCount} messages with model {Model}", 
            messages.Count, model);

        if (messages == null || messages.Count == 0)
        {
            return Task.FromResult("I'm a dummy LLM provider. I didn't receive any messages to echo back.");
        }

        // Find the last user message to echo
        var lastUserMessage = messages.LastOrDefault(m => m.Role.Equals("user", StringComparison.OrdinalIgnoreCase));
        
        if (lastUserMessage != null)
        {
            var response = $"Echo from Dummy LLM: {lastUserMessage.Content}";
            _logger.LogInformation("DummyLlmProvider: Echoing user message: {Message}", lastUserMessage.Content);
            return Task.FromResult(response);
        }

        // If no user message found, create a summary of all messages
        var allContent = string.Join(" | ", messages.Select(m => $"{m.Role}: {m.Content}"));
        var allMessagesResponse = $"Echo from Dummy LLM - All messages: {allContent}";
        
        _logger.LogInformation("DummyLlmProvider: No user message found, echoing all messages");
        return Task.FromResult(allMessagesResponse);
    }
} 