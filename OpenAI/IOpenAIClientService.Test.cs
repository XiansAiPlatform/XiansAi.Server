using OpenAI.Chat;
using DotNetEnv;
using Xunit;

public class OpenAIClientServiceTests
{
    private readonly IOpenAIClientService _service;

    public OpenAIClientServiceTests()
    {
        // Load environment variables from .env file
        Env.Load();
        
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? throw new Exception("OPENAI_API_KEY Environment variable is not set");
        var model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4";

        var config = new OpenAIConfig
        {
            Model = model,
            ApiKey = apiKey
        };

        _service = new OpenAIClientService(config);
    }


    /*
    dotnet test --filter "FullyQualifiedName~OpenAIClientServiceTests" -v d
    */
    [Fact]
    public async Task GetChatCompletionAsync_WithValidMessage_ReturnsResponse()
    {
        // Arrange
        List<ChatMessage> messages = 
        [
            new SystemChatMessage("You are a helpful assistant."),
            new UserChatMessage("What's the weather like today?"),
        ];

        // Act
        var response = await _service.GetChatCompletionAsync(messages);

        Console.WriteLine($"Response: {response}");

        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response);
    }
}
