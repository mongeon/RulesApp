using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
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
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "api/admin/debug/chunks")] HttpRequest req,
        CancellationToken ct)
    {
        try
        {
            if (!req.Query.TryGetValue("jobId", out var jobIdValue))
            {
                return new BadRequestObjectResult(new { error = "jobId is required" });
            }
            var jobId = jobIdValue.ToString();
            if (string.IsNullOrEmpty(jobId))
            {
                return new BadRequestObjectResult(new { error = "jobId is required" });
            }
            
            var chunksBlobPath = BlobPaths.GetIngestionChunksPath(jobId);
            
            if (!await _blobStore.ExistsAsync(chunksBlobPath, ct))
            {
                return new NotFoundObjectResult(new { error = "Chunks not found for this jobId" });
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
            
            return new OkObjectResult(new
            {
                jobId,
                chunkCount = chunks?.Count ?? 0,
                chunks = summary
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting chunks");
            return new StatusCodeResult(500);
        }
    }
}
