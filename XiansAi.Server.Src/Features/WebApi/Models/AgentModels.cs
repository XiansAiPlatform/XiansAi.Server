namespace Features.WebApi.Models;

/// <summary>
/// Response model for agent deletion operation
/// </summary>
public class AgentDeleteResult
{
    /// <summary>
    /// Success message about the deletion
    /// </summary>
    public string Message { get; set; } = string.Empty;
    
    /// <summary>
    /// Number of flow definitions that were deleted along with the agent
    /// </summary>
    public int DeletedFlowDefinitions { get; set; }
} 