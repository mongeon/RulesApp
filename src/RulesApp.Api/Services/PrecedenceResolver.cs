using RulesApp.Shared;
using RulesApp.Api.Entities;

namespace RulesApp.Api.Services;

/// <summary>
/// Resolves precedence among chunks with the same ruleKey.
/// Precedence hierarchy: Regional > Quebec > Canada
/// Confirmed overrides can explicitly map rules across scope levels.
/// </summary>
public class PrecedenceResolver
{
    private readonly ITableStore _tableStore;

    public PrecedenceResolver(ITableStore tableStore)
    {
        _tableStore = tableStore;
    }

    /// <summary>
    /// Groups search results by ruleKey and determines the primary chunk based on precedence.
    /// </summary>
    public async Task<List<PrecedenceGroup>> ResolveAsync(
        List<SearchHit> hits,
        string seasonId,
        string? associationId)
    {
        if (hits.Count == 0)
            return new List<PrecedenceGroup>();

        // Load confirmed overrides for this season/association
        var overrides = await GetConfirmedOverridesAsync(seasonId, associationId);

        // Group by ruleKey
        var grouped = hits
            .Where(h => !string.IsNullOrEmpty(h.RuleKey))
            .GroupBy(h => h.RuleKey!);

        var result = new List<PrecedenceGroup>();

        foreach (var group in grouped)
        {
            var chunks = group.ToList();
            
            if (chunks.Count == 1)
            {
                // Single chunk, no precedence needed
                result.Add(new PrecedenceGroup(
                    group.Key,
                    chunks[0],
                    new List<SearchHit>()
                ));
            }
            else
            {
                // Multiple chunks with same ruleKey - resolve precedence
                var resolved = ResolvePrecedence(chunks, associationId, overrides);
                result.Add(resolved);
            }
        }

        return result;
    }

    /// <summary>
    /// Resolves precedence for a set of chunks with the same ruleKey.
    /// </summary>
    private PrecedenceGroup ResolvePrecedence(
        List<SearchHit> chunks,
        string? associationId,
        Dictionary<string, OverrideMappingEntity> overrides)
    {
        // Sort by natural precedence: Regional (3) > Quebec (2) > Canada (1)
        var sorted = chunks
            .Select(c => new
            {
                Chunk = c,
                Precedence = GetPrecedenceLevel(c.Scope, c.AssociationId, associationId)
            })
            .OrderByDescending(x => x.Precedence)
            .ThenByDescending(x => x.Chunk.Score)
            .ToList();

        // Check for confirmed overrides that might change the primary
        var primary = sorted[0].Chunk;
        var alternates = sorted.Skip(1).Select(x => x.Chunk).ToList();

        // Check if any override should promote a different chunk
        foreach (var chunk in sorted)
        {
            if (overrides.TryGetValue(chunk.Chunk.ChunkId, out var overrideMapping))
            {
                // This chunk has a confirmed override - it takes precedence
                if (chunk.Chunk.ChunkId != primary.ChunkId)
                {
                    // Move current primary to alternates
                    alternates.Insert(0, primary);
                    primary = chunk.Chunk;
                    alternates.Remove(chunk.Chunk);
                }
                break;
            }
        }

        return new PrecedenceGroup(
            primary.RuleKey!,
            primary,
            alternates
        );
    }

    /// <summary>
    /// Gets the precedence level for a chunk.
    /// Returns: 3 (Regional), 2 (Quebec), 1 (Canada), 0 (unknown/not applicable)
    /// </summary>
    private int GetPrecedenceLevel(string scope, string? chunkAssociationId, string? contextAssociationId)
    {
        return scope.ToLowerInvariant() switch
        {
            "regional" when chunkAssociationId == contextAssociationId => 3,
            "regional" when contextAssociationId != null => 0, // Different association, not applicable
            "quebec" => 2,
            "canada" => 1,
            _ => 0
        };
    }

    /// <summary>
    /// Retrieves confirmed override mappings for a season/association.
    /// Returns dictionary keyed by sourceChunkId for quick lookup.
    /// </summary>
    public async Task<Dictionary<string, OverrideMappingEntity>> GetConfirmedOverridesAsync(
        string seasonId,
        string? associationId)
    {
        var tableName = "OverrideMappings";
        var partitionKey = $"{seasonId}:{associationId ?? "global"}";

        try
        {
            var filter = $"PartitionKey eq '{partitionKey}' and Status eq 'Confirmed'";
            var entities = await _tableStore.QueryAsync<OverrideMappingEntity>(
                tableName,
                filter
            );

            return entities
                .Where(e => e.SourceChunkId != null)
                .ToDictionary(e => e.SourceChunkId!, e => e);
        }
        catch
        {
            // Table might not exist yet or other error - return empty
            return new Dictionary<string, OverrideMappingEntity>();
        }
    }
}
