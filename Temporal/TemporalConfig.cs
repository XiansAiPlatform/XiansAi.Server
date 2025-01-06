using System.ComponentModel.DataAnnotations;

namespace XiansAi.Server.Temporal;

public class TemporalConfig
{

    [Required]
    public required string FlowServerUrl { get; set; }

    [Required]
    public required string FlowServerNamespace { get; set; }

    [Required]
    public required string FlowServerCertPath { get; set; }

    [Required]
    public required string FlowServerPrivateKeyPath { get; set; }

}