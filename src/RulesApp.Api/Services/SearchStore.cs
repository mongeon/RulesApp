using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using RulesApp.Shared;
using RulesApp.Shared.Helpers;

namespace RulesApp.Api.Services;

public interface ISearchStore
{
    Task CreateOrUpdateIndexAsync(CancellationToken ct = default);
    Task<int> UpsertChunksAsync(string seasonId, string? associationId, DocType docType, List<RuleChunkDto> chunks, CancellationToken ct = default);
    Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken ct = default);
    Task<long> GetDocumentCountAsync(CancellationToken ct = default);
    Task<List<SearchHit>> GetChunksByRuleKeyAsync(string ruleKey, string seasonId, string? associationId, CancellationToken ct = default);
    Task<List<SearchHit>> GetChunksByRuleKeyAndScopeAsync(string ruleKey, string scope, string seasonId, string? associationId, CancellationToken ct = default);
}

public class SearchStore : ISearchStore
{
    private readonly SearchIndexClient _indexClient;
    private readonly string _indexName;

    public SearchStore(string endpoint, string adminKey, string indexName)
    {
        _indexClient = new SearchIndexClient(new Uri(endpoint), new AzureKeyCredential(adminKey));
        _indexName = indexName;
    }

    public async Task CreateOrUpdateIndexAsync(CancellationToken ct = default)
    {
        var definition = new SearchIndex(_indexName)
        {
            Fields =
            {
                new SimpleField("chunkId", SearchFieldDataType.String) { IsKey = true },
                new SearchableField("seasonId") { IsFilterable = true, IsFacetable = true },
                new SearchableField("associationId") { IsFilterable = true, IsFacetable = true },
                new SearchableField("docType") { IsFilterable = true, IsFacetable = true },
                new SearchableField("scope") { IsFilterable = true, IsFacetable = true },
                new SearchableField("ruleKey") { IsFilterable = true },
                new SearchableField("ruleNumberText"),
                new SearchableField("title"),
                new SearchableField("text"),
                new SimpleField("pageStart", SearchFieldDataType.Int32) { IsFilterable = true },
                new SimpleField("pageEnd", SearchFieldDataType.Int32) { IsFilterable = true },
                new SimpleField("textLength", SearchFieldDataType.Int32) { IsFilterable = true }
            }
        };

        await _indexClient.CreateOrUpdateIndexAsync(definition, cancellationToken: ct);
    }

    public async Task<int> UpsertChunksAsync(string seasonId, string? associationId, DocType docType, List<RuleChunkDto> chunks, CancellationToken ct = default)
    {
        var searchClient = _indexClient.GetSearchClient(_indexName);
        
        var docs = chunks.Select(c => new SearchDocument
        {
            ["chunkId"] = c.ChunkId,
            ["seasonId"] = seasonId,
            ["associationId"] = associationId,
            ["docType"] = docType.ToString(),
            ["scope"] = GetScope(docType),
            ["ruleKey"] = c.RuleKey,
            ["ruleNumberText"] = c.RuleNumberText,
            ["title"] = c.Title,
            ["text"] = c.Text,
            ["pageStart"] = c.PageStart,
            ["pageEnd"] = c.PageEnd,
            ["textLength"] = c.Text.Length
        }).ToList();

        if (docs.Count == 0)
            return 0;

        var batch = IndexDocumentsBatch.Upload(docs);
        var result = await searchClient.IndexDocumentsAsync(batch, cancellationToken: ct);
        
        return result.Value.Results.Count(r => r.Succeeded);
    }

    public async Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken ct = default)
    {
        var searchClient = _indexClient.GetSearchClient(_indexName);
        
        var options = new SearchOptions
        {
            Size = request.Top,
            IncludeTotalCount = true,
            // Use hybrid search for better semantic matching
            QueryType = Azure.Search.Documents.Models.SearchQueryType.Simple,
            // Boost rule number and title fields for better relevance
            SearchFields = { "text", "ruleNumberText", "title" },
            // Minimum score threshold to filter out irrelevant results
            MinimumCoverage = 80.0
        };

        // Build filter
        var filters = new List<string>();
        
        if (!string.IsNullOrEmpty(request.SeasonId))
            filters.Add($"seasonId eq '{request.SeasonId}'");
        
        if (request.Scopes != null && request.Scopes.Any())
            filters.Add($"search.in(scope, '{string.Join(",", request.Scopes)}', ',')");
        
        // Association filter logic
        if (!string.IsNullOrEmpty(request.AssociationId))
        {
            // Include docs for this association OR global docs (null associationId)
            filters.Add($"(associationId eq '{request.AssociationId}' or associationId eq null)");
        }
        else
        {
            // Only global docs
            filters.Add("associationId eq null");
        }

        if (filters.Any())
            options.Filter = string.Join(" and ", filters);

        var response = await searchClient.SearchAsync<SearchDocument>(request.Query, options, cancellationToken: ct);
        
        var results = new List<SearchHit>();
        await foreach (var result in response.Value.GetResultsAsync())
        {
            var doc = result.Document;
            results.Add(new SearchHit(
                ChunkId: doc["chunkId"].ToString()!,
                RuleKey: doc.ContainsKey("ruleKey") ? doc["ruleKey"]?.ToString() : null,
                RuleNumberText: doc.ContainsKey("ruleNumberText") ? doc["ruleNumberText"]?.ToString() : null,
                Title: doc.ContainsKey("title") ? doc["title"]?.ToString() : null,
                Scope: doc["scope"].ToString()!,
                DocType: doc["docType"].ToString()!,
                SeasonId: doc["seasonId"].ToString()!,
                AssociationId: doc.ContainsKey("associationId") ? doc["associationId"]?.ToString() : null,
                PageStart: Convert.ToInt32(doc["pageStart"]),
                PageEnd: Convert.ToInt32(doc["pageEnd"]),
                Text: doc["text"].ToString()!,
                TextPreview: TruncateText(doc["text"].ToString()!, 200),
                Score: result.Score ?? 0
            ));
        }

        return new SearchResponse(
            Query: request.Query,
            TotalResults: (int)(response.Value.TotalCount ?? 0),
            Results: results
        );
    }

    public async Task<long> GetDocumentCountAsync(CancellationToken ct = default)
    {
        var searchClient = _indexClient.GetSearchClient(_indexName);
        var count = await searchClient.GetDocumentCountAsync(ct);
        return count.Value;
    }

    private static string GetScope(DocType docType) => docType switch
    {
        DocType.CanadaFr or DocType.CanadaEn => "Canada",
        DocType.QuebecFr => "Quebec",
        DocType.RegionalFr => "Regional",
        _ => "Unknown"
    };

    /// <summary>
    /// Retrieves ALL chunks with a specific RuleKey for a given season and association.
    /// Used to ensure complete rule context is available after a rule is selected.
    /// </summary>
    public async Task<List<SearchHit>> GetChunksByRuleKeyAsync(
        string ruleKey,
        string seasonId,
        string? associationId,
        CancellationToken ct = default)
    {
        var searchClient = _indexClient.GetSearchClient(_indexName);
        
        var options = new SearchOptions
        {
            Size = 100,  // Get up to 100 chunks per rule
            IncludeTotalCount = true,
            QueryType = Azure.Search.Documents.Models.SearchQueryType.Simple
        };

        // Build filter for this rule key, season, and association
        var filters = new List<string>();
        filters.Add($"seasonId eq '{seasonId}'");
        filters.Add($"ruleKey eq '{ruleKey}'");
        
        if (!string.IsNullOrEmpty(associationId))
        {
            filters.Add($"(associationId eq '{associationId}' or associationId eq null)");
        }
        else
        {
            filters.Add("associationId eq null");
        }

        options.Filter = string.Join(" and ", filters);

        var response = await searchClient.SearchAsync<SearchDocument>("*", options, cancellationToken: ct);
        
        var results = new List<SearchHit>();
        await foreach (var result in response.Value.GetResultsAsync())
        {
            var doc = result.Document;
            results.Add(new SearchHit(
                ChunkId: doc["chunkId"].ToString()!,
                RuleKey: doc.ContainsKey("ruleKey") ? doc["ruleKey"]?.ToString() : null,
                RuleNumberText: doc.ContainsKey("ruleNumberText") ? doc["ruleNumberText"]?.ToString() : null,
                Title: doc.ContainsKey("title") ? doc["title"]?.ToString() : null,
                Scope: doc["scope"].ToString()!,
                DocType: doc["docType"].ToString()!,
                SeasonId: doc["seasonId"].ToString()!,
                AssociationId: doc.ContainsKey("associationId") ? doc["associationId"]?.ToString() : null,
                PageStart: Convert.ToInt32(doc["pageStart"]),
                PageEnd: Convert.ToInt32(doc["pageEnd"]),
                Text: doc["text"].ToString()!,
                TextPreview: TruncateText(doc["text"].ToString()!, 200),
                Score: result.Score ?? 0
            ));
        }

        return results;
    }

    /// <summary>
    /// Retrieves ALL chunks with a specific RuleKey AND Scope for a given season and association.
    /// Ensures that chunks from lower-precedence scopes are NOT included.
    /// </summary>
    public async Task<List<SearchHit>> GetChunksByRuleKeyAndScopeAsync(
        string ruleKey,
        string scope,
        string seasonId,
        string? associationId,
        CancellationToken ct = default)
    {
        var searchClient = _indexClient.GetSearchClient(_indexName);
        
        var options = new SearchOptions
        {
            Size = 100,  // Get up to 100 chunks per rule
            IncludeTotalCount = true,
            QueryType = Azure.Search.Documents.Models.SearchQueryType.Simple
        };

        // Build filter for this rule key, scope, season, and association
        var filters = new List<string>();
        filters.Add($"seasonId eq '{seasonId}'");
        filters.Add($"ruleKey eq '{ruleKey}'");
        filters.Add($"scope eq '{scope}'");  // IMPORTANT: Only this scope, not fallback to lower precedence
        
        if (!string.IsNullOrEmpty(associationId))
        {
            filters.Add($"(associationId eq '{associationId}' or associationId eq null)");
        }
        else
        {
            filters.Add("associationId eq null");
        }

        options.Filter = string.Join(" and ", filters);

        var response = await searchClient.SearchAsync<SearchDocument>("*", options, cancellationToken: ct);
        
        var results = new List<SearchHit>();
        await foreach (var result in response.Value.GetResultsAsync())
        {
            var doc = result.Document;
            results.Add(new SearchHit(
                ChunkId: doc["chunkId"].ToString()!,
                RuleKey: doc.ContainsKey("ruleKey") ? doc["ruleKey"]?.ToString() : null,
                RuleNumberText: doc.ContainsKey("ruleNumberText") ? doc["ruleNumberText"]?.ToString() : null,
                Title: doc.ContainsKey("title") ? doc["title"]?.ToString() : null,
                Scope: doc["scope"].ToString()!,
                DocType: doc["docType"].ToString()!,
                SeasonId: doc["seasonId"].ToString()!,
                AssociationId: doc.ContainsKey("associationId") ? doc["associationId"]?.ToString() : null,
                PageStart: Convert.ToInt32(doc["pageStart"]),
                PageEnd: Convert.ToInt32(doc["pageEnd"]),
                Text: doc["text"].ToString()!,
                TextPreview: TruncateText(doc["text"].ToString()!, 200),
                Score: result.Score ?? 0
            ));
        }

        return results;
    }

    private static string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;
        
        return text.Substring(0, maxLength);
    }
}

