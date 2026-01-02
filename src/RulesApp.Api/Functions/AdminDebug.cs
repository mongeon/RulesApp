using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using RulesApp.Api.Entities;
using RulesApp.Api.Services;

namespace RulesApp.Api.Functions;

public class AdminJobsLatest
{
    private readonly ILogger<AdminJobsLatest> _logger;
    private readonly ITableStore _tableStore;

    public AdminJobsLatest(
        ILogger<AdminJobsLatest> logger,
        ITableStore tableStore)
    {
        _logger = logger;
        _tableStore = tableStore;
    }

    [Function("AdminJobsLatest")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "api/admin/jobs/latest")] HttpRequestData req,
        CancellationToken ct)
    {
        try
        {
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            string? associationId = query["associationId"];
            
            if (string.IsNullOrEmpty(associationId))
            {
                associationId = "GLOBAL";
            }
            
            // Get active season
            var seasonState = await _tableStore.GetEntityAsync<SeasonStateEntity>("SeasonState", "SEASON", "ACTIVE", ct);
            var seasonId = seasonState?.ActiveSeasonId ?? "2025";
            
            var partitionKey = $"{seasonId}:{associationId}";
            
            // Query jobs for this partition
            var jobs = await _tableStore.QueryAsync<IngestionJobEntity>("IngestionJobs", 
                $"PartitionKey eq '{partitionKey}'", ct);
            
            var results = jobs
                .OrderByDescending(j => j.StartedAt)
                .Take(10)
                .Select(j => new
                {
                    jobId = j.RowKey,
                    seasonId = j.SeasonId,
                    associationId = j.AssociationId,
                    docType = j.DocType,
                    status = j.Status,
                    startedAt = j.StartedAt,
                    completedAt = j.CompletedAt,
                    pageCount = j.PageCount,
                    chunkCount = j.ChunkCount,
                    errorMessage = j.ErrorMessage
                });
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                seasonId,
                associationId = associationId == "GLOBAL" ? null : associationId,
                jobs = results
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting latest jobs");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = ex.Message });
            return response;
        }
    }
}
