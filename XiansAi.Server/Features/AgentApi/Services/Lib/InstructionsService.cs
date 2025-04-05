using Features.AgentApi.Data.Repositories;
using XiansAi.Server.Database;

namespace Features.AgentApi.Services.Lib;
public class InstructionsService
{
    private readonly ILogger<InstructionsService> _logger;
    private readonly InstructionRepository _instructionRepository;
    
    public InstructionsService(
        InstructionRepository instructionRepository,
        ILogger<InstructionsService> logger
    )
    {
        _instructionRepository = instructionRepository;
        _logger = logger;
    }

    public async Task<IResult> GetLatestInstruction(string name)
    {
        var instruction = await _instructionRepository.GetLatestInstructionByNameAsync(name);
        if (instruction == null)
            return Results.NotFound("Instruction not found");
        else
            return Results.Ok(instruction);
    }
}
