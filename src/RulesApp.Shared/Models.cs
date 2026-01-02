namespace RulesApp.Shared;

// Core enums
public enum DocType
{
    CanadaFr,
    CanadaEn,
    QuebecFr,
    RegionalFr
}

public enum ScopeLevel
{
    Canada,
    Quebec,
    Regional
}

public enum Language
{
    FR,
    EN
}

public enum IngestionStatus
{
    Queued,
    InProgress,
    Completed,
    Failed
}

// Queue message for ingestion
public record IngestRulesMessage(
    string JobId,
    string SeasonId,
    string? AssociationId,
    DocType DocType,
    ScopeLevel ScopeLevel,
    Language Language,
    string PdfBlobPath
);

// Chunk DTO
public record RuleChunkDto(
    string ChunkId,
    ScopeLevel ScopeLevel,
    string? AssociationId,
    string Rulebook,
    Language Language,
    string? RuleNumberText,
    string? RuleKey,
    string? Title,
    int PageStart,
    int PageEnd,
    string PdfPath,
    string Text
);

// Page extraction result
public record PageDto(
    int PageNumber,
    string Text,
    int CharCount
);

// Ingestion job result
public record IngestionResultDto(
    string JobId,
    string SeasonId,
    string? AssociationId,
    DocType DocType,
    IngestionStatus Status,
    int PageCount,
    int ChunkCount,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? ErrorMessage
);

// Search DTOs
public record SearchRequest(
    string Query,
    string? SeasonId = null,
    string? AssociationId = null,
    List<string>? Scopes = null, // "Canada", "Quebec", "Regional"
    int Top = 10
);

public record SearchResponse(
    string Query,
    int TotalResults,
    List<SearchHit> Results
);

public record SearchHit(
    string ChunkId,
    string? RuleKey,
    string? RuleNumberText,
    string? Title,
    string Scope,
    string DocType,
    string SeasonId,
    string? AssociationId,
    int PageStart,
    int PageEnd,
    string TextPreview,
    double Score
);
