using XiansAi.Server.EndpointExt.WebClient;
using XiansAi.Server.MongoDB;
using XiansAi.Server.MongoDB.Repositories;

namespace XiansAi.Server.EndpointExt.FlowServer;
public class InstructionsServerEndpoint
{
    private readonly ILogger<InstructionsEndpoint> _logger;
    private readonly IDatabaseService _databaseService;
    public InstructionsServerEndpoint(
        IDatabaseService databaseService,
        ILogger<InstructionsEndpoint> logger
    )
    {
        _databaseService = databaseService;
        _logger = logger;
    }

    public async Task<IResult> GetLatestInstruction(string name)
    {
        var instructionRepository = new InstructionRepository(await _databaseService.GetDatabase());
        var instruction = await instructionRepository.GetLatestInstructionByNameAsync(name);
        if (instruction == null)
            return Results.NotFound("Instruction not found");
        else
            return Results.Ok(instruction);
    }
}
