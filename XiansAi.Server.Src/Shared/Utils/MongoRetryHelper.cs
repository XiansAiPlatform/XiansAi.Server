using MongoDB.Driver;
using Microsoft.Extensions.Logging;

namespace Shared.Utils;

public static class MongoRetryHelper
{
    /// <summary>
    /// Executes a MongoDB operation with retry logic to handle write conflicts.
    /// Uses exponential backoff strategy.
    /// </summary>
    /// <typeparam name="T">The return type of the operation</typeparam>
    /// <param name="operation">The operation to execute</param>
    /// <param name="logger">Logger for logging retry attempts</param>
    /// <param name="maxRetries">Maximum number of retry attempts (default: 3)</param>
    /// <param name="baseDelayMs">Base delay in milliseconds for exponential backoff (default: 50)</param>
    /// <param name="operationName">Name of the operation for logging purposes</param>
    /// <returns>The result of the operation</returns>
    public static async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation,
        ILogger logger,
        int maxRetries = 3,
        int baseDelayMs = 50,
        string operationName = "MongoDB operation")
    {
        var baseDelay = TimeSpan.FromMilliseconds(baseDelayMs);
        
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                var result = await operation();
                if (attempt > 0)
                {
                    logger.LogDebug("Successfully completed {OperationName} on attempt {Attempt}", operationName, attempt + 1);
                }
                return result;
            }
            catch (MongoCommandException ex) when (ex.Message.Contains("Write conflict") && attempt < maxRetries)
            {
                var delay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * Math.Pow(2, attempt));
                logger.LogWarning(ex, "Write conflict detected in {OperationName} on attempt {Attempt}, retrying after {Delay}ms", 
                    operationName, attempt + 1, delay.TotalMilliseconds);
                await Task.Delay(delay);
                continue;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in {OperationName} on attempt {Attempt}", operationName, attempt + 1);
                throw;
            }
        }
        
        throw new InvalidOperationException($"Failed to complete {operationName} after {maxRetries + 1} attempts");
    }

    /// <summary>
    /// Executes a MongoDB operation with retry logic to handle write conflicts.
    /// This overload is for operations that don't return a value.
    /// </summary>
    /// <param name="operation">The operation to execute</param>
    /// <param name="logger">Logger for logging retry attempts</param>
    /// <param name="maxRetries">Maximum number of retry attempts (default: 3)</param>
    /// <param name="baseDelayMs">Base delay in milliseconds for exponential backoff (default: 50)</param>
    /// <param name="operationName">Name of the operation for logging purposes</param>
    public static async Task ExecuteWithRetryAsync(
        Func<Task> operation,
        ILogger logger,
        int maxRetries = 3,
        int baseDelayMs = 50,
        string operationName = "MongoDB operation")
    {
        await ExecuteWithRetryAsync(async () =>
        {
            await operation();
            return true; // Dummy return value
        }, logger, maxRetries, baseDelayMs, operationName);
    }
} 