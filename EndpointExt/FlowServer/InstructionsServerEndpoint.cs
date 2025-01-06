using XiansAi.Server.EndpointExt.WebClient;
using XiansAi.Server.MongoDB;
using XiansAi.Server.MongoDB.Repositories;

namespace XiansAi.Server.EndpointExt.FlowServer;
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
        var instruction = await _instructionRepository.GetLatestInstructionAsync(name);
        if (instruction == null)
            return Results.NotFound("Instruction not found");
        else
            return Results.Ok(instruction);
    }
}
