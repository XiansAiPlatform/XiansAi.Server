using MongoDB.Driver;
using XiansAi.Server.Shared.Data.Models;
using Shared.Auth;
using XiansAi.Server.Shared.Data;
using Shared.Data;

namespace Features.WebApi.Repositories
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

        public WebhookRepository(IDatabaseService databaseService)
        {
            var database = databaseService.GetDatabaseAsync().Result;
            _webhooks = database.GetCollection<Webhook>("webhooks");
        }

        public async Task<Webhook> GetByIdAsync(string id, string tenantId)
        {
            return await _webhooks.Find(w => w.Id == id && w.TenantId == tenantId)
                .FirstOrDefaultAsync();
        }

        public async Task<IEnumerable<Webhook>> GetByWorkflowIdAsync(string workflowId, string tenantId)
        {
            return await _webhooks.Find(w => 
                w.WorkflowId == workflowId && 
                w.TenantId == tenantId)
                .ToListAsync();
        }

        public async Task<IEnumerable<Webhook>> GetAllForTenantAsync(string tenantId)
        {
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