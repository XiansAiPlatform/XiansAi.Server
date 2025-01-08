namespace XiansAi.Server.MongoDB;

public class MongoDBConfig
{
    public required string ConnectionString { get; set; }
    public required string DatabaseName { get; set; }

    // optionally read from local file system
    public string? CertificateFilePath { get; set; }
    public string? CertificateFilePassword { get; set; }

    // optionally read from key vault
    public string? CertificateKeyVaultName { get; set; }
}
