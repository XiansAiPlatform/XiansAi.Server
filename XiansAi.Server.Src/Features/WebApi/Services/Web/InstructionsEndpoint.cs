using MongoDB.Bson;
using XiansAi.Server.Database;
using XiansAi.Server.Database.Models;
using XiansAi.Server.Database.Repositories;
using XiansAi.Server.Utils;

namespace Features.WebApi.Services.Web;
public class InstructionRequest
{
    public required string Name { get; set; }
    public required string Content { get; set; }
    public required string Type { get; set; }
}

public class DeleteAllVersionsRequest
{
    public required string Name { get; set; }
}

public class InstructionsEndpoint
{
    private readonly IDatabaseService _databaseService;
    private readonly ILogger<InstructionsEndpoint> _logger;

    public InstructionsEndpoint(
        IDatabaseService databaseService,
        ILogger<InstructionsEndpoint> logger
    )
    {
        _databaseService = databaseService;
        _logger = logger;
    }

    public async Task<IResult> GetInstructionById(string id)
    {
        var instructionRepository = new InstructionRepository(await _databaseService.GetDatabase());
        var instruction = await instructionRepository.GetByIdAsync(id);
        return Results.Ok(instruction);
    }

    public async Task<IResult> GetInstructionVersions(string name)
    {
        var instructionRepository = new InstructionRepository(await _databaseService.GetDatabase());
        var versions = await instructionRepository.GetByNameAsync(name);
        return Results.Ok(versions);
    }

    public async Task<IResult> DeleteInstruction(string id)
    {
        var instructionRepository = new InstructionRepository(await _databaseService.GetDatabase());
        await instructionRepository.DeleteAsync(id);
        return Results.Ok();
    }

    public async Task<IResult> DeleteAllVersions(DeleteAllVersionsRequest request)
    {
        var instructionRepository = new InstructionRepository(await _databaseService.GetDatabase());
        await instructionRepository.DeleteAllVersionsAsync(request.Name);
        return Results.Ok(new { message = "All versions deleted" });
    }

    public async Task<IResult> GetLatestInstructions()
    {
        var instructionRepository = new InstructionRepository(await _databaseService.GetDatabase());
        var instructions = await instructionRepository.GetUniqueLatestInstructionsAsync();
        _logger.LogInformation("Found {Count} instructions", instructions.Count);
        return Results.Ok(instructions);
    }

    public async Task<IResult> GetLatestInstructionByName(string name)
    {
        var instructionRepository = new InstructionRepository(await _databaseService.GetDatabase());
        var instruction = await instructionRepository.GetLatestInstructionByNameAsync(name);
        return Results.Ok(instruction);
    }

    public async Task<IResult> GetInstructions()
    {
        var instructionRepository = new InstructionRepository(await _databaseService.GetDatabase());
        var instructions = await instructionRepository.GetAllAsync();
        _logger.LogInformation("Found {Count} instructions", instructions.Count);
        return Results.Ok(instructions);
    }

    public async Task<IResult> CreateInstruction(InstructionRequest request)
    {
        //var hash = HashGenerator.GenerateContentHash(request.Content + request.Name + request.Type + DateTime.UtcNow.ToString());
        var instruction = new Instruction
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Name = request.Name,
            Content = request.Content,
            Type = request.Type,
            Version = HashGenerator.GenerateContentHash(ObjectId.GenerateNewId().ToString() + DateTime.UtcNow.ToString()),
            CreatedAt = DateTime.UtcNow
        };
        var instructionRepository = new InstructionRepository(await _databaseService.GetDatabase());
        await instructionRepository.CreateAsync(instruction);
        return Results.Ok(instruction);
    }
}