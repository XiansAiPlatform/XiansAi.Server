using Features.AgentApi.Repositories;
using XiansAi.Server.Shared.Data;

namespace Features.AgentApi.Services.Lib;

public interface IKnowledgeService
{
    Task<IResult> GetLatestKnowledge(string name);
}

public class KnowledgeService : IKnowledgeService
{
    private readonly ILogger<KnowledgeService> _logger;
    private readonly IKnowledgeRepository _knowledgeRepository;
    
    public KnowledgeService(
        IKnowledgeRepository knowledgeRepository,
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
