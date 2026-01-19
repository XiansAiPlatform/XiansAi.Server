using MongoDB.Bson;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Utils.Services;
using Shared.Utils.Temporal;
using Temporalio.Client;
using Features.WebApi.Services;

namespace Shared.Services;

public interface IActivationService
{
    Task<ServiceResult<AgentActivation>> CreateActivationAsync(CreateActivationRequest request, string userId, string tenantId);
    Task<ServiceResult<AgentActivation>> GetActivationByIdAsync(string id);
    Task<ServiceResult<List<AgentActivation>>> GetActivationsByTenantAsync(string tenantId);
    Task<ServiceResult<AgentActivation>> ActivateAgentAsync(string activationId, string tenantId, ActivationWorkflowConfiguration? workflowConfiguration = null);
    Task<ServiceResult<AgentActivation>> DeactivateAgentAsync(string activationId, string tenantId);
    Task<ServiceResult<bool>> DeleteActivationAsync(string activationId);
}

public class CreateActivationRequest
{
    public required string Name { get; set; }
    public required string AgentName { get; set; }
    public string? Description { get; set; }
    public string? ParticipantId { get; set; }
    public ActivationWorkflowConfiguration? WorkflowConfiguration { get; set; }
}

/// <summary>
/// Service for managing agent activations.
/// Activating and deactivating agents uses the Temporal client.
/// Creating and deleting activations uses the database repository.
/// </summary>
public class ActivationService : IActivationService
{
    private readonly IActivationRepository _activationRepository;
    private readonly IAgentRepository _agentRepository;
    private readonly IFlowDefinitionRepository _flowDefinitionRepository;
    private readonly IWorkflowStarterService _workflowStarterService;
    private readonly ITemporalClientService _temporalClientService;
    private readonly IActivationCleanupService _cleanupService;
    private readonly ILogger<ActivationService> _logger;

    public ActivationService(
        IActivationRepository activationRepository,
        IAgentRepository agentRepository,
        IFlowDefinitionRepository flowDefinitionRepository,
        IWorkflowStarterService workflowStarterService,
        ITemporalClientService temporalClientService,
        IActivationCleanupService cleanupService,
        ILogger<ActivationService> logger)
    {
        _activationRepository = activationRepository ?? throw new ArgumentNullException(nameof(activationRepository));
        _agentRepository = agentRepository ?? throw new ArgumentNullException(nameof(agentRepository));
        _flowDefinitionRepository = flowDefinitionRepository ?? throw new ArgumentNullException(nameof(flowDefinitionRepository));
        _workflowStarterService = workflowStarterService ?? throw new ArgumentNullException(nameof(workflowStarterService));
        _temporalClientService = temporalClientService ?? throw new ArgumentNullException(nameof(temporalClientService));
        _cleanupService = cleanupService ?? throw new ArgumentNullException(nameof(cleanupService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates a new agent activation record in the database.
    /// </summary>
    public async Task<ServiceResult<AgentActivation>> CreateActivationAsync(
        CreateActivationRequest request, 
        string userId, 
        string tenantId)
    {
        try
        {
            _logger.LogInformation("Creating activation {Name} for agent {AgentName} in tenant {TenantId}", 
                request.Name, request.AgentName, tenantId);

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return ServiceResult<AgentActivation>.BadRequest("Activation name is required");
            }

            if (string.IsNullOrWhiteSpace(request.AgentName))
            {
                return ServiceResult<AgentActivation>.BadRequest("AgentName is required");
            }

            // Verify that the agent exists
            var agent = await _agentRepository.GetByNameInternalAsync(request.AgentName, tenantId);
            if (agent == null)
            {
                _logger.LogWarning("Agent with name {AgentName} not found in tenant {TenantId}", request.AgentName, tenantId);
                return ServiceResult<AgentActivation>.NotFound($"Agent with name '{request.AgentName}' not found in tenant");
            }

            // Verify the agent belongs to the correct tenant
            if (agent.Tenant != tenantId)
            {
                _logger.LogWarning("Agent {AgentName} does not belong to tenant {TenantId}", request.AgentName, tenantId);
                return ServiceResult<AgentActivation>.Forbidden("Agent does not belong to this tenant");
            }

            var activation = new AgentActivation
            {
                Id = ObjectId.GenerateNewId().ToString(),
                Name = request.Name,
                AgentName = request.AgentName,
                Description = request.Description,
                ParticipantId = request.ParticipantId,
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow,
                TenantId = tenantId,
                WorkflowConfiguration = request.WorkflowConfiguration
            };

            // Validate the activation
            try
            {
                activation = activation.SanitizeAndValidate();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Validation failed for activation {Name}", request.Name);
                return ServiceResult<AgentActivation>.BadRequest($"Validation error: {ex.Message}");
            }

            await _activationRepository.CreateAsync(activation);

            _logger.LogInformation("Successfully created activation {ActivationId}", activation.Id);
            return ServiceResult<AgentActivation>.Success(activation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating activation {Name}", request.Name);
            return ServiceResult<AgentActivation>.InternalServerError(
                "An error occurred while creating the activation");
        }
    }

    /// <summary>
    /// Gets an activation by its ID.
    /// </summary>
    public async Task<ServiceResult<AgentActivation>> GetActivationByIdAsync(string id)
    {
        try
        {
            _logger.LogInformation("Retrieving activation by ID {ActivationId}", id);

            if (string.IsNullOrWhiteSpace(id))
            {
                return ServiceResult<AgentActivation>.BadRequest("Activation ID is required");
            }

            var activation = await _activationRepository.GetByIdAsync(id);
            if (activation == null)
            {
                _logger.LogWarning("Activation with ID {ActivationId} not found", id);
                return ServiceResult<AgentActivation>.NotFound($"Activation with ID '{id}' not found");
            }

            return ServiceResult<AgentActivation>.Success(activation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving activation by ID {ActivationId}", id);
            return ServiceResult<AgentActivation>.InternalServerError(
                "An error occurred while retrieving the activation");
        }
    }

    /// <summary>
    /// Gets all activations for a tenant.
    /// </summary>
    public async Task<ServiceResult<List<AgentActivation>>> GetActivationsByTenantAsync(string tenantId)
    {
        try
        {
            _logger.LogInformation("Retrieving activations for tenant {TenantId}", tenantId);

            var activations = await _activationRepository.GetByTenantIdAsync(tenantId);

            _logger.LogInformation("Found {Count} activations for tenant {TenantId}", activations.Count, tenantId);
            return ServiceResult<List<AgentActivation>>.Success(activations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving activations for tenant {TenantId}", tenantId);
            return ServiceResult<List<AgentActivation>>.InternalServerError(
                "An error occurred while retrieving activations");
        }
    }

    /// <summary>
    /// Activates an agent by starting a workflow in Temporal.
    /// </summary>
    public async Task<ServiceResult<AgentActivation>> ActivateAgentAsync(
        string activationId, 
        string tenantId,
        ActivationWorkflowConfiguration? workflowConfiguration = null)
    {
        try
        {
            _logger.LogInformation("Activating agent for activation {ActivationId}", activationId);

            var activation = await _activationRepository.GetByIdAsync(activationId);
            if (activation == null)
            {
                _logger.LogWarning("Activation with ID {ActivationId} not found", activationId);
                return ServiceResult<AgentActivation>.NotFound($"Activation with ID '{activationId}' not found");
            }

            if (activation.TenantId != tenantId)
            {
                _logger.LogWarning("Activation {ActivationId} does not belong to tenant {TenantId}", activationId, tenantId);
                return ServiceResult<AgentActivation>.Forbidden("Activation does not belong to this tenant");
            }

            // If workflow configuration is provided in the request, update the activation with it
            if (workflowConfiguration != null)
            {
                _logger.LogInformation("Updating activation {ActivationId} with workflow configuration from request", activationId);
                
                // Validate the workflow configuration
                try
                {
                    workflowConfiguration.Validate();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Invalid workflow configuration provided for activation {ActivationId}", activationId);
                    return ServiceResult<AgentActivation>.BadRequest($"Invalid workflow configuration: {ex.Message}");
                }

                // Update the activation with the new workflow configuration
                activation.WorkflowConfiguration = workflowConfiguration;
                await _activationRepository.UpdateAsync(activationId, activation);
                
                _logger.LogInformation("Successfully updated workflow configuration for activation {ActivationId}", activationId);
            }

            // Get the agent details
            var agent = await _agentRepository.GetByNameInternalAsync(activation.AgentName, tenantId);
            if (agent == null)
            {
                _logger.LogWarning("Agent with name {AgentName} not found in tenant {TenantId}", activation.AgentName, tenantId);
                return ServiceResult<AgentActivation>.NotFound($"Agent with name '{activation.AgentName}' not found in tenant");
            }

            // Get all flow definitions (workflows) for the agent
            var systemScoped = agent.SystemScoped;
            var flowDefinitions = await _flowDefinitionRepository.GetByNameAsync(
                activation.AgentName, 
                systemScoped ? null : tenantId);

            if (flowDefinitions == null || flowDefinitions.Count == 0)
            {
                _logger.LogWarning("No workflow definitions found for agent {AgentName}", activation.AgentName);
                return ServiceResult<AgentActivation>.BadRequest($"No workflow definitions found for agent '{activation.AgentName}'");
            }

            _logger.LogInformation("Found {Count} workflow definitions for agent {AgentName}", 
                flowDefinitions.Count, activation.AgentName);

            // Start all workflows using WorkflowStarterService
            try
            {
                var workflowIds = new List<string>();
                var startedCount = 0;

                foreach (var flowDefinition in flowDefinitions)
                {
                    try
                    {
                        // Find matching workflow configuration for this workflow type
                        var workflowConfig = activation.WorkflowConfiguration?.Workflows
                            .FirstOrDefault(w => w.WorkflowType == flowDefinition.WorkflowType);

                        // Convert workflow inputs to string parameters array
                        string[] parameters;

                        if (workflowConfig != null)
                        {
                            // Use inputs from configuration
                            parameters = workflowConfig.Inputs
                                .Select(input => input.Value)
                                .ToArray();
                        }
                        else
                        {
                            // No configuration provided, use empty parameters
                            parameters = Array.Empty<string>();
                        }

                        // Always use activation name as the workflow ID postfix
                        var workflowIdPostfix = $"{activation.Name}";

                        // Create workflow request
                        var workflowRequest = new WorkflowRequest
                        {
                            WorkflowType = flowDefinition.WorkflowType,
                            WorkflowIdPostfix = workflowIdPostfix,
                            Parameters = parameters,
                            AgentName = activation.AgentName
                        };

                        // Start workflow using WorkflowStarterService
                        var result = await _workflowStarterService.HandleStartWorkflow(workflowRequest, activation.ParticipantId);

                        if (result.IsSuccess && result.Data != null)
                        {
                            workflowIds.Add(result.Data.WorkflowId);
                            startedCount++;
                            
                            _logger.LogInformation("Started workflow {WorkflowType} with ID {WorkflowId} for activation {ActivationId}", 
                                flowDefinition.WorkflowType, result.Data.WorkflowId, activationId);
                        }
                        else
                        {
                            _logger.LogWarning("Failed to start workflow {WorkflowType} for activation {ActivationId}: {Error}", 
                                flowDefinition.WorkflowType, activationId, result.ErrorMessage);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error starting workflow {WorkflowType} for activation {ActivationId}", 
                            flowDefinition.WorkflowType, activationId);
                        // stop here and return the error
                        return ServiceResult<AgentActivation>.InternalServerError(
                            $"Failed to start workflow {flowDefinition.WorkflowType} for activation {activationId}: {ex.Message}");
                    }
                }

                if (workflowIds.Count == 0)
                {
                    _logger.LogError("Failed to start any workflows for activation {ActivationId}", activationId);
                    return ServiceResult<AgentActivation>.InternalServerError(
                        "Failed to start any workflows. Check logs for details.");
                }

                // Update activation with workflow IDs and timestamp
                activation.WorkflowIds = workflowIds;
                activation.ActivatedAt = DateTime.UtcNow;

                await _activationRepository.UpdateAsync(activationId, activation);

                _logger.LogInformation("Successfully activated {StartedCount}/{TotalCount} workflows for activation {ActivationId}", 
                    startedCount, flowDefinitions.Count, activationId);
                
                return ServiceResult<AgentActivation>.Success(activation);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting workflows for activation {ActivationId}", activationId);
                return ServiceResult<AgentActivation>.InternalServerError(
                    $"Failed to start workflows: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error activating agent for activation {ActivationId}", activationId);
            return ServiceResult<AgentActivation>.InternalServerError(
                "An error occurred while activating the agent");
        }
    }

    /// <summary>
    /// Deactivates an agent by canceling/terminating workflows and deleting schedules in Temporal.
    /// This method now performs a comprehensive cleanup of all resources associated with the activation:
    /// - Finds all workflows matching the activation (by tenantId, agent, and idPostfix)
    /// - Cancels/stops all running workflows
    /// - Finds all schedules matching the activation
    /// - Deletes all schedules
    /// </summary>
    public async Task<ServiceResult<AgentActivation>> DeactivateAgentAsync(
        string activationId, 
        string tenantId)
    {
        try
        {
            _logger.LogInformation("Deactivating agent for activation {ActivationId}", activationId);

            var activation = await _activationRepository.GetByIdAsync(activationId);
            if (activation == null)
            {
                _logger.LogWarning("Activation with ID {ActivationId} not found", activationId);
                return ServiceResult<AgentActivation>.NotFound($"Activation with ID '{activationId}' not found");
            }

            if (activation.TenantId != tenantId)
            {
                _logger.LogWarning("Activation {ActivationId} does not belong to tenant {TenantId}", activationId, tenantId);
                return ServiceResult<AgentActivation>.Forbidden("Activation does not belong to this tenant");
            }

            // Perform comprehensive cleanup of all workflows and schedules associated with this activation
            _logger.LogInformation(
                "Starting comprehensive cleanup for activation {ActivationId} (Name: {Name}, Agent: {Agent})",
                activationId, activation.Name, activation.AgentName);

            var cleanupResult = await _cleanupService.CleanupActivationResourcesAsync(activation);

            if (!cleanupResult.IsSuccess || cleanupResult.Data == null)
            {
                _logger.LogError(
                    "Failed to cleanup resources for activation {ActivationId}: {Error}",
                    activationId, cleanupResult.ErrorMessage);
                return ServiceResult<AgentActivation>.InternalServerError(
                    $"Failed to cleanup activation resources: {cleanupResult.ErrorMessage}");
            }

            var cleanup = cleanupResult.Data;

            // Log cleanup results
            _logger.LogInformation(
                "Cleanup completed for activation {ActivationId}. " +
                "Workflows: {CancelledWorkflows}/{TotalWorkflows} cancelled ({FailedWorkflows} failed), " +
                "Schedules: {DeletedSchedules}/{TotalSchedules} deleted ({FailedSchedules} failed)",
                activationId,
                cleanup.WorkflowCleanup.CancelledCount,
                cleanup.WorkflowCleanup.TotalWorkflows,
                cleanup.WorkflowCleanup.FailedCount,
                cleanup.ScheduleCleanup.DeletedCount,
                cleanup.ScheduleCleanup.TotalSchedules,
                cleanup.ScheduleCleanup.FailedCount);

            // Check if there were any failures during cleanup
            if (!cleanup.Success)
            {
                _logger.LogWarning(
                    "Activation {ActivationId} cleanup had failures. " +
                    "Failed workflows: {FailedWorkflows}, Failed schedules: {FailedSchedules}",
                    activationId,
                    cleanup.WorkflowCleanup.FailedCount,
                    cleanup.ScheduleCleanup.FailedCount);
                
                // Even with some failures, we'll proceed to update the activation status
                // The user should be informed that some resources may still exist
            }

            // Clear workflow IDs and update timestamp
            activation.WorkflowIds = new List<string>();
            activation.DeactivatedAt = DateTime.UtcNow;

            await _activationRepository.UpdateAsync(activationId, activation);

            _logger.LogInformation(
                "Successfully deactivated activation {ActivationId}. " +
                "Total resources cleaned: {TotalWorkflows} workflows, {TotalSchedules} schedules",
                activationId,
                cleanup.WorkflowCleanup.TotalWorkflows,
                cleanup.ScheduleCleanup.TotalSchedules);
            
            return ServiceResult<AgentActivation>.Success(activation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deactivating agent for activation {ActivationId}", activationId);
            return ServiceResult<AgentActivation>.InternalServerError(
                "An error occurred while deactivating the agent");
        }
    }

    /// <summary>
    /// Deletes an activation from the database.
    /// Note: This will not cancel any running workflows. Deactivate first if needed.
    /// </summary>
    public async Task<ServiceResult<bool>> DeleteActivationAsync(string activationId)
    {
        try
        {
            _logger.LogInformation("Deleting activation {ActivationId}", activationId);

            if (string.IsNullOrWhiteSpace(activationId))
            {
                return ServiceResult<bool>.BadRequest("Activation ID is required");
            }

            var activation = await _activationRepository.GetByIdAsync(activationId);
            if (activation == null)
            {
                _logger.LogWarning("Activation with ID {ActivationId} not found", activationId);
                return ServiceResult<bool>.NotFound($"Activation with ID '{activationId}' not found");
            }

            // Warn if trying to delete an activation with workflows (has workflows)
            if (activation.WorkflowIds != null && activation.WorkflowIds.Count > 0)
            {
                _logger.LogWarning("Attempting to delete activation {ActivationId} with {Count} workflows running.", 
                    activationId, activation.WorkflowIds.Count);
                return ServiceResult<bool>.Conflict("Cannot delete an activation with running workflows. Please deactivate it first.");
            }

            var deleted = await _activationRepository.DeleteAsync(activationId);
            if (!deleted)
            {
                _logger.LogWarning("Failed to delete activation {ActivationId}", activationId);
                return ServiceResult<bool>.InternalServerError("Failed to delete the activation");
            }

            _logger.LogInformation("Successfully deleted activation {ActivationId}", activationId);
            return ServiceResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting activation {ActivationId}", activationId);
            return ServiceResult<bool>.InternalServerError(
                "An error occurred while deleting the activation");
        }
    }
}
