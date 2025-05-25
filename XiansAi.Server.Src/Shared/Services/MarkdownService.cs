using Shared.Auth;
using Shared.Services;
using XiansAi.Server.Shared.Data.Models;
using XiansAi.Server.Shared.Repositories;

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
    private readonly string _model = "gpt-4o-mini";
    
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
        
        var messages = new List<XiansAi.Server.Providers.ChatMessage>
        {
            new XiansAi.Server.Providers.ChatMessage { Role = "system", Content = instruction },
            new XiansAi.Server.Providers.ChatMessage { Role = "user", Content = "Workflow code:\n" + source }
        };
        
        var markdown = await _llmService.GetChatCompletionAsync(messages, _model);

        // Remove spaces between classes to make it valid mermaid code
        markdown = markdown.Replace(", ", ",");
        markdown = markdown.Replace("\"", "");

        _logger.LogInformation("New *markdown* generated for definition");

        return markdown;
    }
}
