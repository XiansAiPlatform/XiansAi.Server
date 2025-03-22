using MongoDB.Bson;
using Microsoft.AspNetCore.Http;
using XiansAi.Server.Auth;
using XiansAi.Server.Database;
using XiansAi.Server.Database.Models;
using XiansAi.Server.Database.Repositories;
using XiansAi.Server.Utils;

namespace XiansAi.Server.Services.Web;

public class DefinitionsEndpoint
{
    private readonly IDatabaseService _databaseService;
    private readonly ILogger<DefinitionsEndpoint> _logger;
    private readonly ITenantContext _tenantContext;
    public DefinitionsEndpoint(
        IDatabaseService databaseService,
        ILogger<DefinitionsEndpoint> logger,
        ITenantContext tenantContext
    )
    {
        _databaseService = databaseService;
        _logger = logger;
        _tenantContext = tenantContext;
    }

    public async Task<IResult> DeleteDefinition(string definitionId)
    {
        var definitionRepository = new FlowDefinitionRepository(await _databaseService.GetDatabase());
        var definition = await definitionRepository.GetByIdAsync(definitionId);
        if (definition == null)
        {
            return Results.NotFound();
        }
        if (definition.Owner != _tenantContext.LoggedInUser)
        {
            return Results.Json(new { message = "You are not allowed to delete this definition. Only the owner can delete their own definitions." }, statusCode: StatusCodes.Status403Forbidden);
        }
        await definitionRepository.DeleteAsync(definitionId);
        return Results.Ok();
    }

    public async Task<IResult> GetLatestDefinitions(DateTime? startTime, DateTime? endTime, string? owner)
    {
        if (owner == "current" && _tenantContext.LoggedInUser == null)
        {
            return Results.Unauthorized();
        }

        if (owner == "current")
        {
            owner = _tenantContext.LoggedInUser;
        }

        var definitionRepository = new FlowDefinitionRepository(await _databaseService.GetDatabase());
        var definitions = await definitionRepository.GetLatestDefinitionsAsync(startTime, endTime, owner);
        _logger.LogInformation("Found {Count} definitions", definitions.Count);
        return Results.Ok(definitions);
    }
    

}