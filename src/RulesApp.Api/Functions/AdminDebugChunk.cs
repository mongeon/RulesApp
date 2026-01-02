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
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "api/admin/debug/chunk")] HttpRequestData req,
        CancellationToken ct)
    {
        try
        {
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            string? jobId = query["jobId"];
            string? chunkId = query["chunkId"];
            
            if (string.IsNullOrEmpty(jobId) || string.IsNullOrEmpty(chunkId))
            {
                var response = req.CreateResponse(HttpStatusCode.BadRequest);
                await response.WriteAsJsonAsync(new { error = "jobId and chunkId are required" });
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
            var chunk = chunks?.FirstOrDefault(c => c.ChunkId == chunkId);
            
            if (chunk == null)
            {
                var response = req.CreateResponse(HttpStatusCode.NotFound);
                await response.WriteAsJsonAsync(new { error = "Chunk not found" });
                return response;
            }
            
            var okResponse = req.CreateResponse(HttpStatusCode.OK);
            await okResponse.WriteAsJsonAsync(chunk);
            return okResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting chunk");
            var response = req.CreateResponse(HttpStatusCode.InternalServerError);
            await response.WriteAsJsonAsync(new { error = ex.Message });
            return response;
        }
    }
}
