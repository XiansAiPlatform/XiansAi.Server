using Features.AgentApi.Repositories;
using XiansAi.Server.Database;

namespace Features.AgentApi.Services.Lib;
public class KnowledgeService
{
    private readonly ILogger<KnowledgeService> _logger;
    private readonly KnowledgeRepository _knowledgeRepository;
    
    public KnowledgeService(
        KnowledgeRepository knowledgeRepository,
        ILogger<KnowledgeService> logger
    )
    {
        _knowledgeRepository = knowledgeRepository;
        _logger = logger;
    }

    public async Task<IResult> GetLatestKnowledge(string name)
    {
        var knowledge = await _knowledgeRepository.GetLatestKnowledgeByNameAsync(name);
        if (knowledge == null)
            return Results.NotFound("Knowledge not found");
        else
            return Results.Ok(knowledge);
    }
}
