using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Shared.Auth;
using Shared.Utils.GenAi;

namespace XiansAi.Server.Providers;

/// <summary>
/// Azure OpenAI implementation of the LLM provider
/// </summary>
public class AzureOpenAILlmProvider : ILlmProvider
{
    private readonly ILogger<AzureOpenAILlmProvider> _logger;
    private readonly LlmConfig _config;
    private readonly ITenantContext _tenantContext;
    private readonly HttpClient _httpClient;
    private readonly string _deploymentName;
    private readonly string _apiVersion;

    public AzureOpenAILlmProvider(
        LlmConfig config,
        ILogger<AzureOpenAILlmProvider> logger,
        ITenantContext tenantContext,
        IHttpClientFactory httpClientFactory)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _httpClient = httpClientFactory.CreateClient();
        _deploymentName = config.AdditionalConfig != null && config.AdditionalConfig.TryGetValue("DeploymentName", out var dep) ? dep : throw new ArgumentException("DeploymentName must be set in AdditionalConfig for AzureOpenAI");
        _apiVersion = config.AdditionalConfig != null && config.AdditionalConfig.TryGetValue("ApiVersion", out var ver) ? ver : "2024-02-15-preview";
    }

    /// <summary>
    /// Gets the API key for the Anthropic provider
    /// </summary>
    /// <returns>The API key</returns>
    public string GetApiKey()
    {
        var apiKey = _config.ApiKey;
        if (string.IsNullOrEmpty(apiKey))
            throw new Exception("Azure OpenAI ApiKey is not set");
        return apiKey;
    }
    
    /// <summary>
    /// Gets the name of the LLM provider
    /// </summary>
    /// <returns>The LLM provider</returns>
    public string GetLlmProvider()
    {
        return _config.Provider ?? string.Empty;
    }

    /// <summary>
    /// Gets the model for the Anthropic provider
    /// </summary>
    /// <returns>The model</returns>
    public string GetModel()
    {
        Console.WriteLine("FROM CONFIG" + _config.Model);
        return _config.Model;
    }

    /// <summary>
    /// Gets the additional details of the LLM provider
    /// </summary>
    /// <returns>Additional configuration details</returns>
    public Dictionary<string, string> GetAdditionalConfig()
    {
        if (_config.AdditionalConfig == null || _config.AdditionalConfig.Count == 0)
        {
            _logger.LogWarning("Additional configuration is missing or empty for LLM provider");
            return new Dictionary<string, string>();
        }

        return _config.AdditionalConfig;
    }
    
    /// <summary>
    /// Gets Base URL of the Model
    /// </summary>
    /// <returns>Base URL</returns>
    public string GetBaseUrl()
    {
        return _config.BaseUrl ?? string.Empty;
    }

    public async Task<string> GetChatCompletionAsync(List<ChatMessage> messages, string model = null)
    {
        var endpoint = _config.BaseUrl?.TrimEnd('/') + $"/openai/deployments/{_deploymentName}/chat/completions?api-version={_apiVersion}";
        var requestBody = new
        {
            messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToList(),
            model = model ?? _config.Model
        };
        var json = JsonSerializer.Serialize(requestBody);
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        request.Headers.Add("api-key", GetApiKey());
        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Azure OpenAI request failed: {Error}", error);
            throw new Exception($"Azure OpenAI request failed: {error}");
        }

        var responseString = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseString);
        var completion = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

        return completion ?? string.Empty;
    }
} 