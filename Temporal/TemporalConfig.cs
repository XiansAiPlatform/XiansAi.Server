using System.ComponentModel.DataAnnotations;

namespace XiansAi.Server.Temporal;

public class TemporalConfig
{
    [Required]
    public string? ServiceAccountApiKey { get; set; }

    public string? FlowServerUrl { get; set; }

    public string? FlowServerNamespace { get; set; }

    // optionally read from local file system
    public string? CertificateFilePath { get; set; }
    public string? PrivateKeyFilePath { get; set; }

    // optionally read from key vault as secrets
    public string? CertificateKeyVaultName { get; set; }
    public string? PrivateKeyKeyVaultName { get; set; }

}