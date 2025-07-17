namespace XiansAi.Server.Providers;

/// <summary>
/// Interface for LLM providers that abstracts LLM implementation details
/// </summary>
public interface ILlmProvider
{
    /// <summary>
    /// Gets the API key for the LLM provider
    /// </summary>
    /// <returns>The API key</returns>
    string GetApiKey();

    /// <summary>
    /// Gets the name of the LLM provider
    /// </summary>
    /// <returns>The LLM provider</returns>
    string? GetLlmProvider();

    /// <summary>
    /// Gets the model for the LLM provider
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
    /// Gets a chat completion from the LLM provider
    /// </summary>
    /// <param name="messages">The chat messages</param>
    /// <param name="model">The model to use</param>
    /// <returns>The completion response</returns>
    Task<string> GetChatCompletionAsync(List<ChatMessage> messages, string model);
}

/// <summary>
/// Represents a chat message for LLM providers
/// </summary>
public class ChatMessage
{
    public required string Role { get; set; }
    public required string Content { get; set; }
} 