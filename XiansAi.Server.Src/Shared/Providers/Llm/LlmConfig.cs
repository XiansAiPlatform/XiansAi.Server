namespace Shared.Utils.GenAi;

/// <summary>
/// Generic LLM configuration that can be used for any LLM provider
/// </summary>
public class LlmConfig
{
    /// <summary>
    /// The API key for the LLM provider
    /// </summary>
    public required string ApiKey { get; set; }

    /// <summary>
    /// The provider type (e.g., "OpenAI", "Anthropic", etc.)
    /// </summary>
    public string Provider { get; set; } = "OpenAI";

    /// <summary>
    /// The base URL for the LLM provider (optional, for custom endpoints)
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Additional provider-specific configuration
    /// </summary>
    public Dictionary<string, string>? AdditionalConfig { get; set; }
} 