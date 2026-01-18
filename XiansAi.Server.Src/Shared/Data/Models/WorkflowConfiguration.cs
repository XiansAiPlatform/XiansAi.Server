using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace Shared.Data.Models;

/// <summary>
/// Represents a single input parameter for a workflow
/// </summary>
[BsonIgnoreExtraElements]
public class WorkflowInput
{
    [BsonElement("name")]
    [Required(ErrorMessage = "Input name is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Input name must be between 1 and 100 characters")]
    public required string Name { get; set; }

    [BsonElement("value")]
    [Required(ErrorMessage = "Input value is required")]
    public required string Value { get; set; }
}

/// <summary>
/// Represents a workflow configuration with its type and inputs
/// </summary>
[BsonIgnoreExtraElements]
public class WorkflowConfiguration
{
    [BsonElement("workflow_type")]
    [Required(ErrorMessage = "Workflow type is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Workflow type must be between 1 and 100 characters")]
    public required string WorkflowType { get; set; }

    [BsonElement("inputs")]
    public List<WorkflowInput> Inputs { get; set; } = new();
}

/// <summary>
/// Container for multiple workflow configurations in an activation
/// </summary>
[BsonIgnoreExtraElements]
public class ActivationWorkflowConfiguration
{
    [BsonElement("workflows")]
    public List<WorkflowConfiguration> Workflows { get; set; } = new();

    /// <summary>
    /// Validates that at least one workflow is configured
    /// </summary>
    public void Validate()
    {
        if (Workflows == null || Workflows.Count == 0)
        {
            throw new ValidationException("At least one workflow configuration is required");
        }

        foreach (var workflow in Workflows)
        {
            if (string.IsNullOrWhiteSpace(workflow.WorkflowType))
            {
                throw new ValidationException("Workflow type is required for all workflows");
            }

            if (workflow.Inputs != null)
            {
                foreach (var input in workflow.Inputs)
                {
                    if (string.IsNullOrWhiteSpace(input.Name))
                    {
                        throw new ValidationException($"Input name is required for workflow {workflow.WorkflowType}");
                    }
                }
            }
        }
    }
}
