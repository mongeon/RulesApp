using System.Text;
using System.Text.Json;
using RulesApp.Shared;

namespace RulesApp.Api.Services;

public interface IChatService
{
    Task<ChatResponse> ProcessQueryAsync(ChatRequest request, CancellationToken cancellationToken = default);
}

public class ChatService : IChatService
{
    private readonly ISearchStore _searchStore;
    private readonly PrecedenceResolver _precedenceResolver;
    private readonly HttpClient? _httpClient;
    private readonly string? _openAiEndpoint;
    private readonly string? _openAiKey;
    private readonly string? _openAiDeploymentName;

    public ChatService(
        ISearchStore searchStore,
        PrecedenceResolver precedenceResolver,
        string? openAiEndpoint = null,
        string? openAiKey = null,
        string? openAiDeploymentName = null)
    {
        _searchStore = searchStore;
        _precedenceResolver = precedenceResolver;
        _openAiEndpoint = openAiEndpoint;
        _openAiKey = openAiKey;
        _openAiDeploymentName = openAiDeploymentName;

        if (!string.IsNullOrEmpty(_openAiEndpoint) && !string.IsNullOrEmpty(_openAiKey))
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("api-key", _openAiKey);
        }
    }

    public async Task<ChatResponse> ProcessQueryAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        // 1. Retrieve candidates from search
        var searchRequest = new SearchRequest(
            Query: request.Query,
            SeasonId: request.SeasonId,
            AssociationId: request.AssociationId,
            Scopes: null, // Search all scopes
            Top: 20 // Retrieve more for precedence grouping
        );

        var searchResults = await _searchStore.SearchAsync(searchRequest, cancellationToken);

        if (searchResults.TotalResults == 0)
        {
            return new ChatResponse(
                Status: "not_found",
                Query: request.Query,
                Answer: "No relevant rules found in the provided rulebooks.",
                Citations: new List<ChatCitation>(),
                ContextUsed: 0,
                TotalRetrieved: 0
            );
        }

        // 2. Apply precedence resolution
        var precedenceGroups = await _precedenceResolver.ResolveAsync(
            searchResults.Results,
            request.SeasonId ?? "2025",
            request.AssociationId
        );

        // 3. Select top contexts (primary chunks from each rule group + ungrouped)
        var contextChunks = new List<SearchHit>();
        
        // Add primary chunks from rule groups (respecting precedence)
        foreach (var group in precedenceGroups.Take(request.MaxContext))
        {
            contextChunks.Add(group.PrimaryChunk);
        }
        
        // Fill remaining slots with results that weren't grouped
        var remainingSlots = request.MaxContext - contextChunks.Count;
        if (remainingSlots > 0)
        {
            var groupedChunkIds = precedenceGroups
                .SelectMany(g => new[] { g.PrimaryChunk.ChunkId }.Concat(g.AlternateChunks.Select(c => c.ChunkId)))
                .ToHashSet();
            
            var ungrouped = searchResults.Results
                .Where(h => !groupedChunkIds.Contains(h.ChunkId))
                .Take(remainingSlots);
            
            contextChunks.AddRange(ungrouped);
        }

        if (contextChunks.Count == 0)
        {
            return new ChatResponse(
                Status: "not_found",
                Query: request.Query,
                Answer: "No relevant rules found in the provided rulebooks.",
                Citations: new List<ChatCitation>(),
                ContextUsed: 0,
                TotalRetrieved: searchResults.TotalResults
            );
        }

        // 4. Generate answer (with or without AI)
        string answer;
        if (request.UseAI && _httpClient != null && !string.IsNullOrEmpty(_openAiEndpoint))
        {
            answer = await GenerateAIAnswerAsync(request.Query, contextChunks, cancellationToken);
        }
        else
        {
            answer = GenerateDirectAnswer(contextChunks);
        }

        // 5. Build citations
        var citations = contextChunks.Select(chunk => new ChatCitation(
            ChunkId: chunk.ChunkId,
            RuleKey: chunk.RuleKey,
            RuleNumberText: chunk.RuleNumberText,
            Title: chunk.Title,
            Scope: chunk.Scope,
            DocType: chunk.DocType,
            SeasonId: chunk.SeasonId,
            AssociationId: chunk.AssociationId,
            PageStart: chunk.PageStart,
            PageEnd: chunk.PageEnd,
            TextPreview: chunk.TextPreview
        )).ToList();

        // 6. Validate citations (ensure answer doesn't reference chunks not in context)
        var isValid = ValidateCitations(answer, citations);
        if (!isValid)
        {
            // Log warning but continue (strict validation for production)
            Console.WriteLine($"[WARNING] Answer may contain ungrounded information for query: {request.Query}");
        }

        return new ChatResponse(
            Status: "ok",
            Query: request.Query,
            Answer: answer,
            Citations: citations,
            ContextUsed: contextChunks.Count,
            TotalRetrieved: searchResults.TotalResults
        );
    }

    private string GenerateDirectAnswer(List<SearchHit> contextChunks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Based on the rulebook context:");
        sb.AppendLine();

        foreach (var chunk in contextChunks)
        {
            var ruleId = chunk.RuleNumberText ?? chunk.RuleKey ?? "N/A";
            var title = chunk.Title ?? "Untitled";
            sb.AppendLine($"**{ruleId} - {title}** ({chunk.Scope}, Page {chunk.PageStart})");
            sb.AppendLine(chunk.TextPreview);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private async Task<string> GenerateAIAnswerAsync(
        string query,
        List<SearchHit> contextChunks,
        CancellationToken cancellationToken)
    {
        if (_httpClient == null || string.IsNullOrEmpty(_openAiEndpoint) || string.IsNullOrEmpty(_openAiDeploymentName))
        {
            return GenerateDirectAnswer(contextChunks);
        }

        // Build context string
        var contextBuilder = new StringBuilder();
        foreach (var chunk in contextChunks)
        {
            var ruleId = chunk.RuleNumberText ?? chunk.RuleKey ?? "N/A";
            contextBuilder.AppendLine($"Rule {ruleId} ({chunk.Scope}, Page {chunk.PageStart}):");
            contextBuilder.AppendLine(chunk.TextPreview);
            contextBuilder.AppendLine();
        }

        // Build prompt
        var systemPrompt = @"You are a baseball rules assistant. Answer questions strictly based on the provided rule context.
CRITICAL REQUIREMENTS:
1. Only use information from the provided context
2. Always cite rule numbers and page numbers
3. If the context doesn't contain enough information, say so
4. Never invent or assume rules
5. Answer in the same language as the question
6. Be concise but complete";

        var userPrompt = $@"Context from official rulebooks:
{contextBuilder}

Question: {query}

Answer the question based ONLY on the context above. Include rule numbers and page references in your answer.";

        var requestBody = new
        {
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            max_tokens = 800,
            temperature = 0.3,
            top_p = 0.95
        };

        try
        {
            var jsonContent = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var apiUrl = $"{_openAiEndpoint.TrimEnd('/')}/openai/deployments/{_openAiDeploymentName}/chat/completions?api-version=2024-02-15-preview";
            var response = await _httpClient.PostAsync(apiUrl, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"[WARNING] OpenAI API error: {response.StatusCode}");
                return GenerateDirectAnswer(contextChunks);
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var apiResponse = JsonSerializer.Deserialize<JsonElement>(responseJson);

            if (apiResponse.TryGetProperty("choices", out var choices) &&
                choices.GetArrayLength() > 0 &&
                choices[0].TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var aiContent))
            {
                return aiContent.GetString() ?? GenerateDirectAnswer(contextChunks);
            }

            return GenerateDirectAnswer(contextChunks);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Failed to generate AI answer: {ex.Message}");
            return GenerateDirectAnswer(contextChunks);
        }
    }

    private bool ValidateCitations(string answer, List<ChatCitation> citations)
    {
        // Basic validation: check if answer contains any rule references that aren't in citations
        // This is a simple heuristic check - production would need more sophisticated validation
        
        var citedRuleNumbers = citations
            .Where(c => !string.IsNullOrEmpty(c.RuleNumberText))
            .Select(c => c.RuleNumberText!)
            .ToHashSet();

        var citedRuleKeys = citations
            .Where(c => !string.IsNullOrEmpty(c.RuleKey))
            .Select(c => c.RuleKey!)
            .ToHashSet();

        // Simple check: if answer contains "Rule X.XX" pattern, verify it's in our citations
        // This is not exhaustive but catches obvious violations
        var rulePattern = System.Text.RegularExpressions.Regex.Matches(
            answer,
            @"(?:Rule|Règle)\s+(\d+\.\d+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );

        foreach (System.Text.RegularExpressions.Match match in rulePattern)
        {
            var ruleNumber = match.Groups[1].Value;
            if (!citedRuleNumbers.Contains(ruleNumber))
            {
                // Rule mentioned in answer but not in citations
                return false;
            }
        }

        return true;
    }
}
