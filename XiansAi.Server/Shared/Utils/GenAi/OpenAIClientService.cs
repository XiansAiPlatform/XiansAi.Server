using OpenAI.Chat;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using XiansAi.Server.Auth;
using Shared.Auth;
namespace XiansAi.Server.GenAi;

public interface IOpenAIClientService
{
    Task<string> GetChatCompletionAsync(List<ChatMessage> messages);
}

public class OpenAIClientService : IOpenAIClientService
{
    private static readonly ConcurrentDictionary<string, ChatClient> _clients = new();
    private readonly ILogger<OpenAIClientService>? _logger;
    private readonly OpenAIConfig _config;
    private readonly ITenantContext _tenantContext;

    public OpenAIClientService(OpenAIConfig config, 
        ILogger<OpenAIClientService>? logger,
        ITenantContext tenantContext)
    {
        _logger = logger;
        _config = config;
        _tenantContext = tenantContext;
    }

    public async Task<string> GetChatCompletionAsync(List<ChatMessage> messages)
    {
        var tenantId = _tenantContext.TenantId;
        var chatClient = _clients.GetOrAdd(tenantId, _ =>
        {
            var apiKey = _config.ApiKey;

            if (string.IsNullOrEmpty(apiKey))
                throw new Exception("OpenAI ApiKey is not set");

            return new ChatClient(_config.Model, apiKey);
        });

        var completion = await chatClient.CompleteChatAsync(messages);
        return completion.Value.Content[0].Text;
    }

}
