using Shared.Data.Models;

namespace Shared.Repositories;

/// <summary>
/// Repository interface for managing agent templates (system-scoped reusable agent definitions).
/// </summary>
public interface IAgentTemplateRepository
{
    /// <summary>
    /// Gets an agent template by name.
    /// </summary>
    Task<AgentTemplate?> GetByNameAsync(string name);

    /// <summary>
    /// Gets all agent templates.
    /// </summary>
    Task<List<AgentTemplate>> GetAllAsync();

    /// <summary>
    /// Gets agent templates by category.
    /// </summary>
    Task<List<AgentTemplate>> GetByCategoryAsync(string category);

    /// <summary>
    /// Creates a new agent template.
    /// </summary>
    Task CreateAsync(AgentTemplate template);

    /// <summary>
    /// Gets an agent template by MongoDB ObjectId.
    /// </summary>
    Task<AgentTemplate?> GetByIdAsync(string id);

    /// <summary>
    /// Updates an existing agent template.
    /// </summary>
    Task<bool> UpdateAsync(string id, AgentTemplate template);

    /// <summary>
    /// Deletes an agent template.
    /// </summary>
    Task<bool> DeleteAsync(string id);

    /// <summary>
    /// Checks if an agent template exists by name.
    /// </summary>
    Task<bool> ExistsAsync(string name);
}



