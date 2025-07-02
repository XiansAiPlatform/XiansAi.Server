using MongoDB.Driver;
using XiansAi.Server.Shared.Data.Models;
using Shared.Auth;
using XiansAi.Server.Shared.Data;
using Shared.Data;

namespace Shared.Repositories
{
    public interface IWebhookRepository
    {
        Task<Webhook> GetByIdAsync(string id, string tenantId);
        Task<IEnumerable<Webhook>> GetByWorkflowIdAsync(string workflowId, string tenantId);
        Task<IEnumerable<Webhook>> GetAllForTenantAsync(string tenantId);
        Task<Webhook> CreateAsync(Webhook webhook);
        Task<bool> DeleteAsync(string id, string tenantId);
        Task UpdateAsync(Webhook webhook);
    }

    public class WebhookRepository : IWebhookRepository
    {
        private readonly IMongoCollection<Webhook> _webhooks;
        private readonly Lazy<Task> _indexCreationTask;
        private volatile bool _indexesCreated = false;

        public WebhookRepository(IDatabaseService databaseService)
        {
            var database = databaseService.GetDatabaseAsync().Result;
            _webhooks = database.GetCollection<Webhook>("webhooks");
            
            // Initialize indexes asynchronously without blocking constructor
            _indexCreationTask = new Lazy<Task>(() => InitializeIndexesAsync());
            
            // Start index creation in background (fire and forget)
            _ = Task.Run(async () =>
            {
                try
                {
                    await _indexCreationTask.Value;
                }
                catch (Exception ex)
                {
                    // Log but don't crash - we don't have a logger here, so we'll use console
                    Console.WriteLine($"Warning: Background index creation failed during WebhookRepository initialization: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Initializes indexes asynchronously with resilient error handling.
        /// This method won't throw exceptions and allows the application to continue running even if MongoDB is temporarily unavailable.
        /// </summary>
        private async Task InitializeIndexesAsync()
        {
            if (_indexesCreated) return;

            try
            {
                // Create indexes if they don't exist
                var indexKeysId = Builders<Webhook>.IndexKeys.Ascending(x => x.TenantId);
                var indexOptionsId = new CreateIndexOptions { Background = true };
                var indexModelId = new CreateIndexModel<Webhook>(indexKeysId, indexOptionsId);
                await _webhooks.Indexes.CreateOneAsync(indexModelId);

                var indexKeysWorkflow = Builders<Webhook>.IndexKeys
                    .Ascending(x => x.WorkflowId)
                    .Ascending(x => x.TenantId);
                var indexOptionsWorkflow = new CreateIndexOptions { Background = true };
                var indexModelWorkflow = new CreateIndexModel<Webhook>(indexKeysWorkflow, indexOptionsWorkflow);
                await _webhooks.Indexes.CreateOneAsync(indexModelWorkflow);

                _indexesCreated = true;
                Console.WriteLine("Successfully created indexes for Webhook collection");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to create indexes for Webhook collection, but repository will continue to function: {ex.Message}");
            }
        }

        /// <summary>
        /// Ensures indexes are created before performing operations (lazy initialization).
        /// This method is called by repository methods that benefit from having indexes.
        /// </summary>
        private async Task EnsureIndexesAsync()
        {
            if (!_indexesCreated)
            {
                try
                {
                    await _indexCreationTask.Value;
                }
                catch (Exception)
                {
                    // Ignore - the operation can continue without indexes
                }
            }
        }

        public async Task<Webhook> GetByIdAsync(string id, string tenantId)
        {
            return await _webhooks.Find(w => w.Id == id && w.TenantId == tenantId)
                .FirstOrDefaultAsync();
        }

        public async Task<IEnumerable<Webhook>> GetByWorkflowIdAsync(string workflowId, string tenantId)
        {
            // Ensure indexes are created for optimal query performance
            await EnsureIndexesAsync();
            
            return await _webhooks.Find(w => 
                w.WorkflowId == workflowId && 
                w.TenantId == tenantId)
                .ToListAsync();
        }

        public async Task<IEnumerable<Webhook>> GetAllForTenantAsync(string tenantId)
        {
            // Ensure indexes are created for optimal query performance
            await EnsureIndexesAsync();
            
            return await _webhooks.Find(w => w.TenantId == tenantId)
                .SortByDescending(w => w.CreatedAt)
                .ToListAsync();
        }

        public async Task<Webhook> CreateAsync(Webhook webhook)
        {
            webhook.CreatedAt = DateTime.UtcNow;
            await _webhooks.InsertOneAsync(webhook);
            return webhook;
        }

        public async Task<bool> DeleteAsync(string id, string tenantId)
        {
            var result = await _webhooks.DeleteOneAsync(w => w.Id == id && w.TenantId == tenantId);
            return result.DeletedCount > 0;
        }

        public async Task UpdateAsync(Webhook webhook)
        {
            var filter = Builders<Webhook>.Filter.Eq(w => w.Id, webhook.Id);
            await _webhooks.ReplaceOneAsync(filter, webhook);
        }
    }
} 