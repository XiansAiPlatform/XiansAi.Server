using MongoDB.Driver;
using System.Net.Sockets;

namespace Shared.Utils;

public static class MongoRetryHelper
{
    /// <summary>
    /// Executes a MongoDB operation with retry logic to handle write conflicts.
    /// Uses exponential backoff strategy and supports both MongoDB and CosmosDB.
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
            catch (MongoCommandException ex) when (IsRetryableException(ex) && attempt < maxRetries)
            {
                var delay = CalculateDelay(ex, baseDelay, attempt);
                logger.LogWarning(ex, "Retryable error in {OperationName} on attempt {Attempt}, retrying after {Delay}ms", 
                    operationName, attempt + 1, delay.TotalMilliseconds);
                await Task.Delay(delay);
                continue;
            }
            catch (MongoWriteException ex) when (IsRetryableWriteException(ex) && attempt < maxRetries)
            {
                var delay = CalculateDelay(ex.WriteError, baseDelay, attempt);
                logger.LogWarning(ex, "Retryable write error in {OperationName} on attempt {Attempt}, retrying after {Delay}ms", 
                    operationName, attempt + 1, delay.TotalMilliseconds);
                await Task.Delay(delay);
                continue;
            }
            catch (MongoConnectionException ex) when (attempt < maxRetries)
            {
                var delay = CalculateConnectionDelay(baseDelay, attempt);
                logger.LogWarning(ex, "Connection error in {OperationName} on attempt {Attempt}, retrying after {Delay}ms", 
                    operationName, attempt + 1, delay.TotalMilliseconds);
                await Task.Delay(delay);
                continue;
            }
            catch (TimeoutException ex) when (attempt < maxRetries)
            {
                var delay = CalculateConnectionDelay(baseDelay, attempt);
                logger.LogWarning(ex, "Timeout error in {OperationName} on attempt {Attempt}, retrying after {Delay}ms", 
                    operationName, attempt + 1, delay.TotalMilliseconds);
                await Task.Delay(delay);
                continue;
            }
            catch (SocketException ex) when (attempt < maxRetries)
            {
                var delay = CalculateConnectionDelay(baseDelay, attempt);
                logger.LogWarning(ex, "Socket error in {OperationName} on attempt {Attempt}, retrying after {Delay}ms", 
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

    /// <summary>
    /// Executes a MongoDB operation with error handling that allows graceful failure.
    /// This is useful for non-critical operations like index creation that shouldn't block application startup.
    /// </summary>
    /// <param name="operation">The operation to execute</param>
    /// <param name="logger">Logger for logging attempts</param>
    /// <param name="maxRetries">Maximum number of retry attempts (default: 2 for non-critical operations)</param>
    /// <param name="baseDelayMs">Base delay in milliseconds for exponential backoff (default: 1000)</param>
    /// <param name="operationName">Name of the operation for logging purposes</param>
    /// <returns>True if operation succeeded, false if it failed after all retries</returns>
    public static async Task<bool> ExecuteWithGracefulRetryAsync(
        Func<Task> operation,
        ILogger logger,
        int maxRetries = 2,
        int baseDelayMs = 1000,
        string operationName = "MongoDB operation")
    {
        try
        {
            await ExecuteWithRetryAsync(operation, logger, maxRetries, baseDelayMs, operationName);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Non-critical operation {OperationName} failed after all retry attempts, continuing without it", operationName);
            return false;
        }
    }

    /// <summary>
    /// Determines if a MongoCommandException is retryable
    /// </summary>
    private static bool IsRetryableException(MongoCommandException ex)
    {
        // MongoDB write conflicts
        if (ex.Message.Contains("Write conflict"))
            return true;

        // CosmosDB specific error codes
        switch (ex.Code)
        {
            case 429: // TooManyRequests (CosmosDB throttling)
            case 16500: // RequestRateTooLarge (CosmosDB)
            case 16501: // TransactionCommitTimeout (CosmosDB)
            case 112: // WriteConflict (MongoDB)
                return true;
        }

        // CosmosDB specific error labels
        if (ex.HasErrorLabel("TransientTransactionError") || 
            ex.HasErrorLabel("RetryableWriteError"))
            return true;

        return false;
    }

    /// <summary>
    /// Determines if a MongoWriteException is retryable
    /// </summary>
    private static bool IsRetryableWriteException(MongoWriteException ex)
    {
        if (ex.WriteError == null) return false;

        // CosmosDB/MongoDB specific retryable write error codes
        switch (ex.WriteError.Code)
        {
            case 429: // TooManyRequests (CosmosDB throttling)
            case 16500: // RequestRateTooLarge (CosmosDB)
            case 112: // WriteConflict (MongoDB)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Calculates delay based on error type and attempt number
    /// </summary>
    private static TimeSpan CalculateDelay(MongoCommandException ex, TimeSpan baseDelay, int attempt)
    {
        // For CosmosDB throttling (429), use longer delays
        if (ex.Code == 429 || ex.Code == 16500)
        {
            // Use exponential backoff with longer base delay for throttling
            var throttleDelay = TimeSpan.FromMilliseconds(1000 * Math.Pow(2, attempt));
            return throttleDelay;
        }

        // Standard exponential backoff for other errors
        return TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * Math.Pow(2, attempt));
    }

    /// <summary>
    /// Calculates delay based on write error and attempt number
    /// </summary>
    private static TimeSpan CalculateDelay(WriteError writeError, TimeSpan baseDelay, int attempt)
    {
        // For CosmosDB throttling (429), use longer delays
        if (writeError.Code == 429 || writeError.Code == 16500)
        {
            // Use exponential backoff with longer base delay for throttling
            var throttleDelay = TimeSpan.FromMilliseconds(1000 * Math.Pow(2, attempt));
            return throttleDelay;
        }

        // Standard exponential backoff for other errors
        return TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * Math.Pow(2, attempt));
    }

    /// <summary>
    /// Calculates delay for connection-related errors with longer delays
    /// </summary>
    private static TimeSpan CalculateConnectionDelay(TimeSpan baseDelay, int attempt)
    {
        // Use longer delays for connection issues as they often indicate infrastructure problems
        var connectionDelay = TimeSpan.FromMilliseconds(Math.Max(1000, baseDelay.TotalMilliseconds) * Math.Pow(2, attempt));
        // Cap the maximum delay at 30 seconds to avoid excessively long waits
        return connectionDelay > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : connectionDelay;
    }
} 