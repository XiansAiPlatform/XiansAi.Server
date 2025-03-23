using XiansAi.Server.Auth;
using XiansAi.Server.Database.Repositories;

namespace XiansAi.Server.Services.Web;

/// <summary>
/// Endpoint for managing flow definitions with operations for retrieval and deletion.
/// </summary>
public class DefinitionsEndpoint
{
    private readonly IDatabaseService _databaseService;
    private readonly ILogger<DefinitionsEndpoint> _logger;
    private readonly ITenantContext _tenantContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefinitionsEndpoint"/> class.
    /// </summary>
    /// <param name="databaseService">Service for database operations.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="tenantContext">Context for the current tenant and user information.</param>
    public DefinitionsEndpoint(
        IDatabaseService databaseService,
        ILogger<DefinitionsEndpoint> logger,
        ITenantContext tenantContext
    )
    {
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    /// <summary>
    /// Deletes a definition by its ID if the current user is the owner.
    /// </summary>
    /// <param name="definitionId">The ID of the definition to delete.</param>
    /// <returns>OK if successful, NotFound if definition doesn't exist, or Forbidden if user is not the owner.</returns>
    /// <exception cref="ArgumentNullException">Thrown if definitionId is null.</exception>
    public async Task<IResult> DeleteDefinition(string definitionId)
    {
        if (string.IsNullOrEmpty(definitionId))
        {
            _logger.LogWarning("Attempted to delete definition with null or empty ID");
            return Results.BadRequest(new { message = "Definition ID cannot be null or empty." });
        }

        try
        {
            var definitionRepository = new FlowDefinitionRepository(await _databaseService.GetDatabase());
            var definition = await definitionRepository.GetByIdAsync(definitionId);
            
            if (definition == null)
            {
                _logger.LogInformation("Definition with ID {DefinitionId} not found", definitionId);
                return Results.NotFound();
            }
            
            if (definition.Owner != _tenantContext.LoggedInUser)
            {
                _logger.LogWarning("User {User} attempted to delete definition {DefinitionId} owned by {Owner}", 
                    _tenantContext.LoggedInUser, definitionId, definition.Owner);
                return Results.Json(
                    new { message = "You are not allowed to delete this definition. Only the owner can delete their own definitions." }, 
                    statusCode: StatusCodes.Status403Forbidden
                );
            }
            
            await definitionRepository.DeleteAsync(definitionId);
            _logger.LogInformation("Definition with ID {DefinitionId} successfully deleted", definitionId);
            return Results.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting definition with ID {DefinitionId}", definitionId);
            return Results.Problem("An error occurred while deleting the definition.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Retrieves the latest definitions based on filtering criteria.
    /// </summary>
    /// <param name="startTime">Optional start time filter.</param>
    /// <param name="endTime">Optional end time filter.</param>
    /// <param name="owner">Optional owner filter. Use "current" to filter by the logged-in user.</param>
    /// <returns>Collection of definitions matching the criteria.</returns>
    public async Task<IResult> GetLatestDefinitions(DateTime? startTime, DateTime? endTime, string? owner)
    {
        try
        {
            if (owner == "current")
            {
                if (_tenantContext.LoggedInUser == null)
                {
                    _logger.LogWarning("Unauthorized attempt to access current user's definitions");
                    return Results.Unauthorized();
                }
                
                owner = _tenantContext.LoggedInUser;
            }

            // Validate date range if both dates are provided
            if (startTime.HasValue && endTime.HasValue && startTime > endTime)
            {
                _logger.LogWarning("Invalid date range: StartTime {StartTime} is after EndTime {EndTime}", startTime, endTime);
                return Results.BadRequest(new { message = "Start time cannot be after end time." });
            }

            var definitionRepository = new FlowDefinitionRepository(await _databaseService.GetDatabase());
            var definitions = await definitionRepository.GetLatestDefinitionsAsync(startTime, endTime, owner);
            
            _logger.LogInformation("Found {Count} definitions for query: StartTime={StartTime}, EndTime={EndTime}, Owner={Owner}", 
                definitions.Count, startTime, endTime, owner);
                
            return Results.Ok(definitions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving definitions with filters: StartTime={StartTime}, EndTime={EndTime}, Owner={Owner}", 
                startTime, endTime, owner);
            return Results.Problem("An error occurred while retrieving definitions.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}