using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using RulesApp.Api.Services;

namespace RulesApp.Api.Functions;

public class AdminSearchStats
{
    private readonly ISearchStore _searchStore;
    private readonly ILogger<AdminSearchStats> _logger;

    public AdminSearchStats(ISearchStore searchStore, ILogger<AdminSearchStats> logger)
    {
        _searchStore = searchStore;
        _logger = logger;
    }

    [Function("AdminSearchStats")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/admin/index/stats")] HttpRequest req,
        CancellationToken ct)
    {
        try
        {
            var documentCount = await _searchStore.GetDocumentCountAsync(ct);
            
            return new OkObjectResult(new 
            { 
                indexName = "rules-active",
                documentCount = documentCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get search statistics");
            return new ObjectResult(new { error = ex.Message }) { StatusCode = 500 };
        }
    }
}
