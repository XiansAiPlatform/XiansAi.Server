using OpenAI.Chat;
using Microsoft.Extensions.Logging;
namespace XiansAi.Server.GenAi;

public interface IOpenAIClientService
{
    Task<string> GetChatCompletionAsync(List<ChatMessage> messages);
}

public class OpenAIClientService : IOpenAIClientService
{
    private readonly ILogger<OpenAIClientService>? _logger;
    private readonly IKeyVaultService _keyVaultService;
    private readonly OpenAIConfig _config;

    public OpenAIClientService(OpenAIConfig config, 
        IKeyVaultService keyVaultService,
        ILogger<OpenAIClientService>? logger = null)
    {
        _logger = logger;
        _keyVaultService = keyVaultService;
        _config = config;
    }

    public async Task<string> GetChatCompletionAsync(List<ChatMessage> messages)
    {
        string? apiKey = null;
        if (!string.IsNullOrEmpty(_config.ApiKeyKeyVaultName))
        {
            apiKey = await _keyVaultService.LoadSecret(_config.ApiKeyKeyVaultName);
        }
        else if (!string.IsNullOrEmpty(_config.ApiKey))
        {
            apiKey = _config.ApiKey;
        }
        else
        {
            throw new Exception("OpenAI ApiKey is not set");
        }
        var chatClient = new ChatClient(_config.Model, apiKey);
        var completion = await chatClient.CompleteChatAsync(messages);
        var text = completion.Value.Content[0].Text;
        return text;
    }

}
