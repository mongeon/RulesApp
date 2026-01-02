using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using RulesApp.Api.Entities;
using RulesApp.Api.Services;
using RulesApp.Shared;
using RulesApp.Shared.Helpers;
using System.Net;

namespace RulesApp.Api.Functions;

public class AdminBuild
{
    private readonly ILogger<AdminBuild> _logger;
    private readonly IBlobStore _blobStore;
    private readonly IQueueStore _queueStore;
    private readonly ITableStore _tableStore;

    public AdminBuild(
        ILogger<AdminBuild> logger, 
        IBlobStore blobStore,
        IQueueStore queueStore,
        ITableStore tableStore)
    {
        _logger = logger;
        _blobStore = blobStore;
        _queueStore = queueStore;
        _tableStore = tableStore;
    }

    [Function("AdminBuild")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "api/admin/build")] HttpRequestData req,
        CancellationToken ct)
    {
        try
        {
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            string? associationId = query["associationId"];
            
            if (string.IsNullOrEmpty(associationId))
            {
                associationId = null;
            }
            
            // Get active season
            var seasonState = await _tableStore.GetEntityAsync<SeasonStateEntity>("SeasonState", "SEASON", "ACTIVE", ct);
            var seasonId = seasonState?.ActiveSeasonId ?? "2025";
            
            _logger.LogInformation("Starting build for season {SeasonId}, association {AssociationId}", 
                seasonId, associationId ?? "GLOBAL");
            
            var enqueuedJobs = new List<object>();
            
            // Enqueue global docs (always)
            var globalDocs = new[] { DocType.CanadaFr, DocType.CanadaEn, DocType.QuebecFr, DocType.QuebecEn };
            foreach (var docType in globalDocs)
            {
                var blobPath = BlobPaths.GetRulesPdfPath(seasonId, null, docType);
                
                // Check if blob exists
                if (!await _blobStore.ExistsAsync(blobPath, ct))
                {
                    _logger.LogWarning("PDF not found: {BlobPath}", blobPath);
                    continue;
                }
                
                var jobId = Guid.NewGuid().ToString();
                var message = new IngestRulesMessage(
                    JobId: jobId,
                    SeasonId: seasonId,
                    AssociationId: null,
                    DocType: docType,
                    ScopeLevel: docType.GetScopeLevel(),
                    Language: docType.GetLanguage(),
                    PdfBlobPath: blobPath
                );
                
                var json = System.Text.Json.JsonSerializer.Serialize(message);
                _logger.LogInformation("Enqueueing message: {Json}", json);
                
                await _queueStore.EnqueueAsync("rules-ingest", message, ct);
                
                _logger.LogInformation("✓ Enqueued job {JobId}", jobId);
                
                // Create job entity
                var jobEntity = new IngestionJobEntity
                {
                    PartitionKey = $"{seasonId}:GLOBAL",
                    RowKey = jobId,
                    SeasonId = seasonId,
                    AssociationId = null,
                    DocType = docType.ToString(),
                    Status = IngestionStatus.Queued.ToString(),
                    StartedAt = DateTimeOffset.UtcNow,
                    PageCount = 0,
                    ChunkCount = 0
                };
                
                await _tableStore.UpsertEntityAsync("IngestionJobs", jobEntity, ct);
                
                enqueuedJobs.Add(new { jobId, docType = docType.ToString(), scope = "global" });
            }
            
            // Enqueue regional doc if associationId provided
            if (!string.IsNullOrEmpty(associationId))
            {
                var docType = DocType.RegionalFr;
                var blobPath = BlobPaths.GetRulesPdfPath(seasonId, associationId, docType);
                
                if (await _blobStore.ExistsAsync(blobPath, ct))
                {
                    var jobId = Guid.NewGuid().ToString();
                    var message = new IngestRulesMessage(
                        JobId: jobId,
                        SeasonId: seasonId,
                        AssociationId: associationId,
                        DocType: docType,
                        ScopeLevel: docType.GetScopeLevel(),
                        Language: docType.GetLanguage(),
                        PdfBlobPath: blobPath
                    );
                    
                    await _queueStore.EnqueueAsync("rules-ingest", message, ct);
                    
                    var jobEntity = new IngestionJobEntity
                    {
                        PartitionKey = $"{seasonId}:{associationId}",
                        RowKey = jobId,
                        SeasonId = seasonId,
                        AssociationId = associationId,
                        DocType = docType.ToString(),
                        Status = IngestionStatus.Queued.ToString(),
                        StartedAt = DateTimeOffset.UtcNow,
                        PageCount = 0,
                        ChunkCount = 0
                    };
                    
                    await _tableStore.UpsertEntityAsync("IngestionJobs", jobEntity, ct);
                    
                    enqueuedJobs.Add(new { jobId, docType = docType.ToString(), scope = associationId });
                }
                else
                {
                    _logger.LogWarning("Regional PDF not found: {BlobPath}", blobPath);
                }
            }
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new
            {
                message = "Build started",
                seasonId,
                associationId,
                enqueuedJobs = enqueuedJobs.Count,
                jobs = enqueuedJobs
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting build");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = ex.Message });
            return response;
        }
    }
}
