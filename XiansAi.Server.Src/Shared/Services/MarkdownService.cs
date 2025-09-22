using Shared.Auth;
using Shared.Services;
using Shared.Data.Models;
using Shared.Repositories;

namespace Shared.Services;

public interface IMarkdownService
{
    Task<string?> GenerateMarkdown(string? source);
}

public class MarkdownService : IMarkdownService
{
    private readonly ILlmService _llmService;
    private readonly ILogger<MarkdownService> _logger;
    private readonly IKnowledgeRepository _knowledgeRepository;
    private readonly ITenantContext _tenantContext;
    
    public MarkdownService(ILlmService llmService, 
        IKnowledgeRepository knowledgeRepository, 
        ITenantContext tenantContext, 
        ILogger<MarkdownService> logger)
    {
        _logger = logger;
        _llmService = llmService;
        _tenantContext = tenantContext;
        _knowledgeRepository = knowledgeRepository;
    }

    public async Task<string?> GenerateMarkdown(string? source)
    {
        var instruction = MarkDownPrompt.Value;
        
        var messages = new List<Shared.Providers.ChatMessage>
        {
            new Shared.Providers.ChatMessage { Role = "system", Content = instruction },
            new Shared.Providers.ChatMessage { Role = "user", Content = "Workflow code:\n" + source }
        };
        var markdown = await _llmService.GetChatCompletionAsync(messages, _llmService.GetModel());

        // Remove spaces between classes to make it valid mermaid code
        markdown = markdown.Replace(", ", ",");
        markdown = markdown.Replace("\"", "");

        _logger.LogInformation("New *markdown* generated for definition");

        return markdown;
    }
}
