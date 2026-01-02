using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using RulesApp.Api.Entities;
using RulesApp.Api.Services;
using RulesApp.Shared;
using RulesApp.Shared.Helpers;

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
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "api/admin/debug/invoke-worker")] HttpRequestData req,
        CancellationToken ct)
    {
        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        string? jobId = query["jobId"];
        
        if (string.IsNullOrWhiteSpace(jobId))
        {
            var response = req.CreateResponse(HttpStatusCode.BadRequest);
            await response.WriteAsJsonAsync(new { error = "Missing 'jobId' query parameter." });
            return response;
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
            var okResponse = req.CreateResponse(HttpStatusCode.OK);
            await okResponse.WriteAsJsonAsync(new { message = "Worker invoked successfully", jobId });
            return okResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Worker invocation failed for job {JobId}", jobId);
            var errResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errResponse.WriteAsJsonAsync(new { error = ex.Message, jobId });
            return errResponse;
        }
    }
}
