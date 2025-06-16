using OpenAI.Chat;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
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

    /// <summary>
    /// Gets a structured chat completion from the OpenAI provider using JSON schema
    /// </summary>
    /// <typeparam name="T">The type to deserialize the response to</typeparam>
    /// <param name="messages">The chat messages</param>
    /// <param name="model">The model to use</param>
    /// <returns>The completion response deserialized to type T</returns>
    public async Task<T> GetStructuredChatCompletionAsync<T>(List<ChatMessage> messages, string model = "gpt-4o-mini") where T : class
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

        try
        {
            // Create chat completion options with structured output
            var options = new ChatCompletionOptions
            {
                ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                    jsonSchemaFormatName: typeof(T).Name.ToLower(),
                    jsonSchema: BinaryData.FromString(GenerateJsonSchema<T>()),
                    jsonSchemaIsStrict: true
                )
            };

            _logger.LogInformation("Requesting structured output for type {TypeName} from tenant {TenantId}", typeof(T).Name, tenantId);

            var completion = await chatClient.CompleteChatAsync(openAIMessages, options);
            var jsonContent = completion.Value.Content[0].Text;

            // Deserialize the guaranteed valid JSON to our target type
            var result = JsonSerializer.Deserialize<T>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (result == null)
            {
                throw new InvalidOperationException($"Failed to deserialize response to type {typeof(T).Name}");
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting structured chat completion for type {TypeName} from tenant {TenantId}", typeof(T).Name, tenantId);
            throw;
        }
    }

    /// <summary>
    /// Generates a JSON schema for the given type
    /// </summary>
    /// <typeparam name="T">The type to generate schema for</typeparam>
    /// <returns>JSON schema string</returns>
    private string GenerateJsonSchema<T>() where T : class
    {
        // For now, we'll create a basic schema. In a production environment,
        // you might want to use a library like NJsonSchema or System.Text.Json.Schema
        var typeName = typeof(T).Name;
        
        // We'll use reflection to build a basic schema
        var properties = typeof(T).GetProperties()
            .Where(p => p.CanWrite)
            .ToDictionary(
                p => JsonNamingPolicy.CamelCase.ConvertName(p.Name),
                p => GetPropertySchema(p.PropertyType)
            );

        // Get required properties based on [Required] attribute or required modifier
        var requiredProperties = typeof(T).GetProperties()
            .Where(p => p.CanWrite && IsPropertyRequired(p))
            .Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name))
            .ToArray();

        var schema = new
        {
            type = "object",
            properties = properties,
            required = requiredProperties,
            additionalProperties = false
        };

        return JsonSerializer.Serialize(schema, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });
    }

    /// <summary>
    /// Gets the JSON schema for a property type
    /// </summary>
    /// <param name="propertyType">The property type</param>
    /// <returns>Schema object for the property</returns>
    private object GetPropertySchema(Type propertyType)
    {
        // Handle nullable types
        if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            propertyType = Nullable.GetUnderlyingType(propertyType)!;
        }

        // Handle arrays and lists
        if (propertyType.IsArray || (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(List<>)))
        {
            var elementType = propertyType.IsArray ? propertyType.GetElementType()! : propertyType.GetGenericArguments()[0];
            return new
            {
                type = "array",
                items = GetPropertySchema(elementType)
            };
        }

        // Handle basic types
        if (propertyType == typeof(string))
            return new { type = "string" };
        if (propertyType == typeof(int) || propertyType == typeof(long))
            return new { type = "integer" };
        if (propertyType == typeof(double) || propertyType == typeof(float) || propertyType == typeof(decimal))
            return new { type = "number" };
        if (propertyType == typeof(bool))
            return new { type = "boolean" };
        if (propertyType == typeof(DateTime))
            return new { type = "string", format = "date-time" };

        // Handle complex objects recursively
        if (propertyType.IsClass && propertyType != typeof(string))
        {
            var nestedProperties = propertyType.GetProperties()
                .Where(p => p.CanWrite)
                .ToDictionary(
                    p => JsonNamingPolicy.CamelCase.ConvertName(p.Name),
                    p => GetPropertySchema(p.PropertyType)
                );

            // Get required properties for nested objects
            var requiredProperties = propertyType.GetProperties()
                .Where(p => p.CanWrite && IsPropertyRequired(p))
                .Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name))
                .ToArray();

            return new
            {
                type = "object",
                properties = nestedProperties,
                required = requiredProperties,
                additionalProperties = false
            };
        }

        // Default to string for unknown types
        return new { type = "string" };
    }

    /// <summary>
    /// Determines if a property is required based on [Required] attribute or required modifier
    /// </summary>
    /// <param name="property">The property to check</param>
    /// <returns>True if the property is required</returns>
    private bool IsPropertyRequired(System.Reflection.PropertyInfo property)
    {
        // Check for [Required] attribute
        if (property.GetCustomAttributes(typeof(RequiredAttribute), false).Any())
        {
            return true;
        }

        // For our specific LLM models, all properties with the 'required' keyword should be marked as required
        // Since we can't easily detect the required modifier via reflection, we'll check the declaring type
        var declaringType = property.DeclaringType;
        if (declaringType != null && 
            (declaringType.Name == "LlmAgentCodeResponse" || declaringType.Name == "LlmGeneratedFile"))
        {
            // All properties in our LLM models are required
            return true;
        }

        // Check if the property type is non-nullable reference type
        var propertyType = property.PropertyType;
        
        // If it's a nullable value type (like int?), it's not required
        if (propertyType.IsGenericType && propertyType.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            return false;
        }

        // For other types, be conservative and only require if explicitly marked
        return false;
    }
} 