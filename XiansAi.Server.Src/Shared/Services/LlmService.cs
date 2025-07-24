using XiansAi.Server.Providers;

namespace Shared.Services;

/// <summary>
/// LLM service that uses the provider pattern for flexible LLM implementations
/// </summary>
public interface ILlmService
{
    /// <summary>
    /// Gets the API key for the current LLM provider
    /// </summary>
    /// <returns>The API key</returns>
    string GetApiKey();

    /// <summary>
    /// Gets the name of the LLM provider
    /// </summary>
    /// <returns>The LLM provider</returns>
    string? GetLlmProvider();

    /// <summary>
    /// Gets the model for the current LLM provider
    /// </summary>
    /// <returns>The model</returns>
    string GetModel();

    /// <summary>
    /// Gets the additional details of the LLM provider
    /// </summary>
    /// <returns>Additional configuration details</returns>
    Dictionary<string, string>? GetAdditionalConfig();

    /// <summary>
    /// Gets Base URL of the Model
    /// </summary>
    /// <returns>Base URL</returns>
    string? GetBaseUrl();

    /// <summary>
    /// Gets a chat completion from the current LLM provider
    /// </summary>
    /// <param name="messages">The chat messages</param>
    /// <param name="model">The model to use</param>
    /// <returns>The completion response</returns>
    Task<string> GetChatCompletionAsync(List<ChatMessage> messages, string model);
}

/// <summary>
/// Implementation of LLM service using the provider pattern
/// </summary>
public class LlmService : ILlmService
{
    private readonly ILlmProvider _llmProvider;
    private readonly ILogger<LlmService> _logger;

    /// <summary>
    /// Creates a new instance of the LlmService
    /// </summary>
    /// <param name="llmProvider">Factory for creating LLM providers</param>
    /// <param name="logger">Logger for the service</param>
    public LlmService(
        ILlmProvider llmProvider,
        ILogger<LlmService> logger)
    {
        _llmProvider = llmProvider ?? throw new ArgumentNullException(nameof(llmProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets the API key for the current LLM provider
    /// </summary>
    /// <returns>The API key</returns>
    public string GetApiKey()
    {
        try
        {
            return _llmProvider.GetApiKey();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting API key from LLM provider");
            throw new Exception("Error getting API key from LLM provider", ex);
        }
    }

    /// <summary>
    /// Gets the name of the LLM provider
    /// </summary>
    /// <returns>The LLM provider</returns>
    public string? GetLlmProvider()
    {
        try
        {
            return _llmProvider.GetLlmProvider();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting API key from LLM provider");
            throw new Exception("Error getting API key from LLM provider", ex);
        }
    }

    /// <summary>
    /// Gets the model for the current LLM provider
    /// </summary>
    /// <returns>The model</returns>
    public string GetModel()
    {
        try {
            var model = _llmProvider.GetModel();
            if (string.IsNullOrEmpty(model))
            {
                _logger.LogError("Model is not set for LLM provider");
                throw new Exception("Model is not set for LLM provider");
            }
            return model;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting model from LLM provider");
            throw new Exception("Error getting model from LLM provider", ex);
        }
    }

    /// <summary>
    /// Gets the additional details of the LLM provider
    /// </summary>
    /// <returns>Additional configuration details</returns>
    public Dictionary<string, string>? GetAdditionalConfig()
    {
        try {
            return _llmProvider.GetAdditionalConfig();            
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting model from LLM provider");
            throw new Exception("Error getting model from LLM provider", ex);
        }
    }

    /// <summary>
    /// Gets Base URL of the Model
    /// </summary>
    /// <returns>Base URL</returns>
    public string? GetBaseUrl()
    {
        try
        {
            return _llmProvider.GetBaseUrl();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Base URL from LLM provider");
            return null; // Optional behavior
        }
    }

    /// <summary>
    /// Gets a chat completion from the current LLM provider
    /// </summary>
    /// <param name="messages">The chat messages</param>
    /// <param name="model">The model to use</param>
    /// <returns>The completion response</returns>
    public async Task<string> GetChatCompletionAsync(List<ChatMessage> messages, string model)
    {
        try
        {
            return await _llmProvider.GetChatCompletionAsync(messages, model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting chat completion from LLM provider with model {Model}", model);
            throw new Exception("Error getting chat completion from LLM provider", ex);
        }
    }
} 