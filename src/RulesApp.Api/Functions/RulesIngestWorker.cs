using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using RulesApp.Api.Entities;
using RulesApp.Api.Services;
using RulesApp.Shared;
using RulesApp.Shared.Helpers;

namespace RulesApp.Api.Functions;

public class RulesIngestWorker
{
    private readonly ILogger<RulesIngestWorker> _logger;
    private readonly IBlobStore _blobStore;
    private readonly ITableStore _tableStore;
    private readonly IPdfExtractor _pdfExtractor;
    private readonly IChunker _chunker;
    private readonly ISearchStore _searchStore;

    private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public RulesIngestWorker(
        ILogger<RulesIngestWorker> logger,
        IBlobStore blobStore,
        ITableStore tableStore,
        IPdfExtractor pdfExtractor,
        IChunker chunker,
        ISearchStore searchStore)
    {
        _logger = logger;
        _blobStore = blobStore;
        _tableStore = tableStore;
        _pdfExtractor = pdfExtractor;
        _chunker = chunker;
        _searchStore = searchStore;
    }

    [Function("RulesIngestWorker")]
    public async Task Run(
        [QueueTrigger("rules-ingest", Connection = "AzureWebJobsStorage")] string messageJson,
        CancellationToken ct)
    {
        _logger.LogInformation("[WORKER ENTRY] Queue trigger fired! Message length: {Length}", messageJson?.Length ?? 0);
        
        if (string.IsNullOrWhiteSpace(messageJson))
        {
            _logger.LogWarning("Received empty queue message; skipping.");
            return;
        }

        IngestRulesMessage message;

        try
        {
            message = JsonSerializer.Deserialize<IngestRulesMessage>(messageJson, _jsonOptions)
                ?? throw new InvalidOperationException("Queue message deserialized to null");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize queue message: {Message}", messageJson);
            return;
        }

        var partitionKey = $"{message.SeasonId}:{message.AssociationId ?? "GLOBAL"}";
        var jobId = message.JobId;

        _logger.LogInformation("Processing job {JobId} for docType {DocType} (partition {PartitionKey})", jobId, message.DocType, partitionKey);

        var jobEntity = await _tableStore.GetEntityAsync<IngestionJobEntity>("IngestionJobs", partitionKey, jobId, ct)
            ?? new IngestionJobEntity
            {
                PartitionKey = partitionKey,
                RowKey = jobId,
                SeasonId = message.SeasonId,
                AssociationId = message.AssociationId,
                DocType = message.DocType.ToString()
            };

        try
        {
            jobEntity.Status = IngestionStatus.InProgress.ToString();
            jobEntity.StartedAt ??= DateTimeOffset.UtcNow;
            jobEntity.CompletedAt = null;
            jobEntity.ErrorMessage = null;
            await _tableStore.UpsertEntityAsync("IngestionJobs", jobEntity, ct);

            using var pdfStream = await _blobStore.GetBlobAsync(message.PdfBlobPath, ct);

            _logger.LogInformation("Extracting pages for job {JobId} from {BlobPath}", jobId, message.PdfBlobPath);
            var pages = await _pdfExtractor.ExtractPagesAsync(pdfStream, ct);
            jobEntity.PageCount = pages.Count;

            var pagesJson = JsonSerializer.Serialize(pages, _jsonOptions);
            await _blobStore.UploadTextAsync(BlobPaths.GetIngestionPagesPath(jobId), pagesJson, ct);

            _logger.LogInformation("Chunking {PageCount} pages for job {JobId}", pages.Count, jobId);
            var chunks = _chunker.ChunkPages(pages, message.SeasonId, message.AssociationId, message.DocType);
            jobEntity.ChunkCount = chunks.Count;

            var chunksJson = JsonSerializer.Serialize(chunks, _jsonOptions);
            await _blobStore.UploadTextAsync(BlobPaths.GetIngestionChunksPath(jobId), chunksJson, ct);

            // Index chunks to Azure AI Search
            try
            {
                var indexed = await _searchStore.UpsertChunksAsync(message.SeasonId, message.AssociationId, message.DocType, chunks, ct);
                _logger.LogInformation("[{jobId}] Indexed {count} chunks to search", jobId, indexed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{jobId}] Failed to index chunks (continuing anyway)", jobId);
                // Continue - don't fail the job if indexing fails
            }

            jobEntity.Status = IngestionStatus.Completed.ToString();
            jobEntity.CompletedAt = DateTimeOffset.UtcNow;
            await _tableStore.UpsertEntityAsync("IngestionJobs", jobEntity, ct);

            _logger.LogInformation("✓ Job {JobId} completed: {Pages} pages, {Chunks} chunks", jobId, pages.Count, chunks.Count);
        }
        catch (Exception ex)
        {
            jobEntity.Status = IngestionStatus.Failed.ToString();
            jobEntity.CompletedAt = DateTimeOffset.UtcNow;
            jobEntity.ErrorMessage = ex.Message;
            await _tableStore.UpsertEntityAsync("IngestionJobs", jobEntity, ct);

            _logger.LogError(ex, "Job {JobId} failed", jobId);
        }
    }
}

