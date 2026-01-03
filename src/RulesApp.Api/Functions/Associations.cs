using System.Net;
using System.Web;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using RulesApp.Api.Entities;
using RulesApp.Api.Services;
using RulesApp.Shared;

namespace RulesApp.Api.Functions;

public class Associations
{
    private readonly ITableStore _tableStore;
    private readonly ILogger<Associations> _logger;

    public Associations(ITableStore tableStore, ILogger<Associations> logger)
    {
        _tableStore = tableStore;
        _logger = logger;
    }

    [Function("Associations")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/associations")] HttpRequestData req,
        CancellationToken ct)
    {
        try
        {
            var query = HttpUtility.ParseQueryString(req.Url.Query);
            var seasonId = query["seasonId"];

            // Default to active season if none provided
            if (string.IsNullOrWhiteSpace(seasonId))
            {
                var seasonState = await _tableStore.GetEntityAsync<SeasonStateEntity>("SeasonState", "SEASON", "ACTIVE", ct);
                seasonId = seasonState?.ActiveSeasonId ?? "2025";
            }

            var jobs = await _tableStore.QueryAsync<IngestionJobEntity>("IngestionJobs", null, ct);

            var associations = jobs
                .Where(j => !string.IsNullOrWhiteSpace(j.AssociationId) && string.Equals(j.SeasonId, seasonId, StringComparison.OrdinalIgnoreCase))
                .Select(j => j.AssociationId!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(a => a)
                .ToList();

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new AssociationListResponse(seasonId!, associations), cancellationToken: ct);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list associations");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = ex.Message }, cancellationToken: ct);
            return errorResponse;
        }
    }
}