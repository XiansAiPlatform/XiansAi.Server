public class MongoDBConfig
{
    public required string ConnectionString { get; set; }
    public required string DatabaseName { get; set; }
    public required string CertificatePath { get; set; }
    public required string CertificatePassword { get; set; }
}
