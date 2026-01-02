namespace RulesApp.Shared;

// Core enums
public enum DocType
{
    CanadaFr,
    CanadaEn,
    QuebecFr,
    QuebecEn,
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

public enum OverrideStatus
{
    Proposed,
    Confirmed,
    Rejected
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

public record SearchResponseWithPrecedence(
    string Query,
    int TotalResults,
    List<RuleGroupResult> RuleGroups,
    List<SearchHit> UngroupedResults
);

public record RuleGroupResult(
    string RuleKey,
    SearchHit PrimaryChunk,
    List<SearchHit> AlternateChunks
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
    string Text,
    string TextPreview,
    double Score
);

// Override management DTOs
public record OverrideMappingDto(
    string MappingId,
    string SeasonId,
    string? AssociationId,
    string SourceRuleKey,
    string SourceChunkId,
    string SourceScope,
    string TargetRuleKey,
    string TargetChunkId,
    string TargetScope,
    OverrideStatus Status,
    double Confidence,
    string? DetectionReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReviewedAt,
    string? ReviewedBy,
    string? RejectionReason
);

public record OverrideReviewRequest(
    string Action, // "confirm" or "reject"
    string ReviewedBy,
    string? Reason = null
);

public record OverrideProposal(
    string SourceRuleKey,
    string SourceChunkId,
    string SourceScope,
    string TargetRuleKey,
    string TargetChunkId,
    string TargetScope,
    double Confidence,
    string DetectionReason
);

// Precedence-resolved result
public record PrecedenceGroup(
    string RuleKey,
    SearchHit PrimaryChunk,
    List<SearchHit> AlternateChunks
);

// Chat DTOs
public record ChatRequest(
    string Query,
    string? SeasonId = null,
    string? AssociationId = null,
    int MaxContext = 5,
    bool UseAI = false
);

public record ChatResponse(
    string Status, // "ok" or "not_found"
    string Query,
    string Answer,
    List<ChatCitation> Citations,
    int ContextUsed,
    int TotalRetrieved
);

public record ChatCitation(
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
    string TextPreview
);

