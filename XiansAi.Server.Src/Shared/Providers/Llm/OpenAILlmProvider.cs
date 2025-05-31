using OpenAI.Chat;
using System.Collections.Concurrent;
using Shared.Auth;
using Shared.Utils.GenAi;

namespace XiansAi.Server.Providers;

/// <summary>
/// OpenAI implementation of the LLM provider
/// </summary>
public class OpenAILlmProvider : ILlmProvider
{
    private static readonly ConcurrentDictionary<string, ChatClient> _clients = new();
    private readonly ILogger<OpenAILlmProvider> _logger;
    private readonly LlmConfig _config;
    private readonly ITenantContext _tenantContext;

    /// <summary>
    /// Creates a new instance of the OpenAILlmProvider
    /// </summary>
    /// <param name="config">LlmConfig (specifically, OpenAI related settings will be used from here)</param>
    /// <param name="logger">Logger for the provider</param>
    /// <param name="tenantContext">Tenant context</param>
    public OpenAILlmProvider(
        LlmConfig config,
        ILogger<OpenAILlmProvider> logger,
        ITenantContext tenantContext)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    /// <summary>
    /// Gets the API key for the OpenAI provider
    /// </summary>
    /// <returns>The API key</returns>
    public string GetApiKey()
    {
        var apiKey = _config.ApiKey;
        if (string.IsNullOrEmpty(apiKey))
            throw new Exception("OpenAI ApiKey is not set");

        return apiKey;
    }

    /// <summary>
    /// Gets the model for the OpenAI provider
    /// </summary>
    /// <returns>The model</returns>
    public string GetModel()
    {
        return _config.Model;
    }

    /// <summary>
    /// Gets a chat completion from the OpenAI provider
    /// </summary>
    /// <param name="messages">The chat messages</param>
    /// <param name="model">The model to use</param>
    /// <returns>The completion response</returns>
    public async Task<string> GetChatCompletionAsync(List<ChatMessage> messages, string model = "gpt-4o-mini")
    {
        var tenantId = _tenantContext.TenantId;
        var chatClient = _clients.GetOrAdd(tenantId, _ =>
        {
            var apiKey = GetApiKey();
            return new ChatClient(model, apiKey);
        });

        // Convert our generic ChatMessage to OpenAI's ChatMessage
        var openAIMessages = messages.Select<ChatMessage, OpenAI.Chat.ChatMessage>(m => m.Role.ToLower() switch
        {
            "system" => new SystemChatMessage(m.Content),
            "user" => new UserChatMessage(m.Content),
            "assistant" => new AssistantChatMessage(m.Content),
            _ => throw new ArgumentException($"Unknown message role: {m.Role}")
        }).ToList();

        var completion = await chatClient.CompleteChatAsync(openAIMessages);
        return completion.Value.Content[0].Text;
    }
} 