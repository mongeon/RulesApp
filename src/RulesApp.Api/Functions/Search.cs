using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using RulesApp.Api.Services;
using RulesApp.Shared;
using System.Net;
using System.Text.Json;

namespace RulesApp.Api.Functions;

public class Search
{
    private readonly ISearchStore _searchStore;
    private readonly PrecedenceResolver _precedenceResolver;
    private readonly ILogger<Search> _logger;
    
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public Search(ISearchStore searchStore, PrecedenceResolver precedenceResolver, ILogger<Search> logger)
    {
        _searchStore = searchStore;
        _precedenceResolver = precedenceResolver;
        _logger = logger;
    }

    [Function("Search")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/search")] HttpRequestData req,
        CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Search endpoint called");
            
            if (req.Body == null)
            {
                _logger.LogWarning("Request body is null");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteAsJsonAsync(new { error = "Request body is required" });
                return badResponse;
            }
            
            // Read the body as string first for debugging
            string bodyContent;
            using (var reader = new StreamReader(req.Body, leaveOpen: true))
            {
                bodyContent = await reader.ReadToEndAsync();
            }
            
            _logger.LogInformation("Request body: {Body}", bodyContent);
            
            if (string.IsNullOrWhiteSpace(bodyContent))
            {
                _logger.LogWarning("Request body is empty");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteAsJsonAsync(new { error = "Request body cannot be empty" });
                return badResponse;
            }
            
            var request = JsonSerializer.Deserialize<SearchRequest>(bodyContent, _jsonOptions);
            
            _logger.LogInformation("Request deserialized: {IsNull}, Query={Query}", 
                request == null, request?.Query);
            
            if (request == null || string.IsNullOrWhiteSpace(request.Query))
            {
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteAsJsonAsync(new { error = "Query is required" });
                return badResponse;
            }

            // Validate Regional scope requires associationId
            if (request.Scopes?.Contains("Regional") == true && string.IsNullOrEmpty(request.AssociationId))
            {
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteAsJsonAsync(new { error = "Regional scope requires associationId" });
                return badResponse;
            }

            _logger.LogInformation("Searching: query={Query}, scopes={Scopes}, association={Association}", 
                request.Query, request.Scopes != null ? string.Join(",", request.Scopes) : "all", request.AssociationId);

            var searchResponse = await _searchStore.SearchAsync(request, ct);
            
            _logger.LogInformation("Search completed: {Count} raw results", searchResponse.TotalResults);

            // Apply precedence resolution to group by ruleKey
            var seasonId = request.SeasonId ?? "2025"; // TODO: get from active season
            var precedenceGroups = await _precedenceResolver.ResolveAsync(
                searchResponse.Results, 
                seasonId, 
                request.AssociationId);

            _logger.LogInformation("Precedence resolved: {GroupCount} rule groups", precedenceGroups.Count);

            // Build response with precedence groups
            var ruleGroups = precedenceGroups.Select(g => new RuleGroupResult(
                g.RuleKey,
                g.PrimaryChunk,
                g.AlternateChunks.ToList()
            )).ToList();

            var ungroupedResults = searchResponse.Results
                .Where(r => string.IsNullOrEmpty(r.RuleKey))
                .ToList();

            var response = new SearchResponseWithPrecedence(
                request.Query,
                searchResponse.TotalResults,
                ruleGroups,
                ungroupedResults
            );
            
            _logger.LogInformation("Response prepared with precedence groups");
            
            var okResponse = req.CreateResponse(HttpStatusCode.OK);
            await okResponse.WriteAsJsonAsync(response);
            return okResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = "Search failed" });
            return errorResponse;
        }
    }
}
