namespace XiansAi.Server.Database;

public interface IMongoDBConfig
{
    string ConnectionString { get; set; }
    string DatabaseName { get; set; }
}

public class MongoDBConfig: IMongoDBConfig
{

    public required string ConnectionString { get; set; }

    public required string DatabaseName { get; set; }
    
}
