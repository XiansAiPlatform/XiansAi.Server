using MongoDB.Bson;


public class InstructionsServerEndpoint
{
    private readonly InstructionRepository _instructionRepository;
    private readonly ILogger<InstructionsEndpoint> _logger;

    public InstructionsServerEndpoint(
        IMongoDbClientService mongoDbClientService,
        ILogger<InstructionsEndpoint> logger
    )
    {
        var database = mongoDbClientService.GetDatabase();
        _instructionRepository = new InstructionRepository(database);
        _logger = logger;
    }

    public async Task<IResult> GetLatestInstruction(string name)
    {
        Console.WriteLine("GetLatestInstruction called with name: " + name);
        var instruction = await _instructionRepository.GetLatestInstructionAsync(name);
        Console.WriteLine(instruction.ToJson());
        return Results.Ok(instruction);
    }
}
