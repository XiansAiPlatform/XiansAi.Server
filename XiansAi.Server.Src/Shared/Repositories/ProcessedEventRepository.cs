using MongoDB.Driver;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Shared.Data;
using Shared.Utils;
using Shared.Auth;
using Shared.Services;

namespace Shared.Repositories;

public class ProcessedEvent
{
    [BsonId]
    public string DocumentId { get; set; } // ResumeToken string
    public DateTime ProcessedAt { get; set; }
}

public interface IProcessedEventRepository
{
    Task<bool> CreateProcessedEventAsync(string documentId);
}
public class ProcessedEventRepository : IProcessedEventRepository
{
    private readonly IMongoCollection<ProcessedEvent> _processedEvents;
    private readonly ILogger<ProcessedEventRepository> _logger;

    public ProcessedEventRepository(IDatabaseService databaseService, ILogger<ProcessedEventRepository> logger)
    {
        var database = databaseService.GetDatabaseAsync().Result;
        _processedEvents = database.GetCollection<ProcessedEvent>("processed_events");
        _logger = logger;
    }

    public async Task<bool> CreateProcessedEventAsync(string documentId)
    {
        try
        {
            return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
            {
                ProcessedEvent processedEvent = new ProcessedEvent
                {
                    DocumentId = documentId,
                    ProcessedAt = DateTime.UtcNow
                };

                await _processedEvents.InsertOneAsync(processedEvent);

                return true;
            },
            _logger,
            maxRetries: 3,
            baseDelayMs: 100,
            operationName: "CreateProcessedEvent");
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }
}
