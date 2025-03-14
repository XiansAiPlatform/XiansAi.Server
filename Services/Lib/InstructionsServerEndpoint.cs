using XiansAi.Server.Services.Web;
using XiansAi.Server.Database;
using XiansAi.Server.Database.Repositories;

namespace XiansAi.Server.Services.Lib;
public class InstructionsServerEndpoint
{
    private readonly ILogger<InstructionsServerEndpoint> _logger;
    private readonly IDatabaseService _databaseService;
    public InstructionsServerEndpoint(
        IDatabaseService databaseService,
        ILogger<InstructionsServerEndpoint> logger
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
