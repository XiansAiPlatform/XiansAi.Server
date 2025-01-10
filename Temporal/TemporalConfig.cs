using System.ComponentModel.DataAnnotations;

namespace XiansAi.Server.Temporal;

public class TemporalConfig
{

    [Required]
    public required string FlowServerUrl { get; set; }

    [Required]
    public required string FlowServerNamespace { get; set; }

    // optionally read from local file system
    public string? CertificateFilePath { get; set; }
    public string? PrivateKeyFilePath { get; set; }

    // optionally read from key vault as secrets
    public string? CertificateKeyVaultName { get; set; }
    public string? PrivateKeyKeyVaultName { get; set; }

}