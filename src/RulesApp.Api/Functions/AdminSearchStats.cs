using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
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
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/admin/index/stats")] HttpRequestData req,
        CancellationToken ct)
    {
        try
        {
            var documentCount = await _searchStore.GetDocumentCountAsync(ct);
            
            var okResponse = req.CreateResponse(HttpStatusCode.OK);
            await okResponse.WriteAsJsonAsync(new 
            { 
                indexName = "rules-active",
                documentCount = documentCount
            });
            return okResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get search statistics");
            var errResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errResponse.WriteAsJsonAsync(new { error = ex.Message });
            return errResponse;
        }
    }
}
