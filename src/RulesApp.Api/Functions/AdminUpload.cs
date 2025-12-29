using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using RulesApp.Api.Services;
using RulesApp.Shared;
using RulesApp.Shared.Helpers;

namespace RulesApp.Api.Functions;

public class AdminUpload
{
    private readonly ILogger<AdminUpload> _logger;
    private readonly IBlobStore _blobStore;

    public AdminUpload(ILogger<AdminUpload> logger, IBlobStore blobStore)
    {
        _logger = logger;
        _blobStore = blobStore;
    }

    [Function("AdminUpload")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "api/admin/upload")] HttpRequest req,
        CancellationToken ct)
    {
        try
        {
            // Parse form data
            var form = await req.ReadFormAsync(ct);
            
            var seasonId = form["seasonId"].ToString();
            if (string.IsNullOrEmpty(seasonId))
            {
                seasonId = "2025"; // Default to current season if not specified
            }
            
            var associationId = form["associationId"].ToString();
            if (string.IsNullOrEmpty(associationId))
            {
                associationId = null;
            }
            
            var docTypeStr = form["docType"].ToString();
            if (!Enum.TryParse<DocType>(docTypeStr, out var docType))
            {
                return new BadRequestObjectResult(new { error = "Invalid docType. Must be: CanadaFr, CanadaEn, QuebecFr, or RegionalFr" });
            }
            
            // Validate regional requires association
            if (docType.RequiresAssociation() && string.IsNullOrEmpty(associationId))
            {
                return new BadRequestObjectResult(new { error = "associationId is required for RegionalFr" });
            }
            
            var file = form.Files.GetFile("file");
            if (file == null || file.Length == 0)
            {
                return new BadRequestObjectResult(new { error = "No file uploaded" });
            }
            
            if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                return new BadRequestObjectResult(new { error = "Only PDF files are allowed" });
            }
            
            // Upload to blob
            var blobPath = BlobPaths.GetRulesPdfPath(seasonId, associationId, docType);
            
            using var stream = file.OpenReadStream();
            await _blobStore.UploadAsync(blobPath, stream, "application/pdf", ct);
            
            _logger.LogInformation("Uploaded PDF to {BlobPath}", blobPath);
            
            return new OkObjectResult(new 
            { 
                message = "Upload successful",
                blobPath,
                seasonId,
                associationId,
                docType = docType.ToString(),
                size = file.Length
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading PDF");
            return new StatusCodeResult(500);
        }
    }
}
