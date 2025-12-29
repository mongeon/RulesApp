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
