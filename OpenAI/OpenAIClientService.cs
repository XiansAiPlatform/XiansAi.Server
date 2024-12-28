using OpenAI.Chat;
using Microsoft.Extensions.Logging;


public interface IOpenAIClientService
{
    Task<string> GetChatCompletionAsync(List<ChatMessage> messages);
}

public class OpenAIClientService : IOpenAIClientService
{
    private readonly ChatClient _chatClient;
    private readonly ILogger<OpenAIClientService>? _logger;
    public OpenAIClientService(OpenAIConfig config, ILogger<OpenAIClientService>? logger = null)
    {
        _logger = logger;
        _logger?.LogInformation("OpenAIClientService constructor initiated with model: {0} and apiKey: {1}...", config.Model, config.ApiKey.Substring(0, 2) + "..." + config.ApiKey.Substring(config.ApiKey.Length - 2));
        _chatClient = new ChatClient(config.Model, config.ApiKey);
    }

    public async Task<string> GetChatCompletionAsync(List<ChatMessage> messages)
    {
        var completion = await _chatClient.CompleteChatAsync(messages);
        var text = completion.Value.Content[0].Text;
        return text;
    }

}
