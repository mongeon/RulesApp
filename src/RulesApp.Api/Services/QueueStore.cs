using Azure.Storage.Queues;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RulesApp.Api.Services;

public interface IQueueStore
{
    Task EnqueueAsync<T>(string queueName, T message, CancellationToken ct = default);
}

public class QueueStore : IQueueStore
{
    private readonly QueueServiceClient _client;
    private readonly ILogger<QueueStore> _logger;
    private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public QueueStore(QueueServiceClient client, ILogger<QueueStore> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task EnqueueAsync<T>(string queueName, T message, CancellationToken ct = default)
    {
        var queue = _client.GetQueueClient(queueName);
        try
        {
            var created = await queue.CreateIfNotExistsAsync(cancellationToken: ct);
            _logger.LogInformation("Using queue {QueueName} at {Uri}", queueName, queue.Uri);

            var json = JsonSerializer.Serialize(message, _jsonOptions);
            var receipt = await queue.SendMessageAsync(json, cancellationToken: ct);
            _logger.LogInformation("Sent message to {QueueName}: id {MessageId}, popReceipt {PopReceipt}", queueName, receipt.Value.MessageId, receipt.Value.PopReceipt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue message to {QueueName}", queueName);
            throw;
        }
    }
}
