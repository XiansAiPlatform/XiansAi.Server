using System.ComponentModel.DataAnnotations;

namespace XiansAi.Server.Temporal;

public class TemporalConfig
{

    [Required]
    public required string TemporalServerUrl { get; set; }

    [Required]
    public required string Namespace { get; set; }

    [Required]
    public required string ClientCert { get; set; }

    [Required]
    public required string ClientPrivateKey { get; set; }

}