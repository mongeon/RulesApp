using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using RulesApp.Api.Entities;
using RulesApp.Api.Services;
using RulesApp.Shared;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RulesApp.Api.Functions;

public class AdminOverrides
{
    private readonly ILogger<AdminOverrides> _logger;
    private readonly ITableStore _tableStore;

    private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public AdminOverrides(ILogger<AdminOverrides> logger, ITableStore tableStore)
    {
        _logger = logger;
        _tableStore = tableStore;
    }

    [Function("AdminOverridesList")]
    public async Task<HttpResponseData> ListOverrides(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "api/admin/overrides")] HttpRequestData req,
        CancellationToken ct)
    {
        _logger.LogInformation("Listing override mappings");

        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var seasonId = query["seasonId"];
        var associationId = query["associationId"];
        var status = query["status"]; // "Proposed", "Confirmed", "Rejected"

        // If no seasonId provided, get active season
        if (string.IsNullOrEmpty(seasonId))
        {
            var seasonState = await _tableStore.GetEntityAsync<SeasonStateEntity>(
                "SeasonState", "SEASON", "ACTIVE", ct);
            seasonId = seasonState?.ActiveSeasonId ?? "2025";
        }

        var partitionKey = $"{seasonId}:{associationId ?? "global"}";

        List<OverrideMappingEntity> entities;
        
        if (!string.IsNullOrEmpty(status))
        {
            var filter = $"PartitionKey eq '{partitionKey}' and Status eq '{status}'";
            entities = await _tableStore.QueryAsync<OverrideMappingEntity>(
                "OverrideMappings",
                filter,
                ct);
        }
        else
        {
            var filter = $"PartitionKey eq '{partitionKey}'";
            entities = await _tableStore.QueryAsync<OverrideMappingEntity>(
                "OverrideMappings",
                filter,
                ct);
        }

        var dtos = entities.Select(e => new OverrideMappingDto(
            MappingId: e.RowKey,
            SeasonId: e.SeasonId!,
            AssociationId: e.AssociationId,
            SourceRuleKey: e.SourceRuleKey!,
            SourceChunkId: e.SourceChunkId!,
            SourceScope: e.SourceScope!,
            TargetRuleKey: e.TargetRuleKey!,
            TargetChunkId: e.TargetChunkId!,
            TargetScope: e.TargetScope!,
            Status: Enum.Parse<OverrideStatus>(e.Status!),
            Confidence: e.Confidence,
            DetectionReason: e.DetectionReason,
            CreatedAt: e.CreatedAt ?? DateTimeOffset.MinValue,
            ReviewedAt: e.ReviewedAt,
            ReviewedBy: e.ReviewedBy,
            RejectionReason: e.RejectionReason
        )).ToList();

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            seasonId,
            associationId,
            status,
            count = dtos.Count,
            overrides = dtos
        });

        return response;
    }

    [Function("AdminOverridesGet")]
    public async Task<HttpResponseData> GetOverride(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "api/admin/overrides/{mappingId}")] HttpRequestData req,
        string mappingId,
        CancellationToken ct)
    {
        _logger.LogInformation("Getting override mapping {MappingId}", mappingId);

        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var seasonId = query["seasonId"];
        var associationId = query["associationId"];

        // If no seasonId provided, get active season
        if (string.IsNullOrEmpty(seasonId))
        {
            var seasonState = await _tableStore.GetEntityAsync<SeasonStateEntity>(
                "SeasonState", "SEASON", "ACTIVE", ct);
            seasonId = seasonState?.ActiveSeasonId ?? "2025";
        }

        var partitionKey = $"{seasonId}:{associationId ?? "global"}";

        var entity = await _tableStore.GetEntityAsync<OverrideMappingEntity>(
            "OverrideMappings", partitionKey, mappingId, ct);

        if (entity == null)
        {
            var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
            await notFoundResponse.WriteAsJsonAsync(new { error = "Override mapping not found" });
            return notFoundResponse;
        }

        var dto = new OverrideMappingDto(
            MappingId: entity.RowKey,
            SeasonId: entity.SeasonId!,
            AssociationId: entity.AssociationId,
            SourceRuleKey: entity.SourceRuleKey!,
            SourceChunkId: entity.SourceChunkId!,
            SourceScope: entity.SourceScope!,
            TargetRuleKey: entity.TargetRuleKey!,
            TargetChunkId: entity.TargetChunkId!,
            TargetScope: entity.TargetScope!,
            Status: Enum.Parse<OverrideStatus>(entity.Status!),
            Confidence: entity.Confidence,
            DetectionReason: entity.DetectionReason,
            CreatedAt: entity.CreatedAt ?? DateTimeOffset.MinValue,
            ReviewedAt: entity.ReviewedAt,
            ReviewedBy: entity.ReviewedBy,
            RejectionReason: entity.RejectionReason
        );

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(dto);
        return response;
    }

    [Function("AdminOverridesReview")]
    public async Task<HttpResponseData> ReviewOverride(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "api/admin/overrides/{mappingId}")] HttpRequestData req,
        string mappingId,
        CancellationToken ct)
    {
        _logger.LogInformation("Reviewing override mapping {MappingId}", mappingId);

        var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var seasonId = query["seasonId"];
        var associationId = query["associationId"];

        // If no seasonId provided, get active season
        if (string.IsNullOrEmpty(seasonId))
        {
            var seasonState = await _tableStore.GetEntityAsync<SeasonStateEntity>(
                "SeasonState", "SEASON", "ACTIVE", ct);
            seasonId = seasonState?.ActiveSeasonId ?? "2025";
        }

        var partitionKey = $"{seasonId}:{associationId ?? "global"}";

        var entity = await _tableStore.GetEntityAsync<OverrideMappingEntity>(
            "OverrideMappings", partitionKey, mappingId, ct);

        if (entity == null)
        {
            var notFoundResponse = req.CreateResponse(HttpStatusCode.NotFound);
            await notFoundResponse.WriteAsJsonAsync(new { error = "Override mapping not found" });
            return notFoundResponse;
        }

        // Parse request body
        OverrideReviewRequest? reviewRequest;
        try
        {
            reviewRequest = await req.ReadFromJsonAsync<OverrideReviewRequest>(ct);
            if (reviewRequest == null)
                throw new InvalidOperationException("Request body is null");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse review request");
            var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
            await badResponse.WriteAsJsonAsync(new { error = "Invalid request body" });
            return badResponse;
        }

        // Validate action
        if (reviewRequest.Action.ToLowerInvariant() != "confirm" && 
            reviewRequest.Action.ToLowerInvariant() != "reject")
        {
            var badResponse = req.CreateResponse(HttpStatusCode.BadRequest);
            await badResponse.WriteAsJsonAsync(new { error = "Action must be 'confirm' or 'reject'" });
            return badResponse;
        }

        // Update entity
        entity.Status = reviewRequest.Action.ToLowerInvariant() == "confirm" 
            ? OverrideStatus.Confirmed.ToString() 
            : OverrideStatus.Rejected.ToString();
        entity.ReviewedAt = DateTimeOffset.UtcNow;
        entity.ReviewedBy = reviewRequest.ReviewedBy;
        
        if (reviewRequest.Action.ToLowerInvariant() == "reject" && !string.IsNullOrEmpty(reviewRequest.Reason))
        {
            entity.RejectionReason = reviewRequest.Reason;
        }

        await _tableStore.UpsertEntityAsync("OverrideMappings", entity, ct);

        _logger.LogInformation("Override mapping {MappingId} {Action}ed by {User}", 
            mappingId, reviewRequest.Action, reviewRequest.ReviewedBy);

        var dto = new OverrideMappingDto(
            MappingId: entity.RowKey,
            SeasonId: entity.SeasonId!,
            AssociationId: entity.AssociationId,
            SourceRuleKey: entity.SourceRuleKey!,
            SourceChunkId: entity.SourceChunkId!,
            SourceScope: entity.SourceScope!,
            TargetRuleKey: entity.TargetRuleKey!,
            TargetChunkId: entity.TargetChunkId!,
            TargetScope: entity.TargetScope!,
            Status: Enum.Parse<OverrideStatus>(entity.Status!),
            Confidence: entity.Confidence,
            DetectionReason: entity.DetectionReason,
            CreatedAt: entity.CreatedAt ?? DateTimeOffset.MinValue,
            ReviewedAt: entity.ReviewedAt,
            ReviewedBy: entity.ReviewedBy,
            RejectionReason: entity.RejectionReason
        );

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(dto);
        return response;
    }
}
