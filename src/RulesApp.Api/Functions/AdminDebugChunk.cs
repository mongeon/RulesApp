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

public class AdminDebugChunk
{
    private readonly ILogger<AdminDebugChunk> _logger;
    private readonly IBlobStore _blobStore;

    private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public AdminDebugChunk(
        ILogger<AdminDebugChunk> logger,
        IBlobStore blobStore)
    {
        _logger = logger;
        _blobStore = blobStore;
    }

    [Function("AdminDebugChunk")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "api/admin/debug/chunk")] HttpRequest req,
        CancellationToken ct)
    {
        try
        {
            if (!req.Query.TryGetValue("jobId", out var jobIdValue) ||
                !req.Query.TryGetValue("chunkId", out var chunkIdValue))
            {
                return new BadRequestObjectResult(new { error = "jobId and chunkId are required" });
            }
            
            var jobId = jobIdValue.ToString();
            var chunkId = chunkIdValue.ToString();
            
            if (string.IsNullOrEmpty(jobId) || string.IsNullOrEmpty(chunkId))
            {
                return new BadRequestObjectResult(new { error = "jobId and chunkId are required" });
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
            var chunk = chunks?.FirstOrDefault(c => c.ChunkId == chunkId);
            
            if (chunk == null)
            {
                return new NotFoundObjectResult(new { error = "Chunk not found" });
            }
            
            return new OkObjectResult(chunk);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting chunk");
            return new StatusCodeResult(500);
        }
    }
}
