using Shared.Auth;
using XiansAi.Server.Features.WebApi.Repositories;
using XiansAi.Server.Shared.Data;
using Shared.Data;

namespace Features.WebApi.Services;

/// <summary>
/// Endpoint for managing flow definitions with operations for retrieval and deletion.
/// </summary>
public class DefinitionsService
{
    private readonly IFlowDefinitionRepository _definitionRepository;
    private readonly ILogger<DefinitionsService> _logger;
    private readonly ITenantContext _tenantContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefinitionsService"/> class.
    /// </summary>
    /// <param name="definitionRepository">Repository for flow definition operations.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="tenantContext">Context for the current tenant and user information.</param>
    public DefinitionsService(
        XiansAi.Server.Features.WebApi.Repositories.IFlowDefinitionRepository definitionRepository,
        ILogger<DefinitionsService> logger,
        ITenantContext tenantContext
    )
    {
        _definitionRepository = definitionRepository ?? throw new ArgumentNullException(nameof(definitionRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    /// <summary>
    /// Deletes a definition by its ID if the current user has owner permission.
    /// </summary>
    /// <param name="definitionId">The ID of the definition to delete.</param>
    /// <returns>OK if successful, NotFound if definition doesn't exist, or Forbidden if user is not the owner.</returns>
    /// <exception cref="ArgumentNullException">Thrown if definitionId is null.</exception>
    public async Task<IResult> DeleteDefinition(string definitionId)
    {
        if (string.IsNullOrEmpty(definitionId))
        {
            throw new ArgumentNullException(nameof(definitionId));
        }

        _logger.LogInformation("Attempting to delete definition with ID: {DefinitionId}", definitionId);

        var definition = await _definitionRepository.GetByIdAsync(definitionId);
        if (definition == null)
        {
            _logger.LogWarning("Definition not found with ID: {DefinitionId}", definitionId);
            return Results.NotFound();
        }
        if (definition.Permissions == null)
        {
            //depend on the created by
            if (definition.CreatedBy != _tenantContext.LoggedInUser)
            {
                _logger.LogWarning("User {UserId} attempted to delete definition without owner permission", 
                    _tenantContext.LoggedInUser);
                return Results.Forbid();
            }
        } else if (!definition.Permissions.HasPermission(_tenantContext.LoggedInUser, Array.Empty<string>(), PermissionLevel.Owner))
        {
            _logger.LogWarning("User {UserId} attempted to delete definition without owner permission", 
                _tenantContext.LoggedInUser);
            return Results.Forbid();
        }

        var result = await _definitionRepository.DeleteAsync(definitionId);
        if (!result)
        {
            _logger.LogError("Failed to delete definition with ID: {DefinitionId}", definitionId);
            return Results.Problem("Failed to delete definition");
        }

        _logger.LogInformation("Successfully deleted definition with ID: {DefinitionId}", definitionId);
        return Results.Ok();
    }

    /// <summary>
    /// Retrieves the latest definitions that the current user has read access to.
    /// </summary>
    /// <returns>A list of the latest definitions.</returns>
    public async Task<IResult> GetLatestDefinitions(DateTime? startTime, DateTime? endTime)
    {
        _logger.LogInformation("Retrieving latest definitions");
        
        try
        {
            var definitions = await _definitionRepository.GetDefinitionsWithPermissionAsync(_tenantContext.LoggedInUser, startTime, endTime);
            _logger.LogInformation("Retrieved {Count} definitions", definitions.Count);
            return Results.Ok(definitions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving latest definitions");
            return Results.Problem("Error retrieving definitions");
        }
    }
}