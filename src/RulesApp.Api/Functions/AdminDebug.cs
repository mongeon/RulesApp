using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
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
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "api/admin/jobs/latest")] HttpRequest req,
        CancellationToken ct)
    {
        try
        {
            var associationId = req.Query["associationId"].ToString();
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
            
            return new OkObjectResult(new
            {
                seasonId,
                associationId = associationId == "GLOBAL" ? null : associationId,
                jobs = results
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting latest jobs");
            return new StatusCodeResult(500);
        }
    }
}
