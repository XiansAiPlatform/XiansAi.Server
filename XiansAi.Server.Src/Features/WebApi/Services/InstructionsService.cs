using MongoDB.Bson;
using XiansAi.Server.Features.WebApi.Models;
using XiansAi.Server.Features.WebApi.Repositories;
using XiansAi.Server.Shared.Data;
using XiansAi.Server.Utils;

namespace Features.WebApi.Services;
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

public class InstructionsService
{
    private readonly IInstructionRepository _instructionRepository;
    private readonly ILogger<InstructionsService> _logger;

    public InstructionsService(
        IInstructionRepository instructionRepository,
        ILogger<InstructionsService> logger
    )
    {
        _instructionRepository = instructionRepository;
        _logger = logger;
    }

    public async Task<IResult> GetInstructionById(string id)
    {
        var instruction = await _instructionRepository.GetByIdAsync(id);
        return Results.Ok(instruction);
    }

    public async Task<IResult> GetInstructionVersions(string name)
    {
        var versions = await _instructionRepository.GetByNameAsync(name);
        return Results.Ok(versions);
    }

    public async Task<IResult> DeleteInstruction(string id)
    {
        await _instructionRepository.DeleteAsync(id);
        return Results.Ok();
    }

    public async Task<IResult> DeleteAllVersions(DeleteAllVersionsRequest request)
    {
        await _instructionRepository.DeleteAllVersionsAsync(request.Name);
        return Results.Ok(new { message = "All versions deleted" });
    }

    public async Task<IResult> GetLatestInstructions()
    {
        var instructions = await _instructionRepository.GetUniqueLatestInstructionsAsync();
        _logger.LogInformation("Found {Count} instructions", instructions.Count);
        return Results.Ok(instructions);
    }

    public async Task<IResult> GetLatestInstructionByName(string name)
    {
        var instruction = await _instructionRepository.GetLatestInstructionByNameAsync(name);
        return Results.Ok(instruction);
    }

    public async Task<IResult> GetInstructions()
    {
        var instructions = await _instructionRepository.GetAllAsync();
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
        await _instructionRepository.CreateAsync(instruction);
        return Results.Ok(instruction);
    }
}