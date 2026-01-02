using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using RulesApp.Api.Services;
using RulesApp.Shared;
using RulesApp.Shared.Helpers;

namespace RulesApp.Api.Functions;

public class AdminDebugChunks
{
    private readonly ILogger<AdminDebugChunks> _logger;
    private readonly IBlobStore _blobStore;

    private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public AdminDebugChunks(
        ILogger<AdminDebugChunks> logger,
        IBlobStore blobStore)
    {
        _logger = logger;
        _blobStore = blobStore;
    }

    [Function("AdminDebugChunks")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "api/admin/debug/chunks")] HttpRequestData req,
        CancellationToken ct)
    {
        try
        {
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            string? jobId = query["jobId"];
            
            if (string.IsNullOrEmpty(jobId))
            {
                var response = req.CreateResponse(HttpStatusCode.BadRequest);
                await response.WriteAsJsonAsync(new { error = "jobId is required" });
                return response;
            }
            
            var chunksBlobPath = BlobPaths.GetIngestionChunksPath(jobId);
            
            if (!await _blobStore.ExistsAsync(chunksBlobPath, ct))
            {
                var response = req.CreateResponse(HttpStatusCode.NotFound);
                await response.WriteAsJsonAsync(new { error = "Chunks not found for this jobId" });
                return response;
            }
            
            using var stream = await _blobStore.GetBlobAsync(chunksBlobPath, ct);
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync(ct);
            
            var chunks = JsonSerializer.Deserialize<List<RuleChunkDto>>(json, _jsonOptions);
            
            var summary = chunks?.Select(c => new
            {
                chunkId = c.ChunkId,
                ruleKey = c.RuleKey,
                ruleNumberText = c.RuleNumberText,
                title = c.Title,
                pageStart = c.PageStart,
                pageEnd = c.PageEnd,
                textPreview = c.Text?.Length > 100 ? c.Text.Substring(0, 100) + "..." : c.Text ?? "",
                textLength = c.Text?.Length ?? 0
            });
            
            var okResponse = req.CreateResponse(HttpStatusCode.OK);
            await okResponse.WriteAsJsonAsync(new
            {
                jobId,
                chunkCount = chunks?.Count ?? 0,
                chunks = summary
            });
            return okResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting chunks");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = ex.Message });
            return response;
        }
    }
}
