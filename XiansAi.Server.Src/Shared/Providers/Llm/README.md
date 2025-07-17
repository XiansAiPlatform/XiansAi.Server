# LLM Provider Pattern

## Overview

The LLM Provider Pattern in XiansAi.Server provides a flexible LLM system that creates the appropriate LLM provider based on configuration. The system allows easy integration of different LLM providers (OpenAI, Anthropic, etc.) without changing the application code.

## Architecture

### Core Components

```text
Shared/Providers/Llm/
├── ILlmProvider.cs                # Main LLM abstraction
├── ILlmProviderRegistration.cs    # Self-registration interface
├── OpenAILlmProvider.cs           # OpenAI implementation
├── AnthropicLlmProvider.cs        # Anthropic implementation (placeholder)
├── LlmProviderFactory.cs          # Factory for provider creation
└── README.md                      # This documentation
```

### 1. LLM Provider Interface

```csharp
public interface ILlmProvider
{
    string GetApiKey();
    Task<string> GetChatCompletionAsync(List<ChatMessage> messages, string model);
}
```

### 2. Provider Registration Interface

```csharp
public interface ILlmProviderRegistration
{
    static abstract string ProviderName { get; }
    static abstract bool CanRegister(IConfiguration configuration);
    static abstract void RegisterServices(IServiceCollection services, IConfiguration configuration);
}
```

### 3. LLM Service Interface

```csharp
public interface ILlmService
{
    string GetApiKey();
    Task<string> GetChatCompletionAsync(List<ChatMessage> messages, string model);
}
```

### 4. Provider Implementations

#### OpenAI Provider

- **Production Ready**: Fully implemented with OpenAI SDK
- **Configuration**: Uses LLM configuration with Provider = "OpenAI"
- **Models**: Supports all OpenAI models (default: gpt-4o-mini)

### Example Configuration

```json
{
  "Llm": {
    "Provider": "OpenAI",
    "ApiKey": "your-api-key-here"
  }
}
```

#### Anthropic Provider

- **Placeholder**: Currently a placeholder implementation
- **Configuration**: Uses LLM configuration with Provider = "Anthropic"
- **Models**: Supports Claude models (default: claude-3-sonnet-20240229)

#### Azure OpenAI Provider

- **Production Ready**: Uses Azure OpenAI REST API
- **Configuration**: Uses LLM configuration with Provider = "AzureOpenAI"
- **Models**: Supports all Azure OpenAI deployed models (e.g., gpt-35-turbo, gpt-4)

##### Example Configuration

```json
"Llm": {
  "Provider": "AzureOpenAI",
  "ApiKey": "YOUR_AZURE_OPENAI_KEY",
  "Model": "MODEL_NAME",
  "Endpoint": "https://resourcename.openai.azure.com/",
  "AdditionalConfig": {
    "DeploymentName": "YOUR_DEPLOYMENT_NAME",
    "ApiVersion": "VERSION"
  }
}
```


### Switching Providers

To switch to Anthropic:

```json
{
  "Llm": {
    "Provider": "Anthropic",
    "ApiKey": "your-anthropic-api-key"
  }
}
```

## Usage

### Application Startup

```csharp
// In SharedServices.cs - automatically registered when you call:
services.AddInfrastructureServices(configuration);

// This automatically handles:
// - LlmProviderFactory.RegisterProviders(services, configuration);
// - services.AddSingleton<ILlmProviderFactory, LlmProviderFactory>();
// - services.AddScoped<ILlmService, LlmService>();
```

### Service Usage

```csharp
public class MyService
{
    private readonly ILlmService _llmService;

    public MyService(ILlmService llmService)
    {
        _llmService = llmService;
    }

    public async Task<string> GenerateResponseAsync(string userMessage)
    {
        var messages = new List<ChatMessage>
        {
            new ChatMessage { Role = "system", Content = "You are a helpful assistant." },
            new ChatMessage { Role = "user", Content = userMessage }
        };

        return await _llmService.GetChatCompletionAsync(messages, "gpt-4o-mini");
    }
}
```

## Adding New Providers

### Step 1: Implement the Provider

```csharp
public class CustomLlmProvider : ILlmProvider, ILlmProviderRegistration
{
    public static string ProviderName => "CustomLLM";

    public static bool CanRegister(IConfiguration configuration)
    {
        var llmConfig = configuration.GetSection("Llm").Get<LlmConfig>();
        return llmConfig != null && 
               !string.IsNullOrEmpty(llmConfig.ApiKey) && 
               llmConfig.Provider.Equals("CustomLLM", StringComparison.OrdinalIgnoreCase);
    }

    public static void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        // Register any custom services needed
        services.AddHttpClient<CustomLlmClient>();
    }

    // Implement ILlmProvider methods...
}
```

### Step 2: Add to Factory

```csharp
public ILlmProvider CreateLlmProvider()
{
    var configuration = _serviceProvider.GetRequiredService<IConfiguration>();
    var llmConfig = GetLlmConfigFromConfiguration(configuration);

    return llmConfig.Provider.ToLower() switch
    {
        "openai" => CreateOpenAIProvider(llmConfig),
        "anthropic" => CreateAnthropicProvider(llmConfig),
        "customllm" => CreateCustomProvider(llmConfig),
        _ => throw new InvalidOperationException($"Unsupported LLM provider: {llmConfig.Provider}")
    };
}
```

### Step 3: Add Configuration

```json
{
  "Llm": {
    "Provider": "CustomLLM",
    "ApiKey": "your-custom-api-key"
  }
}
```

## Migration from OpenAI-Specific Code

### Before (Tightly Coupled)

```csharp
public class MyService
{
    private readonly IOpenAIClientService _openAIService;

    public MyService(IOpenAIClientService openAIService)
    {
        _openAIService = openAIService;
    }

    public async Task<string> GenerateAsync(List<ChatMessage> messages)
    {
        return await _openAIService.GetChatCompletionAsync(messages, "gpt-4o-mini");
    }
}
```

### After (Provider Pattern)

```csharp
public class MyService
{
    private readonly ILlmService _llmService;

    public MyService(ILlmService llmService)
    {
        _llmService = llmService;
    }

    public async Task<string> GenerateAsync(List<ChatMessage> messages)
    {
        return await _llmService.GetChatCompletionAsync(messages, "gpt-4o-mini");
    }
}
```

## Benefits

1. **Provider Agnostic**: Application code doesn't know about specific LLM providers
2. **Easy Extension**: Add new providers without changing existing code
3. **Simple Configuration**: Single configuration determines which provider to use
4. **Clean Architecture**: No complex priority systems, just direct provider selection
5. **Maintainable**: Simple switch-based provider creation 