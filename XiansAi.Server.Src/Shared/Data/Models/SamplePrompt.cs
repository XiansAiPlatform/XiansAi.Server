using System.ComponentModel.DataAnnotations;

namespace Shared.Data.Models;

/// <summary>
/// Represents a sample prompt for an agent template.
/// Sample prompts are used for testing and demonstrating agent capabilities.
/// </summary>
public class SamplePrompt
{
    /// <summary>
    /// Unique identifier for the sample prompt
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Title of the sample prompt (displayed in UI)
    /// </summary>
    [StringLength(200, MinimumLength = 1)]
    [Required]
    public required string Title { get; set; }

    /// <summary>
    /// The actual prompt text to send to the agent
    /// </summary>
    [StringLength(2000, MinimumLength = 1)]
    [Required]
    public required string Prompt { get; set; }

    /// <summary>
    /// Description of what this prompt tests or demonstrates
    /// </summary>
    [StringLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Icon identifier or URL for the prompt (optional)
    /// Can be an icon name (e.g., "document", "calendar", "eye") or an icon URL
    /// </summary>
    [StringLength(200)]
    public string? Icon { get; set; }

    /// <summary>
    /// Category of the prompt (basic, advanced, edge-case)
    /// </summary>
    [StringLength(50)]
    public string? Category { get; set; }
}



