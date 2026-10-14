using Shared.Repositories;

namespace Shared.Data.Migrations;

/// <summary>
/// Backfills knowledge.updated_at = created_at for legacy documents that predate the field.
/// Idempotent: only documents without updated_at are touched.
/// </summary>
public class KnowledgeUpdatedAtMigration
{
    private readonly IKnowledgeRepository _knowledgeRepository;
    private readonly ILogger<KnowledgeUpdatedAtMigration> _logger;

    public KnowledgeUpdatedAtMigration(
        IKnowledgeRepository knowledgeRepository,
        ILogger<KnowledgeUpdatedAtMigration> logger)
    {
        _knowledgeRepository = knowledgeRepository;
        _logger = logger;
    }

    public async Task<long> MigrateAsync()
    {
        var modified = await _knowledgeRepository.BackfillUpdatedAtAsync();
        _logger.LogInformation("Knowledge updated_at backfill complete. Documents updated: {Count}", modified);
        return modified;
    }
}
