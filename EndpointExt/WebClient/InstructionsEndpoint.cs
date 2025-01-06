using MongoDB.Bson;

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
    private readonly InstructionRepository _instructionRepository;
    private readonly ILogger<InstructionsEndpoint> _logger;

    public InstructionsEndpoint(
        IMongoDbClientService mongoDbClientService,
        ILogger<InstructionsEndpoint> logger
    )
    {
        var database = mongoDbClientService.GetDatabase();
        _instructionRepository = new InstructionRepository(database);
        _logger = logger;
    }

    public async Task<IResult> GetInstruction(string id)
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