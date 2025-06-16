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
    /// Gets the model for the LLM provider
    /// </summary>
    /// <returns>The model</returns>
    string GetModel();

    /// <summary>
    /// Gets a chat completion from the LLM provider
    /// </summary>
    /// <param name="messages">The chat messages</param>
    /// <param name="model">The model to use</param>
    /// <returns>The completion response</returns>
    Task<string> GetChatCompletionAsync(List<ChatMessage> messages, string model);

    /// <summary>
    /// Gets a structured chat completion from the LLM provider
    /// </summary>
    /// <typeparam name="T">The type to deserialize the response to</typeparam>
    /// <param name="messages">The chat messages</param>
    /// <param name="model">The model to use</param>
    /// <returns>The completion response deserialized to type T</returns>
    Task<T> GetStructuredChatCompletionAsync<T>(List<ChatMessage> messages, string model) where T : class;
}

/// <summary>
/// Represents a chat message for LLM providers
/// </summary>
public class ChatMessage
{
    public required string Role { get; set; }
    public required string Content { get; set; }
} 