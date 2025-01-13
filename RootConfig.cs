public class FlowServerConfig
{
    // optionally read from key vault
    public string? RootCertKeyVaultName { get; set; }

    // optionally read from local file system
    public string? RootCertPath { get; set; }
    public string? RootCertPassword { get; set; }
}