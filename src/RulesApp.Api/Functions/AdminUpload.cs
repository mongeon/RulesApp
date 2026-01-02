using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using RulesApp.Api.Services;
using RulesApp.Shared;
using RulesApp.Shared.Helpers;
using System.Net;
using System.Text.Json;

namespace RulesApp.Api.Functions;

public class AdminUpload
{
    private readonly ILogger<AdminUpload> _logger;
    private readonly IBlobStore _blobStore;
    private readonly IQueueStore _queueStore;

    public AdminUpload(ILogger<AdminUpload> logger, IBlobStore blobStore, IQueueStore queueStore)
    {
        _logger = logger;
        _blobStore = blobStore;
        _queueStore = queueStore;
    }

    [Function("AdminUpload")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "api/admin/upload")] HttpRequestData req,
        CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Upload request received");
            
            // Parse JSON body
            UploadRequest? uploadRequest;
            try
            {
                uploadRequest = await req.ReadFromJsonAsync<UploadRequest>(ct);
                if (uploadRequest == null)
                {
                    var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                    await badResponse.WriteAsJsonAsync(new { error = "Invalid request body" });
                    return badResponse;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse request");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteAsJsonAsync(new { error = $"Failed to parse request: {ex.Message}" });
                return badResponse;
            }

            var seasonId = string.IsNullOrEmpty(uploadRequest.SeasonId) ? "2026" : uploadRequest.SeasonId;
            var associationId = uploadRequest.AssociationId;
            
            if (!Enum.TryParse<ScopeLevel>(uploadRequest.ScopeLevel, true, out var scopeLevel))
            {
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteAsJsonAsync(new { error = "Invalid scopeLevel. Must be: Canada, Quebec, or Regional" });
                return badResponse;
            }

            if (string.IsNullOrEmpty(uploadRequest.DocType))
            {
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteAsJsonAsync(new { error = "docType is required. Must be: Fr or En" });
                return badResponse;
            }

            // Combine scopeLevel + docType to get DocType enum
            var docTypeEnumStr = $"{scopeLevel}{uploadRequest.DocType}";
            if (!Enum.TryParse<DocType>(docTypeEnumStr, true, out var docType))
            {
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteAsJsonAsync(new { error = $"Invalid combination: {scopeLevel} + {uploadRequest.DocType}. Valid docTypes are Fr or En." });
                return badResponse;
            }
            
            // Validate regional requires association
            if (scopeLevel == ScopeLevel.Regional && string.IsNullOrEmpty(associationId))
            {
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteAsJsonAsync(new { error = "associationId is required for Regional scope" });
                return badResponse;
            }

            if (string.IsNullOrEmpty(uploadRequest.FileName) || !uploadRequest.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteAsJsonAsync(new { error = "Only PDF files are allowed" });
                return badResponse;
            }

            if (string.IsNullOrEmpty(uploadRequest.FileContentBase64))
            {
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteAsJsonAsync(new { error = "File content is required" });
                return badResponse;
            }

            // Decode base64 file content
            byte[] fileBytes;
            try
            {
                fileBytes = Convert.FromBase64String(uploadRequest.FileContentBase64);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decode base64 file content");
                var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResponse.WriteAsJsonAsync(new { error = "Invalid base64 file content" });
                return badResponse;
            }
            
            // Upload to blob
            var blobPath = BlobPaths.GetRulesPdfPath(seasonId, associationId, docType);
            
            using var stream = new MemoryStream(fileBytes);
            await _blobStore.UploadAsync(blobPath, stream, "application/pdf", ct);
            
            _logger.LogInformation("Uploaded PDF to {BlobPath}, size: {Size}", blobPath, fileBytes.Length);

            // Queue ingestion job
            var jobId = $"job_{Guid.NewGuid():N}";
            var language = uploadRequest.DocType.Equals("Fr", StringComparison.OrdinalIgnoreCase) ? Language.FR : Language.EN;
            
            var message = new IngestRulesMessage(
                jobId,
                seasonId,
                associationId,
                docType,
                scopeLevel,
                language,
                blobPath
            );

            await _queueStore.EnqueueAsync("rules-ingest", message, ct);
            
            _logger.LogInformation("Queued ingestion job {JobId} for {BlobPath}", jobId, blobPath);
            
            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteAsJsonAsync(new 
            { 
                jobId,
                status = "queued",
                blobPath,
                seasonId,
                associationId,
                scopeLevel = scopeLevel.ToString(),
                docType = docType.ToString(),
                size = fileBytes.Length
            });
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading PDF");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new { error = ex.Message });
            return errorResponse;
        }
    }

    private record UploadRequest(
        string? SeasonId,
        string? AssociationId,
        string ScopeLevel,
        string DocType,
        string FileName,
        string FileContentBase64
    );
}
