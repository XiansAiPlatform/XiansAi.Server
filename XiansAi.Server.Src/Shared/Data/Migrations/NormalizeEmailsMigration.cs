using MongoDB.Driver;
using Shared.Data.Models;

namespace Shared.Data.Migrations;

/// <summary>
/// Migration to normalize all existing email addresses to lowercase
/// </summary>
public class NormalizeEmailsMigration
{
    private readonly IMongoCollection<User> _users;
    private readonly ILogger<NormalizeEmailsMigration> _logger;

    public NormalizeEmailsMigration(
        IDatabaseService databaseService, 
        ILogger<NormalizeEmailsMigration> logger)
    {
        var database = databaseService.GetDatabaseAsync().Result;
        _users = database.GetCollection<User>("users");
        _logger = logger;
    }

    /// <summary>
    /// Migrate all user emails to lowercase
    /// </summary>
    public async Task<MigrationResult> MigrateAsync()
    {
        _logger.LogInformation("Starting email normalization migration...");
        
        var updateCount = 0;
        var errorCount = 0;
        var totalCount = 0;

        try
        {
            // Get all users
            var users = await _users.Find(_ => true).ToListAsync();
            totalCount = users.Count;
            
            _logger.LogInformation("Found {Count} users to process", totalCount);

            foreach (var user in users)
            {
                try
                {
                    var originalEmail = user.Email;
                    var normalizedEmail = originalEmail.ToLowerInvariant();

                    // Only update if email needs normalization
                    if (originalEmail != normalizedEmail)
                    {
                        var filter = Builders<User>.Filter.Eq(u => u.Id, user.Id);
                        var update = Builders<User>.Update
                            .Set(u => u.Email, normalizedEmail)
                            .Set(u => u.UpdatedAt, DateTime.UtcNow);

                        var result = await _users.UpdateOneAsync(filter, update);
                        
                        if (result.ModifiedCount > 0)
                        {
                            updateCount++;
                            _logger.LogInformation("Normalized email: {Original} -> {Normalized}", 
                                originalEmail, normalizedEmail);
                        }
                    }
                }
                catch (Exception ex)
                {
                    errorCount++;
                    _logger.LogError(ex, "Error normalizing email for user {UserId}", user.UserId);
                }
            }

            _logger.LogInformation(
                "Email normalization migration completed. Total: {Total}, Updated: {Updated}, Errors: {Errors}",
                totalCount, updateCount, errorCount);

            return new MigrationResult
            {
                Success = errorCount == 0,
                TotalProcessed = totalCount,
                Updated = updateCount,
                Errors = errorCount
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error during email normalization migration");
            return new MigrationResult
            {
                Success = false,
                TotalProcessed = totalCount,
                Updated = updateCount,
                Errors = errorCount + 1
            };
        }
    }
}

public class MigrationResult
{
    public bool Success { get; set; }
    public int TotalProcessed { get; set; }
    public int Updated { get; set; }
    public int Errors { get; set; }
}
