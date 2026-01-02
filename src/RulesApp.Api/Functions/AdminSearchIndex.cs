using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using RulesApp.Api.Services;

namespace RulesApp.Api.Functions;

public class AdminSearchIndex
{
    private readonly ISearchStore _searchStore;
    private readonly ILogger<AdminSearchIndex> _logger;

    public AdminSearchIndex(ISearchStore searchStore, ILogger<AdminSearchIndex> logger)
    {
        _searchStore = searchStore;
        _logger = logger;
    }

    [Function("AdminSearchIndex")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "api/admin/index/create")] HttpRequest req,
        CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Creating/updating search index");
            
            await _searchStore.CreateOrUpdateIndexAsync(ct);
            
            _logger.LogInformation("Search index created/updated successfully");
            
            return new OkObjectResult(new 
            { 
                message = "Index created/updated successfully",
                indexName = "rules-active"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Index creation failed");
            return new ObjectResult(new { error = ex.Message }) { StatusCode = 500 };
        }
    }
}
