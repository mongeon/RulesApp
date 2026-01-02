using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using RulesApp.Api.Entities;
using RulesApp.Api.Services;
using RulesApp.Shared;
using RulesApp.Shared.Helpers;
using System.Text.Json;

namespace RulesApp.Api.Functions;

/// <summary>
/// Debug endpoint to manually invoke the ingestion worker, bypassing queue entirely.
/// </summary>
public class AdminDebugInvokeWorker
{
    private readonly ILogger<AdminDebugInvokeWorker> _logger;
    private readonly RulesIngestWorker _worker;

    public AdminDebugInvokeWorker(ILogger<AdminDebugInvokeWorker> logger, RulesIngestWorker worker)
    {
        _logger = logger;
        _worker = worker;
    }

    [Function("AdminDebugInvokeWorker")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "api/admin/debug/invoke-worker")] HttpRequest req,
        CancellationToken ct)
    {
        if (!req.Query.TryGetValue("jobId", out var jobIdValue))
        {
            return new BadRequestObjectResult(new { error = "Missing 'jobId' query parameter." });
        }
        var jobId = jobIdValue.ToString();
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return new BadRequestObjectResult(new { error = "Missing 'jobId' query parameter." });
        }

        _logger.LogInformation("Manually invoking worker for job {JobId}", jobId);

        // Construct message for CanadaFr (hardcoded for testing)
        var message = new IngestRulesMessage(
            JobId: jobId,
            SeasonId: "2025",
            AssociationId: null,
            DocType: DocType.CanadaFr,
            ScopeLevel: ScopeLevel.Canada,
            Language: Language.FR,
            PdfBlobPath: BlobPaths.GetRulesPdfPath("2025", null, DocType.CanadaFr)
        );

        var json = JsonSerializer.Serialize(message, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        try
        {
            await _worker.Run(json, ct);
            return new OkObjectResult(new { message = "Worker invoked successfully", jobId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Worker invocation failed for job {JobId}", jobId);
            return new ObjectResult(new { error = ex.Message, jobId }) { StatusCode = 500 };
        }
    }
}
