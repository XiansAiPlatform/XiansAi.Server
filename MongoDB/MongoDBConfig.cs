public class MongoDBConfig
{
    public required string ConnectionString { get; set; }
    public required string DatabaseName { get; set; }
    public required string PfxPath { get; set; }
    public required string PfxPassphrase { get; set; }
}
