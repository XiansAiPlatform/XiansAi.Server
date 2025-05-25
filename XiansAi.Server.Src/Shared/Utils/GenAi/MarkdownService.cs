using System.Xml;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using OpenAI.Chat;
using Shared.Auth;
using XiansAi.Server.GenAi;
using XiansAi.Server.Shared.Data.Models;
using XiansAi.Server.Shared.Repositories;

namespace Shared.Utils.GenAi;

public interface IMarkdownService
{
    Task<string?> GenerateMarkdown(string? source);
}

public class MarkdownService : IMarkdownService
{
    private readonly IOpenAIClientService _openAIClientService;
    private readonly ILogger<MarkdownService> _logger;
    private readonly IKnowledgeRepository _knowledgeRepository;
    private readonly ITenantContext _tenantContext;
    private readonly string _model = "gpt-4o-mini";
    public MarkdownService(IOpenAIClientService openAIClientService, 
        IKnowledgeRepository knowledgeRepository, 
        ITenantContext tenantContext, 
        ILogger<MarkdownService> logger)
    {
        _logger = logger;
        _openAIClientService = openAIClientService;
        _tenantContext = tenantContext;
        _knowledgeRepository = knowledgeRepository;
    }

    public async Task<string?> GenerateMarkdown(string? source)
    {
        var agent = "--SYSTEM--";
        var knowledgeName = "How to generate Mermaid chart for Agent Visualization";
        string? tenantId = _tenantContext.TenantId;
        var instruction = await _knowledgeRepository.GetLatestByNameAsync<Knowledge>(knowledgeName, agent, tenantId);
        
        _logger.LogInformation("Generating new  markdown for source");
        if (instruction == null)
        {
            throw new Exception($"Mermaid generation instruction `{knowledgeName}` of agent `{agent}` not found");
        }

        if (string.IsNullOrEmpty(source))
        {
            return null;
        }
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(instruction.Content),
            new UserChatMessage("Workflow code:\n" + source)
        };
        var markdown = await _openAIClientService.GetChatCompletionAsync(messages, _model);

        // Remove spaces between classes to make it valid mermaid code
        markdown = markdown.Replace(", ", ",");
        markdown = markdown.Replace("\"", "");

        _logger.LogInformation("New *markdown* generated for definition");

        return markdown;
    }
}
