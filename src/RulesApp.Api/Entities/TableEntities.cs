using Azure;
using Azure.Data.Tables;

namespace RulesApp.Api.Entities;

public class SeasonStateEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "SEASON";
    public string RowKey { get; set; } = "ACTIVE";
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    
    public string? ActiveSeasonId { get; set; }
    public string? PreviousSeasonId { get; set; }
}

public class IngestionJobEntity : ITableEntity
{
    public string PartitionKey { get; set; } = null!; // {seasonId}:{associationId}
    public string RowKey { get; set; } = null!; // {jobId}
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    
    public string? SeasonId { get; set; }
    public string? AssociationId { get; set; }
    public string? DocType { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int PageCount { get; set; }
    public int ChunkCount { get; set; }
    public string? ErrorMessage { get; set; }
}
