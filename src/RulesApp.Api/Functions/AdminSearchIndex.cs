using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
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
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "api/admin/index/create")] HttpRequestData req,
        CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Creating/updating search index");
            
            await _searchStore.CreateOrUpdateIndexAsync(ct);
            
            _logger.LogInformation("Search index created/updated successfully");
            
            var okResponse = req.CreateResponse(HttpStatusCode.OK);
            await okResponse.WriteAsJsonAsync(new 
            { 
                message = "Index created/updated successfully",
                indexName = "rules-active"
            });
            return okResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Index creation failed");
            var errResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errResponse.WriteAsJsonAsync(new { error = ex.Message });
            return errResponse;
        }
    }
}
